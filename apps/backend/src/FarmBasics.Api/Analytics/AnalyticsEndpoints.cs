using FarmBasics.Api.Agents;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Analytics;

/// <summary>
/// Computed, strictly scope-respecting Analytics Baseline API (WP-92 / FR-AR-001 / Doc 03-ENG §13).
/// Computes live aggregates from domain tables (never a secondary copy) and isolates region-scoped admins.
/// </summary>
internal static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/analytics/baseline", HandleGetAnalyticsBaselineAsync)
            .RequireAuthorization()
            .RequireAdminPermission("analytics", "read");
    }

    private static async Task<IResult> HandleGetAnalyticsBaselineAsync(
        Guid? stateId,
        Guid? lgaId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated admin identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        AdminPermissionScope scope = await AdminScopeResolver.ResolveScopesAsync(conn, null, callerId, "analytics", "read", cancellationToken);

        if (!scope.HasGlobal && scope.RegionIds.Count == 0 && scope.OrganizationIds.Count == 0)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "PERMISSION_DENIED",
                "Caller holds no active scope assignment for analytics:read.");
        }

        bool applyRegionScope = !scope.HasGlobal && scope.RegionIds.Count > 0;
        if (applyRegionScope)
        {
            if (stateId.HasValue && stateId.Value != Guid.Empty && !scope.RegionIds.Contains(stateId.Value))
            {
                return SelfServiceRegistrationEndpoints.ErrorResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "SCOPE_VIOLATION",
                    "Region-scoped administrator cannot query analytics outside their assigned state/LGA (FR-AR-001).");
            }

            if (lgaId.HasValue && lgaId.Value != Guid.Empty)
            {
                bool lgaInScope = scope.RegionIds.Contains(lgaId.Value);
                if (!lgaInScope)
                {
                    await using NpgsqlCommand lgaStateCmd = conn.CreateCommand();
                    lgaStateCmd.CommandText = "SELECT state_id FROM identity.lga WHERE id = @lgaId;";
                    lgaStateCmd.Parameters.AddWithValue("lgaId", lgaId.Value);
                    if (await lgaStateCmd.ExecuteScalarAsync(cancellationToken) is Guid parentStateId
                        && scope.RegionIds.Contains(parentStateId))
                    {
                        lgaInScope = true;
                    }
                }

                if (!lgaInScope)
                {
                    return SelfServiceRegistrationEndpoints.ErrorResult(
                        httpContext,
                        StatusCodes.Status403Forbidden,
                        "SCOPE_VIOLATION",
                        "Region-scoped administrator cannot query analytics outside their assigned state/LGA (FR-AR-001).");
                }
            }
        }

        if (!scope.HasGlobal && scope.RegionIds.Count == 0)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "SCOPE_VIOLATION",
                "Analytics baseline requires global or region-scoped assignment.");
        }

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH scoped_farmers AS (
                SELECT i.id, i.status
                FROM identity.identity i
                WHERE EXISTS (
                    SELECT 1
                    FROM identity.role_profile rp
                    WHERE rp.identity_id = i.id
                      AND rp.role_type = 'farmer'
                )
                  AND (@applyRegionScope = FALSE OR i.state_id = ANY(@regionIds) OR i.lga_id = ANY(@regionIds))
                  AND (@filterStateId IS NULL OR i.state_id = @filterStateId)
                  AND (@filterLgaId IS NULL OR i.lga_id = @filterLgaId)
            ),
            scoped_units AS (
                SELECT pu.id, pu.boundary, pu.area_hectares
                FROM field.production_unit pu
                JOIN identity.identity owner ON owner.id = pu.farm_identity_id
                LEFT JOIN identity.lga pu_lga ON pu_lga.id = pu.lga_id
                WHERE (@applyRegionScope = FALSE
                       OR COALESCE(pu.lga_id, owner.lga_id) = ANY(@regionIds)
                       OR COALESCE(pu_lga.state_id, owner.state_id) = ANY(@regionIds))
                  AND (@filterStateId IS NULL OR COALESCE(pu_lga.state_id, owner.state_id) = @filterStateId)
                  AND (@filterLgaId IS NULL OR COALESCE(pu.lga_id, owner.lga_id) = @filterLgaId)
            ),
            scoped_cycles AS (
                SELECT cc.id, cc.stage
                FROM field.crop_cycle cc
                JOIN scoped_units su ON su.id = cc.production_unit_id
            ),
            scoped_harvest AS (
                SELECT COALESCE(SUM(hb.quantity_kg), 0)::numeric(14,2) AS total_harvested_kg
                FROM field.harvest_batch hb
                JOIN scoped_cycles sc ON sc.id = hb.crop_cycle_id
            )
            SELECT
                (SELECT COUNT(*)::int FROM scoped_farmers) AS total_farmers,
                (SELECT COUNT(*)::int FROM scoped_farmers WHERE status = 'active') AS verified_farmers,
                (SELECT COUNT(*)::int FROM scoped_units) AS production_units_count,
                (SELECT COALESCE(SUM(area_hectares) FILTER (WHERE boundary IS NOT NULL), 0)::numeric(14,4) FROM scoped_units) AS measured_area_ha,
                (SELECT COALESCE(SUM(area_hectares) FILTER (WHERE boundary IS NULL), 0)::numeric(14,4) FROM scoped_units) AS declared_area_ha,
                (SELECT COUNT(*)::int FROM scoped_cycles WHERE stage NOT IN ('closed', 'abandoned')) AS active_crop_cycles,
                (SELECT total_harvested_kg FROM scoped_harvest) AS total_harvested_kg;
            """;

        cmd.Parameters.AddWithValue("applyRegionScope", applyRegionScope);
        cmd.Parameters.AddWithValue("regionIds", scope.RegionIds.ToArray());

        NpgsqlParameter stateParam = cmd.Parameters.Add("filterStateId", NpgsqlDbType.Uuid);
        stateParam.Value = stateId.HasValue && stateId.Value != Guid.Empty ? stateId.Value : DBNull.Value;

        NpgsqlParameter lgaParam = cmd.Parameters.Add("filterLgaId", NpgsqlDbType.Uuid);
        lgaParam.Value = lgaId.HasValue && lgaId.Value != Guid.Empty ? lgaId.Value : DBNull.Value;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                effectiveScopeType = scope.HasGlobal ? "global" : "region",
                scopedRegionIds = scope.HasGlobal ? Array.Empty<Guid>() : scope.RegionIds,
                computedLive = true,
                totalFarmers = reader.GetInt32(0),
                verifiedFarmers = reader.GetInt32(1),
                productionUnitsCount = reader.GetInt32(2),
                measuredAreaHectares = reader.GetDecimal(3),
                declaredAreaHectares = reader.GetDecimal(4),
                activeCropCycles = reader.GetInt32(5),
                totalHarvestedKg = reader.GetDecimal(6)
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }
}
