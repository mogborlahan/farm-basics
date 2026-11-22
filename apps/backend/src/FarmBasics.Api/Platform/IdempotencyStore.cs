using System.Security.Cryptography;
using System.Text;
using FarmBasics.BuildingBlocks.Api;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Platform;

internal enum IdempotencyLookupOutcome
{
    Completed = 1,
    FingerprintMismatch = 2,
    InFlightTimeout = 3
}

internal sealed record IdempotencyLookupResult(
    IdempotencyLookupOutcome Outcome,
    int? StatusCode = null,
    string? ResponseBody = null);

/// <summary>
/// Shared PostgreSQL 15+ idempotency backing store implementing Doc/03-ENG §1.5 &amp; 02-ENG §3.6.
/// </summary>
internal static class IdempotencyStore
{
    public static bool TryValidateKeyHeader(HttpRequest request, out Guid idempotencyKey)
    {
        string? rawKey = request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey)
            || !Guid.TryParse(rawKey, out idempotencyKey)
            || idempotencyKey == Guid.Empty)
        {
            idempotencyKey = Guid.Empty;
            return false;
        }

        return true;
    }

    public static IResult MissingKeyResult(HttpContext httpContext)
        => Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("IDEMPOTENCY_KEY_REQUIRED", "A valid UUID Idempotency-Key header is required."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status400BadRequest);

    public static IResult KeyReusedResult(HttpContext httpContext)
        => Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("IDEMPOTENCY_KEY_REUSED", "Idempotency-Key cannot be reused with a different request payload."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status409Conflict);

    public static IResult InFlightTimeoutResult(HttpContext httpContext)
        => Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("IDEMPOTENCY_IN_FLIGHT", "A concurrent request with the same Idempotency-Key did not complete in time."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status409Conflict);

    public static string ComputeFingerprint(string canonicalPayload)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<long?> TryClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid idempotencyKey,
        Guid? identityId,
        string routeTemplate,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO platform.idempotency_keys (idempotency_key, identity_id, route, request_fingerprint)
            VALUES (@key, @identityId, @route, @fp)
            ON CONFLICT (idempotency_key, identity_id, route) DO NOTHING
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        NpgsqlParameter idParam = cmd.Parameters.Add("identityId", NpgsqlDbType.Uuid);
        idParam.Value = identityId.HasValue ? identityId.Value : DBNull.Value;
        cmd.Parameters.AddWithValue("route", routeTemplate);
        cmd.Parameters.AddWithValue("fp", fingerprint);

        object? inserted = await cmd.ExecuteScalarAsync(cancellationToken);
        return inserted is long rowId ? rowId : null;
    }

    public static async Task StoreResponseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        long lockedRowId,
        int statusCode,
        string serializedJsonBody,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE platform.idempotency_keys
            SET response_status_code = @status,
                response_body = CAST(@body AS jsonb)
            WHERE id = @rowId;
            """;
        cmd.Parameters.AddWithValue("status", statusCode);
        cmd.Parameters.AddWithValue("body", serializedJsonBody);
        cmd.Parameters.AddWithValue("rowId", lockedRowId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ReleaseUnfinishedLockAsync(
        NpgsqlDataSource dataSource,
        long lockedRowId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM platform.idempotency_keys
            WHERE id = @rowId AND response_status_code IS NULL;
            """;
        cmd.Parameters.AddWithValue("rowId", lockedRowId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<IdempotencyLookupResult> WaitForStoredResponseAsync(
        NpgsqlConnection connection,
        Guid idempotencyKey,
        Guid? identityId,
        string routeTemplate,
        string expectedFingerprint,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            await using NpgsqlCommand readCmd = connection.CreateCommand();
            readCmd.CommandText = """
                SELECT request_fingerprint, response_status_code, response_body::text
                FROM platform.idempotency_keys
                WHERE idempotency_key = @key
                  AND identity_id IS NOT DISTINCT FROM @identityId
                  AND route = @route;
                """;
            readCmd.Parameters.AddWithValue("key", idempotencyKey);
            NpgsqlParameter idParam = readCmd.Parameters.Add("identityId", NpgsqlDbType.Uuid);
            idParam.Value = identityId.HasValue ? identityId.Value : DBNull.Value;
            readCmd.Parameters.AddWithValue("route", routeTemplate);

            await using NpgsqlDataReader reader = await readCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                string storedFingerprint = reader.GetString(0).Trim();
                int? storedStatus = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                string? storedBody = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (storedBody is not null && storedStatus.HasValue)
                {
                    if (!string.Equals(storedFingerprint, expectedFingerprint, StringComparison.Ordinal))
                    {
                        return new IdempotencyLookupResult(IdempotencyLookupOutcome.FingerprintMismatch);
                    }

                    return new IdempotencyLookupResult(
                        IdempotencyLookupOutcome.Completed,
                        storedStatus.Value,
                        storedBody);
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        return new IdempotencyLookupResult(IdempotencyLookupOutcome.InFlightTimeout);
    }

    /// <summary>
    /// Bounded 5,000-row expiry purge preserving a 72h grace window per Doc/02-ENG §3.6.
    /// </summary>
    public static async Task<int> PurgeExpiredAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using NpgsqlCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM platform.idempotency_keys
            WHERE id IN (
                SELECT id FROM platform.idempotency_keys
                WHERE expires_at < now() - interval '72 hours'
                ORDER BY expires_at
                LIMIT 5000
            );
            """;
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
