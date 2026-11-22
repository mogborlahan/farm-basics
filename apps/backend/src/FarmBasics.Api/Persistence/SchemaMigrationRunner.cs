using System.Reflection;
using Npgsql;

namespace FarmBasics.Api.Persistence;

internal sealed class SchemaMigrationRunner : IDisposable
{
    private const long MigrationAdvisoryLockKey = 71001001;
    private const string MigrationPrefix = "FarmBasics.Api.Persistence.Migrations.";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _migrated;

    public void Dispose() => _lock.Dispose();

    public async Task EnsureMigratedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (_migrated)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_migrated)
            {
                return;
            }

            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);

            await using (NpgsqlCommand lockCmd = conn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@lockKey);";
                lockCmd.Parameters.AddWithValue("lockKey", MigrationAdvisoryLockKey);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                await using (NpgsqlCommand tableCmd = conn.CreateCommand())
                {
                    tableCmd.CommandText = """
                        CREATE SCHEMA IF NOT EXISTS platform;
                        CREATE TABLE IF NOT EXISTS platform.schema_migrations (
                            version text PRIMARY KEY,
                            applied_at timestamptz NOT NULL DEFAULT now()
                        );
                        """;
                    await tableCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                Assembly assembly = typeof(SchemaMigrationRunner).Assembly;
                IReadOnlyList<string> resourceNames = assembly.GetManifestResourceNames()
                    .Where(name => name.StartsWith(MigrationPrefix, StringComparison.Ordinal)
                                   && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                foreach (string resourceName in resourceNames)
                {
                    string version = resourceName[MigrationPrefix.Length..];
                    bool alreadyApplied;
                    await using (NpgsqlCommand checkCmd = conn.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM platform.schema_migrations WHERE version = @version);";
                        checkCmd.Parameters.AddWithValue("version", version);
                        alreadyApplied = Convert.ToBoolean(await checkCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (alreadyApplied)
                    {
                        continue;
                    }

                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream is null)
                    {
                        throw new InvalidOperationException($"Embedded migration resource '{resourceName}' could not be loaded.");
                    }

                    using StreamReader reader = new(stream);
                    string sql = await reader.ReadToEndAsync(cancellationToken);

                    await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);
                    await using (NpgsqlCommand applyCmd = conn.CreateCommand())
                    {
                        applyCmd.Transaction = tx;
                        applyCmd.CommandText = sql;
                        await applyCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (NpgsqlCommand recordCmd = conn.CreateCommand())
                    {
                        recordCmd.Transaction = tx;
                        recordCmd.CommandText = "INSERT INTO platform.schema_migrations (version) VALUES (@version) ON CONFLICT DO NOTHING;";
                        recordCmd.Parameters.AddWithValue("version", version);
                        await recordCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                }

                _migrated = true;
            }
            finally
            {
                await using NpgsqlCommand unlockCmd = conn.CreateCommand();
                unlockCmd.CommandText = "SELECT pg_advisory_unlock(@lockKey);";
                unlockCmd.Parameters.AddWithValue("lockKey", MigrationAdvisoryLockKey);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
