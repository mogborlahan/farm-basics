using Npgsql;

namespace FarmBasics.Api.ProductionUnits;

internal sealed record RadiusMatchedUnitDto(Guid Id, string Name, string CaptureMethod, decimal? DistanceMeters);

internal sealed record CoarseBuyerLocationDto(double ApproxLat, double ApproxLng);

internal sealed record OverlapDetectedDto(
    Guid OverlapEventId,
    Guid ExistingProductionUnitId,
    decimal OverlapAreaSqm,
    decimal OverlapPctOfNew,
    decimal OverlapPctOfExisting);

internal sealed record SoilSamplingEligibilityResult(bool Eligible, string? ErrorCode, string? Message);

internal sealed record RegionalPestCountDto(Guid LgaId, int UnitCount);

internal sealed record SeparateAreaTotalsDto(decimal MeasuredGeodesicHectares, decimal DeclaredHectares);

internal sealed record NullGeometryReconciliationResult(int UnitsAudited, int IntegrityViolations);

/// <summary>
/// Consumer-audit service implementing every geometry consumer in Doc/02B-ENG §5 (WP-56 / BR-E-04).
/// Ensures null-boundary and null-centroid production units never throw or corrupt downstream consumers.
/// </summary>
internal static class ProductionUnitConsumerService
{
    public static async Task<IReadOnlyList<RadiusMatchedUnitDto>> FindUnitsWithinDeliveryRadiusAsync(
        NpgsqlConnection conn,
        double targetLat,
        double targetLng,
        double radiusMeters,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                id,
                name,
                capture_method::text,
                ROUND(ST_Distance(centroid, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography)::numeric, 2)
            FROM field.production_unit
            WHERE centroid IS NOT NULL
              AND ST_DWithin(centroid, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography, @radius)
            ORDER BY ST_Distance(centroid, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography) ASC;
            """;
        cmd.Parameters.AddWithValue("lng", targetLng);
        cmd.Parameters.AddWithValue("lat", targetLat);
        cmd.Parameters.AddWithValue("radius", radiusMeters);

        List<RadiusMatchedUnitDto> results = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RadiusMatchedUnitDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3)));
        }

        return results;
    }

    public static async Task<CoarseBuyerLocationDto?> GetCoarseBuyerLocationAsync(
        NpgsqlConnection conn,
        Guid productionUnitId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                CASE WHEN centroid IS NULL THEN NULL ELSE ROUND(ST_Y(centroid::geometry)::numeric, 2)::double precision END,
                CASE WHEN centroid IS NULL THEN NULL ELSE ROUND(ST_X(centroid::geometry)::numeric, 2)::double precision END
            FROM field.production_unit
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", productionUnitId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return new CoarseBuyerLocationDto(reader.GetDouble(0), reader.GetDouble(1));
    }

    public static async Task<IReadOnlyList<OverlapDetectedDto>> DetectOverlapsForUnitAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid newUnitId,
        CancellationToken cancellationToken)
    {
        // Doc/02B-ENG §1 A-3 & §5: Only units with walked polygon boundaries participate in overlap detection.
        await using NpgsqlCommand findCmd = conn.CreateCommand();
        findCmd.Transaction = tx;
        findCmd.CommandText = """
            WITH target AS (
                SELECT id, boundary, area_square_meters
                FROM field.production_unit
                WHERE id = @newId AND boundary IS NOT NULL
            )
            SELECT
                other.id,
                ROUND(ST_Area(ST_Intersection(target.boundary::geometry, other.boundary::geometry)::geography)::numeric, 2) AS overlap_sqm,
                target.area_square_meters,
                other.area_square_meters
            FROM target
            JOIN field.production_unit other
              ON other.id <> target.id
             AND other.boundary IS NOT NULL
             AND ST_Intersects(target.boundary::geometry, other.boundary::geometry);
            """;
        findCmd.Parameters.AddWithValue("newId", newUnitId);

        List<(Guid ExistingId, decimal OverlapSqm, decimal NewArea, decimal ExistingArea)> candidates = [];
        await using (NpgsqlDataReader reader = await findCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                decimal overlapSqm = reader.GetDecimal(1);
                if (overlapSqm > 1.0m)
                {
                    candidates.Add((reader.GetGuid(0), overlapSqm, reader.GetDecimal(2), reader.GetDecimal(3)));
                }
            }
        }

        List<OverlapDetectedDto> created = [];
        foreach ((Guid existingId, decimal overlapSqm, decimal newArea, decimal existingArea) in candidates)
        {
            decimal pctNew = newArea > 0m ? Math.Min(100.00m, Math.Round(overlapSqm / newArea * 100.0m, 2)) : 0m;
            decimal pctExisting = existingArea > 0m ? Math.Min(100.00m, Math.Round(overlapSqm / existingArea * 100.0m, 2)) : 0m;
            Guid overlapId = Guid.NewGuid();

            await using NpgsqlCommand insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO field.field_overlap_event (
                    id, new_production_unit_id, existing_production_unit_id,
                    overlap_area_sqm, overlap_pct_of_new, overlap_pct_of_existing, status
                )
                VALUES (@id, @newId, @existingId, @overlapSqm, @pctNew, @pctExisting, 'pending_review');
                """;
            insertCmd.Parameters.AddWithValue("id", overlapId);
            insertCmd.Parameters.AddWithValue("newId", newUnitId);
            insertCmd.Parameters.AddWithValue("existingId", existingId);
            insertCmd.Parameters.AddWithValue("overlapSqm", overlapSqm);
            insertCmd.Parameters.AddWithValue("pctNew", pctNew);
            insertCmd.Parameters.AddWithValue("pctExisting", pctExisting);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);

            created.Add(new OverlapDetectedDto(overlapId, existingId, overlapSqm, pctNew, pctExisting));
        }

        return created;
    }

    public static async Task<SoilSamplingEligibilityResult> ValidateSoilSamplingEligibilityAsync(
        NpgsqlConnection conn,
        Guid productionUnitId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT boundary IS NOT NULL FROM field.production_unit WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", productionUnitId);

        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar is not bool hasBoundary)
        {
            return new SoilSamplingEligibilityResult(false, "RESOURCE_NOT_FOUND", "Production unit was not found.");
        }

        if (!hasBoundary)
        {
            return new SoilSamplingEligibilityResult(false, "BOUNDARY_REQUIRED", "GPS-guided soil sampling requires a walked polygon boundary.");
        }

        return new SoilSamplingEligibilityResult(true, null, null);
    }

    public static async Task<string?> GetLandHistoryModeAsync(
        NpgsqlConnection conn,
        Guid productionUnitId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (boundary IS NOT NULL OR centroid IS NOT NULL)
            FROM field.production_unit
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", productionUnitId);

        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar is not bool hasLocation)
        {
            return null;
        }

        return hasLocation ? "land_location" : "cultivation_history_only";
    }

    public static decimal ComputeYieldPrefillKg(
        string growingMediumClass,
        decimal? areaSquareMeters,
        decimal? areaHectares,
        short tierCount,
        decimal expectedYieldPerSqmKg,
        decimal expectedYieldPerHaKg)
    {
        if (string.Equals(growingMediumClass, "soilless", StringComparison.Ordinal))
        {
            decimal areaSqm = areaSquareMeters ?? 0m;
            return Math.Round(expectedYieldPerSqmKg * areaSqm * tierCount, 2);
        }

        decimal areaHa = areaHectares ?? 0m;
        return Math.Round(expectedYieldPerHaKg * areaHa, 2);
    }

    public static async Task<IReadOnlyList<RegionalPestCountDto>> AggregatePestCountsByRegionAsync(
        NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT lga_id, COUNT(*)::int
            FROM field.production_unit
            WHERE lga_id IS NOT NULL
            GROUP BY lga_id
            ORDER BY COUNT(*) DESC;
            """;

        List<RegionalPestCountDto> rows = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RegionalPestCountDto(reader.GetGuid(0), reader.GetInt32(1)));
        }

        return rows;
    }

    public static async Task<Guid?> ResolveAdminRegionScopeAsync(
        NpgsqlConnection conn,
        Guid productionUnitId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT lga_id FROM field.production_unit WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", productionUnitId);

        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is Guid lgaId ? lgaId : null;
    }

    public static async Task<SeparateAreaTotalsDto> GetSeparateAreaTotalsAsync(
        NpgsqlConnection conn,
        Guid farmIdentityId,
        CancellationToken cancellationToken)
    {
        // Doc/02B-ENG §5: Never add measured and declared area into one aggregate figure.
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN area_basis = 'computed_geodesic' THEN area_hectares ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN area_basis = 'declared' THEN area_hectares ELSE 0 END), 0)
            FROM field.production_unit
            WHERE farm_identity_id = @farmId;
            """;
        cmd.Parameters.AddWithValue("farmId", farmIdentityId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SeparateAreaTotalsDto(0m, 0m);
        }

        return new SeparateAreaTotalsDto(reader.GetDecimal(0), reader.GetDecimal(1));
    }

    public static async Task<NullGeometryReconciliationResult> RunNullGeometryReconciliationAsync(
        NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*)::int,
                COUNT(*) FILTER (
                    WHERE (capture_method = 'gps_walked_boundary' AND (boundary IS NULL OR centroid IS NULL OR area_square_meters IS NULL OR area_basis <> 'computed_geodesic'))
                       OR (capture_method IN ('gps_pin_drop_with_footprint', 'manual_footprint_entry', 'address_only') AND boundary IS NOT NULL)
                )::int
            FROM field.production_unit;
            """;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new NullGeometryReconciliationResult(0, 0);
        }

        return new NullGeometryReconciliationResult(reader.GetInt32(0), reader.GetInt32(1));
    }
}
