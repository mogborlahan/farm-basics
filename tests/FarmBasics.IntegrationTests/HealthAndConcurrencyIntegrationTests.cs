using System.Net;
using System.Text.Json;
using FarmBasics.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FarmBasics.IntegrationTests;

public sealed class HealthAndConcurrencyIntegrationTests : IClassFixture<PostgisDatabaseFixture>
{
    private readonly PostgisDatabaseFixture _dbFixture;

    public HealthAndConcurrencyIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
    }

    [Fact]
    public async Task HealthReadyReturns200WithLivePostgisVersion()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = _dbFixture.ConnectionString,
                        ["Auth:RequireHttpsMetadata"] = "false"
                    });
                });
            });

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(payload);
        Assert.Equal("ready", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("reachable", doc.RootElement.GetProperty("database").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("postgis").GetString()));
    }

    [Fact]
    public async Task PostgisGeodesicAreaComputesRealSquareMeters()
    {
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        // ~111m x 111m plot near Kano, Nigeria (geodesic area via geography)
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ST_Area(
                ST_GeogFromText('POLYGON((8.5160 12.0000, 8.5170 12.0000, 8.5170 12.0010, 8.5160 12.0010, 8.5160 12.0000))')
            );
            """;

        double areaSqMeters = Convert.ToDouble(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(areaSqMeters, 11_000.0, 13_000.0);
    }

    [Fact]
    public async Task BarrierSynchronisedDatabaseContentionAllowsExactlyOneWinnerPerIteration()
    {
        await using NpgsqlConnection setupConn = new(_dbFixture.ConnectionString);
        await setupConn.OpenAsync();

        await using (NpgsqlCommand ddl = setupConn.CreateCommand())
        {
            ddl.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS public_dummy_check (id int);
                DROP TABLE IF EXISTS public.s0_race_probe;
                CREATE TABLE public.s0_race_probe (
                    probe_key text PRIMARY KEY,
                    winner_worker integer NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now()
                );
                """;
            await ddl.ExecuteNonQueryAsync();
        }

        const int iterations = 50;
        const int concurrency = 4;

        RaceSummary summary = await ParallelRaceHarness.RunAsync(
            iterations,
            concurrency,
            iteration =>
            {
                string key = $"race-key-{iteration}";
                Func<int, Task<bool>> worker = async workerIndex =>
                {
                    await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
                    await conn.OpenAsync();

                    await using NpgsqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        INSERT INTO public.s0_race_probe (probe_key, winner_worker)
                        VALUES (@key, @worker)
                        ON CONFLICT (probe_key) DO NOTHING;
                        """;
                    cmd.Parameters.AddWithValue("key", key);
                    cmd.Parameters.AddWithValue("worker", workerIndex);

                    int affected = await cmd.ExecuteNonQueryAsync();
                    return affected == 1;
                };
                return Task.FromResult(worker);
            });

        Assert.Equal(iterations, summary.TotalWinners);
        Assert.Equal(iterations * (concurrency - 1), summary.TotalConflicts);
        Assert.All(summary.WinnersPerIteration, winners => Assert.Equal(1, winners));

        await using NpgsqlCommand countCmd = setupConn.CreateCommand();
        countCmd.CommandText = "SELECT count(*) FROM public.s0_race_probe;";
        long rowCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(iterations, rowCount);
    }
}
