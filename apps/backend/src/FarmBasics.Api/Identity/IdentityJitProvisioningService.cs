using System.Security.Claims;
using System.Text.Json;
using FarmBasics.Api.Audit;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Identity;

public sealed record JitProvisioningOutcome(Guid IdentityId, bool Provisioned);

/// <summary>
/// Atomically JIT-provisions first-time authenticated Keycloak subjects into identity.identity
/// with a same-transaction audit entry (WP-11 / WP-13 / ADR-008).
/// </summary>
internal sealed class IdentityJitProvisioningService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<JitProvisioningOutcome> EnsureProvisionedAsync(
        NpgsqlDataSource dataSource,
        Guid identityId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        string firstName = user.FindFirst("given_name")?.Value?.Trim() ?? string.Empty;
        string lastName = user.FindFirst("family_name")?.Value?.Trim() ?? string.Empty;
        string phone = user.FindFirst("phone_number")?.Value?.Trim() ?? string.Empty;
        string? email = user.FindFirst("email")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            email = null;
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        bool created;
        bool emailConflictFallback = false;

        if (email is not null)
        {
            await ExecuteTransactionSqlAsync(conn, tx, "SAVEPOINT jit_email_insert;", cancellationToken);
            try
            {
                created = await TryInsertIdentityAsync(conn, tx, identityId, firstName, lastName, phone, email, cancellationToken);
                await ExecuteTransactionSqlAsync(conn, tx, "RELEASE SAVEPOINT jit_email_insert;", cancellationToken);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await ExecuteTransactionSqlAsync(conn, tx, "ROLLBACK TO SAVEPOINT jit_email_insert;", cancellationToken);
                await ExecuteTransactionSqlAsync(conn, tx, "RELEASE SAVEPOINT jit_email_insert;", cancellationToken);
                emailConflictFallback = true;
                created = await TryInsertIdentityAsync(conn, tx, identityId, firstName, lastName, phone, email: null, cancellationToken);
            }
        }
        else
        {
            created = await TryInsertIdentityAsync(conn, tx, identityId, firstName, lastName, phone, email: null, cancellationToken);
        }

        if (created)
        {
            string payloadJson = JsonSerializer.Serialize(new
            {
                source = "jwt_jit",
                hasEmail = email is not null && !emailConflictFallback,
                emailConflictFallback
            }, JsonOptions);

            await AuditWriter.WriteAsync(
                conn,
                tx,
                new AuditRecord(
                    Domain: "identity",
                    EventType: "identity.jit_provisioned",
                    EntityType: "identity",
                    EntityId: identityId,
                    ActorId: identityId,
                    PayloadJson: payloadJson),
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new JitProvisioningOutcome(identityId, created);
    }

    private static async Task<bool> TryInsertIdentityAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid identityId,
        string firstName,
        string lastName,
        string phone,
        string? email,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO identity.identity (id, first_name, last_name, phone_primary, email_primary, status)
            VALUES (@id, @firstName, @lastName, @phone, @email, 'active')
            ON CONFLICT (id) DO NOTHING
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("id", identityId);
        cmd.Parameters.AddWithValue("firstName", firstName);
        cmd.Parameters.AddWithValue("lastName", lastName);
        cmd.Parameters.AddWithValue("phone", phone);
        NpgsqlParameter emailParam = cmd.Parameters.Add("email", NpgsqlDbType.Text);
        emailParam.Value = email is not null ? email : DBNull.Value;

        object? inserted = await cmd.ExecuteScalarAsync(cancellationToken);
        return inserted is Guid;
    }

    private static async Task ExecuteTransactionSqlAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string sql,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
