using System.Globalization;
using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using FarmBasics.BuildingBlocks.Observability;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.ProductionUnits;

public sealed record TrackingPointBatchItem(
    Guid ClientGuid,
    int SequenceNumber,
    double Lat,
    double Lng,
    double AccuracyM,
    DateTimeOffset RecordedAt,
    double? SpeedMps = null);

public sealed record TrackingPointBatchRequest(IReadOnlyList<TrackingPointBatchItem> Points);

public sealed record ConfirmBoundarySessionRequest(
    string Name,
    string SystemType = "open_field",
    string GrowingMediumClass = "soil_based",
    Guid? LgaId = null,
    short TierCount = 1,
    decimal? ClientAreaSqm = null);

public sealed record GeoPinRequest(double Lat, double Lng, double? AccuracyM = null);

public sealed record CreateNonWalkedProductionUnitRequest(
    string Name,
    string SystemType,
    string GrowingMediumClass,
    string CaptureMethod,
    GeoPinRequest? Pin = null,
    decimal? FootprintSqm = null,
    string? Address = null,
    Guid? LgaId = null,
    short TierCount = 1);

public sealed record PatchProductionUnitRequest(
    string? Name = null,
    string? SystemType = null,
    string? GrowingMediumClass = null,
    short? TierCount = null,
    string? SoilType = null,
    string? DrainageClass = null,
    decimal? SlopePercent = null,
    decimal? ElevationM = null,
    JsonElement? Boundary = null,
    string? CaptureMethod = null,
    string? AreaBasis = null);

/// <summary>
/// ProductionUnit & walked boundary API endpoints per Doc/02B-ENG §2, §4 and WP-50..WP-52.
/// Strictly mounted under /v1/production-units (zero /v1/fields routes).
/// </summary>
internal static class ProductionUnitEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedSystemTypes = new(StringComparer.Ordinal)
    {
        "open_field", "greenhouse", "shade_net_house", "container_farm",
        "dutch_bucket", "substrate_trough", "hydroponic_nft", "hydroponic_dwc",
        "aeroponic", "aquaponic", "vertical_rack", "kratky", "ebb_flow",
        "wick_system", "hydroponic_fodder", "fogponic"
    };

    private static readonly HashSet<string> AllowedMediumClasses = new(StringComparer.Ordinal)
    {
        "soil_based", "soilless"
    };

    private static readonly HashSet<string> NonWalkedCaptureMethods = new(StringComparer.Ordinal)
    {
        "gps_pin_drop_with_footprint", "manual_footprint_entry", "address_only"
    };

    private static readonly HashSet<string> AllowedDrainageClasses = new(StringComparer.Ordinal)
    {
        "poor", "moderate", "good"
    };

    public static void MapProductionUnitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/production-units/boundary-sessions", HandleStartSessionAsync)
            .RequireAuthorization();

        app.MapPost("/v1/production-units/boundary-sessions/{sessionId:guid}/points/batch", HandleUploadPointsBatchAsync)
            .RequireAuthorization();

        app.MapPost("/v1/production-units/boundary-sessions/{sessionId:guid}/complete", HandleCompleteSessionAsync)
            .RequireAuthorization();

        app.MapPost("/v1/production-units/boundary-sessions/{sessionId:guid}/confirm", HandleConfirmSessionAsync)
            .RequireAuthorization();

        app.MapPost("/v1/production-units", HandleCreateNonWalkedUnitAsync)
            .RequireAuthorization();

        app.MapGet("/v1/production-units", HandleListUnitsAsync)
            .RequireAuthorization();

        app.MapGet("/v1/production-units/{unitId:guid}", HandleGetUnitAsync)
            .RequireAuthorization();

        app.MapPatch("/v1/production-units/{unitId:guid}", HandlePatchUnitAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleStartSessionAsync(
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid farmerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid sessionId = Guid.NewGuid();
        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO field.field_tracking_session (id, farm_identity_id, status, started_at, updated_at)
                VALUES (@id, @farmerId, 'recording', now(), now());
                """;
            cmd.Parameters.AddWithValue("id", sessionId);
            cmd.Parameters.AddWithValue("farmerId", farmerId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("field", "tracking_session.started", "field_tracking_session", sessionId, farmerId, null),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { sessionId, status = "recording" }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleUploadPointsBatchAsync(
        Guid sessionId,
        TrackingPointBatchRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        if (request.Points is null || request.Points.Count == 0)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "At least one tracking point is required.");
        }

        foreach (TrackingPointBatchItem pt in request.Points)
        {
            if (pt.ClientGuid == Guid.Empty
                || double.IsNaN(pt.Lat) || double.IsInfinity(pt.Lat) || pt.Lat is < -90.0 or > 90.0
                || double.IsNaN(pt.Lng) || double.IsInfinity(pt.Lng) || pt.Lng is < -180.0 or > 180.0)
            {
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Each point must provide a valid ClientGuid and WGS-84 coordinates.");
            }
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        (bool exists, Guid ownerId, string? sessionStatus) = await LoadSessionOwnershipAsync(conn, tx, sessionId, cancellationToken);
        if (!exists)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Tracking session was not found.");
        }

        if (ownerId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this tracking session.");
        }

        if (string.Equals(sessionStatus, "saved", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "SESSION_ALREADY_SAVED", "Tracking session is already saved.");
        }

        int acceptedCount = 0;
        int rejectedLowAccuracyCount = 0;
        int duplicatesIgnored = 0;

        foreach (TrackingPointBatchItem pt in request.Points.OrderBy(p => p.SequenceNumber).ThenBy(p => p.RecordedAt))
        {
            bool accuracyOk = pt.AccuracyM > 0.0 && pt.AccuracyM <= 15.0;
            await using NpgsqlCommand insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO field.tracking_point (
                    session_id, client_guid, sequence_number, location,
                    accuracy_meters, speed_mps, recorded_at, accepted, rejection_reason
                )
                VALUES (
                    @sessionId, @clientGuid, @seq, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography,
                    @acc, @speed, @recordedAt, @accepted, @reason
                )
                ON CONFLICT (session_id, client_guid) DO NOTHING
                RETURNING id;
                """;
            insCmd.Parameters.AddWithValue("sessionId", sessionId);
            insCmd.Parameters.AddWithValue("clientGuid", pt.ClientGuid);
            insCmd.Parameters.AddWithValue("seq", pt.SequenceNumber);
            insCmd.Parameters.AddWithValue("lng", pt.Lng);
            insCmd.Parameters.AddWithValue("lat", pt.Lat);
            insCmd.Parameters.AddWithValue("acc", Math.Round((decimal)pt.AccuracyM, 2));
            NpgsqlParameter speedParam = insCmd.Parameters.Add("speed", NpgsqlDbType.Numeric);
            speedParam.Value = pt.SpeedMps.HasValue ? Math.Round((decimal)pt.SpeedMps.Value, 2) : DBNull.Value;
            insCmd.Parameters.AddWithValue("recordedAt", pt.RecordedAt.ToUniversalTime());
            insCmd.Parameters.AddWithValue("accepted", accuracyOk);
            NpgsqlParameter reasonParam = insCmd.Parameters.Add("reason", NpgsqlDbType.Text);
            reasonParam.Value = accuracyOk ? DBNull.Value : "low_accuracy";

            object? inserted = await insCmd.ExecuteScalarAsync(cancellationToken);
            if (inserted is not Guid)
            {
                duplicatesIgnored++;
            }
            else if (accuracyOk)
            {
                acceptedCount++;
            }
            else
            {
                rejectedLowAccuracyCount++;
            }
        }

        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE field.field_tracking_session
                SET accepted_point_count = (SELECT COUNT(*)::int FROM field.tracking_point WHERE session_id = @sessionId AND accepted = TRUE),
                    updated_at = now()
                WHERE id = @sessionId;
                """;
            updCmd.Parameters.AddWithValue("sessionId", sessionId);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    accepted = acceptedCount,
                    rejectedLowAccuracy = rejectedLowAccuracyCount,
                    duplicatesIgnored
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> HandleCompleteSessionAsync(
        Guid sessionId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        (bool exists, Guid ownerId, _) = await LoadSessionOwnershipAsync(conn, tx, sessionId, cancellationToken);
        if (!exists)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Tracking session was not found.");
        }

        if (ownerId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this tracking session.");
        }

        BoundaryAnalysisResult analysis = await BoundaryGeometryService.AnalyzeAndRepairAsync(conn, tx, sessionId, cancellationToken);
        if (analysis.Status == BoundaryAnalysisStatus.InsufficientPoints)
        {
            await using NpgsqlCommand invCmd = conn.CreateCommand();
            invCmd.Transaction = tx;
            invCmd.CommandText = """
                UPDATE field.field_tracking_session
                SET status = 'invalid',
                    validation_issue = 'insufficient_points',
                    distance_meters = @dist,
                    accepted_point_count = @ptCount,
                    updated_at = now()
                WHERE id = @id;
                """;
            invCmd.Parameters.AddWithValue("id", sessionId);
            invCmd.Parameters.AddWithValue("dist", analysis.DistanceMeters);
            invCmd.Parameters.AddWithValue("ptCount", analysis.AcceptedPointCount);
            await invCmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            PlatformMetrics.PilotBoundaryWalkOutcomesTotal.WithLabels("invalid").Inc();

            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status422UnprocessableEntity, "INSUFFICIENT_POINTS", "At least 3 distinct valid GPS points are required to close a field boundary.");
        }

        string statusString = analysis.Status == BoundaryAnalysisStatus.Valid ? "valid" : "review";
        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE field.field_tracking_session
                SET status = @status::tracking_status,
                    completed_at = now(),
                    distance_meters = @dist,
                    accepted_point_count = @ptCount,
                    computed_boundary = CASE WHEN @wkt IS NULL THEN NULL ELSE ST_GeomFromText(@wkt, 4326)::geography END,
                    computed_area_sqm = @areaSqm,
                    computed_perimeter_m = @perimM,
                    validation_issue = @issue,
                    updated_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", sessionId);
            updCmd.Parameters.AddWithValue("status", statusString);
            updCmd.Parameters.AddWithValue("dist", analysis.DistanceMeters);
            updCmd.Parameters.AddWithValue("ptCount", analysis.AcceptedPointCount);
            NpgsqlParameter wktParam = updCmd.Parameters.Add("wkt", NpgsqlDbType.Text);
            wktParam.Value = analysis.BoundaryWkt is not null ? analysis.BoundaryWkt : DBNull.Value;
            updCmd.Parameters.AddWithValue("areaSqm", analysis.AreaSquareMeters);
            updCmd.Parameters.AddWithValue("perimM", analysis.PerimeterMeters);
            NpgsqlParameter issueParam = updCmd.Parameters.Add("issue", NpgsqlDbType.Text);
            issueParam.Value = analysis.Issue is not null ? analysis.Issue : DBNull.Value;
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "tracking_session.completed",
                "field_tracking_session",
                sessionId,
                callerId,
                JsonSerializer.Serialize(new
                {
                    status = statusString,
                    areaSquareMeters = analysis.AreaSquareMeters,
                    repaired = analysis.Repaired,
                    repairDeltaPct = analysis.RepairDeltaPct,
                    outliersRemoved = analysis.OutliersRemoved,
                    issue = analysis.Issue
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        if (analysis.Status == BoundaryAnalysisStatus.NeedsReview)
        {
            PlatformMetrics.PilotBoundaryWalkOutcomesTotal.WithLabels("review_required").Inc();
            return Results.Ok(new ApiResponse<object>(
                new
                {
                    sessionId,
                    status = "review",
                    issue = analysis.Issue,
                    areaSquareMeters = analysis.AreaSquareMeters,
                    areaHectares = analysis.AreaHectares,
                    perimeterMeters = analysis.PerimeterMeters,
                    outliersRemoved = analysis.OutliersRemoved,
                    repaired = analysis.Repaired,
                    repairDeltaPct = analysis.RepairDeltaPct,
                    requiresConfirmation = false
                },
                new ApiMeta(httpContext.TraceIdentifier)));
        }

        JsonElement? geoJsonElement = analysis.BoundaryGeoJson is not null
            ? JsonDocument.Parse(analysis.BoundaryGeoJson).RootElement.Clone()
            : null;

        return Results.Ok(new ApiResponse<object>(
            new
            {
                sessionId,
                status = "valid",
                boundary = geoJsonElement,
                areaSquareMeters = analysis.AreaSquareMeters,
                areaHectares = analysis.AreaHectares,
                perimeterMeters = analysis.PerimeterMeters,
                distanceMeters = analysis.DistanceMeters,
                outliersRemoved = analysis.OutliersRemoved,
                repaired = analysis.Repaired,
                repairDeltaPct = analysis.RepairDeltaPct,
                requiresConfirmation = false
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleConfirmSessionAsync(
        Guid sessionId,
        ConfirmBoundarySessionRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        string name = request.Name?.Trim() ?? string.Empty;
        string systemType = string.IsNullOrWhiteSpace(request.SystemType) ? "open_field" : request.SystemType.Trim().ToLowerInvariant();
        string mediumClass = string.IsNullOrWhiteSpace(request.GrowingMediumClass) ? "soil_based" : request.GrowingMediumClass.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name) || !AllowedSystemTypes.Contains(systemType) || !AllowedMediumClasses.Contains(mediumClass))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Valid Name, SystemType, and GrowingMediumClass are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid ownerId;
        string currentStatus;
        string? boundaryWkt;
        await using (NpgsqlCommand sessCmd = conn.CreateCommand())
        {
            sessCmd.Transaction = tx;
            sessCmd.CommandText = """
                SELECT farm_identity_id, status::text, ST_AsText(computed_boundary::geometry)
                FROM field.field_tracking_session
                WHERE id = @id
                FOR UPDATE;
                """;
            sessCmd.Parameters.AddWithValue("id", sessionId);

            await using NpgsqlDataReader reader = await sessCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Tracking session was not found.");
            }

            ownerId = reader.GetGuid(0);
            currentStatus = reader.GetString(1);
            boundaryWkt = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        if (ownerId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this tracking session.");
        }

        if (!string.Equals(currentStatus, "valid", StringComparison.Ordinal) || boundaryWkt is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status422UnprocessableEntity, "SESSION_NOT_VALID", "Only completed sessions with a valid boundary can be confirmed.");
        }

        if (!string.Equals(systemType, "open_field", StringComparison.Ordinal))
        {
            bool walkedCeaEnabled = await NotificationAndCapabilityEndpoints.IsCapabilityEnabledAsync(
                conn,
                tx,
                "cea.walked_units",
                organizationId: null,
                cancellationToken);
            if (!walkedCeaEnabled)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "CAPABILITY_DISABLED", "CEA walked production unit types are disabled behind capability flag cea.walked_units.");
            }
        }

        Guid unitId = Guid.NewGuid();
        decimal dbAreaSqm;
        decimal dbAreaHa;
        decimal dbAreaAcres;
        decimal dbPerimeterM;
        string dbAreaBasis;
        double centroidLat;
        double centroidLng;

        try
        {
            // Explicitly pass ClientAreaSqm (or fallback 1) into INSERT so DB trigger trg_pu_derive proves it overwrites client area server-side.
            await using NpgsqlCommand insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO field.production_unit (
                    id, farm_identity_id, name, system_type, capture_method, growing_medium_class,
                    boundary, lga_id, area_square_meters, area_hectares, area_acres, perimeter_meters,
                    area_basis, tier_count
                )
                VALUES (
                    @id, @farmerId, @name, @sys::system_type, 'gps_walked_boundary'::capture_method, @med::growing_medium_class,
                    ST_GeomFromText(@wkt, 4326)::geography, @lgaId, @clientAreaSqm, 0, 0, 0,
                    'declared'::area_basis, @tierCount
                )
                RETURNING area_square_meters, area_hectares, area_acres, perimeter_meters, area_basis::text,
                          ST_Y(centroid::geometry), ST_X(centroid::geometry);
                """;
            insertCmd.Parameters.AddWithValue("id", unitId);
            insertCmd.Parameters.AddWithValue("farmerId", callerId);
            insertCmd.Parameters.AddWithValue("name", name);
            insertCmd.Parameters.AddWithValue("sys", systemType);
            insertCmd.Parameters.AddWithValue("med", mediumClass);
            insertCmd.Parameters.AddWithValue("wkt", boundaryWkt);
            NpgsqlParameter lgaParam = insertCmd.Parameters.Add("lgaId", NpgsqlDbType.Uuid);
            lgaParam.Value = request.LgaId.HasValue ? request.LgaId.Value : DBNull.Value;
            insertCmd.Parameters.AddWithValue("clientAreaSqm", request.ClientAreaSqm ?? 999999m);
            insertCmd.Parameters.AddWithValue("tierCount", request.TierCount);

            await using NpgsqlDataReader retReader = await insertCmd.ExecuteReaderAsync(cancellationToken);
            await retReader.ReadAsync(cancellationToken);
            dbAreaSqm = retReader.GetDecimal(0);
            dbAreaHa = retReader.GetDecimal(1);
            dbAreaAcres = retReader.GetDecimal(2);
            dbPerimeterM = retReader.GetDecimal(3);
            dbAreaBasis = retReader.GetString(4);
            centroidLat = retReader.GetDouble(5);
            centroidLng = retReader.GetDouble(6);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return MapCheckConstraintError(httpContext, ex);
        }

        await using (NpgsqlCommand tenCmd = conn.CreateCommand())
        {
            tenCmd.Transaction = tx;
            tenCmd.CommandText = """
                INSERT INTO field.field_tenancy (id, production_unit_id, identity_id, tenancy_type, start_date, status)
                VALUES (gen_random_uuid(), @unitId, @farmerId, 'owner', CURRENT_DATE, 'active');
                """;
            tenCmd.Parameters.AddWithValue("unitId", unitId);
            tenCmd.Parameters.AddWithValue("farmerId", callerId);
            await tenCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        IReadOnlyList<OverlapDetectedDto> overlaps = await ProductionUnitConsumerService.DetectOverlapsForUnitAsync(conn, tx, unitId, cancellationToken);

        await using (NpgsqlCommand updSessCmd = conn.CreateCommand())
        {
            updSessCmd.Transaction = tx;
            updSessCmd.CommandText = """
                UPDATE field.field_tracking_session
                SET status = 'saved',
                    resulting_production_unit_id = @unitId,
                    updated_at = now()
                WHERE id = @sessionId;
                """;
            updSessCmd.Parameters.AddWithValue("sessionId", sessionId);
            updSessCmd.Parameters.AddWithValue("unitId", unitId);
            await updSessCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "production_unit.confirmed",
                "production_unit",
                unitId,
                callerId,
                JsonSerializer.Serialize(new { sessionId, systemType, areaSquareMeters = dbAreaSqm, overlapsDetected = overlaps.Count }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        PlatformMetrics.PilotBoundaryWalkOutcomesTotal.WithLabels("confirmed").Inc();

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    productionUnitId = unitId,
                    name,
                    systemType,
                    captureMethod = "gps_walked_boundary",
                    growingMediumClass = mediumClass,
                    areaSquareMeters = dbAreaSqm,
                    areaHectares = dbAreaHa,
                    areaAcres = dbAreaAcres,
                    perimeterMeters = dbPerimeterM,
                    areaBasis = dbAreaBasis,
                    centroid = new { lat = centroidLat, lng = centroidLng },
                    overlapsDetected = overlaps
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleCreateNonWalkedUnitAsync(
        CreateNonWalkedProductionUnitRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        string name = request.Name?.Trim() ?? string.Empty;
        string systemType = request.SystemType?.Trim().ToLowerInvariant() ?? string.Empty;
        string mediumClass = request.GrowingMediumClass?.Trim().ToLowerInvariant() ?? string.Empty;
        string captureMethod = request.CaptureMethod?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || !AllowedSystemTypes.Contains(systemType) || !AllowedMediumClasses.Contains(mediumClass))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Valid Name, SystemType, and GrowingMediumClass are required.");
        }

        if (!NonWalkedCaptureMethods.Contains(captureMethod))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "CAPTURE_SHAPE_INVALID", "Use boundary sessions for gps_walked_boundary or provide a valid non-walked captureMethod.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        bool ceaSoillessEnabled = await NotificationAndCapabilityEndpoints.IsCapabilityEnabledAsync(
            conn,
            tx,
            "cea.soilless",
            organizationId: null,
            cancellationToken);
        if (!ceaSoillessEnabled)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "CAPABILITY_DISABLED", "Non-walked CEA production unit capture is disabled behind capability flag cea.soilless.");
        }

        Guid unitId = Guid.NewGuid();
        try
        {
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO field.production_unit (
                    id, farm_identity_id, name, system_type, capture_method, growing_medium_class,
                    centroid, lga_id, address_text, area_square_meters, tier_count
                )
                VALUES (
                    @id, @farmerId, @name, @sys::system_type, @cap::capture_method, @med::growing_medium_class,
                    CASE WHEN @lat IS NULL OR @lng IS NULL THEN NULL ELSE ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography END,
                    @lgaId, @addr, @areaSqm, @tierCount
                );
                """;
            cmd.Parameters.AddWithValue("id", unitId);
            cmd.Parameters.AddWithValue("farmerId", callerId);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("sys", systemType);
            cmd.Parameters.AddWithValue("cap", captureMethod);
            cmd.Parameters.AddWithValue("med", mediumClass);

            NpgsqlParameter latParam = cmd.Parameters.Add("lat", NpgsqlDbType.Double);
            latParam.Value = request.Pin is not null ? request.Pin.Lat : DBNull.Value;
            NpgsqlParameter lngParam = cmd.Parameters.Add("lng", NpgsqlDbType.Double);
            lngParam.Value = request.Pin is not null ? request.Pin.Lng : DBNull.Value;
            NpgsqlParameter lgaParam = cmd.Parameters.Add("lgaId", NpgsqlDbType.Uuid);
            lgaParam.Value = request.LgaId.HasValue ? request.LgaId.Value : DBNull.Value;
            NpgsqlParameter addrParam = cmd.Parameters.Add("addr", NpgsqlDbType.Text);
            addrParam.Value = string.IsNullOrWhiteSpace(request.Address) ? DBNull.Value : request.Address.Trim();
            NpgsqlParameter areaParam = cmd.Parameters.Add("areaSqm", NpgsqlDbType.Numeric);
            areaParam.Value = request.FootprintSqm.HasValue ? request.FootprintSqm.Value : DBNull.Value;
            cmd.Parameters.AddWithValue("tierCount", request.TierCount);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return MapCheckConstraintError(httpContext, ex);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("field", "production_unit.created", "production_unit", unitId, callerId, JsonSerializer.Serialize(new { captureMethod, systemType }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { productionUnitId = unitId, name, systemType, captureMethod, growingMediumClass = mediumClass }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListUnitsAsync(
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, system_type::text, capture_method::text, growing_medium_class::text,
                   area_square_meters, area_hectares, area_basis::text, tier_count, lga_id
            FROM field.production_unit
            WHERE farm_identity_id = @farmerId
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("farmerId", callerId);

        List<object> units = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(new
            {
                id = reader.GetGuid(0),
                name = reader.GetString(1),
                systemType = reader.GetString(2),
                captureMethod = reader.GetString(3),
                growingMediumClass = reader.GetString(4),
                areaSquareMeters = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                areaHectares = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                areaBasis = reader.IsDBNull(7) ? null : reader.GetString(7),
                tierCount = reader.GetInt16(8),
                lgaId = reader.IsDBNull(9) ? (Guid?)null : reader.GetGuid(9)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(units, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleGetUnitAsync(
        Guid unitId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT farm_identity_id, name, system_type::text, capture_method::text, growing_medium_class::text,
                   area_square_meters, area_hectares, area_acres, perimeter_meters, area_basis::text,
                   tier_count, soil_type, drainage_class::text, slope_percent, elevation_m
            FROM field.production_unit
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", unitId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Production unit was not found.");
        }

        Guid ownerId = reader.GetGuid(0);
        if (ownerId != callerId)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not have access to this production unit.");
        }

        return Results.Ok(new ApiResponse<object>(
            new
            {
                id = unitId,
                farmIdentityId = ownerId,
                name = reader.GetString(1),
                systemType = reader.GetString(2),
                captureMethod = reader.GetString(3),
                growingMediumClass = reader.GetString(4),
                areaSquareMeters = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                areaHectares = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                areaAcres = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7),
                perimeterMeters = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                areaBasis = reader.IsDBNull(9) ? null : reader.GetString(9),
                tierCount = reader.GetInt16(10),
                soilType = reader.IsDBNull(11) ? null : reader.GetString(11),
                drainageClass = reader.IsDBNull(12) ? null : reader.GetString(12),
                slopePercent = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                elevationM = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14)
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandlePatchUnitAsync(
        Guid unitId,
        PatchProductionUnitRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        if ((request.Boundary.HasValue && request.Boundary.Value.ValueKind != JsonValueKind.Undefined)
            || !string.IsNullOrWhiteSpace(request.CaptureMethod)
            || !string.IsNullOrWhiteSpace(request.AreaBasis))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "BOUNDARY_IMMUTABLE", "Boundary, captureMethod, and areaBasis are immutable. Run a new boundary tracking session to correct geometry.");
        }

        if (request.SystemType is not null && !AllowedSystemTypes.Contains(request.SystemType.Trim().ToLowerInvariant()))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Unsupported systemType.");
        }

        if (request.GrowingMediumClass is not null && !AllowedMediumClasses.Contains(request.GrowingMediumClass.Trim().ToLowerInvariant()))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Unsupported growingMediumClass.");
        }

        if (request.DrainageClass is not null && !AllowedDrainageClasses.Contains(request.DrainageClass.Trim().ToLowerInvariant()))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Unsupported drainageClass.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand ownerCmd = conn.CreateCommand())
        {
            ownerCmd.Transaction = tx;
            ownerCmd.CommandText = "SELECT farm_identity_id FROM field.production_unit WHERE id = @id FOR UPDATE;";
            ownerCmd.Parameters.AddWithValue("id", unitId);

            object? scalar = await ownerCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar is not Guid ownerId)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Production unit was not found.");
            }

            if (ownerId != callerId)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You cannot modify another farmer's production unit.");
            }
        }

        try
        {
            await using NpgsqlCommand updCmd = conn.CreateCommand();
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE field.production_unit
                SET name = COALESCE(@name, name),
                    system_type = COALESCE(@sys::system_type, system_type),
                    growing_medium_class = COALESCE(@med::growing_medium_class, growing_medium_class),
                    tier_count = COALESCE(@tierCount, tier_count),
                    soil_type = COALESCE(@soilType, soil_type),
                    drainage_class = COALESCE(@drainage::drainage_class, drainage_class),
                    slope_percent = COALESCE(@slope, slope_percent),
                    elevation_m = COALESCE(@elev, elevation_m),
                    updated_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", unitId);
            NpgsqlParameter nameP = updCmd.Parameters.Add("name", NpgsqlDbType.Text);
            nameP.Value = string.IsNullOrWhiteSpace(request.Name) ? DBNull.Value : request.Name.Trim();
            NpgsqlParameter sysP = updCmd.Parameters.Add("sys", NpgsqlDbType.Text);
            sysP.Value = request.SystemType is null ? DBNull.Value : request.SystemType.Trim().ToLowerInvariant();
            NpgsqlParameter medP = updCmd.Parameters.Add("med", NpgsqlDbType.Text);
            medP.Value = request.GrowingMediumClass is null ? DBNull.Value : request.GrowingMediumClass.Trim().ToLowerInvariant();
            NpgsqlParameter tierP = updCmd.Parameters.Add("tierCount", NpgsqlDbType.Smallint);
            tierP.Value = request.TierCount.HasValue ? request.TierCount.Value : DBNull.Value;
            NpgsqlParameter soilP = updCmd.Parameters.Add("soilType", NpgsqlDbType.Text);
            soilP.Value = request.SoilType is null ? DBNull.Value : request.SoilType.Trim();
            NpgsqlParameter drainP = updCmd.Parameters.Add("drainage", NpgsqlDbType.Text);
            drainP.Value = request.DrainageClass is null ? DBNull.Value : request.DrainageClass.Trim().ToLowerInvariant();
            NpgsqlParameter slopeP = updCmd.Parameters.Add("slope", NpgsqlDbType.Numeric);
            slopeP.Value = request.SlopePercent.HasValue ? request.SlopePercent.Value : DBNull.Value;
            NpgsqlParameter elevP = updCmd.Parameters.Add("elev", NpgsqlDbType.Numeric);
            elevP.Value = request.ElevationM.HasValue ? request.ElevationM.Value : DBNull.Value;

            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return MapCheckConstraintError(httpContext, ex);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("field", "production_unit.updated", "production_unit", unitId, callerId, null),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(new { productionUnitId = unitId, updated = true }, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<(bool Exists, Guid OwnerId, string? Status)> LoadSessionOwnershipAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT farm_identity_id, status::text FROM field.field_tracking_session WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", sessionId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (false, Guid.Empty, null);
        }

        return (true, reader.GetGuid(0), reader.GetString(1));
    }

    private static IResult MapCheckConstraintError(HttpContext httpContext, PostgresException ex)
    {
        return ex.ConstraintName switch
        {
            "chk_medium_class" => SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "MEDIUM_CLASS_INCOMPATIBLE",
                "GrowingMediumClass is incompatible with the chosen SystemType."),
            "chk_capture_shape" or "chk_walked_only_boundary" => SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "CAPTURE_SHAPE_INVALID",
                "Submitted geometry/location fields do not satisfy the selected captureMethod shape contract."),
            _ => SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Production unit failed database constraint validation.")
        };
    }
}
