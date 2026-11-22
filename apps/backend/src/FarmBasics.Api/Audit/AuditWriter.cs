using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Audit;

public sealed record AuditRecord(
    string Domain,
    string EventType,
    string EntityType,
    Guid EntityId,
    Guid? ActorId = null,
    string? PayloadJson = null);

/// <summary>
/// Writes append-only audit records inside the caller's active database transaction (01A-ENG §6.2, Rule 7).
/// </summary>
public static class AuditWriter
{
    public static async Task<Guid> WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(record);

        await AuditPartitionManager.EnsureMonthlyPartitionsAsync(connection, DateTimeOffset.UtcNow, monthsAhead: 1, cancellationToken);

        Guid auditId = Guid.NewGuid();
        await using NpgsqlCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO audit.audit_log_entry (id, domain, event_type, entity_type, entity_id, actor_id, payload, created_at)
            VALUES (@id, @domain, @eventType, @entityType, @entityId, @actorId, CAST(@payload AS jsonb), now());
            """;
        cmd.Parameters.AddWithValue("id", auditId);
        cmd.Parameters.AddWithValue("domain", record.Domain.Trim());
        cmd.Parameters.AddWithValue("eventType", record.EventType.Trim());
        cmd.Parameters.AddWithValue("entityType", record.EntityType.Trim());
        cmd.Parameters.AddWithValue("entityId", record.EntityId);

        NpgsqlParameter actorParam = cmd.Parameters.Add("actorId", NpgsqlDbType.Uuid);
        actorParam.Value = record.ActorId.HasValue ? record.ActorId.Value : DBNull.Value;

        NpgsqlParameter payloadParam = cmd.Parameters.Add("payload", NpgsqlDbType.Text);
        payloadParam.Value = record.PayloadJson is not null ? record.PayloadJson : DBNull.Value;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return auditId;
    }
}
