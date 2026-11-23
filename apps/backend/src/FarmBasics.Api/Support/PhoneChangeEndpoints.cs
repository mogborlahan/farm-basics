using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Support;

public sealed record InitiatePhoneChangeRequest(string NewPhone, Guid? AgentSessionId = null);

public sealed record ConfirmPhoneChangeRequest(string Otp, Guid? AgentSessionId = null);

/// <summary>
/// In-person, extension-agent-verified farmer phone change flow for lost-phone recovery (WP-94 / Doc/16-OPS gap 6 / P6).
/// Explicitly refuses any remote or self-service caller without a live bound-tablet agent session.
/// </summary>
internal static class PhoneChangeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapPhoneChangeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/identities/{identityId:guid}/phone-change/initiate", HandleInitiatePhoneChangeAsync)
            .RequireAuthorization();

        app.MapPost("/v1/identities/{identityId:guid}/phone-change/{requestId:guid}/confirm", HandleConfirmPhoneChangeAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.OtpVerify);
    }

    private static async Task<IResult> HandleInitiatePhoneChangeAsync(
        Guid identityId,
        InitiatePhoneChangeRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        ISmsGatewayClient smsClient,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated extension agent is required.");
        }

        // PRECEDENCE P6: Zero remote or self-service phone change path; requires active bound-tablet agent session.
        if (!AgentOnboardingEndpoints.TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid sessionId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AGENT_SESSION_REQUIRED",
                "In-person verification on an active bound-tablet agent session is required for phone change recovery (P6).");
        }

        if (identityId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Identity identifier is invalid.");
        }

        if (!SelfServiceRegistrationEndpoints.TryNormalizeNigerianPhone(request.NewPhone, out string normalizedNewPhone))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "New phone number must be a valid Nigerian mobile number.");
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
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, sessionGate.ErrorStatusCode, sessionGate.ErrorCode ?? "PERMISSION_DENIED", sessionGate.ErrorMessage ?? "Active agent tablet session is invalid.");
        }

        string currentStatus = string.Empty;
        string oldPhone = string.Empty;
        bool identityFound = false;
        await using (NpgsqlCommand targetCmd = conn.CreateCommand())
        {
            targetCmd.Transaction = tx;
            targetCmd.CommandText = """
                SELECT status::text, phone_primary
                FROM identity.identity
                WHERE id = @identityId
                FOR UPDATE;
                """;
            targetCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader reader = await targetCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                identityFound = true;
                currentStatus = reader.GetString(0);
                oldPhone = reader.GetString(1);
            }
        }

        if (!identityFound)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Target identity was not found.");
        }

        if (!string.Equals(currentStatus, "active", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Only active identities can undergo in-person phone recovery.");
        }

        await using (NpgsqlCommand dupCmd = conn.CreateCommand())
        {
            dupCmd.Transaction = tx;
            dupCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM identity.identity WHERE phone_primary = @newPhone);";
            dupCmd.Parameters.AddWithValue("newPhone", normalizedNewPhone);
            bool inUse = Convert.ToBoolean(await dupCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (inUse)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "PHONE_ALREADY_IN_USE", "Target phone number is already registered on the platform.");
            }
        }

        await using (NpgsqlCommand cancelPrevCmd = conn.CreateCommand())
        {
            cancelPrevCmd.Transaction = tx;
            cancelPrevCmd.CommandText = """
                UPDATE identity.phone_change_request
                SET status = 'cancelled'
                WHERE identity_id = @identityId
                  AND status = 'pending_otp';
                """;
            cancelPrevCmd.Parameters.AddWithValue("identityId", identityId);
            await cancelPrevCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid requestId = Guid.NewGuid();
        string otpPlaintext = SelfServiceRegistrationEndpoints.GenerateSixDigitOtp();
        string otpHash = SelfServiceRegistrationEndpoints.ComputeOtpHash(identityId, otpPlaintext);

        await using (NpgsqlCommand insCmd = conn.CreateCommand())
        {
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO identity.phone_change_request (
                    id, identity_id, old_phone, new_phone,
                    initiated_by_agent_id, agent_session_id, tablet_device_id,
                    otp_code_hash, otp_expires_at, status
                )
                VALUES (
                    @id, @identityId, @oldPhone, @newPhone,
                    @agentId, @sessionId, @tabletId,
                    @otpHash, now() + interval '10 minutes', 'pending_otp'
                );
                """;
            insCmd.Parameters.AddWithValue("id", requestId);
            insCmd.Parameters.AddWithValue("identityId", identityId);
            insCmd.Parameters.AddWithValue("oldPhone", oldPhone);
            insCmd.Parameters.AddWithValue("newPhone", normalizedNewPhone);
            insCmd.Parameters.AddWithValue("agentId", agentId);
            insCmd.Parameters.AddWithValue("sessionId", sessionId);
            insCmd.Parameters.AddWithValue("tabletId", sessionGate.TabletDeviceId);
            insCmd.Parameters.AddWithValue("otpHash", otpHash);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "identity",
                EventType: "identity.phone_change_initiated",
                EntityType: "phone_change_request",
                EntityId: requestId,
                ActorId: agentId,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    targetIdentityId = identityId,
                    agentSessionId = sessionId,
                    sessionGate.TabletDeviceId,
                    newPhoneLast4 = MaskLast4(normalizedNewPhone)
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        SmsSendResult smsResult = await smsClient.SendSmsAsync(
            new SmsSendRequest(normalizedNewPhone, $"Your Farm Basics phone change verification code is {otpPlaintext}. Valid for 10 minutes."),
            cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    phoneChangeRequestId = requestId,
                    identityId,
                    status = "pending_otp",
                    smsDispatched = smsResult.Succeeded
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleConfirmPhoneChangeAsync(
        Guid identityId,
        Guid requestId,
        ConfirmPhoneChangeRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated extension agent is required.");
        }

        if (!AgentOnboardingEndpoints.TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid sessionId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AGENT_SESSION_REQUIRED",
                "In-person verification on an active bound-tablet agent session is required to confirm a phone change (P6).");
        }

        if (identityId == Guid.Empty || requestId == Guid.Empty || !SelfServiceRegistrationEndpoints.IsSixDigitOtp(request.Otp))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "Enter the 6-digit SMS verification code.");
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
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, sessionGate.ErrorStatusCode, sessionGate.ErrorCode ?? "PERMISSION_DENIED", sessionGate.ErrorMessage ?? "Active agent tablet session is invalid.");
        }

        string oldPhone = string.Empty;
        string newPhone = string.Empty;
        string expectedOtpHash = string.Empty;
        DateTimeOffset expiresAt = default;
        string reqStatus = string.Empty;
        Guid initiatedByAgentId = Guid.Empty;
        bool reqFound = false;

        await using (NpgsqlCommand reqCmd = conn.CreateCommand())
        {
            reqCmd.Transaction = tx;
            reqCmd.CommandText = """
                SELECT old_phone, new_phone, otp_code_hash, otp_expires_at, status, initiated_by_agent_id
                FROM identity.phone_change_request
                WHERE id = @requestId
                  AND identity_id = @identityId
                FOR UPDATE;
                """;
            reqCmd.Parameters.AddWithValue("requestId", requestId);
            reqCmd.Parameters.AddWithValue("identityId", identityId);

            await using NpgsqlDataReader reader = await reqCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                reqFound = true;
                oldPhone = reader.GetString(0);
                newPhone = reader.GetString(1);
                expectedOtpHash = reader.GetString(2);
                expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
                reqStatus = reader.GetString(4);
                initiatedByAgentId = reader.GetGuid(5);
            }
        }

        if (!reqFound)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Phone change request was not found.");
        }

        if (initiatedByAgentId != agentId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "PERMISSION_DENIED",
                "Phone change confirmation must be completed by the initiating extension agent.");
        }

        if (!string.Equals(reqStatus, "pending_otp", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Phone change request is no longer pending confirmation.");
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            await using NpgsqlCommand expCmd = conn.CreateCommand();
            expCmd.Transaction = tx;
            expCmd.CommandText = "UPDATE identity.phone_change_request SET status = 'expired' WHERE id = @requestId;";
            expCmd.Parameters.AddWithValue("requestId", requestId);
            await expCmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "OTP_EXPIRED", "Verification code has expired. Initiate a new phone change request.");
        }

        string actualHash = SelfServiceRegistrationEndpoints.ComputeOtpHash(identityId, request.Otp.Trim());
        bool matched = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHash),
            Encoding.UTF8.GetBytes(expectedOtpHash));
        if (!matched)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "The verification code entered is incorrect.");
        }

        await using (NpgsqlCommand updIdCmd = conn.CreateCommand())
        {
            updIdCmd.Transaction = tx;
            updIdCmd.CommandText = """
                UPDATE identity.identity
                SET phone_primary = @newPhone,
                    phone_verified_at = now(),
                    updated_at = now()
                WHERE id = @identityId;
                """;
            updIdCmd.Parameters.AddWithValue("newPhone", newPhone);
            updIdCmd.Parameters.AddWithValue("identityId", identityId);
            await updIdCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand updReqCmd = conn.CreateCommand())
        {
            updReqCmd.Transaction = tx;
            updReqCmd.CommandText = """
                UPDATE identity.phone_change_request
                SET status = 'completed',
                    completed_at = now()
                WHERE id = @requestId;
                """;
            updReqCmd.Parameters.AddWithValue("requestId", requestId);
            await updReqCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "identity",
                EventType: "identity.phone_changed_in_person",
                EntityType: "identity",
                EntityId: identityId,
                ActorId: agentId,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    phoneChangeRequestId = requestId,
                    agentId,
                    agentSessionId = sessionId,
                    sessionGate.TabletDeviceId,
                    oldPhoneLast4 = MaskLast4(oldPhone),
                    newPhoneLast4 = MaskLast4(newPhone)
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                phoneChangeRequestId = requestId,
                identityId,
                status = "completed",
                newPhone
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static string MaskLast4(string phone)
        => phone.Length <= 4 ? "****" : phone[^4..];
}
