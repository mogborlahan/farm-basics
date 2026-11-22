using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Accounts;

internal sealed record OutboxProcessSummary(int Claimed, int Posted, int Deduplicated, int DeadLettered);

/// <summary>
/// Crash-safe transactional outbox worker enforcing strictly-once ledger posting via
/// SELECT ... FOR UPDATE SKIP LOCKED and accounts.posted_outbox_event (02-ENG §3.6 / WP-81).
/// </summary>
internal static partial class OutboxLedgerProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^journal_entry_\d{4}_\d{2}$")]
    private static partial Regex PartitionNameRegex();

    public static async Task<Guid> EnqueueLedgerEventAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        LedgerEventPayload payload,
        CancellationToken cancellationToken)
    {
        Guid outboxId = Guid.NewGuid();
        string payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO platform.events_outbox (id, aggregate_type, aggregate_id, event_type, payload, channel, status)
            VALUES (@id, @aggType, @aggId, @evType, CAST(@payload AS jsonb), 'queue_consumer', 'pending');
            """;
        cmd.Parameters.AddWithValue("id", outboxId);
        cmd.Parameters.AddWithValue("aggType", aggregateType);
        cmd.Parameters.AddWithValue("aggId", aggregateId);
        cmd.Parameters.AddWithValue("evType", eventType);
        cmd.Parameters.AddWithValue("payload", payloadJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return outboxId;
    }

    public static async Task EnsureJournalPartitionAsync(
        NpgsqlDataSource dataSource,
        DateOnly entryDate,
        CancellationToken cancellationToken)
    {
        DateOnly monthStart = new(entryDate.Year, entryDate.Month, 1);
        DateOnly monthEnd = monthStart.AddMonths(1);
        string partitionName = $"journal_entry_{monthStart.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";

        if (!PartitionNameRegex().IsMatch(partitionName))
        {
            throw new InvalidOperationException($"Computed journal partition name '{partitionName}' is invalid.");
        }

        string fromLit = monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string toLit = monthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);

        await using (NpgsqlCommand checkRegCmd = conn.CreateCommand())
        {
            checkRegCmd.CommandText = $"SELECT to_regclass('accounts.{partitionName}') IS NOT NULL;";
            if (Convert.ToBoolean(await checkRegCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture))
            {
                return;
            }
        }

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS accounts.{partitionName} PARTITION OF accounts.journal_entry
            FOR VALUES FROM ('{fromLit}') TO ('{toLit}');
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<OutboxProcessSummary> ProcessPendingBatchAsync(
        NpgsqlDataSource dataSource,
        int maxBatch = 50,
        CancellationToken cancellationToken = default)
    {
        int claimed = 0;
        int posted = 0;
        int deduplicated = 0;
        int deadLettered = 0;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        List<PendingOutboxRow> rows = [];
        await using (NpgsqlCommand claimCmd = conn.CreateCommand())
        {
            claimCmd.Transaction = tx;
            claimCmd.CommandText = """
                SELECT id, aggregate_type, aggregate_id, event_type, payload::text
                FROM platform.events_outbox
                WHERE status = 'pending'
                  AND available_at <= now()
                ORDER BY created_at ASC
                LIMIT @limit
                FOR UPDATE SKIP LOCKED;
                """;
            claimCmd.Parameters.AddWithValue("limit", maxBatch);

            await using NpgsqlDataReader reader = await claimCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PendingOutboxRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        claimed = rows.Count;
        if (claimed == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return new OutboxProcessSummary(0, 0, 0, 0);
        }

        Dictionary<string, Guid> accountCodeMap = await LoadAccountCodeMapAsync(conn, tx, cancellationToken);

        foreach (PendingOutboxRow row in rows)
        {
            bool alreadyPosted;
            await using (NpgsqlCommand dupCmd = conn.CreateCommand())
            {
                dupCmd.Transaction = tx;
                dupCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM accounts.posted_outbox_event WHERE outbox_event_id = @id);";
                dupCmd.Parameters.AddWithValue("id", row.Id);
                alreadyPosted = Convert.ToBoolean(await dupCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            if (alreadyPosted)
            {
                await MarkOutboxDispatchedAsync(conn, tx, row.Id, cancellationToken);
                deduplicated++;
                continue;
            }

            LedgerEventPayload? payload = JsonSerializer.Deserialize<LedgerEventPayload>(row.PayloadJson, JsonOptions);
            string? ruleErr = null;
            if (payload is null || !PostingRuleCatalog.TryBuildJournalLines(row.EventType, payload, out List<PlannedJournalLine> plannedLines, out ruleErr))
            {
                await MarkOutboxDeadLetterAsync(conn, tx, row.Id, ruleErr ?? "INVALID_OUTBOX_PAYLOAD", cancellationToken);
                deadLettered++;
                continue;
            }

            bool inClosedPeriod = await IsDateInClosedPeriodAsync(conn, tx, payload.FarmIdentityId, payload.EntryDate, cancellationToken);
            if (inClosedPeriod)
            {
                await MarkOutboxDeadLetterAsync(conn, tx, row.Id, "PERIOD_CLOSED", cancellationToken);
                deadLettered++;
                continue;
            }

            await EnsureJournalPartitionAsync(dataSource, payload.EntryDate, cancellationToken);

            Guid entryId = Guid.NewGuid();
            await using (NpgsqlCommand jeCmd = conn.CreateCommand())
            {
                jeCmd.Transaction = tx;
                jeCmd.CommandText = """
                    INSERT INTO accounts.journal_entry (
                        id, farm_identity_id, farm_organization_id, recorded_by_staff_identity_id,
                        entry_date, description, source_module, source_ref_type, source_ref_id
                    )
                    VALUES (
                        @id, @farmId, @orgId, @staffId,
                        @entryDate, @desc, @srcMod, @refType, @refId
                    );
                    """;
                jeCmd.Parameters.AddWithValue("id", entryId);
                jeCmd.Parameters.AddWithValue("farmId", payload.FarmIdentityId);
                NpgsqlParameter orgP = jeCmd.Parameters.Add("orgId", NpgsqlDbType.Uuid);
                orgP.Value = payload.FarmOrganizationId.HasValue ? payload.FarmOrganizationId.Value : DBNull.Value;
                NpgsqlParameter stfP = jeCmd.Parameters.Add("staffId", NpgsqlDbType.Uuid);
                stfP.Value = payload.RecordedByStaffIdentityId.HasValue ? payload.RecordedByStaffIdentityId.Value : DBNull.Value;
                jeCmd.Parameters.AddWithValue("entryDate", payload.EntryDate);
                jeCmd.Parameters.AddWithValue("desc", string.IsNullOrWhiteSpace(payload.Description) ? row.EventType : payload.Description.Trim());
                jeCmd.Parameters.AddWithValue("srcMod", row.AggregateType);
                jeCmd.Parameters.AddWithValue("refType", row.EventType);
                jeCmd.Parameters.AddWithValue("refId", row.AggregateId);
                await jeCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (PlannedJournalLine line in plannedLines)
            {
                if (!accountCodeMap.TryGetValue(line.AccountCode, out Guid accountId))
                {
                    throw new InvalidOperationException($"Account code '{line.AccountCode}' is missing from accounts.account.");
                }

                await using NpgsqlCommand lineCmd = conn.CreateCommand();
                lineCmd.Transaction = tx;
                lineCmd.CommandText = """
                    INSERT INTO accounts.journal_entry_line (
                        id, journal_entry_id, journal_entry_date, account_id,
                        debit_amount, credit_amount, crop_cycle_id, production_unit_id
                    )
                    VALUES (
                        gen_random_uuid(), @jeId, @jeDate, @acctId,
                        @debit, @credit, @cycleId, @unitId
                    );
                    """;
                lineCmd.Parameters.AddWithValue("jeId", entryId);
                lineCmd.Parameters.AddWithValue("jeDate", payload.EntryDate);
                lineCmd.Parameters.AddWithValue("acctId", accountId);
                lineCmd.Parameters.AddWithValue("debit", line.DebitAmount);
                lineCmd.Parameters.AddWithValue("credit", line.CreditAmount);
                NpgsqlParameter cycP = lineCmd.Parameters.Add("cycleId", NpgsqlDbType.Uuid);
                cycP.Value = line.CropCycleId.HasValue ? line.CropCycleId.Value : DBNull.Value;
                NpgsqlParameter unitP = lineCmd.Parameters.Add("unitId", NpgsqlDbType.Uuid);
                unitP.Value = line.ProductionUnitId.HasValue ? line.ProductionUnitId.Value : DBNull.Value;
                await lineCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (NpgsqlCommand markPostedCmd = conn.CreateCommand())
            {
                markPostedCmd.Transaction = tx;
                markPostedCmd.CommandText = """
                    INSERT INTO accounts.posted_outbox_event (outbox_event_id, journal_entry_id, journal_entry_date)
                    VALUES (@outboxId, @jeId, @jeDate);
                    """;
                markPostedCmd.Parameters.AddWithValue("outboxId", row.Id);
                markPostedCmd.Parameters.AddWithValue("jeId", entryId);
                markPostedCmd.Parameters.AddWithValue("jeDate", payload.EntryDate);
                await markPostedCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await MarkOutboxDispatchedAsync(conn, tx, row.Id, cancellationToken);
            await AuditWriter.WriteAsync(
                conn,
                tx,
                new AuditRecord("accounts", "journal_entry.posted", "journal_entry", entryId, payload.FarmIdentityId, JsonSerializer.Serialize(new { row.EventType, payload.AmountNgn, outboxId = row.Id }, JsonOptions)),
                cancellationToken);

            posted++;
        }

        await tx.CommitAsync(cancellationToken);
        return new OutboxProcessSummary(claimed, posted, deduplicated, deadLettered);
    }

    internal static async Task<bool> IsDateInClosedPeriodAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Guid farmIdentityId,
        DateOnly entryDate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM accounts.accounting_period
                WHERE farm_identity_id = @farmId
                  AND @entryDate BETWEEN start_date AND end_date
                  AND status = 'closed'
            );
            """;
        cmd.Parameters.AddWithValue("farmId", farmIdentityId);
        cmd.Parameters.AddWithValue("entryDate", entryDate);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<Dictionary<string, Guid>> LoadAccountCodeMapAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> map = new(StringComparer.Ordinal);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT code, id FROM accounts.account;";
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            map[reader.GetString(0)] = reader.GetGuid(1);
        }
        return map;
    }

    private static async Task MarkOutboxDispatchedAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE platform.events_outbox
            SET status = 'dispatched',
                attempts = attempts + 1,
                dispatched_at = COALESCE(dispatched_at, now())
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkOutboxDeadLetterAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string error, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE platform.events_outbox
            SET status = 'dead_letter',
                attempts = attempts + 1,
                last_error = @err
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("err", error);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record PendingOutboxRow(
        Guid Id,
        string AggregateType,
        Guid AggregateId,
        string EventType,
        string PayloadJson);
}

internal sealed class OutboxLedgerBackgroundService(
    DatabaseConnectionSource dbSource,
    SchemaMigrationRunner migrationRunner,
    ILogger<OutboxLedgerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            NpgsqlDataSource? dataSource = dbSource.DataSource;
            if (dataSource is null)
            {
                continue;
            }

            try
            {
                await migrationRunner.EnsureMigratedAsync(dataSource, stoppingToken);
                await OutboxLedgerProcessor.ProcessPendingBatchAsync(dataSource, 25, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OutboxLogMessages.OutboxWorkerFailed(logger, ex.GetType().Name);
            }
        }
    }
}

internal static partial class OutboxLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Background outbox ledger batch failed with {ExceptionType}")]
    public static partial void OutboxWorkerFailed(ILogger logger, string exceptionType);
}
