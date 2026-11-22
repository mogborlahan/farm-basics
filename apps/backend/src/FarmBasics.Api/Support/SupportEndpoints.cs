using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.Api.Reviews;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Support;

public sealed record SupportFobRevocationRequest(string SupportCaseRef, string Reason);

/// <summary>
/// Customer Support Agent privacy-minimized read-view and P3 decision-recorded fob revocation escalation (WP-93).
/// Honors Doc/16-OPS §9.2 &amp; PRECEDENCE P3/P4/P5:
/// - Mandatory X-Support-Case-Ref persisted on every support-audited action payload.
/// - Zero NIN, DOB, phone, fobSerial, exact GPS boundary, or financial balance exposure.
/// - CustomerSupportAgent cannot directly revoke fobs; escalations create urgent IdentityTrustAdmin review items.
/// </summary>
internal static class SupportEndpoints
{
    public const string SupportCaseRefHeaderName = "X-Support-Case-Ref";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/support/identities/{identityId:guid}", HandleGetSupportIdentityReadViewAsync)
            .RequireAuthorization()
            .RequireAdminPermission("support", "read");

        app.MapPost("/v1/admin/support/fobs/{fobId:guid}/revoke-request", HandleRequestFobRevocationAsync)
            .RequireAuthorization()
            .RequireAdminPermission("support", "escalate_fob");
    }

    private static async Task<IResult> HandleGetSupportIdentityReadViewAsync(
        Guid identityId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated support identity is required.");
        }

        string? rawCaseRef = httpContext.Request.Headers[SupportCaseRefHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawCaseRef) || rawCaseRef.Trim().Length < 3 || rawCaseRef.Trim().Length > 128)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "SUPPORT_CASE_REF_REQUIRED",
                "A valid X-Support-Case-Ref header (3-128 chars) is required for every support lookup (Doc/16-OPS §10 & gap 4).");
        }

        string supportCaseRef = rawCaseRef.Trim();

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        string identityStatus = string.Empty;
        string? stateName = null;
        string? lgaName = null;
        DateTimeOffset createdAt = default;
        bool identityFound = false;

        await using (NpgsqlCommand idCmd = conn.CreateCommand())
        {
            idCmd.Transaction = tx;
            idCmd.CommandText = """
                SELECT i.status::text, s.name, l.name, i.created_at
                FROM identity.identity i
                LEFT JOIN identity.state s ON s.id = i.state_id
                LEFT JOIN identity.lga l ON l.id = i.lga_id
                WHERE i.id = @identityId;
                """;
            idCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader reader = await idCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                identityFound = true;
                identityStatus = reader.GetString(0);
                stateName = reader.IsDBNull(1) ? null : reader.GetString(1);
                lgaName = reader.IsDBNull(2) ? null : reader.GetString(2);
                createdAt = reader.GetFieldValue<DateTimeOffset>(3);
            }
        }

        if (!identityFound)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Target identity was not found.");
        }

        object? latestReview = null;
        await using (NpgsqlCommand revCmd = conn.CreateCommand())
        {
            revCmd.Transaction = tx;
            revCmd.CommandText = """
                SELECT rc.code, rqi.status::text, rqi.due_at
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rqi.subject_ref_type = 'identity'
                  AND rqi.subject_ref_id = @identityId
                ORDER BY rqi.created_at DESC
                LIMIT 1;
                """;
            revCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader rr = await revCmd.ExecuteReaderAsync(cancellationToken);
            if (await rr.ReadAsync(cancellationToken))
            {
                latestReview = new
                {
                    categoryCode = rr.GetString(0),
                    status = rr.GetString(1),
                    dueAt = rr.GetFieldValue<DateTimeOffset>(2)
                };
            }
        }

        // Doc/16-OPS §9.2: Support sees active/revoked fob status ONLY, never the fob_serial.
        List<object> fobs = [];
        await using (NpgsqlCommand fobCmd = conn.CreateCommand())
        {
            fobCmd.Transaction = tx;
            fobCmd.CommandText = """
                SELECT id, status::text, issued_at, revoked_at
                FROM identity.fob_credential
                WHERE farmer_identity_id = @identityId
                ORDER BY issued_at DESC;
                """;
            fobCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader fr = await fobCmd.ExecuteReaderAsync(cancellationToken);
            while (await fr.ReadAsync(cancellationToken))
            {
                fobs.Add(new
                {
                    fobId = fr.GetGuid(0),
                    status = fr.GetString(1),
                    issuedAt = fr.GetFieldValue<DateTimeOffset>(2),
                    revokedAt = fr.IsDBNull(3) ? (DateTimeOffset?)null : fr.GetFieldValue<DateTimeOffset>(3)
                });
            }
        }

        // Doc/16-OPS §9.2: Field/unit name, system type, capture method, coarse region ONLY (no GPS coordinates or boundaries).
        List<object> units = [];
        await using (NpgsqlCommand unitCmd = conn.CreateCommand())
        {
            unitCmd.Transaction = tx;
            unitCmd.CommandText = """
                SELECT pu.id, pu.name, pu.system_type::text, pu.capture_method::text, l.name
                FROM field.production_unit pu
                LEFT JOIN identity.lga l ON l.id = pu.lga_id
                WHERE pu.farm_identity_id = @identityId
                ORDER BY pu.created_at DESC;
                """;
            unitCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader ur = await unitCmd.ExecuteReaderAsync(cancellationToken);
            while (await ur.ReadAsync(cancellationToken))
            {
                units.Add(new
                {
                    unitId = ur.GetGuid(0),
                    name = ur.GetString(1),
                    systemType = ur.GetString(2),
                    captureMethod = ur.GetString(3),
                    lgaName = ur.IsDBNull(4) ? lgaName : ur.GetString(4)
                });
            }
        }

        List<object> cropCycles = [];
        await using (NpgsqlCommand cycCmd = conn.CreateCommand())
        {
            cycCmd.Transaction = tx;
            cycCmd.CommandText = """
                SELECT
                    cc.id,
                    cp.common_name,
                    cc.stage::text,
                    COUNT(ol.id) FILTER (WHERE ol.status = 'flag' AND ol.override_reason IS NULL)::int AS unresolved_flag_count
                FROM field.crop_cycle cc
                JOIN field.production_unit pu ON pu.id = cc.production_unit_id
                JOIN field.crop_profile cp ON cp.id = cc.crop_profile_id
                LEFT JOIN field.operation_log ol ON ol.crop_cycle_id = cc.id
                WHERE pu.farm_identity_id = @identityId
                GROUP BY cc.id, cp.common_name, cc.stage, cc.created_at
                ORDER BY cc.created_at DESC
                LIMIT 20;
                """;
            cycCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader cr = await cycCmd.ExecuteReaderAsync(cancellationToken);
            while (await cr.ReadAsync(cancellationToken))
            {
                cropCycles.Add(new
                {
                    cycleId = cr.GetGuid(0),
                    cropName = cr.GetString(1),
                    stage = cr.GetString(2),
                    unresolvedFlagCount = cr.GetInt32(3)
                });
            }
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "support",
                EventType: "support.identity_lookup",
                EntityType: "identity",
                EntityId: identityId,
                ActorId: callerId,
                PayloadJson: JsonSerializer.Serialize(new { supportCaseRef, view = "status_only_doc16_9_2" }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                identityId,
                status = identityStatus,
                region = new { stateName, lgaName },
                createdAt,
                supportCaseRef,
                latestReview,
                fobs,
                units,
                cropCycles
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleRequestFobRevocationAsync(
        Guid fobId,
        SupportFobRevocationRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated support identity is required.");
        }

        if (fobId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Fob identifier is invalid.");
        }

        string supportCaseRef = request.SupportCaseRef?.Trim() ?? string.Empty;
        string reason = request.Reason?.Trim() ?? string.Empty;
        if (supportCaseRef.Length < 3 || supportCaseRef.Length > 128)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "SUPPORT_CASE_REF_REQUIRED", "SupportCaseRef must be between 3 and 128 characters.");
        }

        if (reason.Length < 10)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Revocation escalation reason must be at least 10 characters.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid farmerIdentityId = Guid.Empty;
        string fobStatus = string.Empty;
        bool fobFound = false;
        await using (NpgsqlCommand fobCmd = conn.CreateCommand())
        {
            fobCmd.Transaction = tx;
            fobCmd.CommandText = """
                SELECT farmer_identity_id, status::text
                FROM identity.fob_credential
                WHERE id = @fobId
                FOR UPDATE;
                """;
            fobCmd.Parameters.AddWithValue("fobId", fobId);

            await using NpgsqlDataReader reader = await fobCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                fobFound = true;
                farmerIdentityId = reader.GetGuid(0);
                fobStatus = reader.GetString(1);
            }
        }

        if (!fobFound)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Fob credential was not found.");
        }

        if (!string.Equals(fobStatus, "active", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Fob credential is already revoked or inactive.");
        }

        Guid reviewQueueItemId = await ReviewEngineEndpoints.EnqueueReviewItemAsync(
            conn,
            tx,
            "SUPPORT_FOB_REVOCATION_ESCALATION",
            "fob_credential",
            fobId,
            submittedByIdentityId: callerId,
            priority: "urgent",
            cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "support",
                EventType: "support.fob_revocation_escalated",
                EntityType: "fob_credential",
                EntityId: fobId,
                ActorId: callerId,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    supportCaseRef,
                    reason,
                    farmerIdentityId,
                    reviewQueueItemId,
                    routedToRole = "IdentityTrustAdmin",
                    decisionRef = "P3"
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    reviewQueueItemId,
                    fobId,
                    farmerIdentityId,
                    routedToRole = "IdentityTrustAdmin",
                    supportCaseRef,
                    decisionRef = "P3",
                    status = "escalated_for_revocation"
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status202Accepted);
    }
}
