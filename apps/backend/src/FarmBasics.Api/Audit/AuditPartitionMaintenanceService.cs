using FarmBasics.Api.Geography;
using FarmBasics.Api.Persistence;
using Npgsql;

namespace FarmBasics.Api.Audit;

internal sealed class DatabaseBootstrapAndPartitionService(
    DatabaseConnectionSource dbSource,
    SchemaMigrationRunner migrationRunner,
    ILogger<DatabaseBootstrapAndPartitionService> logger) : IHostedService, IDisposable
{
    private CancellationTokenSource? _loopCts;
    private Task? _backgroundLoop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return;
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using (NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            IReadOnlyList<string> partitions = await AuditPartitionManager.EnsureMonthlyPartitionsAsync(
                conn,
                DateTimeOffset.UtcNow,
                monthsAhead: 3,
                cancellationToken);
            AuditLogMessages.PartitionsEnsured(logger, partitions.Count);
        }

        await SeedBaselineGeographyIfEmptyAsync(dataSource, cancellationToken);

        _loopCts = new CancellationTokenSource();
        _backgroundLoop = RunPeriodicMaintenanceAsync(dataSource, _loopCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
        }

        if (_backgroundLoop is not null)
        {
            await _backgroundLoop.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public void Dispose() => _loopCts?.Dispose();

    private static async Task SeedBaselineGeographyIfEmptyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        long existingCount;
        await using (NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (NpgsqlCommand countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT count(*) FROM identity.state;";
            existingCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (existingCount == 0)
        {
            GeographyDataset dataset = GeographySeeder.LoadBaselineDataset();
            await GeographySeeder.SeedAsync(dataSource, dataset, dataset, cancellationToken);
        }
    }

    private async Task RunPeriodicMaintenanceAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(24));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
                IReadOnlyList<string> partitions = await AuditPartitionManager.EnsureMonthlyPartitionsAsync(
                    conn,
                    DateTimeOffset.UtcNow,
                    monthsAhead: 3,
                    cancellationToken);
                AuditLogMessages.PartitionsEnsured(logger, partitions.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            AuditLogMessages.PartitionMaintenanceFailed(logger, ex.GetType().Name);
        }
    }
}

internal static partial class AuditLogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Ensured {PartitionCount} monthly partitions on audit.audit_log_entry")]
    public static partial void PartitionsEnsured(ILogger logger, int partitionCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled audit partition maintenance failed with {ErrorType}")]
    public static partial void PartitionMaintenanceFailed(ILogger logger, string errorType);
}
