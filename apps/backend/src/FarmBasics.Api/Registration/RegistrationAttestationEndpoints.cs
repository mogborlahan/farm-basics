using System.Globalization;
using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Persistence;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Registration;

public sealed record RecordRegistrationAttestationRequest(
    string SummaryVersion,
    string? PilotConsentVersion,
    string LanguageCode,
    string DeliveryMode,
    bool TeachBackQ1Passed,
    bool TeachBackQ2Passed,
    bool TeachBackQ3Passed,
    DateTimeOffset ClientRecordedAt,
    Guid? AgentIdentityId = null);

public sealed record RegistrationAttestationDto(
    Guid AttestationId,
    Guid UserId,
    string SummaryVersion,
    string? PilotConsentVersion,
    string LanguageCode,
    string DeliveryMode,
    bool TeachBackPassed,
    Guid? AgentIdentityId,
    string LawfulBasisScheduleRef,
    DateTimeOffset ClientRecordedAt,
    DateTimeOffset ServerReceivedAt);

/// <summary>
/// Auditable, append-only plain-language summary &amp; teach-back attestation API (WP-32 / Doc/11A-LEGAL §5.2).
/// Zero audio or biometric voiceprint columns permitted per PRECEDENCE P7 &amp; P11.
/// </summary>
internal static class RegistrationAttestationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedLanguageCodes = new(StringComparer.Ordinal)
    {
        "en-NG", "ha", "yo", "ig", "pcm"
    };

    private static readonly HashSet<string> AllowedDeliveryModes = new(StringComparer.Ordinal)
    {
        "IN_APP_TEXT_AND_AUDIO", "AGENT_SPOKEN_PLUS_LAMINATED_CARD", "IVR_PLAYBACK"
    };

    public static void MapRegistrationAttestationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/registrations/{identityId:guid}/attestations", HandleRecordAttestationAsync)
            .RequireAuthorization();
        app.MapGet("/v1/registrations/{identityId:guid}/attestations", HandleListAttestationsAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleRecordAttestationAsync(
        Guid identityId,
        RecordRegistrationAttestationRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        if (identityId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.SummaryVersion)
            || request.SummaryVersion.Trim().Length > 16
            || !AllowedLanguageCodes.Contains(request.LanguageCode?.Trim() ?? string.Empty)
            || !AllowedDeliveryModes.Contains(request.DeliveryMode?.Trim() ?? string.Empty))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Valid identityId, summaryVersion, languageCode (en-NG|ha|yo|ig|pcm), and deliveryMode are required.");
        }

        if (!request.TeachBackQ1Passed || !request.TeachBackQ2Passed || !request.TeachBackQ3Passed)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "ERR_ATTESTATION_INCOMPLETE",
                "All three teach-back comprehension checks must pass before recording registration attestation.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        Guid? effectiveAgentId = request.AgentIdentityId;
        if (!effectiveAgentId.HasValue && callerId != identityId)
        {
            effectiveAgentId = callerId;
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand targetExistsCmd = conn.CreateCommand())
        {
            targetExistsCmd.Transaction = tx;
            targetExistsCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM identity.identity WHERE id = @id);";
            targetExistsCmd.Parameters.AddWithValue("id", identityId);
            if (!Convert.ToBoolean(await targetExistsCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Target registration identity was not found.");
            }
        }

        Guid attestationId = Guid.NewGuid();
        bool teachBackPassed;
        string scheduleRef;
        DateTimeOffset serverReceivedAt;

        await using (NpgsqlCommand insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO identity.registration_attestation (
                    attestation_id, user_id, summary_version, pilot_consent_version,
                    language_code, delivery_mode, teach_back_q1_passed, teach_back_q2_passed,
                    teach_back_q3_passed, agent_identity_id, client_recorded_at
                )
                VALUES (
                    @id, @userId, @summaryVersion, @pilotVersion,
                    @lang, @mode, @q1, @q2,
                    @q3, @agentId, @clientRecordedAt
                )
                RETURNING teach_back_passed, lawful_basis_schedule_ref, server_received_at;
                """;
            insertCmd.Parameters.AddWithValue("id", attestationId);
            insertCmd.Parameters.AddWithValue("userId", identityId);
            insertCmd.Parameters.AddWithValue("summaryVersion", request.SummaryVersion.Trim());
            NpgsqlParameter pilotParam = insertCmd.Parameters.Add("pilotVersion", NpgsqlDbType.Varchar);
            pilotParam.Value = string.IsNullOrWhiteSpace(request.PilotConsentVersion) ? DBNull.Value : request.PilotConsentVersion.Trim();
            insertCmd.Parameters.AddWithValue("lang", request.LanguageCode!.Trim());
            insertCmd.Parameters.AddWithValue("mode", request.DeliveryMode!.Trim());
            insertCmd.Parameters.AddWithValue("q1", request.TeachBackQ1Passed);
            insertCmd.Parameters.AddWithValue("q2", request.TeachBackQ2Passed);
            insertCmd.Parameters.AddWithValue("q3", request.TeachBackQ3Passed);
            NpgsqlParameter agentParam = insertCmd.Parameters.Add("agentId", NpgsqlDbType.Uuid);
            agentParam.Value = effectiveAgentId.HasValue ? effectiveAgentId.Value : DBNull.Value;
            insertCmd.Parameters.AddWithValue("clientRecordedAt", request.ClientRecordedAt.ToUniversalTime());

            await using NpgsqlDataReader reader = await insertCmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            teachBackPassed = reader.GetBoolean(0);
            scheduleRef = reader.GetString(1);
            serverReceivedAt = reader.GetFieldValue<DateTimeOffset>(2);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "identity.attestation_recorded",
                "registration_attestation",
                attestationId,
                callerId,
                JsonSerializer.Serialize(new
                {
                    userId = identityId,
                    summaryVersion = request.SummaryVersion.Trim(),
                    languageCode = request.LanguageCode!.Trim(),
                    deliveryMode = request.DeliveryMode!.Trim(),
                    teachBackPassed
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        RegistrationAttestationDto dto = new(
            attestationId,
            identityId,
            request.SummaryVersion.Trim(),
            string.IsNullOrWhiteSpace(request.PilotConsentVersion) ? null : request.PilotConsentVersion.Trim(),
            request.LanguageCode!.Trim(),
            request.DeliveryMode!.Trim(),
            teachBackPassed,
            effectiveAgentId,
            scheduleRef,
            request.ClientRecordedAt.ToUniversalTime(),
            serverReceivedAt);

        return Results.Json(
            new ApiResponse<RegistrationAttestationDto>(dto, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAttestationsAsync(
        Guid identityId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);

        if (callerId != identityId)
        {
            bool isAuditor = await adminPermissionChecker.HasPermissionAsync(callerId.ToString(), "audit:search", cancellationToken);
            bool isAgentForUser;
            await using (NpgsqlCommand roleCmd = conn.CreateCommand())
            {
                roleCmd.CommandText = """
                    SELECT EXISTS (
                        SELECT 1 FROM identity.role_profile
                        WHERE identity_id = @callerId AND role_type = 'agent'
                    );
                    """;
                roleCmd.Parameters.AddWithValue("callerId", callerId);
                isAgentForUser = Convert.ToBoolean(await roleCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            if (!isAuditor && !isAgentForUser)
            {
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not have permission to view attestations for this identity.");
            }
        }

        await using NpgsqlCommand listCmd = conn.CreateCommand();
        listCmd.CommandText = """
            SELECT
                attestation_id, user_id, summary_version, pilot_consent_version,
                language_code, delivery_mode, teach_back_passed, agent_identity_id,
                lawful_basis_schedule_ref, client_recorded_at, server_received_at
            FROM identity.registration_attestation
            WHERE user_id = @userId
            ORDER BY server_received_at DESC;
            """;
        listCmd.Parameters.AddWithValue("userId", identityId);

        List<RegistrationAttestationDto> results = [];
        await using NpgsqlDataReader reader = await listCmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RegistrationAttestationDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<RegistrationAttestationDto>>(results, new ApiMeta(httpContext.TraceIdentifier)));
    }
}
