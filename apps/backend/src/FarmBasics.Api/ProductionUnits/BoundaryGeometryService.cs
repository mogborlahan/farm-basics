using System.Globalization;
using System.Text;
using Npgsql;

namespace FarmBasics.Api.ProductionUnits;

internal enum BoundaryAnalysisStatus
{
    Valid = 1,
    NeedsReview = 2,
    InsufficientPoints = 3
}

internal sealed record BoundaryAnalysisResult(
    BoundaryAnalysisStatus Status,
    string? Issue,
    string? BoundaryWkt,
    string? BoundaryGeoJson,
    decimal AreaSquareMeters,
    decimal AreaHectares,
    decimal PerimeterMeters,
    decimal DistanceMeters,
    int AcceptedPointCount,
    int OutliersRemoved,
    bool Repaired,
    decimal RepairDeltaPct);

internal readonly record struct TrackPointSample(
    int SequenceNumber,
    double Lat,
    double Lng,
    double? AccuracyMeters,
    DateTimeOffset RecordedAt);

/// <summary>
/// Server-side boundary geometry pipeline implementing WP-51 & WP-52:
/// accuracy filter -> GPS jump outlier removal -> ring closure -> simplification ->
/// geodesic PostGIS ST_MakeValid <3% repair or review rejection.
/// </summary>
internal static class BoundaryGeometryService
{
    private const double EarthRadiusMeters = 6371008.8;
    private const double MinValidAreaSquareMeters = 10.0;
    private const double MaxAutoRepairAreaLossRatio = 0.03;

    public static async Task<BoundaryAnalysisResult> AnalyzeAndRepairAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        List<TrackPointSample> rawAccepted = [];
        await using (NpgsqlCommand loadCmd = conn.CreateCommand())
        {
            loadCmd.Transaction = tx;
            loadCmd.CommandText = """
                SELECT
                    sequence_number,
                    ST_Y(location::geometry),
                    ST_X(location::geometry),
                    accuracy_meters::double precision,
                    recorded_at
                FROM field.tracking_point
                WHERE session_id = @sessionId
                  AND accepted = TRUE
                ORDER BY sequence_number ASC, recorded_at ASC;
                """;
            loadCmd.Parameters.AddWithValue("sessionId", sessionId);

            await using NpgsqlDataReader reader = await loadCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rawAccepted.Add(new TrackPointSample(
                    reader.GetInt32(0),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        (List<TrackPointSample> filtered, int outliersRemoved) = FilterOutliers(rawAccepted);
        decimal totalWalkedDistance = ComputeWalkDistanceMeters(filtered);

        List<TrackPointSample> distinctVertices = DeduplicateRingVertices(filtered);
        if (distinctVertices.Count < 3)
        {
            return new BoundaryAnalysisResult(
                BoundaryAnalysisStatus.InsufficientPoints,
                "insufficient_points",
                null,
                null,
                0m,
                0m,
                0m,
                totalWalkedDistance,
                rawAccepted.Count,
                outliersRemoved,
                false,
                0m);
        }

        string ringWkt = BuildClosedPolygonWkt(distinctVertices);

        await using NpgsqlCommand postgisCmd = conn.CreateCommand();
        postgisCmd.Transaction = tx;
        postgisCmd.CommandText = """
            WITH raw_poly AS (
                SELECT ST_GeomFromText(@wkt, 4326) AS g
            ),
            simplified AS (
                SELECT CASE
                    WHEN ST_NPoints(ST_SimplifyPreserveTopology(g, 0.000005)) >= 4
                    THEN ST_SimplifyPreserveTopology(g, 0.000005)
                    ELSE g
                END AS g
                FROM raw_poly
            ),
            validity AS (
                SELECT
                    g,
                    ST_IsValid(g) AS is_valid,
                    ST_CollectionExtract(ST_MakeValid(g), 3) AS repaired_multi
                FROM simplified
            ),
            parts AS (
                SELECT
                    (d).geom AS part_geom,
                    ST_Area((d).geom::geography) AS part_area
                FROM validity, LATERAL ST_Dump(repaired_multi) AS d
                WHERE GeometryType((d).geom) = 'POLYGON'
            ),
            stats AS (
                SELECT
                    v.is_valid,
                    COALESCE((SELECT SUM(part_area) FROM parts), 0.0) AS total_area,
                    (SELECT part_geom FROM parts ORDER BY part_area DESC LIMIT 1) AS primary_geom,
                    COALESCE((SELECT part_area FROM parts ORDER BY part_area DESC LIMIT 1), 0.0) AS primary_area
                FROM validity v
            )
            SELECT
                is_valid,
                total_area,
                primary_area,
                CASE WHEN primary_geom IS NULL THEN FALSE ELSE ST_IsValid(primary_geom) END AS primary_valid,
                CASE WHEN primary_geom IS NULL THEN 0.0 ELSE ST_Area(primary_geom::geography) END AS geo_area_sqm,
                CASE WHEN primary_geom IS NULL THEN 0.0 ELSE ST_Perimeter(primary_geom::geography) END AS geo_perimeter_m,
                CASE WHEN primary_geom IS NULL THEN NULL ELSE ST_AsText(primary_geom) END AS primary_wkt,
                CASE WHEN primary_geom IS NULL THEN NULL ELSE ST_AsGeoJSON(primary_geom) END AS primary_geojson
            FROM stats;
            """;
        postgisCmd.Parameters.AddWithValue("wkt", ringWkt);

        await using NpgsqlDataReader pgReader = await postgisCmd.ExecuteReaderAsync(cancellationToken);
        if (!await pgReader.ReadAsync(cancellationToken))
        {
            return new BoundaryAnalysisResult(
                BoundaryAnalysisStatus.NeedsReview,
                "invalid_geometry",
                null,
                null,
                0m,
                0m,
                0m,
                totalWalkedDistance,
                rawAccepted.Count,
                outliersRemoved,
                false,
                0m);
        }

        bool originallyValid = pgReader.GetBoolean(0);
        double totalArea = pgReader.GetDouble(1);
        double primaryArea = pgReader.GetDouble(2);
        bool primaryValid = pgReader.GetBoolean(3);
        double geoAreaSqm = pgReader.GetDouble(4);
        double geoPerimeterM = pgReader.GetDouble(5);
        string? primaryWkt = pgReader.IsDBNull(6) ? null : pgReader.GetString(6);
        string? primaryGeoJson = pgReader.IsDBNull(7) ? null : pgReader.GetString(7);

        double lostRatio = totalArea > 0.0
            ? Math.Max(0.0, (totalArea - primaryArea) / totalArea)
            : 1.0;

        bool repaired = false;
        decimal repairDeltaPct = 0m;

        if (!originallyValid)
        {
            if (!primaryValid || primaryWkt is null || primaryArea <= 0.0 || lostRatio >= MaxAutoRepairAreaLossRatio)
            {
                return new BoundaryAnalysisResult(
                    BoundaryAnalysisStatus.NeedsReview,
                    "major_self_intersection",
                    primaryWkt,
                    primaryGeoJson,
                    Math.Round((decimal)geoAreaSqm, 2),
                    Math.Round((decimal)(geoAreaSqm / 10000.0), 4),
                    Math.Round((decimal)geoPerimeterM, 2),
                    totalWalkedDistance,
                    rawAccepted.Count,
                    outliersRemoved,
                    false,
                    Math.Round((decimal)(lostRatio * 100.0), 2));
            }

            repaired = true;
            repairDeltaPct = Math.Round((decimal)(lostRatio * 100.0), 2);
        }

        if (!primaryValid || primaryWkt is null || geoAreaSqm < MinValidAreaSquareMeters)
        {
            return new BoundaryAnalysisResult(
                BoundaryAnalysisStatus.NeedsReview,
                "near_zero_area",
                primaryWkt,
                primaryGeoJson,
                Math.Round((decimal)geoAreaSqm, 2),
                Math.Round((decimal)(geoAreaSqm / 10000.0), 4),
                Math.Round((decimal)geoPerimeterM, 2),
                totalWalkedDistance,
                rawAccepted.Count,
                outliersRemoved,
                repaired,
                repairDeltaPct);
        }

        return new BoundaryAnalysisResult(
            BoundaryAnalysisStatus.Valid,
            null,
            primaryWkt,
            primaryGeoJson,
            Math.Round((decimal)geoAreaSqm, 2),
            Math.Round((decimal)(geoAreaSqm / 10000.0), 4),
            Math.Round((decimal)geoPerimeterM, 2),
            totalWalkedDistance,
            rawAccepted.Count,
            outliersRemoved,
            repaired,
            repairDeltaPct);
    }

    internal static (List<TrackPointSample> Kept, int OutliersRemoved) FilterOutliers(IReadOnlyList<TrackPointSample> points)
    {
        if (points.Count <= 4)
        {
            return ([.. points], 0);
        }

        List<TrackPointSample> kept = [points[0]];
        int removed = 0;

        for (int i = 1; i < points.Count - 1; i++)
        {
            TrackPointSample prev = kept[^1];
            TrackPointSample curr = points[i];
            TrackPointSample next = points[i + 1];

            double dPrevCurr = HaversineMeters(prev.Lat, prev.Lng, curr.Lat, curr.Lng);
            double dCurrNext = HaversineMeters(curr.Lat, curr.Lng, next.Lat, next.Lng);
            double dPrevNext = HaversineMeters(prev.Lat, prev.Lng, next.Lat, next.Lng);
            double dtSeconds = (curr.RecordedAt - prev.RecordedAt).TotalSeconds;

            bool isDetourSpike = dPrevCurr > 100.0
                && dCurrNext > 100.0
                && (dPrevCurr + dCurrNext) > 4.0 * Math.Max(dPrevNext, 10.0);

            bool isTeleportSpike = dtSeconds > 0.0 && dtSeconds <= 5.0 && dPrevCurr > 150.0;

            if (isDetourSpike || isTeleportSpike)
            {
                removed++;
                continue;
            }

            kept.Add(curr);
        }

        kept.Add(points[^1]);
        return (kept, removed);
    }

    private static List<TrackPointSample> DeduplicateRingVertices(IReadOnlyList<TrackPointSample> points)
    {
        List<TrackPointSample> distinct = [];
        foreach (TrackPointSample p in points)
        {
            if (distinct.Count == 0 || !SameVertex(distinct[^1], p))
            {
                distinct.Add(p);
            }
        }

        if (distinct.Count > 1 && SameVertex(distinct[0], distinct[^1]))
        {
            distinct.RemoveAt(distinct.Count - 1);
        }

        return distinct;
    }

    private static bool SameVertex(TrackPointSample a, TrackPointSample b)
        => Math.Abs(a.Lat - b.Lat) < 1e-7 && Math.Abs(a.Lng - b.Lng) < 1e-7;

    private static string BuildClosedPolygonWkt(List<TrackPointSample> distinctVertices)
    {
        StringBuilder sb = new("POLYGON((");
        for (int i = 0; i < distinctVertices.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            AppendCoord(sb, distinctVertices[i]);
        }

        sb.Append(", ");
        AppendCoord(sb, distinctVertices[0]);
        sb.Append("))");
        return sb.ToString();
    }

    private static void AppendCoord(StringBuilder sb, TrackPointSample point)
    {
        sb.Append(point.Lng.ToString("G17", CultureInfo.InvariantCulture));
        sb.Append(' ');
        sb.Append(point.Lat.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static decimal ComputeWalkDistanceMeters(List<TrackPointSample> points)
    {
        double total = 0.0;
        for (int i = 1; i < points.Count; i++)
        {
            total += HaversineMeters(points[i - 1].Lat, points[i - 1].Lng, points[i].Lat, points[i].Lng);
        }

        return Math.Round((decimal)total, 2);
    }

    internal static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        double phi1 = lat1 * Math.PI / 180.0;
        double phi2 = lat2 * Math.PI / 180.0;
        double dPhi = (lat2 - lat1) * Math.PI / 180.0;
        double dLambda = (lng2 - lng1) * Math.PI / 180.0;

        double a = Math.Sin(dPhi / 2.0) * Math.Sin(dPhi / 2.0)
                   + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2.0) * Math.Sin(dLambda / 2.0);
        double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMeters * c;
    }
}
