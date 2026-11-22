using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Reviews;
using FarmBasics.BuildingBlocks.Api;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Registration;

public sealed record SelfServiceRegistrationRequest(
    string Nin,
    string Phone,
    string? Email = null,
    Guid? StateId = null,
    Guid? LgaId = null,
    Guid? WardId = null,
    Guid? CommunityId = null);

public sealed record SelfServiceRegistrationResponse(
    Guid IdentityId,
    string Status,
    string VerificationStep,
    bool Resumed,
    bool SmsDispatched,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth);

public sealed record VerifyOtpRequest(string Otp, string Channel = "phone");

public sealed record VerifyOtpResponse(Guid IdentityId, string Status, string FarmerId);

public sealed record ResendOtpRequest(string Channel = "phone");

public sealed record ResendOtpResponse(Guid IdentityId, string VerificationStep, bool SmsDispatched);

internal static partial class SelfServiceRegistrationEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^(?:\+234|234|0)([789][01]\d{8})$")]
    private static partial Regex NigerianPhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex SimpleEmailRegex();

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigitOtpRegex();

    public static void MapSelfServiceRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/registrations/self-service", HandleRegisterAsync);

        app.MapPost("/v1/registrations/self-service/{identityId:guid}/verify-otp", HandleVerifyOtpAsync)
            .RequireRateLimiting(RateLimitPolicies.OtpVerify);

        app.MapPost("/v1/registrations/self-service/{identityId:guid}/resend-otp", HandleResendOtpAsync)
            .RequireRateLimiting(RateLimitPolicies.OtpVerify);
    }

    private static async Task<IResult> HandleRegisterAsync(
        SelfServiceRegistrationRequest request,
        HttpContext httpContext,
        INinVerificationClient ninClient,
        ISmsGatewayClient smsClient,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!NinVerificationHashHelper.IsValidNinFormat(request.Nin))
        {
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "NIN must be exactly 11 digits.");
        }

        if (!TryNormalizeNigerianPhone(request.Phone, out string normalizedPhone))
        {
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Phone number must be a valid Nigerian mobile number.");
        }

        string? normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (normalizedEmail is not null && !SimpleEmailRegex().IsMatch(normalizedEmail))
        {
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Email address format is invalid.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Registration service database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        NinLookupResult lookup = await ninClient.VerifyNinAsync(request.Nin, cancellationToken);
        switch (lookup.Status)
        {
            case NinLookupStatus.NotFound:
                return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "NIN_NOT_FOUND", "No national identity record matched the provided details. Please check and retry.");

            case NinLookupStatus.ProviderUnavailable:
            case NinLookupStatus.MalformedResponse:
                return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "NIN_PROVIDER_UNAVAILABLE", "The identity verification service is temporarily unavailable. Your form details are preserved for retry.");

            case NinLookupStatus.FuzzyMatch:
                {
                    Guid reviewQueueItemId;
                    Guid pendingIdentityId = Guid.NewGuid();
                    await using NpgsqlConnection fuzzyConn = await dataSource.OpenConnectionAsync(cancellationToken);
                    await using NpgsqlTransaction fuzzyTx = await fuzzyConn.BeginTransactionAsync(cancellationToken);

                    Guid? insertedFuzzyId = await TryInsertPendingIdentityAsync(
                        fuzzyConn,
                        fuzzyTx,
                        pendingIdentityId,
                        lookup,
                        normalizedPhone,
                        normalizedEmail,
                        request,
                        cancellationToken);
                    if (!insertedFuzzyId.HasValue && !string.IsNullOrWhiteSpace(lookup.VerificationRef))
                    {
                        ExistingIdentityState? existingFuzzy = await FindByNinVerificationRefAsync(fuzzyConn, fuzzyTx, lookup.VerificationRef, cancellationToken);
                        if (existingFuzzy is not null)
                        {
                            pendingIdentityId = existingFuzzy.Id;
                        }
                    }

                    reviewQueueItemId = await ReviewEngineEndpoints.EnqueueReviewItemAsync(
                        fuzzyConn,
                        fuzzyTx,
                        "IDENTITY_NIN_MISMATCH",
                        "identity",
                        pendingIdentityId,
                        submittedByIdentityId: pendingIdentityId,
                        priority: "normal",
                        cancellationToken);

                    string auditPayload = JsonSerializer.Serialize(new
                    {
                        reviewCategory = "IDENTITY_NIN_MISMATCH",
                        identityId = pendingIdentityId,
                        confidenceScore = lookup.ConfidenceScore
                    }, JsonOptions);

                    await AuditWriter.WriteAsync(
                        fuzzyConn,
                        fuzzyTx,
                        new AuditRecord(
                            Domain: "identity",
                            EventType: "identity.nin_mismatch_queued",
                            EntityType: "review_queue_item",
                            EntityId: reviewQueueItemId,
                            ActorId: null,
                            PayloadJson: auditPayload),
                        cancellationToken);
                    await fuzzyTx.CommitAsync(cancellationToken);

                    return Results.Json(
                        new ApiResponse<object>(
                            new
                            {
                                status = "under_review",
                                code = "NIN_LOOKUP_UNDER_REVIEW",
                                identityId = pendingIdentityId,
                                reviewQueueItemId,
                                message = "Your registration is under review and is typically resolved within 48 hours."
                            },
                            new ApiMeta(httpContext.TraceIdentifier)),
                        statusCode: StatusCodes.Status202Accepted);
                }

            case NinLookupStatus.Matched:
                break;

            default:
                return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "NIN_PROVIDER_UNAVAILABLE", "Identity verification could not be completed.");
        }

        if (string.IsNullOrWhiteSpace(lookup.VerificationRef)
            || string.IsNullOrWhiteSpace(lookup.FirstName)
            || string.IsNullOrWhiteSpace(lookup.LastName)
            || !lookup.DateOfBirth.HasValue)
        {
            return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "NIN_PROVIDER_UNAVAILABLE", "Identity verification returned incomplete profile attributes.");
        }

        Guid candidateId = Guid.NewGuid();
        Guid effectiveIdentityId;
        bool resumed = false;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid? insertedId = null;
        bool emailConflict = false;

        await ExecuteTxSqlAsync(conn, tx, "SAVEPOINT sp_register_identity;", cancellationToken);
        try
        {
            insertedId = await TryInsertPendingIdentityAsync(
                conn,
                tx,
                candidateId,
                lookup,
                normalizedPhone,
                normalizedEmail,
                request,
                cancellationToken);
            await ExecuteTxSqlAsync(conn, tx, "RELEASE SAVEPOINT sp_register_identity;", cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await ExecuteTxSqlAsync(conn, tx, "ROLLBACK TO SAVEPOINT sp_register_identity;", cancellationToken);
            await ExecuteTxSqlAsync(conn, tx, "RELEASE SAVEPOINT sp_register_identity;", cancellationToken);
            emailConflict = true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Selected geographic location reference is invalid.");
        }

        if (insertedId.HasValue)
        {
            effectiveIdentityId = insertedId.Value;
        }
        else
        {
            ExistingIdentityState? existing = await FindByNinVerificationRefAsync(conn, tx, lookup.VerificationRef, cancellationToken);
            if (existing is null)
            {
                await tx.RollbackAsync(cancellationToken);
                return ErrorResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    emailConflict ? "CONTACT_ALREADY_REGISTERED" : "NIN_ALREADY_REGISTERED",
                    "Unable to complete registration with the provided details.");
            }

            if (!string.Equals(existing.Status, "pending_verification", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return ErrorResult(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "NIN_ALREADY_REGISTERED",
                    "This identity is already registered.");
            }

            effectiveIdentityId = existing.Id;
            resumed = true;
        }

        string otpPlaintext = GenerateSixDigitOtp();
        string otpHash = ComputeOtpHash(effectiveIdentityId, otpPlaintext);
        await InsertOtpChallengeAsync(conn, tx, effectiveIdentityId, "phone", otpHash, cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "identity",
                EventType: resumed ? "identity.self_service_resumed" : "identity.self_service_registered",
                EntityType: "identity",
                EntityId: effectiveIdentityId,
                ActorId: effectiveIdentityId,
                PayloadJson: JsonSerializer.Serialize(new { channel = "self_service", resumed }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        SmsSendResult smsResult = await smsClient.SendSmsAsync(
            new SmsSendRequest(normalizedPhone, $"Your Farm Basics verification code is {otpPlaintext}. Valid for 5 minutes."),
            cancellationToken);

        SelfServiceRegistrationResponse payload = new(
            effectiveIdentityId,
            "pending_verification",
            "phone_otp",
            resumed,
            smsResult.Succeeded,
            lookup.FirstName,
            lookup.LastName,
            lookup.DateOfBirth);

        return Results.Json(
            new ApiResponse<SelfServiceRegistrationResponse>(payload, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleVerifyOtpAsync(
        Guid identityId,
        VerifyOtpRequest request,
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (identityId == Guid.Empty || string.IsNullOrWhiteSpace(request.Otp) || !SixDigitOtpRegex().IsMatch(request.Otp.Trim()))
        {
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "Enter the 6-digit verification code.");
        }

        string channel = string.Equals(request.Channel, "email", StringComparison.OrdinalIgnoreCase) ? "email" : "phone";

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Verification database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        OtpChallengeRow? challenge = await LoadLatestOpenChallengeForUpdateAsync(conn, tx, identityId, channel, cancellationToken);
        if (challenge is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "No active verification challenge was found.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (challenge.LockedUntil.HasValue && challenge.LockedUntil.Value > nowUtc)
        {
            int retryAfter = Math.Max(1, (int)Math.Ceiling((challenge.LockedUntil.Value - nowUtc).TotalSeconds));
            await tx.RollbackAsync(cancellationToken);
            return LockedResult(httpContext, retryAfter);
        }

        if (challenge.ExpiresAt <= nowUtc)
        {
            await tx.RollbackAsync(cancellationToken);
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "OTP_EXPIRED", "Verification code expired. Request a new code.");
        }

        string candidateHash = ComputeOtpHash(identityId, request.Otp.Trim());
        bool matched = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidateHash),
            Encoding.ASCII.GetBytes(challenge.OtpHash));

        if (!matched)
        {
            int nextAttempts = challenge.AttemptCount + 1;
            int lockoutSeconds = 0;
            if (nextAttempts >= 5)
            {
                int power = Math.Min(nextAttempts - 5, 10);
                lockoutSeconds = Math.Min(900, 60 * (1 << power));
            }

            await RecordFailedOtpAttemptAsync(conn, tx, challenge.Id, nextAttempts, lockoutSeconds, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            if (lockoutSeconds > 0)
            {
                return LockedResult(httpContext, lockoutSeconds);
            }

            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail(
                        "INVALID_OTP",
                        "The verification code entered is incorrect.",
                        new { attemptCount = nextAttempts, attemptsRemaining = 5 - nextAttempts }),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        await MarkChallengeVerifiedAsync(conn, tx, challenge.Id, cancellationToken);
        string farmerId = await ActivateIdentityAndCreateFarmerProfileAsync(conn, tx, identityId, cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "identity",
                EventType: "identity.activated",
                EntityType: "identity",
                EntityId: identityId,
                ActorId: identityId,
                PayloadJson: JsonSerializer.Serialize(new { channel, farmerId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<VerifyOtpResponse>(
            new VerifyOtpResponse(identityId, "active", farmerId),
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleResendOtpAsync(
        Guid identityId,
        ResendOtpRequest request,
        HttpContext httpContext,
        ISmsGatewayClient smsClient,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (identityId == Guid.Empty)
        {
            return ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Identity identifier is invalid.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        string channel = string.Equals(request.Channel, "email", StringComparison.OrdinalIgnoreCase) ? "email" : "phone";

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        (string? status, string? phone) = await LoadIdentityContactForResendAsync(conn, tx, identityId, cancellationToken);
        if (status is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Registration record was not found.");
        }

        if (!string.Equals(status, "pending_verification", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(phone))
        {
            await tx.RollbackAsync(cancellationToken);
            return ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Identity does not require pending verification.");
        }

        OtpChallengeRow? existing = await LoadLatestOpenChallengeForUpdateAsync(conn, tx, identityId, channel, cancellationToken);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (existing?.LockedUntil is DateTimeOffset lockedUntil && lockedUntil > nowUtc)
        {
            int retryAfter = Math.Max(1, (int)Math.Ceiling((lockedUntil - nowUtc).TotalSeconds));
            await tx.RollbackAsync(cancellationToken);
            return LockedResult(httpContext, retryAfter);
        }

        string otpPlaintext = GenerateSixDigitOtp();
        string otpHash = ComputeOtpHash(identityId, otpPlaintext);
        await InsertOtpChallengeAsync(conn, tx, identityId, channel, otpHash, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        SmsSendResult smsResult = await smsClient.SendSmsAsync(
            new SmsSendRequest(phone, $"Your Farm Basics verification code is {otpPlaintext}. Valid for 5 minutes."),
            cancellationToken);

        return Results.Ok(new ApiResponse<ResendOtpResponse>(
            new ResendOtpResponse(identityId, "phone_otp", smsResult.Succeeded),
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    internal static bool TryNormalizeNigerianPhone(string? rawPhone, out string e164Phone)
    {
        e164Phone = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return false;
        }

        string cleaned = rawPhone.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        Match match = NigerianPhoneRegex().Match(cleaned);
        if (!match.Success)
        {
            return false;
        }

        e164Phone = $"+234{match.Groups[1].Value}";
        return true;
    }

    internal static string GenerateSixDigitOtp()
    {
        int code = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    internal static string ComputeOtpHash(Guid identityId, string otp)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{identityId:N}:{otp}");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static bool IsSixDigitOtp(string? otp)
        => !string.IsNullOrWhiteSpace(otp) && SixDigitOtpRegex().IsMatch(otp.Trim());

    internal static IResult ErrorResult(HttpContext context, int statusCode, string code, string message)
        => Results.Json(
            new ApiErrorResponse(new ApiErrorDetail(code, message), new ApiMeta(context.TraceIdentifier)),
            statusCode: statusCode);

    internal static IResult LockedResult(HttpContext context, int retryAfterSeconds)
        => Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail(
                    "OTP_LOCKED",
                    "Too many incorrect verification attempts. Please wait before trying again.",
                    new { retryAfterSeconds }),
                new ApiMeta(context.TraceIdentifier)),
            statusCode: StatusCodes.Status429TooManyRequests);

    internal static async Task ExecuteTxSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static Task<Guid?> TryInsertPendingIdentityAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid candidateId,
        NinLookupResult lookup,
        string normalizedPhone,
        string? normalizedEmail,
        SelfServiceRegistrationRequest request,
        CancellationToken cancellationToken)
        => TryInsertPendingIdentityAsync(
            conn,
            tx,
            candidateId,
            lookup,
            normalizedPhone,
            normalizedEmail,
            request.StateId,
            request.LgaId,
            request.WardId,
            request.CommunityId,
            cancellationToken);

    internal static async Task<Guid?> TryInsertPendingIdentityAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid candidateId,
        NinLookupResult lookup,
        string normalizedPhone,
        string? normalizedEmail,
        Guid? stateId,
        Guid? lgaId,
        Guid? wardId,
        Guid? communityId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO identity.identity (
                id, nin_verification_ref, first_name, last_name, date_of_birth,
                phone_primary, email_primary, state_id, lga_id, ward_id, community_id, status
            )
            VALUES (
                @id, @ninRef, @firstName, @lastName, @dob,
                @phone, @email, @stateId, @lgaId, @wardId, @communityId, 'pending_verification'
            )
            ON CONFLICT (nin_verification_ref) DO NOTHING
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("id", candidateId);
        cmd.Parameters.AddWithValue("ninRef", lookup.VerificationRef!);
        cmd.Parameters.AddWithValue("firstName", lookup.FirstName!);
        cmd.Parameters.AddWithValue("lastName", lookup.LastName!);
        cmd.Parameters.AddWithValue("dob", lookup.DateOfBirth!.Value);
        cmd.Parameters.AddWithValue("phone", normalizedPhone);

        NpgsqlParameter emailParam = cmd.Parameters.Add("email", NpgsqlDbType.Text);
        emailParam.Value = normalizedEmail is not null ? normalizedEmail : DBNull.Value;

        AddNullableUuid(cmd, "stateId", stateId);
        AddNullableUuid(cmd, "lgaId", lgaId);
        AddNullableUuid(cmd, "wardId", wardId);
        AddNullableUuid(cmd, "communityId", communityId);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is Guid g ? g : null;
    }

    private static void AddNullableUuid(NpgsqlCommand cmd, string name, Guid? value)
    {
        NpgsqlParameter param = cmd.Parameters.Add(name, NpgsqlDbType.Uuid);
        param.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    internal sealed record ExistingIdentityState(Guid Id, string Status);

    internal static async Task<ExistingIdentityState?> FindByNinVerificationRefAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string ninVerificationRef,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, status::text
            FROM identity.identity
            WHERE nin_verification_ref = @ninRef;
            """;
        cmd.Parameters.AddWithValue("ninRef", ninVerificationRef);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingIdentityState(reader.GetGuid(0), reader.GetString(1));
    }

    internal static async Task InsertOtpChallengeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid identityId,
        string channel,
        string otpHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO identity.registration_otp_challenge (identity_id, channel, otp_hash, expires_at)
            VALUES (@identityId, @channel, @otpHash, now() + interval '5 minutes');
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("channel", channel);
        cmd.Parameters.AddWithValue("otpHash", otpHash);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal sealed record OtpChallengeRow(
        Guid Id,
        string OtpHash,
        int AttemptCount,
        DateTimeOffset? LockedUntil,
        DateTimeOffset ExpiresAt);

    internal static async Task<OtpChallengeRow?> LoadLatestOpenChallengeForUpdateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid identityId,
        string channel,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, otp_hash, attempt_count, locked_until, expires_at
            FROM identity.registration_otp_challenge
            WHERE identity_id = @identityId
              AND channel = @channel
              AND verified_at IS NULL
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE;
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("channel", channel);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OtpChallengeRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    internal static async Task RecordFailedOtpAttemptAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid challengeId,
        int nextAttempts,
        int lockoutSeconds,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE identity.registration_otp_challenge
            SET attempt_count = @attemptCount,
                locked_until = CASE
                    WHEN @lockoutSeconds > 0 THEN now() + make_interval(secs => @lockoutSeconds)
                    ELSE locked_until
                END
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", challengeId);
        cmd.Parameters.AddWithValue("attemptCount", nextAttempts);
        cmd.Parameters.AddWithValue("lockoutSeconds", lockoutSeconds);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task MarkChallengeVerifiedAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE identity.registration_otp_challenge SET verified_at = now() WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", challengeId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<string> ActivateIdentityAndCreateFarmerProfileAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        long sequenceNumber;
        await using (NpgsqlCommand seqCmd = conn.CreateCommand())
        {
            seqCmd.Transaction = tx;
            seqCmd.CommandText = "SELECT nextval('identity.farmer_id_seq');";
            sequenceNumber = Convert.ToInt64(await seqCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        string farmerId = $"FB-NG-{sequenceNumber.ToString("D6", CultureInfo.InvariantCulture)}";

        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE identity.identity
                SET status = 'active',
                    phone_verified_at = COALESCE(phone_verified_at, now()),
                    updated_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", identityId);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand roleCmd = conn.CreateCommand())
        {
            roleCmd.Transaction = tx;
            roleCmd.CommandText = """
                INSERT INTO identity.role_profile (identity_id, role_type, role_specific_id, linked_organization_id)
                VALUES (@identityId, 'farmer', @farmerId, NULL)
                ON CONFLICT (identity_id, role_type, linked_organization_id)
                DO UPDATE SET role_specific_id = COALESCE(identity.role_profile.role_specific_id, EXCLUDED.role_specific_id);
                """;
            roleCmd.Parameters.AddWithValue("identityId", identityId);
            roleCmd.Parameters.AddWithValue("farmerId", farmerId);
            await roleCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand trustCmd = conn.CreateCommand())
        {
            trustCmd.Transaction = tx;
            trustCmd.CommandText = """
                INSERT INTO identity.trust_profile (identity_id, organization_id, verification_tier, updated_at)
                VALUES (@identityId, NULL, 'verified', now())
                ON CONFLICT (identity_id) DO UPDATE
                SET verification_tier = 'verified',
                    updated_at = now();
                """;
            trustCmd.Parameters.AddWithValue("identityId", identityId);
            await trustCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return farmerId;
    }

    private static async Task<(string? Status, string? Phone)> LoadIdentityContactForResendAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid identityId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status::text, phone_primary FROM identity.identity WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", identityId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (null, null);
        }

        return (reader.GetString(0), reader.GetString(1));
    }
}
