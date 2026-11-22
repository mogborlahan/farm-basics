using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;

namespace FarmBasics.Api.Audit;

public static partial class AuditPartitionManager
{
    [GeneratedRegex(@"^audit_log_entry_\d{4}_\d{2}$")]
    private static partial Regex PartitionNameRegex();

    public static async Task<IReadOnlyList<string>> EnsureMonthlyPartitionsAsync(
        NpgsqlConnection connection,
        DateTimeOffset referenceUtc,
        int monthsAhead = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (monthsAhead < 0 || monthsAhead > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(monthsAhead), "monthsAhead must be between 0 and 24.");
        }

        DateTime utcStartMonth = new(referenceUtc.UtcDateTime.Year, referenceUtc.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        List<string> ensured = [];

        for (int offset = 0; offset <= monthsAhead; offset++)
        {
            DateTime monthStart = utcStartMonth.AddMonths(offset);
            DateTime monthEnd = monthStart.AddMonths(1);
            string partitionName = $"audit_log_entry_{monthStart.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";

            if (!PartitionNameRegex().IsMatch(partitionName))
            {
                throw new InvalidOperationException($"Computed partition name '{partitionName}' failed strict format validation.");
            }

            string fromLiteral = monthStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            string toLiteral = monthEnd.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            await using NpgsqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS audit.{partitionName} PARTITION OF audit.audit_log_entry
                FOR VALUES FROM ('{fromLiteral}') TO ('{toLiteral}');
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            ensured.Add($"audit.{partitionName}");
        }

        return ensured;
    }
}
