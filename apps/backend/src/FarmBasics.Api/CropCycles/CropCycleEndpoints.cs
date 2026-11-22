using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.CropCycles;

public sealed record StartCropCycleRequest(
    Guid ProductionUnitId,
    Guid CropProfileId,
    DateOnly? PlantingDate = null);

public sealed record LogOperationTaskRequest(
    JsonElement? Data = null,
    DateTimeOffset? LoggedAt = null,
    Guid? ClientGuid = null);

public sealed record OverrideOperationLogRequest(string Reason);

public sealed record AdvanceCycleStageRequest(string? TargetStage = null);

public sealed record CreateHarvestBatchRequest(
    decimal QuantityKg,
    string? Grade = null,
    decimal? MoisturePct = null,
    DateOnly? HarvestDate = null);

public sealed record UpdateBatchDispositionRequest(string Disposition);

internal sealed record TaskDefinitionRow(
    string Id,
    string Stage,
    string? SubPhase,
    string Name,
    bool Required,
    bool Recurring,
    string? CapabilityCode,
    string? ValidationField);

/// <summary>
/// Crop lifecycle API implementing WP-70..WP-72: CropProfile browsing, CropCycle state machine +
/// BR-A-07 open-field concurrency enforcement, OperationLog OK/Warning/Flag validation, and immutable overrides.
/// </summary>
internal static class CropCycleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedDispositions = new(StringComparer.Ordinal)
    {
        "sold", "stored", "processed", "lost"
    };

    public static void MapCropCycleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/crop-profiles", HandleListCropProfilesAsync);

        app.MapPost("/v1/crop-cycles", HandleStartCropCycleAsync)
            .RequireAuthorization();

        app.MapGet("/v1/crop-cycles/{cycleId:guid}/tasks", HandleGetCycleTasksAsync)
            .RequireAuthorization();

        app.MapPost("/v1/crop-cycles/{cycleId:guid}/tasks/{taskDefId}/log", HandleLogTaskAsync)
            .RequireAuthorization();

        app.MapPost("/v1/operation-logs/{logId:guid}/override", HandleOverrideLogAsync)
            .RequireAuthorization();

        app.MapPost("/v1/crop-cycles/{cycleId:guid}/advance-stage", HandleAdvanceStageAsync)
            .RequireAuthorization();

        app.MapPost("/v1/crop-cycles/{cycleId:guid}/abandon", HandleAbandonCycleAsync)
            .RequireAuthorization();

        app.MapPost("/v1/crop-cycles/{cycleId:guid}/harvest-batches", HandleCreateHarvestBatchAsync)
            .RequireAuthorization();

        app.MapGet("/v1/crop-cycles/{cycleId:guid}/harvest-batches", HandleListHarvestBatchesAsync)
            .RequireAuthorization();

        app.MapPatch("/v1/harvest-batches/{batchId:guid}/disposition", HandleUpdateBatchDispositionAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleListCropProfilesAsync(
        string? search,
        string? category,
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                id, common_name, botanical_name, category::text,
                typical_cycle_days_min, typical_cycle_days_max, planting_method::text,
                expected_yield_per_ha_kg, unit_of_sale::text, kg_per_unit,
                optimal_soil_ph_min, optimal_soil_ph_max, seed_depth_cm_min, seed_depth_cm_max,
                row_spacing_cm_min, row_spacing_cm_max, harvest_moisture_content_pct,
                source_citation, reviewed_by_admin_id, version_number, last_verified_at
            FROM field.crop_profile
            WHERE is_active = TRUE
              AND (@search IS NULL OR common_name ILIKE '%' || @search || '%' OR COALESCE(botanical_name, '') ILIKE '%' || @search || '%')
              AND (@category IS NULL OR category::text = @category)
            ORDER BY common_name ASC;
            """;
        NpgsqlParameter sParam = cmd.Parameters.Add("search", NpgsqlDbType.Text);
        sParam.Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
        NpgsqlParameter cParam = cmd.Parameters.Add("category", NpgsqlDbType.Text);
        cParam.Value = string.IsNullOrWhiteSpace(category) ? DBNull.Value : category.Trim().ToLowerInvariant();

        List<object> profiles = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new
            {
                id = reader.GetGuid(0),
                commonName = reader.GetString(1),
                botanicalName = reader.IsDBNull(2) ? null : reader.GetString(2),
                category = reader.GetString(3),
                typicalCycleDaysMin = reader.GetInt32(4),
                typicalCycleDaysMax = reader.GetInt32(5),
                plantingMethod = reader.GetString(6),
                expectedYieldPerHaKg = reader.GetDecimal(7),
                unitOfSale = reader.GetString(8),
                kgPerUnit = reader.GetDecimal(9),
                optimalSoilPhMin = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10),
                optimalSoilPhMax = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                seedDepthCmMin = reader.IsDBNull(12) ? (decimal?)null : reader.GetDecimal(12),
                seedDepthCmMax = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                rowSpacingCmMin = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14),
                rowSpacingCmMax = reader.IsDBNull(15) ? (decimal?)null : reader.GetDecimal(15),
                harvestMoistureContentPct = reader.IsDBNull(16) ? (decimal?)null : reader.GetDecimal(16),
                sourceCitation = reader.GetString(17),
                reviewedByAdminId = reader.IsDBNull(18) ? (Guid?)null : reader.GetGuid(18),
                versionNumber = reader.GetInt32(19),
                lastVerifiedAt = reader.GetFieldValue<DateTimeOffset>(20)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(profiles, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleStartCropCycleAsync(
        StartCropCycleRequest request,
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

        if (request.ProductionUnitId == Guid.Empty || request.CropProfileId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "ProductionUnitId and CropProfileId are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid unitOwnerId;
        string sysType;
        string medClass;
        decimal areaHa;
        await using (NpgsqlCommand unitCmd = conn.CreateCommand())
        {
            unitCmd.Transaction = tx;
            unitCmd.CommandText = """
                SELECT farm_identity_id, system_type::text, growing_medium_class::text,
                       COALESCE(area_hectares, ROUND((area_square_meters / 10000.0)::numeric, 6), 0.010000)
                FROM field.production_unit
                WHERE id = @unitId;
                """;
            unitCmd.Parameters.AddWithValue("unitId", request.ProductionUnitId);

            await using NpgsqlDataReader r = await unitCmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Production unit was not found.");
            }

            unitOwnerId = r.GetGuid(0);
            sysType = r.GetString(1);
            medClass = r.GetString(2);
            areaHa = r.GetDecimal(3);
        }

        if (unitOwnerId != farmerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this production unit.");
        }

        await using (NpgsqlCommand cropCmd = conn.CreateCommand())
        {
            cropCmd.Transaction = tx;
            cropCmd.CommandText = "SELECT is_active FROM field.crop_profile WHERE id = @id;";
            cropCmd.Parameters.AddWithValue("id", request.CropProfileId);
            if (await cropCmd.ExecuteScalarAsync(cancellationToken) is not bool active || !active)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Active CropProfile was not found.");
            }
        }

        Guid cycleId = Guid.NewGuid();
        DateOnly plantingDate = request.PlantingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            await using NpgsqlCommand insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO field.crop_cycle (
                    id, production_unit_id, crop_profile_id, planting_date, stage, area_ha,
                    unit_system_type, growing_medium_class
                )
                VALUES (
                    @id, @unitId, @cropId, @plantingDate, 'planning'::cycle_stage, @areaHa,
                    @sys::system_type, @med::growing_medium_class
                );
                """;
            insCmd.Parameters.AddWithValue("id", cycleId);
            insCmd.Parameters.AddWithValue("unitId", request.ProductionUnitId);
            insCmd.Parameters.AddWithValue("cropId", request.CropProfileId);
            insCmd.Parameters.AddWithValue("plantingDate", plantingDate);
            insCmd.Parameters.AddWithValue("areaHa", areaHa > 0m ? areaHa : 0.01m);
            insCmd.Parameters.AddWithValue("sys", sysType);
            insCmd.Parameters.AddWithValue("med", medClass);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);

            Guid? existingCycleId = null;
            string? existingStage = null;
            await using NpgsqlCommand lookupCmd = conn.CreateCommand();
            lookupCmd.CommandText = """
                SELECT id, stage::text
                FROM field.crop_cycle
                WHERE production_unit_id = @unitId
                  AND stage NOT IN ('closed', 'abandoned')
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            lookupCmd.Parameters.AddWithValue("unitId", request.ProductionUnitId);
            await using NpgsqlDataReader lr = await lookupCmd.ExecuteReaderAsync(cancellationToken);
            if (await lr.ReadAsync(cancellationToken))
            {
                existingCycleId = lr.GetGuid(0);
                existingStage = lr.GetString(1);
            }

            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail(
                        "ACTIVE_CYCLE_EXISTS",
                        "This open-field unit already has an active crop cycle (BR-A-07).",
                        new { existingCycleId, existingStage }),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status409Conflict);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "crop_cycle.created",
                "crop_cycle",
                cycleId,
                farmerId,
                JsonSerializer.Serialize(new { request.ProductionUnitId, request.CropProfileId, stage = "planning" }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    cycleId,
                    productionUnitId = request.ProductionUnitId,
                    cropProfileId = request.CropProfileId,
                    stage = "planning",
                    plantingDate,
                    unitSystemType = sysType,
                    growingMediumClass = medClass
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleGetCycleTasksAsync(
        Guid cycleId,
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

        CycleMetadata? meta = await LoadCycleMetadataAsync(conn, tx, cycleId, forUpdate: false, cancellationToken);
        if (meta is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "CropCycle was not found.");
        }

        if (meta.OwnerIdentityId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this CropCycle.");
        }

        List<TaskDefinitionRow> candidateTasks = await LoadStageTasksAsync(
            conn,
            tx,
            meta.Stage,
            meta.UnitSystemType,
            meta.GrowingMediumClass,
            meta.PlantingMethod,
            cancellationToken);

        Dictionary<string, LatestLogSnapshot> latestLogs = await LoadLatestLogsByTaskAsync(conn, tx, cycleId, cancellationToken);

        List<object> responseTasks = [];
        foreach (TaskDefinitionRow t in candidateTasks)
        {
            latestLogs.TryGetValue(t.Id, out LatestLogSnapshot? log);
            responseTasks.Add(new
            {
                taskDefinitionId = t.Id,
                stage = t.Stage,
                subPhase = t.SubPhase,
                name = t.Name,
                required = t.Required,
                recurring = t.Recurring,
                validationField = t.ValidationField,
                latestLogId = log?.LogId,
                status = log?.Status ?? "pending",
                validationMessage = log?.ValidationMessage,
                overrideReason = log?.OverrideReason,
                overrideAt = log?.OverrideAt,
                loggedAt = log?.LoggedAt
            });
        }

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { cycleId, stage = meta.Stage, tasks = responseTasks },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleLogTaskAsync(
        Guid cycleId,
        string taskDefId,
        LogOperationTaskRequest request,
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

        if (request.ClientGuid.HasValue && request.ClientGuid.Value != Guid.Empty)
        {
            await using NpgsqlCommand dupCmd = conn.CreateCommand();
            dupCmd.Transaction = tx;
            dupCmd.CommandText = """
                SELECT id, status::text, validation_message, logged_at
                FROM field.operation_log
                WHERE client_guid = @guid;
                """;
            dupCmd.Parameters.AddWithValue("guid", request.ClientGuid.Value);
            await using NpgsqlDataReader dr = await dupCmd.ExecuteReaderAsync(cancellationToken);
            if (await dr.ReadAsync(cancellationToken))
            {
                Guid existingId = dr.GetGuid(0);
                string existingStatus = dr.GetString(1);
                string? existingMsg = dr.IsDBNull(2) ? null : dr.GetString(2);
                DateTimeOffset existingLoggedAt = dr.GetFieldValue<DateTimeOffset>(3);
                await dr.CloseAsync();
                await tx.CommitAsync(cancellationToken);
                return Results.Ok(new ApiResponse<object>(
                    new { operationLogId = existingId, status = existingStatus, validationMessage = existingMsg, loggedAt = existingLoggedAt, replayed = true },
                    new ApiMeta(httpContext.TraceIdentifier)));
            }
        }

        CycleMetadata? meta = await LoadCycleMetadataAsync(conn, tx, cycleId, forUpdate: false, cancellationToken);
        if (meta is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "CropCycle was not found.");
        }

        if (meta.OwnerIdentityId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this CropCycle.");
        }

        if (meta.Stage is "closed" or "abandoned")
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status422UnprocessableEntity, "CYCLE_TERMINAL", "Cannot log tasks on a closed or abandoned CropCycle.");
        }

        string? validationField = null;
        string? capabilityCode = null;
        await using (NpgsqlCommand taskCmd = conn.CreateCommand())
        {
            taskCmd.Transaction = tx;
            taskCmd.CommandText = "SELECT validation_field, capability_code FROM field.operation_task_definition WHERE id = @id;";
            taskCmd.Parameters.AddWithValue("id", taskDefId.Trim());
            await using NpgsqlDataReader tr = await taskCmd.ExecuteReaderAsync(cancellationToken);
            if (!await tr.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", $"Task definition '{taskDefId}' was not found.");
            }
            validationField = tr.IsDBNull(0) ? null : tr.GetString(0);
            capabilityCode = tr.IsDBNull(1) ? null : tr.GetString(1);
        }

        if (!string.IsNullOrWhiteSpace(capabilityCode))
        {
            bool capEnabled = await NotificationAndCapabilityEndpoints.IsCapabilityEnabledAsync(conn, tx, capabilityCode, null, cancellationToken);
            if (!capEnabled)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "CAPABILITY_DISABLED", $"Task '{taskDefId}' is gated behind disabled capability module '{capabilityCode}'.");
            }
        }

        OperationValidationResult validation = OperationLogValidator.Validate(validationField, meta.Ranges, request.Data);
        if (!validation.IsInputValid)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, validation.ErrorCode ?? "VALIDATION_ERROR", validation.Message ?? "Invalid task measurement.");
        }

        Guid logId = Guid.NewGuid();
        DateTimeOffset effectiveLoggedAt = request.LoggedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        string? dataJson = request.Data.HasValue && request.Data.Value.ValueKind != JsonValueKind.Undefined
            ? request.Data.Value.GetRawText()
            : null;

        await using (NpgsqlCommand insCmd = conn.CreateCommand())
        {
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO field.operation_log (
                    id, crop_cycle_id, task_definition_id, logged_at, logged_by_identity_id,
                    status, data, validation_message, client_guid
                )
                VALUES (
                    @id, @cycleId, @taskDefId, @loggedAt, @actorId,
                    @status::task_status, CAST(@data AS jsonb), @msg, @clientGuid
                );
                """;
            insCmd.Parameters.AddWithValue("id", logId);
            insCmd.Parameters.AddWithValue("cycleId", cycleId);
            insCmd.Parameters.AddWithValue("taskDefId", taskDefId.Trim());
            insCmd.Parameters.AddWithValue("loggedAt", effectiveLoggedAt);
            insCmd.Parameters.AddWithValue("actorId", callerId);
            insCmd.Parameters.AddWithValue("status", validation.Status);
            NpgsqlParameter dataP = insCmd.Parameters.Add("data", NpgsqlDbType.Text);
            dataP.Value = dataJson is not null ? dataJson : DBNull.Value;
            NpgsqlParameter msgP = insCmd.Parameters.Add("msg", NpgsqlDbType.Text);
            msgP.Value = validation.Message is not null ? validation.Message : DBNull.Value;
            NpgsqlParameter cgP = insCmd.Parameters.Add("clientGuid", NpgsqlDbType.Uuid);
            cgP.Value = request.ClientGuid.HasValue && request.ClientGuid.Value != Guid.Empty ? request.ClientGuid.Value : DBNull.Value;
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "operation_log.recorded",
                "operation_log",
                logId,
                callerId,
                JsonSerializer.Serialize(new { cycleId, taskDefId, status = validation.Status, loggedAt = effectiveLoggedAt }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    operationLogId = logId,
                    cropCycleId = cycleId,
                    taskDefinitionId = taskDefId.Trim(),
                    status = validation.Status,
                    validationMessage = validation.Message,
                    loggedAt = effectiveLoggedAt
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleOverrideLogAsync(
        Guid logId,
        OverrideOperationLogRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid actorId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller identity is required.");
        }

        string reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Override reason is mandatory.");
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
        string status;
        DateTimeOffset? alreadyOverriddenAt;
        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = """
                SELECT pu.farm_identity_id, ol.status::text, ol.override_at
                FROM field.operation_log ol
                JOIN field.crop_cycle cc ON cc.id = ol.crop_cycle_id
                JOIN field.production_unit pu ON pu.id = cc.production_unit_id
                WHERE ol.id = @id
                FOR UPDATE OF ol;
                """;
            lockCmd.Parameters.AddWithValue("id", logId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "OperationLog entry was not found.");
            }

            ownerId = reader.GetGuid(0);
            status = reader.GetString(1);
            alreadyOverriddenAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        }

        if (ownerId != actorId)
        {
            bool isAdmin = await adminPermissionChecker.HasPermissionAsync(actorId.ToString(), "review:manage", cancellationToken);
            if (!isAdmin)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You cannot override another farmer's OperationLog entry.");
            }
        }

        if (!string.Equals(status, "flag", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status422UnprocessableEntity, "INVALID_OPERATION_STATUS", "Only flagged OperationLog entries can be overridden.");
        }

        if (alreadyOverriddenAt.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "ALREADY_OVERRIDDEN", "This OperationLog flag has already been overridden.");
        }

        // Note: status intentionally stays 'flag' (BR-A-09); override_reason/by/at coexist alongside it.
        DateTimeOffset overrideAt;
        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE field.operation_log
                SET override_reason = @reason,
                    override_by_identity_id = @actorId,
                    override_at = now()
                WHERE id = @id
                RETURNING override_at;
                """;
            updCmd.Parameters.AddWithValue("id", logId);
            updCmd.Parameters.AddWithValue("reason", reason);
            updCmd.Parameters.AddWithValue("actorId", actorId);
            await using NpgsqlDataReader rr = await updCmd.ExecuteReaderAsync(cancellationToken);
            await rr.ReadAsync(cancellationToken);
            overrideAt = rr.GetFieldValue<DateTimeOffset>(0);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "operation_log.overridden",
                "operation_log",
                logId,
                actorId,
                JsonSerializer.Serialize(new { originalStatus = "flag", overrideReason = reason }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                operationLogId = logId,
                status = "flag",
                overrideReason = reason,
                overrideByIdentityId = actorId,
                overrideAt
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleAdvanceStageAsync(
        Guid cycleId,
        AdvanceCycleStageRequest? request,
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

        CycleMetadata? meta = await LoadCycleMetadataAsync(conn, tx, cycleId, forUpdate: true, cancellationToken);
        if (meta is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "CropCycle was not found.");
        }

        if (meta.OwnerIdentityId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this CropCycle.");
        }

        string? nextStage = !string.IsNullOrWhiteSpace(request?.TargetStage)
            ? request.TargetStage.Trim().ToLowerInvariant()
            : CropCycleStateMachine.GetNextSequentialStage(meta.Stage);

        if (nextStage is null || !CropCycleStateMachine.CanTransition(meta.Stage, nextStage))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_STAGE_TRANSITION",
                $"Cannot transition CropCycle from '{meta.Stage}' to '{nextStage ?? "none"}'.");
        }

        if (!string.Equals(nextStage, "abandoned", StringComparison.Ordinal))
        {
            List<object> blockingTasks = await EvaluateStageExitBlockersAsync(conn, tx, cycleId, meta, cancellationToken);
            if (blockingTasks.Count > 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return Results.Json(
                    new ApiErrorResponse(
                        new ApiErrorDetail(
                            "STAGE_GATE_BLOCKED",
                            $"Cannot advance past '{meta.Stage}' until blocking requirements are resolved.",
                            new { currentStage = meta.Stage, targetStage = nextStage, blockingTasks }),
                        new ApiMeta(httpContext.TraceIdentifier)),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE field.crop_cycle
                SET stage = @stage::cycle_stage,
                    updated_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", cycleId);
            updCmd.Parameters.AddWithValue("stage", nextStage);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "crop_cycle.stage_advanced",
                "crop_cycle",
                cycleId,
                callerId,
                JsonSerializer.Serialize(new { fromStage = meta.Stage, toStage = nextStage }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { cycleId, previousStage = meta.Stage, stage = nextStage },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleAbandonCycleAsync(
        Guid cycleId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        return await HandleAdvanceStageAsync(
            cycleId,
            new AdvanceCycleStageRequest("abandoned"),
            httpContext,
            identityAccessor,
            dbSource,
            migrationRunner,
            cancellationToken);
    }

    private static async Task<IResult> HandleCreateHarvestBatchAsync(
        Guid cycleId,
        CreateHarvestBatchRequest request,
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

        if (request.QuantityKg <= 0m)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "QuantityKg must be greater than zero.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        CycleMetadata? meta = await LoadCycleMetadataAsync(conn, tx, cycleId, forUpdate: false, cancellationToken);
        if (meta is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "CropCycle was not found.");
        }

        if (meta.OwnerIdentityId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this CropCycle.");
        }

        Guid batchId = Guid.NewGuid();
        DateOnly harvestDate = request.HarvestDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await using (NpgsqlCommand insCmd = conn.CreateCommand())
        {
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO field.harvest_batch (id, crop_cycle_id, quantity_kg, grade, moisture_pct, harvest_date, disposition)
                VALUES (@id, @cycleId, @qty, @grade, @moisture, @harvestDate, 'open');
                """;
            insCmd.Parameters.AddWithValue("id", batchId);
            insCmd.Parameters.AddWithValue("cycleId", cycleId);
            insCmd.Parameters.AddWithValue("qty", request.QuantityKg);
            NpgsqlParameter gP = insCmd.Parameters.Add("grade", NpgsqlDbType.Text);
            gP.Value = string.IsNullOrWhiteSpace(request.Grade) ? DBNull.Value : request.Grade.Trim();
            NpgsqlParameter mP = insCmd.Parameters.Add("moisture", NpgsqlDbType.Numeric);
            mP.Value = request.MoisturePct.HasValue ? request.MoisturePct.Value : DBNull.Value;
            insCmd.Parameters.AddWithValue("harvestDate", harvestDate);
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("field", "harvest_batch.created", "harvest_batch", batchId, callerId, JsonSerializer.Serialize(new { cycleId, request.QuantityKg }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        decimal expectedCycleYieldKg = meta.AreaHa * meta.ExpectedYieldPerHaKg;
        bool yieldWarning = expectedCycleYieldKg > 0m && (request.QuantityKg > expectedCycleYieldKg * 2.0m || request.QuantityKg < expectedCycleYieldKg * 0.1m);

        return Results.Json(
            new ApiResponse<object>(
                new { harvestBatchId = batchId, cropCycleId = cycleId, quantityKg = request.QuantityKg, disposition = "open", yieldWarning },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListHarvestBatchesAsync(
        Guid cycleId,
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

        if (cycleId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "CropCycle identifier is invalid.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        CycleMetadata? meta = await LoadCycleMetadataAsync(conn, tx, cycleId, forUpdate: false, cancellationToken);
        if (meta is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "CropCycle was not found.");
        }

        if (meta.OwnerIdentityId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this CropCycle.");
        }

        List<object> batches = [];
        decimal totalHarvestedKg = 0m;
        int openBatchCount = 0;

        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, quantity_kg, grade, moisture_pct, harvest_date, disposition, created_at
                FROM field.harvest_batch
                WHERE crop_cycle_id = @cycleId
                ORDER BY harvest_date ASC, created_at ASC;
                """;
            cmd.Parameters.AddWithValue("cycleId", cycleId);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                decimal qty = reader.GetDecimal(1);
                string disp = reader.GetString(5);
                totalHarvestedKg += qty;
                if (string.Equals(disp, "open", StringComparison.Ordinal))
                {
                    openBatchCount++;
                }

                batches.Add(new
                {
                    harvestBatchId = reader.GetGuid(0),
                    quantityKg = qty,
                    grade = reader.IsDBNull(2) ? null : reader.GetString(2),
                    moisturePct = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                    harvestDate = reader.GetFieldValue<DateOnly>(4),
                    disposition = disp,
                    createdAt = reader.GetFieldValue<DateTimeOffset>(6)
                });
            }
        }

        await tx.CommitAsync(cancellationToken);

        decimal expectedCycleYieldKg = Math.Round(meta.AreaHa * meta.ExpectedYieldPerHaKg, 2, MidpointRounding.AwayFromZero);
        decimal? yieldVariancePct = expectedCycleYieldKg > 0m
            ? Math.Round((totalHarvestedKg - expectedCycleYieldKg) / expectedCycleYieldKg * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        return Results.Ok(new ApiResponse<object>(
            new
            {
                cropCycleId = cycleId,
                cropName = meta.Ranges.CommonName,
                cycleStage = meta.Stage,
                unitSystemType = meta.UnitSystemType,
                supportsContinuousHarvest = true,
                totalHarvestedKg,
                expectedCycleYieldKg,
                yieldVariancePct,
                openBatchCount,
                batches
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleUpdateBatchDispositionAsync(
        Guid batchId,
        UpdateBatchDispositionRequest request,
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

        string disposition = request.Disposition?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedDispositions.Contains(disposition))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Disposition must be one of sold, stored, processed, or lost.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid ownerId = Guid.Empty;
        string cycleStage = string.Empty;
        string previousDisposition = string.Empty;
        bool batchFound = false;

        await using (NpgsqlCommand ownerCmd = conn.CreateCommand())
        {
            ownerCmd.Transaction = tx;
            ownerCmd.CommandText = """
                SELECT pu.farm_identity_id, cc.stage::text, hb.disposition
                FROM field.harvest_batch hb
                JOIN field.crop_cycle cc ON cc.id = hb.crop_cycle_id
                JOIN field.production_unit pu ON pu.id = cc.production_unit_id
                WHERE hb.id = @id
                FOR UPDATE OF hb;
                """;
            ownerCmd.Parameters.AddWithValue("id", batchId);

            await using NpgsqlDataReader reader = await ownerCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                batchFound = true;
                ownerId = reader.GetGuid(0);
                cycleStage = reader.GetString(1);
                previousDisposition = reader.GetString(2);
            }
        }

        if (!batchFound)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Harvest batch was not found.");
        }

        if (ownerId != callerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "You do not own this harvest batch.");
        }

        if (cycleStage is "closed" or "abandoned")
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CYCLE_NOT_OPEN", "Harvest batch disposition cannot be modified after a crop cycle is closed or abandoned.");
        }

        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = "UPDATE field.harvest_batch SET disposition = @disp WHERE id = @id;";
            updCmd.Parameters.AddWithValue("id", batchId);
            updCmd.Parameters.AddWithValue("disp", disposition);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "field",
                "harvest_batch.disposition_updated",
                "harvest_batch",
                batchId,
                callerId,
                JsonSerializer.Serialize(new { previousDisposition, disposition }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(new { harvestBatchId = batchId, previousDisposition, disposition }, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<List<object>> EvaluateStageExitBlockersAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid cycleId,
        CycleMetadata meta,
        CancellationToken cancellationToken)
    {
        List<object> blockers = [];
        List<TaskDefinitionRow> stageTasks = await LoadStageTasksAsync(
            conn,
            tx,
            meta.Stage,
            meta.UnitSystemType,
            meta.GrowingMediumClass,
            meta.PlantingMethod,
            cancellationToken);
        Dictionary<string, LatestLogSnapshot> latestLogs = await LoadLatestLogsByTaskAsync(conn, tx, cycleId, cancellationToken);

        foreach (TaskDefinitionRow task in stageTasks.Where(t => t.Required))
        {
            if (!latestLogs.TryGetValue(task.Id, out LatestLogSnapshot? log))
            {
                blockers.Add(new { taskDefinitionId = task.Id, reason = "REQUIRED_TASK_NOT_LOGGED" });
                continue;
            }

            if (string.Equals(log.Status, "flag", StringComparison.Ordinal) && !log.OverrideAt.HasValue)
            {
                blockers.Add(new { taskDefinitionId = task.Id, operationLogId = log.LogId, reason = "UNRESOLVED_FLAG" });
            }
        }

        if (string.Equals(meta.Stage, "crop_care", StringComparison.Ordinal))
        {
            DateOnly minHarvestDate = meta.PlantingDate.AddDays(meta.TypicalCycleDaysMin);
            if (DateOnly.FromDateTime(DateTime.UtcNow) < minHarvestDate)
            {
                blockers.Add(new { reason = "MATURITY_MIN_DAYS_NOT_ELAPSED", earliestDate = minHarvestDate });
            }
        }
        else if (string.Equals(meta.Stage, "harvest", StringComparison.Ordinal))
        {
            await using NpgsqlCommand hbCmd = conn.CreateCommand();
            hbCmd.Transaction = tx;
            hbCmd.CommandText = "SELECT COUNT(*)::int FROM field.harvest_batch WHERE crop_cycle_id = @cid;";
            hbCmd.Parameters.AddWithValue("cid", cycleId);
            int count = Convert.ToInt32(await hbCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (count == 0)
            {
                blockers.Add(new { reason = "HARVEST_BATCH_REQUIRED" });
            }
        }
        else if (string.Equals(meta.Stage, "post_harvest", StringComparison.Ordinal))
        {
            await using NpgsqlCommand dispCmd = conn.CreateCommand();
            dispCmd.Transaction = tx;
            dispCmd.CommandText = """
                SELECT COUNT(*)::int,
                       COUNT(*) FILTER (WHERE disposition = 'open')::int
                FROM field.harvest_batch
                WHERE crop_cycle_id = @cid;
                """;
            dispCmd.Parameters.AddWithValue("cid", cycleId);
            await using NpgsqlDataReader r = await dispCmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                int total = r.GetInt32(0);
                int openCount = r.GetInt32(1);
                if (total == 0 || openCount > 0)
                {
                    blockers.Add(new { reason = "OPEN_HARVEST_BATCH_DISPOSITION_REQUIRED", openBatchCount = openCount });
                }
            }
        }

        return blockers;
    }

    private static async Task<CycleMetadata?> LoadCycleMetadataAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid cycleId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        string lockClause = forUpdate ? "FOR UPDATE OF cc" : string.Empty;
        cmd.CommandText = $"""
            SELECT
                pu.farm_identity_id, cc.stage::text, cc.unit_system_type::text, cc.growing_medium_class::text,
                cc.planting_date, cc.area_ha, cp.common_name, cp.planting_method::text,
                cp.typical_cycle_days_min, cp.expected_yield_per_ha_kg,
                cp.optimal_soil_ph_min, cp.optimal_soil_ph_max,
                cp.seed_depth_cm_min, cp.seed_depth_cm_max,
                cp.row_spacing_cm_min, cp.row_spacing_cm_max,
                cp.harvest_moisture_content_pct
            FROM field.crop_cycle cc
            JOIN field.production_unit pu ON pu.id = cc.production_unit_id
            JOIN field.crop_profile cp ON cp.id = cc.crop_profile_id
            WHERE cc.id = @id
            {lockClause};
            """;
        cmd.Parameters.AddWithValue("id", cycleId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CycleMetadata(
            OwnerIdentityId: reader.GetGuid(0),
            Stage: reader.GetString(1),
            UnitSystemType: reader.GetString(2),
            GrowingMediumClass: reader.GetString(3),
            PlantingDate: reader.GetFieldValue<DateOnly>(4),
            AreaHa: reader.GetDecimal(5),
            PlantingMethod: reader.GetString(7),
            TypicalCycleDaysMin: reader.GetInt32(8),
            ExpectedYieldPerHaKg: reader.GetDecimal(9),
            Ranges: new CropProfileValidationRanges(
                CommonName: reader.GetString(6),
                OptimalSoilPhMin: reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                OptimalSoilPhMax: reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                SeedDepthCmMin: reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                SeedDepthCmMax: reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                RowSpacingCmMin: reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                RowSpacingCmMax: reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                HarvestMoistureContentPct: reader.IsDBNull(16) ? null : reader.GetDecimal(16)));
    }

    private static async Task<List<TaskDefinitionRow>> LoadStageTasksAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string stage,
        string systemType,
        string mediumClass,
        string plantingMethod,
        CancellationToken cancellationToken)
    {
        bool isTransplant = string.Equals(plantingMethod, "transplant", StringComparison.Ordinal);
        List<TaskDefinitionRow> raw = [];
        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, stage::text, sub_phase::text, name, required, recurring, capability_code, validation_field
                FROM field.operation_task_definition
                WHERE stage::text = @stage
                  AND @sys::system_type = ANY(applicable_system_types)
                  AND @med::growing_medium_class = ANY(applicable_medium_classes)
                  AND (requires_transplant = FALSE OR @isTransplant = TRUE)
                ORDER BY id ASC;
                """;
            cmd.Parameters.AddWithValue("stage", stage);
            cmd.Parameters.AddWithValue("sys", systemType);
            cmd.Parameters.AddWithValue("med", mediumClass);
            cmd.Parameters.AddWithValue("isTransplant", isTransplant);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                raw.Add(new TaskDefinitionRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        List<TaskDefinitionRow> filtered = [];
        foreach (TaskDefinitionRow row in raw)
        {
            if (!string.IsNullOrWhiteSpace(row.CapabilityCode))
            {
                bool enabled = await NotificationAndCapabilityEndpoints.IsCapabilityEnabledAsync(
                    conn,
                    tx,
                    row.CapabilityCode,
                    organizationId: null,
                    cancellationToken);
                if (!enabled)
                {
                    continue;
                }
            }

            filtered.Add(row);
        }

        return filtered;
    }

    private static async Task<Dictionary<string, LatestLogSnapshot>> LoadLatestLogsByTaskAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid cycleId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, LatestLogSnapshot> map = new(StringComparer.Ordinal);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT DISTINCT ON (task_definition_id)
                task_definition_id, id, status::text, validation_message, override_reason, override_at, logged_at
            FROM field.operation_log
            WHERE crop_cycle_id = @cycleId
            ORDER BY task_definition_id, server_received_at DESC, logged_at DESC;
            """;
        cmd.Parameters.AddWithValue("cycleId", cycleId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            map[reader.GetString(0)] = new LatestLogSnapshot(
                LogId: reader.GetGuid(1),
                Status: reader.GetString(2),
                ValidationMessage: reader.IsDBNull(3) ? null : reader.GetString(3),
                OverrideReason: reader.IsDBNull(4) ? null : reader.GetString(4),
                OverrideAt: reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                LoggedAt: reader.GetFieldValue<DateTimeOffset>(6));
        }

        return map;
    }

    private sealed record CycleMetadata(
        Guid OwnerIdentityId,
        string Stage,
        string UnitSystemType,
        string GrowingMediumClass,
        DateOnly PlantingDate,
        decimal AreaHa,
        string PlantingMethod,
        int TypicalCycleDaysMin,
        decimal ExpectedYieldPerHaKg,
        CropProfileValidationRanges Ranges);

    private sealed record LatestLogSnapshot(
        Guid LogId,
        string Status,
        string? ValidationMessage,
        string? OverrideReason,
        DateTimeOffset? OverrideAt,
        DateTimeOffset LoggedAt);
}
