using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FarmBasics.IntegrationTests.Infrastructure;

/// <summary>
/// Ephemeral PostgreSQL 16 + PostGIS 3.4 Testcontainers fixture (01A §9, AGENTS.md Rule 3).
/// Never substitutes vanilla postgres, SQLite, or EF in-memory providers.
/// </summary>
public sealed class PostgisDatabaseFixture : IAsyncLifetime
{
    public const string PostgisImage = "postgis/postgis:15-3.5";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgisImage)
        .WithDatabase("farmbasics_test")
        .WithUsername("postgres")
        .WithPassword("test_only_postgres_pw")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using NpgsqlConnection conn = new(ConnectionString);
        await conn.OpenAsync();

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis; SELECT PostGIS_Version();";
        object? result = await cmd.ExecuteScalarAsync();
        if (result is null || string.IsNullOrWhiteSpace(result.ToString()))
        {
            throw new InvalidOperationException("PostGIS extension did not initialize cleanly in Testcontainers.");
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
