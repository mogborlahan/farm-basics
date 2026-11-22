using System.Text.Json;
using FarmBasics.Api.Audit;
using FarmBasics.BuildingBlocks.Observability;
using Npgsql;

namespace FarmBasics.Api.Accounts;

internal sealed record ReconciliationDriftAlertDto(
    Guid AlertId,
    string AccountCode,
    string AccountName,
    decimal CachedClosingBalance,
    decimal ComputedClosingBalance,
    decimal DriftAmount,
    string Severity);

/// <summary>
/// Cache refresh and non-mutating nightly reconciliation service implementing S8.3.M2 &amp; Doc 08-ENG §10:
/// alerts CRITICAL on any divergence between AccountBalance cache and raw journal lines rather than silently fixing.
/// </summary>
internal static class LedgerReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RefreshAccountBalanceCacheAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid farmIdentityId,
        Guid periodId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            WITH period_bounds AS (
                SELECT start_date, end_date
                FROM accounts.accounting_period
                WHERE id = @periodId AND farm_identity_id = @farmId
            ),
            aggregated AS (
                SELECT
                    a.id AS account_id,
                    a.normal_balance,
                    COALESCE(SUM(CASE WHEN je.entry_date < pb.start_date THEN jel.debit_amount - jel.credit_amount ELSE 0 END), 0) AS prior_net,
                    COALESCE(SUM(CASE WHEN je.entry_date BETWEEN pb.start_date AND pb.end_date THEN jel.debit_amount ELSE 0 END), 0) AS period_debits,
                    COALESCE(SUM(CASE WHEN je.entry_date BETWEEN pb.start_date AND pb.end_date THEN jel.credit_amount ELSE 0 END), 0) AS period_credits
                FROM period_bounds pb
                CROSS JOIN accounts.account a
                JOIN accounts.journal_entry je
                  ON je.farm_identity_id = @farmId
                 AND je.entry_date <= pb.end_date
                JOIN accounts.journal_entry_line jel
                  ON jel.journal_entry_id = je.id
                 AND jel.journal_entry_date = je.entry_date
                 AND jel.account_id = a.id
                GROUP BY a.id, a.normal_balance
            )
            INSERT INTO accounts.account_balance (farm_identity_id, account_id, period_id, opening_balance, debits, credits, closing_balance)
            SELECT
                @farmId,
                ag.account_id,
                @periodId,
                CASE WHEN ag.normal_balance = 'debit' THEN ag.prior_net ELSE -ag.prior_net END,
                ag.period_debits,
                ag.period_credits,
                CASE
                    WHEN ag.normal_balance = 'debit' THEN ag.prior_net + ag.period_debits - ag.period_credits
                    ELSE -ag.prior_net + ag.period_credits - ag.period_debits
                END
            FROM aggregated ag
            ON CONFLICT (farm_identity_id, account_id, period_id) DO UPDATE
            SET opening_balance = EXCLUDED.opening_balance,
                debits = EXCLUDED.debits,
                credits = EXCLUDED.credits,
                closing_balance = EXCLUDED.closing_balance;
            """;
        cmd.Parameters.AddWithValue("farmId", farmIdentityId);
        cmd.Parameters.AddWithValue("periodId", periodId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<ReconciliationDriftAlertDto>> RunReconciliationAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid farmIdentityId,
        Guid periodId,
        Guid? actorAdminId,
        CancellationToken cancellationToken)
    {
        List<(Guid AccountId, string Code, string Name, decimal Cached, decimal Computed)> divergences = [];
        await using (NpgsqlCommand checkCmd = conn.CreateCommand())
        {
            checkCmd.Transaction = tx;
            checkCmd.CommandText = """
                WITH period_bounds AS (
                    SELECT start_date, end_date
                    FROM accounts.accounting_period
                    WHERE id = @periodId AND farm_identity_id = @farmId
                ),
                live_totals AS (
                    SELECT
                        a.id AS account_id,
                        CASE
                            WHEN a.normal_balance = 'debit' THEN COALESCE(SUM(jel.debit_amount - jel.credit_amount), 0)
                            ELSE COALESCE(SUM(jel.credit_amount - jel.debit_amount), 0)
                        END AS computed_closing
                    FROM period_bounds pb
                    CROSS JOIN accounts.account a
                    LEFT JOIN accounts.journal_entry je
                      ON je.farm_identity_id = @farmId
                     AND je.entry_date <= pb.end_date
                    LEFT JOIN accounts.journal_entry_line jel
                      ON jel.journal_entry_id = je.id
                     AND jel.journal_entry_date = je.entry_date
                     AND jel.account_id = a.id
                    GROUP BY a.id, a.normal_balance
                )
                SELECT
                    ab.account_id,
                    a.code,
                    a.name,
                    ab.closing_balance,
                    COALESCE(lt.computed_closing, 0)
                FROM accounts.account_balance ab
                JOIN accounts.account a ON a.id = ab.account_id
                LEFT JOIN live_totals lt ON lt.account_id = ab.account_id
                WHERE ab.farm_identity_id = @farmId
                  AND ab.period_id = @periodId
                  AND ab.closing_balance <> COALESCE(lt.computed_closing, 0);
                """;
            checkCmd.Parameters.AddWithValue("farmId", farmIdentityId);
            checkCmd.Parameters.AddWithValue("periodId", periodId);

            await using NpgsqlDataReader reader = await checkCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                divergences.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4)));
            }
        }

        List<ReconciliationDriftAlertDto> alerts = [];
        foreach ((Guid accountId, string code, string name, decimal cached, decimal computed) in divergences)
        {
            decimal drift = cached - computed;
            Guid alertId = Guid.NewGuid();

            await using (NpgsqlCommand alertCmd = conn.CreateCommand())
            {
                alertCmd.Transaction = tx;
                alertCmd.CommandText = """
                    INSERT INTO accounts.reconciliation_alert (
                        id, farm_identity_id, account_id, period_id,
                        cached_closing_balance, computed_closing_balance, drift_amount, severity
                    )
                    VALUES (@id, @farmId, @acctId, @periodId, @cached, @computed, @drift, 'CRITICAL');
                    """;
                alertCmd.Parameters.AddWithValue("id", alertId);
                alertCmd.Parameters.AddWithValue("farmId", farmIdentityId);
                alertCmd.Parameters.AddWithValue("acctId", accountId);
                alertCmd.Parameters.AddWithValue("periodId", periodId);
                alertCmd.Parameters.AddWithValue("cached", cached);
                alertCmd.Parameters.AddWithValue("computed", computed);
                alertCmd.Parameters.AddWithValue("drift", drift);
                await alertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditWriter.WriteAsync(
                conn,
                tx,
                new AuditRecord(
                    "accounts",
                    "reconciliation.drift_detected",
                    "reconciliation_alert",
                    alertId,
                    actorAdminId,
                    JsonSerializer.Serialize(new { code, cached, computed, drift, severity = "CRITICAL" }, JsonOptions)),
                cancellationToken);

            PlatformMetrics.LedgerReconciliationDriftTotal.Inc();
            alerts.Add(new ReconciliationDriftAlertDto(alertId, code, name, cached, computed, drift, "CRITICAL"));
        }

        return alerts;
    }
}
