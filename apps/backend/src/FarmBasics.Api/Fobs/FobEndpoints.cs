using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Fobs;

public sealed record IssueFobRequest(Guid IdentityId, string FobSerial, Guid? AgentSessionId = null);

public sealed record ReissueFobRequest(Guid IdentityId, string FobSerial, Guid ReplacesFobId, Guid? AgentSessionId = null);

/// <summary>
/// Physical NFC fob issuance, immediate permanent loss revocation, and in-person reissuance (WP-31 / BR-A-02 / Rule 12).
/// By deliberate design (Doc/AGENTS.md §4), zero reactivation endpoint exists at any privilege level.
/// </summary>
internal static class FobEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapFobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/fobs/issue", HandleIssueFobAsync)
            .RequireAuthorization();

        app.MapPost("/v1/fobs/{fobId:guid}/report-lost", HandleReportLostAsync)
            .RequireAuthorization();

        app.MapPost("/v1/fobs/reissue", HandleReissueFobAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleIssueFobAsync(
        IssueFobRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent is required.");
        }

        if (!AgentOnboardingEndpoints.TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid sessionId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "AGENT_SESSION_REQUIRED", "Active bound-tablet agent session is required to issue a fob.");
        }

        if (request.IdentityId == Guid.Empty || string.IsNullOrWhiteSpace(request.FobSerial))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "IdentityId and FobSerial are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        AgentSessionValidationResult sessionGate = await AgentOnboardingEndpoints.ValidateActiveAgentSessionAsync(conn, tx, agentId, sessionId, cancellationToken);
        if (!sessionGate.IsValid)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, sessionGate.ErrorStatusCode, sessionGate.ErrorCode ?? "PERMISSION_DENIED", sessionGate.ErrorMessage ?? "Agent session is invalid.");
        }

        await using (NpgsqlCommand targetCmd = conn.CreateCommand())
        {
            targetCmd.Transaction = tx;
            targetCmd.CommandText = "SELECT status::text FROM identity.identity WHERE id = @id;";
            targetCmd.Parameters.AddWithValue("id", request.IdentityId);
            string? targetStatus = await targetCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(targetStatus, "active", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Farmer identity must be verified and active before issuing a physical fob.");
            }
        }

        Guid fobId = Guid.NewGuid();
        Guid? insertedFobId;
        await using (NpgsqlCommand insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO identity.fob_credential (id, fob_serial, farmer_identity_id, status, issued_by_agent_id)
                VALUES (@id, @serial, @farmerId, 'active', @agentId)
                ON CONFLICT (fob_serial) DO NOTHING
                RETURNING id;
                """;
            insertCmd.Parameters.AddWithValue("id", fobId);
            insertCmd.Parameters.AddWithValue("serial", request.FobSerial.Trim());
            insertCmd.Parameters.AddWithValue("farmerId", request.IdentityId);
            insertCmd.Parameters.AddWithValue("agentId", agentId);
            insertedFobId = await insertCmd.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (!insertedFobId.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "FOB_SERIAL_ALREADY_BOUND", "This fob serial is already bound to an identity.");
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "fob.issued",
                "fob_credential",
                fobId,
                agentId,
                JsonSerializer.Serialize(new { farmerIdentityId = request.IdentityId, sessionGate.TabletDeviceId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new { fobId, status = "active", fobSerial = request.FobSerial.Trim() },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleReportLostAsync(
        Guid fobId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent or IdentityTrustAdmin is required.");
        }

        bool isAdminRevoker = await adminPermissionChecker.HasPermissionAsync(callerId.ToString(), "fob:revoke", cancellationToken);

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        if (!isAdminRevoker)
        {
            bool authorizedAgent;
            if (AgentOnboardingEndpoints.TryResolveSessionId(httpContext.Request, null, out Guid headerSessionId))
            {
                AgentSessionValidationResult gate = await AgentOnboardingEndpoints.ValidateActiveAgentSessionAsync(conn, tx, callerId, headerSessionId, cancellationToken);
                authorizedAgent = gate.IsValid;
            }
            else
            {
                await using NpgsqlCommand sessCheckCmd = conn.CreateCommand();
                sessCheckCmd.Transaction = tx;
                sessCheckCmd.CommandText = """
                    SELECT EXISTS (
                        SELECT 1
                        FROM identity.agent_session s
                        JOIN identity.tablet_device td ON td.id = s.tablet_device_id
                        JOIN identity.organization o ON o.id = s.organization_id
                        WHERE s.agent_identity_id = @callerId
                          AND s.ended_at IS NULL
                          AND s.expires_at > now()
                          AND td.status = 'active'
                          AND o.verification_status = 'verified'
                    );
                    """;
                sessCheckCmd.Parameters.AddWithValue("callerId", callerId);
                authorizedAgent = Convert.ToBoolean(await sessCheckCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!authorizedAgent)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Only an active agent session or IdentityTrustAdmin may revoke a fob.");
            }
        }

        Guid targetFarmerId = Guid.Empty;
        bool fobExists = false;
        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = """
                SELECT farmer_identity_id
                FROM identity.fob_credential
                WHERE id = @fobId
                FOR UPDATE;
                """;
            lockCmd.Parameters.AddWithValue("fobId", fobId);

            if (await lockCmd.ExecuteScalarAsync(cancellationToken) is Guid foundFarmerId)
            {
                fobExists = true;
                targetFarmerId = foundFarmerId;
            }
        }

        if (!fobExists)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Fob credential was not found.");
        }

        if (isAdminRevoker)
        {
            AdminPermissionScope scope = await AdminScopeResolver.ResolveScopesAsync(conn, tx, callerId, "fob", "revoke", cancellationToken);
            if (!await AdminScopeResolver.IsIdentityInScopeAsync(conn, tx, scope, targetFarmerId, cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "SCOPE_VIOLATION",
                    "Administrator cannot revoke a fob outside their assigned scope (Rule 8).");
            }
        }

        string? resultingStatus = null;
        Guid? farmerIdentityId = null;
        await using (NpgsqlCommand revokeCmd = conn.CreateCommand())
        {
            revokeCmd.Transaction = tx;
            revokeCmd.CommandText = """
                UPDATE identity.fob_credential
                SET status = CASE WHEN status = 'replaced' THEN 'replaced'::fob_status ELSE 'revoked'::fob_status END,
                    revoked_at = COALESCE(revoked_at, now())
                WHERE id = @fobId
                RETURNING status::text, farmer_identity_id;
                """;
            revokeCmd.Parameters.AddWithValue("fobId", fobId);

            await using NpgsqlDataReader reader = await revokeCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                resultingStatus = reader.GetString(0);
                farmerIdentityId = reader.GetGuid(1);
            }
        }

        if (resultingStatus is null || !farmerIdentityId.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Fob credential was not found.");
        }

        string? rawCaseRef = httpContext.Request.Headers[Support.SupportEndpoints.SupportCaseRefHeaderName].FirstOrDefault();
        string? supportCaseRef = string.IsNullOrWhiteSpace(rawCaseRef) ? null : rawCaseRef.Trim();

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "fob.revoked",
                "fob_credential",
                fobId,
                callerId,
                JsonSerializer.Serialize(new { farmerIdentityId, resultingStatus, revokedByAdmin = isAdminRevoker, supportCaseRef }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { fobId, status = resultingStatus },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleReissueFobAsync(
        ReissueFobRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent is required.");
        }

        // BR-D-09 & Rule 12: No remote reissue path exists — requires active in-person agent tablet session.
        if (!AgentOnboardingEndpoints.TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid sessionId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "AGENT_SESSION_REQUIRED", "In-person verification on an active agent tablet session is required for fob reissuance.");
        }

        if (request.IdentityId == Guid.Empty || request.ReplacesFobId == Guid.Empty || string.IsNullOrWhiteSpace(request.FobSerial))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "IdentityId, ReplacesFobId, and FobSerial are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        AgentSessionValidationResult sessionGate = await AgentOnboardingEndpoints.ValidateActiveAgentSessionAsync(conn, tx, agentId, sessionId, cancellationToken);
        if (!sessionGate.IsValid)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, sessionGate.ErrorStatusCode, sessionGate.ErrorCode ?? "PERMISSION_DENIED", sessionGate.ErrorMessage ?? "Agent session is invalid.");
        }

        Guid oldFarmerId;
        string oldStatus;
        await using (NpgsqlCommand oldCmd = conn.CreateCommand())
        {
            oldCmd.Transaction = tx;
            oldCmd.CommandText = "SELECT farmer_identity_id, status::text FROM identity.fob_credential WHERE id = @replacesId FOR UPDATE;";
            oldCmd.Parameters.AddWithValue("replacesId", request.ReplacesFobId);

            await using NpgsqlDataReader reader = await oldCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Previous fob credential was not found.");
            }

            oldFarmerId = reader.GetGuid(0);
            oldStatus = reader.GetString(1);
        }

        if (oldFarmerId != request.IdentityId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "ReplacesFobId does not belong to the target farmer identity.");
        }

        if (oldStatus is not ("revoked" or "lost"))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Only a revoked or lost fob can be replaced by a reissue.");
        }

        await using (NpgsqlCommand markOldCmd = conn.CreateCommand())
        {
            markOldCmd.Transaction = tx;
            markOldCmd.CommandText = "UPDATE identity.fob_credential SET status = 'replaced' WHERE id = @replacesId;";
            markOldCmd.Parameters.AddWithValue("replacesId", request.ReplacesFobId);
            await markOldCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid newFobId = Guid.NewGuid();
        Guid? insertedNewId;
        await using (NpgsqlCommand insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO identity.fob_credential (id, fob_serial, farmer_identity_id, status, issued_by_agent_id, replaces_fob_id)
                VALUES (@id, @serial, @farmerId, 'active', @agentId, @replacesId)
                ON CONFLICT (fob_serial) DO NOTHING
                RETURNING id;
                """;
            insertCmd.Parameters.AddWithValue("id", newFobId);
            insertCmd.Parameters.AddWithValue("serial", request.FobSerial.Trim());
            insertCmd.Parameters.AddWithValue("farmerId", request.IdentityId);
            insertCmd.Parameters.AddWithValue("agentId", agentId);
            insertCmd.Parameters.AddWithValue("replacesId", request.ReplacesFobId);
            insertedNewId = await insertCmd.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (!insertedNewId.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "FOB_SERIAL_ALREADY_BOUND", "This replacement fob serial is already bound.");
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "fob.reissued",
                "fob_credential",
                newFobId,
                agentId,
                JsonSerializer.Serialize(new { farmerIdentityId = request.IdentityId, request.ReplacesFobId, sessionGate.TabletDeviceId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new { fobId = newFobId, status = "active", replacesFobId = request.ReplacesFobId },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }
}
