using System.Reflection;
using System.Text.Json;
using Npgsql;

namespace FarmBasics.Api.Geography;

internal sealed record GeographySeedResult(int StatesSeeded, int LgasSeeded);

internal sealed class GeographySeeder
{
    private const string EmbeddedResourceName = "FarmBasics.Api.Geography.Data.nigeria-states-lgas.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GeographyDataset LoadBaselineDataset()
    {
        Assembly assembly = typeof(GeographySeeder).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded geography seed resource '{EmbeddedResourceName}' was not found.");
        }

        GeographyDataset? dataset = JsonSerializer.Deserialize<GeographyDataset>(stream, JsonOptions);
        if (dataset is null || dataset.States.Count == 0)
        {
            throw new InvalidOperationException("Baseline Nigerian geography dataset is empty or invalid.");
        }

        return dataset;
    }

    public static async Task<GeographySeedResult> SeedAsync(
        NpgsqlDataSource dataSource,
        GeographyDataset primary,
        GeographyDataset secondary,
        CancellationToken cancellationToken)
    {
        GeographyValidationReport report = GeographyCrossSourceValidator.Validate(primary, secondary);
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                $"Geography cross-source validation failed with {report.Mismatches.Count} unresolved discrepancy(ies); aborting seed per Doc/06-DATA §2.5.");
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        int statesSeeded = 0;
        int lgasSeeded = 0;

        foreach (StateSeedEntry state in primary.States)
        {
            string normalizedStateName = GeographyCrossSourceValidator.NormalizeStateName(state.Name);
            string normalizedStateCode = state.Code.Trim().ToUpperInvariant();

            Guid stateId;
            await using (NpgsqlCommand stateCmd = conn.CreateCommand())
            {
                stateCmd.Transaction = tx;
                stateCmd.CommandText = """
                    INSERT INTO identity.state (name, code)
                    VALUES (@name, @code)
                    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name
                    RETURNING id;
                    """;
                stateCmd.Parameters.AddWithValue("name", normalizedStateName);
                stateCmd.Parameters.AddWithValue("code", normalizedStateCode);
                stateId = (Guid)(await stateCmd.ExecuteScalarAsync(cancellationToken))!;
                statesSeeded++;
            }

            foreach (LgaSeedEntry lga in state.Lgas)
            {
                string normalizedLgaName = GeographyCrossSourceValidator.NormalizePlaceName(lga.Name);
                string normalizedLgaCode = lga.Code.Trim().ToUpperInvariant();

                await using NpgsqlCommand lgaCmd = conn.CreateCommand();
                lgaCmd.Transaction = tx;
                lgaCmd.CommandText = """
                    INSERT INTO identity.lga (state_id, name, code)
                    VALUES (@stateId, @name, @code)
                    ON CONFLICT (state_id, code) DO UPDATE SET name = EXCLUDED.name;
                    """;
                lgaCmd.Parameters.AddWithValue("stateId", stateId);
                lgaCmd.Parameters.AddWithValue("name", normalizedLgaName);
                lgaCmd.Parameters.AddWithValue("code", normalizedLgaCode);
                await lgaCmd.ExecuteNonQueryAsync(cancellationToken);
                lgasSeeded++;
            }
        }

        await tx.CommitAsync(cancellationToken);
        return new GeographySeedResult(statesSeeded, lgasSeeded);
    }
}
