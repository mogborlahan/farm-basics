using FarmBasics.Api.Agents;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.FieldOps;

/// <summary>
/// Field Operations console API (WP-91 / PRD-Volume-D FR-FO-001..003 / Doc 03-ENG §13.3).
/// Surfaces agent roster, tablet fleet, and small-sample-aware quality signals with strict Org/Region isolation.
/// </summary>
internal static class FieldOpsEndpoints
{
    public static void MapFieldOpsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/field-ops/agents/{orgId:guid}", HandleGetOrgAgentsAsync)
            .RequireAuthorization()
            .RequireAdminPermission("field_ops", "read");

        app.MapGet("/v1/admin/field-ops/agents", HandleListAgentsAsync)
            .RequireAuthorization()
            .RequireAdminPermission("field_ops", "read");

        app.MapGet("/v1/admin/field-ops/tablets", HandleListTabletsAsync)
            .RequireAuthorization()
            .RequireAdminPermission("field_ops", "read");
    }

    private static Task<IResult> HandleGetOrgAgentsAsync(
        Guid orgId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (orgId == Guid.Empty)
        {
            return Task.FromResult(SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Organization identifier is required."));
        }

        return QueryAgentsInternalAsync(orgId, httpContext, identityAccessor, dbSource, migrationRunner, cancellationToken);
    }

    private static Task<IResult> HandleListAgentsAsync(
        Guid? organizationId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
        => QueryAgentsInternalAsync(organizationId, httpContext, identityAccessor, dbSource, migrationRunner, cancellationToken);

    private static async Task<IResult> QueryAgentsInternalAsync(
        Guid? requestedOrgId,
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
        AdminPermissionScope scope = await AdminScopeResolver.ResolveScopesAsync(conn, null, callerId, "field_ops", "read", cancellationToken);

        IResult? scopeError = ValidateScope(httpContext, scope, requestedOrgId);
        if (scopeError is not null)
        {
            return scopeError;
        }

        bool applyOrgScope = !scope.HasGlobal && scope.OrganizationIds.Count > 0;
        bool applyRegionScope = !scope.HasGlobal && scope.OrganizationIds.Count == 0 && scope.RegionIds.Count > 0;

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ag.id,
                ag.first_name,
                ag.last_name,
                ag.status::text,
                rp.linked_organization_id,
                o.name AS organization_name,
                o.verification_status::text,
                COALESCE(log_stats.total_logs, 0)::int,
                COALESCE(log_stats.flagged_logs, 0)::int,
                COALESCE(rev_stats.total_reviews, 0)::int,
                COALESCE(rev_stats.rejected_reviews, 0)::int,
                COALESCE(tab_stats.bound_tablets, 0)::int
            FROM identity.role_profile rp
            JOIN identity.identity ag ON ag.id = rp.identity_id
            JOIN identity.organization o ON o.id = rp.linked_organization_id
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*)::int AS total_logs,
                    COUNT(*) FILTER (WHERE ol.status = 'flag')::int AS flagged_logs
                FROM field.operation_log ol
                WHERE ol.acting_agent_id = ag.id
                   OR (ol.acting_agent_id IS NULL AND ol.logged_by_identity_id = ag.id)
            ) log_stats ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*) FILTER (WHERE rqi.status = 'resolved')::int AS total_reviews,
                    COUNT(*) FILTER (WHERE rqi.status = 'resolved' AND rqi.verdict_code ILIKE 'REJECT%')::int AS rejected_reviews
                FROM review.review_queue_item rqi
                WHERE rqi.submitted_by_identity_id = ag.id
            ) rev_stats ON TRUE
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS bound_tablets
                FROM identity.tablet_device td
                WHERE td.assigned_agent_id = ag.id
                  AND td.bound_organization_id = rp.linked_organization_id
                  AND td.status = 'active'
            ) tab_stats ON TRUE
            WHERE rp.role_type = 'agent'
              AND rp.linked_organization_id IS NOT NULL
              AND (@requestedOrgId IS NULL OR rp.linked_organization_id = @requestedOrgId)
              AND (@applyOrgScope = FALSE OR rp.linked_organization_id = ANY(@orgIds))
              AND (@applyRegionScope = FALSE OR ag.state_id = ANY(@regionIds) OR ag.lga_id = ANY(@regionIds))
            ORDER BY o.name ASC, ag.last_name ASC, ag.first_name ASC;
            """;

        NpgsqlParameter reqOrgParam = cmd.Parameters.Add("requestedOrgId", NpgsqlDbType.Uuid);
        reqOrgParam.Value = requestedOrgId.HasValue && requestedOrgId.Value != Guid.Empty ? requestedOrgId.Value : DBNull.Value;
        cmd.Parameters.AddWithValue("applyOrgScope", applyOrgScope);
        cmd.Parameters.AddWithValue("orgIds", scope.OrganizationIds.ToArray());
        cmd.Parameters.AddWithValue("applyRegionScope", applyRegionScope);
        cmd.Parameters.AddWithValue("regionIds", scope.RegionIds.ToArray());

        List<object> agents = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid agentId = reader.GetGuid(0);
            string firstName = reader.GetString(1);
            string lastName = reader.GetString(2);
            string status = reader.GetString(3);
            Guid orgId = reader.GetGuid(4);
            string orgName = reader.GetString(5);
            string orgStatus = reader.GetString(6);
            int totalLogs = reader.GetInt32(7);
            int flaggedLogs = reader.GetInt32(8);
            int totalReviews = reader.GetInt32(9);
            int rejectedReviews = reader.GetInt32(10);
            int boundTablets = reader.GetInt32(11);

            FieldOpsQualitySignal quality = FieldOpsQualityCalculator.Compute(
                totalLogs,
                flaggedLogs,
                totalReviews,
                rejectedReviews,
                FieldOpsQualityCalculator.DefaultMinimumSampleSize);

            agents.Add(new
            {
                agentId,
                fullName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                    ? agentId.ToString("N")[..8]
                    : $"{firstName} {lastName}".Trim(),
                status,
                organizationId = orgId,
                organizationName = orgName,
                organizationStatus = orgStatus,
                activeBoundTablets = boundTablets,
                minimumSampleSize = FieldOpsQualityCalculator.DefaultMinimumSampleSize,
                totalLogs = quality.TotalLogs,
                flaggedLogs = quality.FlaggedLogs,
                isLogSampleSufficient = quality.IsLogSampleSufficient,
                flagRate = quality.FlagRate,
                totalReviews = quality.TotalReviews,
                rejectedReviews = quality.RejectedReviews,
                isReviewSampleSufficient = quality.IsReviewSampleSufficient,
                rejectionRate = quality.RejectionRate,
                signalStatus = quality.SignalStatus
            });
        }

        return Results.Ok(new ApiResponse<object>(
            new
            {
                minimumSampleSize = FieldOpsQualityCalculator.DefaultMinimumSampleSize,
                effectiveScopeType = scope.HasGlobal ? "global" : applyOrgScope ? "organization" : "region",
                agents
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleListTabletsAsync(
        Guid? organizationId,
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
        AdminPermissionScope scope = await AdminScopeResolver.ResolveScopesAsync(conn, null, callerId, "field_ops", "read", cancellationToken);

        IResult? scopeError = ValidateScope(httpContext, scope, organizationId);
        if (scopeError is not null)
        {
            return scopeError;
        }

        bool applyOrgScope = !scope.HasGlobal && scope.OrganizationIds.Count > 0;
        bool applyRegionScope = !scope.HasGlobal && scope.OrganizationIds.Count == 0 && scope.RegionIds.Count > 0;

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                td.id,
                td.device_serial,
                td.status::text,
                td.bound_organization_id,
                o.name AS organization_name,
                td.assigned_agent_id,
                ag.first_name,
                ag.last_name,
                td.bound_at,
                td.deactivated_at,
                (
                    SELECT COUNT(*)::int
                    FROM identity.agent_session s
                    WHERE s.tablet_device_id = td.id
                      AND s.ended_at IS NULL
                      AND s.expires_at > now()
                ) AS active_session_count,
                (
                    SELECT MAX(s.started_at)
                    FROM identity.agent_session s
                    WHERE s.tablet_device_id = td.id
                ) AS last_session_started_at
            FROM identity.tablet_device td
            LEFT JOIN identity.organization o ON o.id = td.bound_organization_id
            LEFT JOIN identity.identity ag ON ag.id = td.assigned_agent_id
            WHERE (@organizationId IS NULL OR td.bound_organization_id = @organizationId)
              AND (@applyOrgScope = FALSE OR td.bound_organization_id = ANY(@orgIds))
              AND (@applyRegionScope = FALSE OR ag.state_id = ANY(@regionIds) OR ag.lga_id = ANY(@regionIds))
            ORDER BY td.status ASC, td.device_serial ASC;
            """;

        NpgsqlParameter reqOrgParam = cmd.Parameters.Add("organizationId", NpgsqlDbType.Uuid);
        reqOrgParam.Value = organizationId.HasValue && organizationId.Value != Guid.Empty ? organizationId.Value : DBNull.Value;
        cmd.Parameters.AddWithValue("applyOrgScope", applyOrgScope);
        cmd.Parameters.AddWithValue("orgIds", scope.OrganizationIds.ToArray());
        cmd.Parameters.AddWithValue("applyRegionScope", applyRegionScope);
        cmd.Parameters.AddWithValue("regionIds", scope.RegionIds.ToArray());

        List<object> tablets = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid? assignedAgentId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
            string agentFirst = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            string agentLast = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            string? assignedAgentName = assignedAgentId.HasValue
                ? (string.IsNullOrWhiteSpace(agentFirst) && string.IsNullOrWhiteSpace(agentLast)
                    ? assignedAgentId.Value.ToString("N")[..8]
                    : $"{agentFirst} {agentLast}".Trim())
                : null;

            tablets.Add(new
            {
                tabletId = reader.GetGuid(0),
                deviceSerial = reader.GetString(1),
                status = reader.GetString(2),
                organizationId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                organizationName = reader.IsDBNull(4) ? null : reader.GetString(4),
                assignedAgentId,
                assignedAgentName,
                boundAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8),
                deactivatedAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
                activeSessionCount = reader.GetInt32(10),
                lastSessionStartedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11)
            });
        }

        return Results.Ok(new ApiResponse<object>(
            new
            {
                effectiveScopeType = scope.HasGlobal ? "global" : applyOrgScope ? "organization" : "region",
                tablets
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static IResult? ValidateScope(HttpContext httpContext, AdminPermissionScope scope, Guid? requestedOrgId)
    {
        if (!scope.HasGlobal && scope.OrganizationIds.Count == 0 && scope.RegionIds.Count == 0)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "PERMISSION_DENIED",
                "Caller holds no active scope assignment for field_ops:read.");
        }

        if (!scope.HasGlobal
            && requestedOrgId.HasValue
            && requestedOrgId.Value != Guid.Empty
            && scope.OrganizationIds.Count > 0
            && !scope.OrganizationIds.Contains(requestedOrgId.Value))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "SCOPE_VIOLATION",
                "Organization-scoped administrator cannot access another organization's field operations (FR-FO-003).");
        }

        return null;
    }
}
