using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Sync;

public sealed record SyncSpikeRequest(
    Guid ClientGuid,
    string EntityType,
    string PayloadJson,
    DateTimeOffset ClientRecordedAt);

public sealed record SyncSpikeResult(
    Guid RecordId,
    Guid ClientGuid,
    DateTimeOffset ClientRecordedAt,
    DateTimeOffset ServerReceivedAt);

internal sealed class SyncSpikeSchemaInitializer : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public void Dispose() => _lock.Dispose();

    public async Task EnsureInitializedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using NpgsqlCommand cmd = dataSource.CreateCommand("""
                CREATE SCHEMA IF NOT EXISTS platform;

                CREATE TABLE IF NOT EXISTS platform.idempotency_keys (
                    id bigserial PRIMARY KEY,
                    idempotency_key uuid NOT NULL,
                    identity_id uuid NULL,
                    route text NOT NULL,
                    request_fingerprint char(64) NOT NULL,
                    response_status_code integer NULL,
                    response_body jsonb NULL,
                    locked_until timestamptz NOT NULL DEFAULT (now() + interval '30 seconds'),
                    expires_at timestamptz NOT NULL DEFAULT (now() + interval '24 hours'),
                    created_at timestamptz NOT NULL DEFAULT now()
                );

                CREATE UNIQUE INDEX IF NOT EXISTS uq_idempotency_key_per_identity_route
                    ON platform.idempotency_keys (idempotency_key, identity_id, route)
                    NULLS NOT DISTINCT;

                CREATE TABLE IF NOT EXISTS platform.sync_spike_records (
                    id uuid PRIMARY KEY,
                    client_guid uuid NOT NULL UNIQUE,
                    entity_type text NOT NULL,
                    payload_json jsonb NOT NULL,
                    client_recorded_at timestamptz NOT NULL,
                    server_received_at timestamptz NOT NULL DEFAULT now()
                );
                """);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal static class SyncSpikeEndpoint
{
    private const string RouteTemplate = "POST /v1/sync/spike-records";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapSyncSpikeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/sync/spike-records", HandlePostAsync);
    }

    private static async Task<IResult> HandlePostAsync(
        HttpContext httpContext,
        SyncSpikeRequest request,
        DatabaseConnectionSource dbSource,
        SyncSpikeSchemaInitializer schemaInitializer,
        ICurrentIdentityAccessor identityAccessor,
        CancellationToken cancellationToken)
    {
        string? rawKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey) || !Guid.TryParse(rawKey, out Guid idempotencyKey) || idempotencyKey == Guid.Empty)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("IDEMPOTENCY_KEY_REQUIRED", "A valid UUID Idempotency-Key header is required."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ClientGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("VALIDATION_ERROR", "ClientGuid, EntityType, and PayloadJson are required."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(request.PayloadJson);
        }
        catch (JsonException)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("INVALID_JSON", "PayloadJson must contain valid JSON."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("SERVICE_UNAVAILABLE", "Database connection is not configured."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await schemaInitializer.EnsureInitializedAsync(dataSource, cancellationToken);

        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
        Guid? identityGuid = identity is not null && Guid.TryParse(identity.Subject, out Guid parsedSub)
            ? parsedSub
            : null;

        string fingerprint = ComputeFingerprint(request);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        long? lockedRowId = null;
        await using (NpgsqlCommand claimCmd = conn.CreateCommand())
        {
            claimCmd.Transaction = tx;
            claimCmd.CommandText = """
                INSERT INTO platform.idempotency_keys (idempotency_key, identity_id, route, request_fingerprint)
                VALUES (@key, @identityId, @route, @fp)
                ON CONFLICT (idempotency_key, identity_id, route) DO NOTHING
                RETURNING id;
                """;
            claimCmd.Parameters.AddWithValue("key", idempotencyKey);
            NpgsqlParameter idParam = claimCmd.Parameters.Add("identityId", NpgsqlDbType.Uuid);
            idParam.Value = identityGuid.HasValue ? identityGuid.Value : DBNull.Value;
            claimCmd.Parameters.AddWithValue("route", RouteTemplate);
            claimCmd.Parameters.AddWithValue("fp", fingerprint);

            object? insertedId = await claimCmd.ExecuteScalarAsync(cancellationToken);
            if (insertedId is long rowId)
            {
                lockedRowId = rowId;
            }
        }

        if (lockedRowId.HasValue)
        {
            Guid recordId = Guid.NewGuid();
            DateTimeOffset serverReceivedAt;

            await using (NpgsqlCommand insertRecordCmd = conn.CreateCommand())
            {
                insertRecordCmd.Transaction = tx;
                insertRecordCmd.CommandText = """
                    INSERT INTO platform.sync_spike_records (id, client_guid, entity_type, payload_json, client_recorded_at, server_received_at)
                    VALUES (@id, @clientGuid, @entityType, CAST(@payloadJson AS jsonb), @clientRecordedAt, now())
                    ON CONFLICT (client_guid) DO NOTHING
                    RETURNING server_received_at;
                    """;
                insertRecordCmd.Parameters.AddWithValue("id", recordId);
                insertRecordCmd.Parameters.AddWithValue("clientGuid", request.ClientGuid);
                insertRecordCmd.Parameters.AddWithValue("entityType", request.EntityType.Trim());
                insertRecordCmd.Parameters.AddWithValue("payloadJson", request.PayloadJson);
                insertRecordCmd.Parameters.AddWithValue("clientRecordedAt", request.ClientRecordedAt.ToUniversalTime());

                object? tsObj = await insertRecordCmd.ExecuteScalarAsync(cancellationToken);
                if (tsObj is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Results.Json(
                        new ApiErrorResponse(
                            new ApiErrorDetail("CLIENT_GUID_ALREADY_SYNCED", "This offline entity GUID was already synced under a different Idempotency-Key."),
                            new ApiMeta(httpContext.TraceIdentifier)),
                        statusCode: StatusCodes.Status409Conflict);
                }

                serverReceivedAt = tsObj switch
                {
                    DateTimeOffset dto => dto,
                    DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                    _ => DateTimeOffset.UtcNow
                };
            }

            ApiResponse<SyncSpikeResult> responseEnvelope = new(
                new SyncSpikeResult(recordId, request.ClientGuid, request.ClientRecordedAt.ToUniversalTime(), serverReceivedAt),
                new ApiMeta(httpContext.TraceIdentifier));
            string serializedBody = JsonSerializer.Serialize(responseEnvelope, JsonOptions);

            await using (NpgsqlCommand storeCmd = conn.CreateCommand())
            {
                storeCmd.Transaction = tx;
                storeCmd.CommandText = """
                    UPDATE platform.idempotency_keys
                    SET response_status_code = @status,
                        response_body = CAST(@body AS jsonb)
                    WHERE id = @rowId;
                    """;
                storeCmd.Parameters.AddWithValue("status", StatusCodes.Status201Created);
                storeCmd.Parameters.AddWithValue("body", serializedBody);
                storeCmd.Parameters.AddWithValue("rowId", lockedRowId.Value);
                await storeCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return Results.Text(serializedBody, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
        }

        await tx.RollbackAsync(cancellationToken);

        for (int attempt = 0; attempt < 60; attempt++)
        {
            await using NpgsqlCommand readCmd = conn.CreateCommand();
            readCmd.CommandText = """
                SELECT request_fingerprint, response_status_code, response_body::text
                FROM platform.idempotency_keys
                WHERE idempotency_key = @key
                  AND identity_id IS NOT DISTINCT FROM @identityId
                  AND route = @route;
                """;
            readCmd.Parameters.AddWithValue("key", idempotencyKey);
            NpgsqlParameter idParam = readCmd.Parameters.Add("identityId", NpgsqlDbType.Uuid);
            idParam.Value = identityGuid.HasValue ? identityGuid.Value : DBNull.Value;
            readCmd.Parameters.AddWithValue("route", RouteTemplate);

            await using NpgsqlDataReader reader = await readCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                string storedFingerprint = reader.GetString(0).Trim();
                int? storedStatus = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                string? storedBody = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (storedBody is not null && storedStatus.HasValue)
                {
                    if (!string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Results.Json(
                            new ApiErrorResponse(
                                new ApiErrorDetail("IDEMPOTENCY_KEY_REUSED", "Idempotency-Key cannot be reused with a different request payload."),
                                new ApiMeta(httpContext.TraceIdentifier)),
                            statusCode: StatusCodes.Status409Conflict);
                    }

                    return Results.Text(storedBody, "application/json", Encoding.UTF8, storedStatus.Value);
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        return Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("IDEMPOTENCY_IN_FLIGHT", "A concurrent request with the same Idempotency-Key did not complete in time."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status409Conflict);
    }

    private static string ComputeFingerprint(SyncSpikeRequest request)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            request.ClientGuid,
            entityType = request.EntityType.Trim(),
            payloadJson = request.PayloadJson,
            clientRecordedAtUtc = request.ClientRecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        }, JsonOptions);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }
}
