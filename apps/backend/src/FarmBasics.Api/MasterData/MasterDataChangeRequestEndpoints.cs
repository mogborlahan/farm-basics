using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.MasterData;

public sealed record ProposeChangeRequest(
    string TableName,
    Guid? RecordId,
    string ChangeType,
    JsonElement Diff);

public sealed record ApproveChangeRequest(string? ReviewNotes = null);

/// <summary>
/// Governed MasterDataChangeRequest lifecycle enforcing DB-level no-self-approval (WP-42 / BR-D-03 / Doc 02 §7).
/// </summary>
internal static class MasterDataChangeRequestEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/admin/master-data/change-requests", HandleProposeAsync)
            .RequireAuthorization()
            .RequireAdminPermission("master_data", "propose");

        app.MapGet("/v1/admin/master-data/change-requests", HandleListAsync)
            .RequireAuthorization()
            .RequireAdminPermission("master_data", "approve");

        app.MapPost("/v1/admin/master-data/change-requests/{changeRequestId:guid}/approve", HandleApproveAsync)
            .RequireAuthorization()
            .RequireAdminPermission("master_data", "approve");
    }

    private static async Task<IResult> HandleProposeAsync(
        ProposeChangeRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid proposerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated administrator is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TableName) || string.IsNullOrWhiteSpace(request.ChangeType))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "TableName and ChangeType are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid changeRequestId = Guid.NewGuid();
        string diffJson = request.Diff.ValueKind == JsonValueKind.Undefined ? "{}" : request.Diff.GetRawText();

        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO admin.master_data_change_request (
                    id, table_name, record_id, change_type, proposed_by_admin_id, diff, status
                )
                VALUES (@id, @tableName, @recordId, @changeType, @proposerId, CAST(@diff AS jsonb), 'pending_review');
                """;
            cmd.Parameters.AddWithValue("id", changeRequestId);
            cmd.Parameters.AddWithValue("tableName", request.TableName.Trim());
            NpgsqlParameter recParam = cmd.Parameters.Add("recordId", NpgsqlDbType.Uuid);
            recParam.Value = request.RecordId.HasValue ? request.RecordId.Value : DBNull.Value;
            cmd.Parameters.AddWithValue("changeType", request.ChangeType.Trim());
            cmd.Parameters.AddWithValue("proposerId", proposerId);
            cmd.Parameters.AddWithValue("diff", diffJson);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "admin",
                "master_data.change_proposed",
                "master_data_change_request",
                changeRequestId,
                proposerId,
                JsonSerializer.Serialize(new { tableName = request.TableName.Trim(), changeType = request.ChangeType.Trim() }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    changeRequestId,
                    tableName = request.TableName.Trim(),
                    changeType = request.ChangeType.Trim(),
                    status = "pending_review",
                    proposedByAdminId = proposerId
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(
        string? status,
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
            SELECT id, table_name, record_id, change_type, proposed_by_admin_id, proposed_at, diff::text, status::text, reviewed_by_admin_id, applied_at
            FROM admin.master_data_change_request
            WHERE (@status IS NULL OR status::text = @status)
            ORDER BY proposed_at DESC
            LIMIT 100;
            """;
        NpgsqlParameter statusParam = cmd.Parameters.Add("status", NpgsqlDbType.Text);
        statusParam.Value = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim();

        List<object> rows = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                id = reader.GetGuid(0),
                tableName = reader.GetString(1),
                recordId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
                changeType = reader.GetString(3),
                proposedByAdminId = reader.GetGuid(4),
                proposedAt = reader.GetFieldValue<DateTimeOffset>(5),
                diffJson = reader.GetString(6),
                status = reader.GetString(7),
                reviewedByAdminId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
                appliedAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(rows, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleApproveAsync(
        Guid changeRequestId,
        ApproveChangeRequest? request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid reviewerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated reviewer is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid proposedById;
        string currentStatus;
        string tableName;
        string changeType;
        string diffJson;
        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT proposed_by_admin_id, status::text, table_name, change_type, diff::text FROM admin.master_data_change_request WHERE id = @id FOR UPDATE;";
            lockCmd.Parameters.AddWithValue("id", changeRequestId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Master data change request was not found.");
            }

            proposedById = reader.GetGuid(0);
            currentStatus = reader.GetString(1);
            tableName = reader.GetString(2);
            changeType = reader.GetString(3);
            diffJson = reader.GetString(4);
        }

        if (proposedById == reviewerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "SELF_APPROVAL_BLOCKED",
                "Proposer cannot approve their own master data change request (BR-D-03).");
        }

        if (!string.Equals(currentStatus, "pending_review", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Change request is not pending review.");
        }

        try
        {
            await using NpgsqlCommand updCmd = conn.CreateCommand();
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE admin.master_data_change_request
                SET status = 'applied',
                    reviewed_by_admin_id = @reviewerId,
                    review_notes = @notes,
                    applied_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", changeRequestId);
            updCmd.Parameters.AddWithValue("reviewerId", reviewerId);
            NpgsqlParameter notesParam = updCmd.Parameters.Add("notes", NpgsqlDbType.Text);
            notesParam.Value = string.IsNullOrWhiteSpace(request?.ReviewNotes) ? DBNull.Value : request.ReviewNotes.Trim();
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "SELF_APPROVAL_BLOCKED",
                "Self-approval refused by database constraint chk_no_self_approval (BR-D-03).");
        }

        Guid? appliedRecordId = null;
        if (string.Equals(tableName, "field.crop_profile", StringComparison.OrdinalIgnoreCase))
        {
            (bool ok, Guid? cropId, string? errMsg) = await ApplyCropProfileDiffAsync(
                conn,
                tx,
                changeType,
                diffJson,
                reviewerId,
                cancellationToken);
            if (!ok)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(
                    httpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_ERROR",
                    errMsg ?? "CropProfile diff failed agronomic plausibility validation.");
            }
            appliedRecordId = cropId;
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("admin", "master_data.change_approved", "master_data_change_request", changeRequestId, reviewerId),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { changeRequestId, status = "applied", reviewedByAdminId = reviewerId, appliedRecordId },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<(bool Ok, Guid? CropProfileId, string? ErrorMessage)> ApplyCropProfileDiffAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string changeType,
        string diffJson,
        Guid reviewerId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(changeType, "deactivate", StringComparison.OrdinalIgnoreCase))
        {
            using JsonDocument delDoc = JsonDocument.Parse(diffJson);
            if (!delDoc.RootElement.TryGetProperty("commonName", out JsonElement cnEl) || string.IsNullOrWhiteSpace(cnEl.GetString()))
            {
                return (false, null, "commonName is required to deactivate a CropProfile.");
            }

            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE field.crop_profile
                SET is_active = FALSE,
                    reviewed_by_admin_id = @reviewerId,
                    last_verified_at = now(),
                    version_number = version_number + 1,
                    updated_at = now()
                WHERE common_name = @commonName
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("reviewerId", reviewerId);
            cmd.Parameters.AddWithValue("commonName", cnEl.GetString()!.Trim());
            object? res = await cmd.ExecuteScalarAsync(cancellationToken);
            return res is Guid id ? (true, id, null) : (false, null, "Referenced CropProfile was not found.");
        }

        using JsonDocument doc = JsonDocument.Parse(diffJson);
        JsonElement root = doc.RootElement;
        string commonName = root.TryGetProperty("commonName", out JsonElement c) ? c.GetString()?.Trim() ?? string.Empty : string.Empty;
        string category = root.TryGetProperty("category", out JsonElement cat) ? cat.GetString()?.Trim() ?? "cereal" : "cereal";
        int daysMin = root.TryGetProperty("typicalCycleDaysMin", out JsonElement dMin) ? dMin.GetInt32() : 0;
        int daysMax = root.TryGetProperty("typicalCycleDaysMax", out JsonElement dMax) ? dMax.GetInt32() : 0;
        string plantingMethod = root.TryGetProperty("plantingMethod", out JsonElement pm) ? pm.GetString()?.Trim() ?? "dibble" : "dibble";
        decimal expectedYield = root.TryGetProperty("expectedYieldPerHaKg", out JsonElement ey) ? ey.GetDecimal() : 0m;
        string unitOfSale = root.TryGetProperty("unitOfSale", out JsonElement uos) ? uos.GetString()?.Trim() ?? "kg" : "kg";
        decimal kgPerUnit = root.TryGetProperty("kgPerUnit", out JsonElement kpu) ? kpu.GetDecimal() : 1m;
        string sourceCitation = root.TryGetProperty("sourceCitation", out JsonElement src) ? src.GetString()?.Trim() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(commonName)
            || string.IsNullOrWhiteSpace(sourceCitation)
            || daysMin <= 0
            || daysMin > daysMax
            || expectedYield <= 0m
            || kgPerUnit <= 0m)
        {
            return (false, null, "CropProfile requires commonName, sourceCitation, valid cycle day bounds, expectedYieldPerHaKg > 0, and kgPerUnit > 0.");
        }

        decimal? soilPhMin = root.TryGetProperty("optimalSoilPhMin", out JsonElement spMin) && spMin.ValueKind == JsonValueKind.Number ? spMin.GetDecimal() : null;
        decimal? soilPhMax = root.TryGetProperty("optimalSoilPhMax", out JsonElement spMax) && spMax.ValueKind == JsonValueKind.Number ? spMax.GetDecimal() : null;
        decimal? seedDepthMin = root.TryGetProperty("seedDepthCmMin", out JsonElement sdMin) && sdMin.ValueKind == JsonValueKind.Number ? sdMin.GetDecimal() : null;
        decimal? seedDepthMax = root.TryGetProperty("seedDepthCmMax", out JsonElement sdMax) && sdMax.ValueKind == JsonValueKind.Number ? sdMax.GetDecimal() : null;
        decimal? rowSpacingMin = root.TryGetProperty("rowSpacingCmMin", out JsonElement rsMin) && rsMin.ValueKind == JsonValueKind.Number ? rsMin.GetDecimal() : null;
        decimal? rowSpacingMax = root.TryGetProperty("rowSpacingCmMax", out JsonElement rsMax) && rsMax.ValueKind == JsonValueKind.Number ? rsMax.GetDecimal() : null;
        decimal? harvestMoisture = root.TryGetProperty("harvestMoistureContentPct", out JsonElement hm) && hm.ValueKind == JsonValueKind.Number ? hm.GetDecimal() : null;
        string? botanicalName = root.TryGetProperty("botanicalName", out JsonElement bn) ? bn.GetString()?.Trim() : null;

        await using NpgsqlCommand upsertCmd = conn.CreateCommand();
        upsertCmd.Transaction = tx;
        upsertCmd.CommandText = """
            INSERT INTO field.crop_profile (
                common_name, botanical_name, category, typical_cycle_days_min, typical_cycle_days_max,
                planting_method, seed_depth_cm_min, seed_depth_cm_max, row_spacing_cm_min, row_spacing_cm_max,
                optimal_soil_ph_min, optimal_soil_ph_max, harvest_moisture_content_pct,
                expected_yield_per_ha_kg, unit_of_sale, kg_per_unit,
                source_citation, reviewed_by_admin_id, version_number, last_verified_at
            )
            VALUES (
                @commonName, @botanicalName, @category::crop_category, @daysMin, @daysMax,
                @plantingMethod::planting_method, @sdMin, @sdMax, @rsMin, @rsMax,
                @phMin, @phMax, @harvestMoisture,
                @expectedYield, @unitOfSale::unit_of_sale, @kgPerUnit,
                @sourceCitation, @reviewerId, 1, now()
            )
            ON CONFLICT (common_name) DO UPDATE
            SET botanical_name = COALESCE(EXCLUDED.botanical_name, field.crop_profile.botanical_name),
                typical_cycle_days_min = EXCLUDED.typical_cycle_days_min,
                typical_cycle_days_max = EXCLUDED.typical_cycle_days_max,
                expected_yield_per_ha_kg = EXCLUDED.expected_yield_per_ha_kg,
                source_citation = EXCLUDED.source_citation,
                reviewed_by_admin_id = EXCLUDED.reviewed_by_admin_id,
                version_number = field.crop_profile.version_number + 1,
                last_verified_at = now(),
                updated_at = now()
            RETURNING id;
            """;
        upsertCmd.Parameters.AddWithValue("commonName", commonName);
        NpgsqlParameter bnParam = upsertCmd.Parameters.Add("botanicalName", NpgsqlDbType.Text);
        bnParam.Value = string.IsNullOrWhiteSpace(botanicalName) ? DBNull.Value : botanicalName;
        upsertCmd.Parameters.AddWithValue("category", category);
        upsertCmd.Parameters.AddWithValue("daysMin", daysMin);
        upsertCmd.Parameters.AddWithValue("daysMax", daysMax);
        upsertCmd.Parameters.AddWithValue("plantingMethod", plantingMethod);
        NpgsqlParameter sdMinP = upsertCmd.Parameters.Add("sdMin", NpgsqlDbType.Numeric);
        sdMinP.Value = seedDepthMin.HasValue ? seedDepthMin.Value : DBNull.Value;
        NpgsqlParameter sdMaxP = upsertCmd.Parameters.Add("sdMax", NpgsqlDbType.Numeric);
        sdMaxP.Value = seedDepthMax.HasValue ? seedDepthMax.Value : DBNull.Value;
        NpgsqlParameter rsMinP = upsertCmd.Parameters.Add("rsMin", NpgsqlDbType.Numeric);
        rsMinP.Value = rowSpacingMin.HasValue ? rowSpacingMin.Value : DBNull.Value;
        NpgsqlParameter rsMaxP = upsertCmd.Parameters.Add("rsMax", NpgsqlDbType.Numeric);
        rsMaxP.Value = rowSpacingMax.HasValue ? rowSpacingMax.Value : DBNull.Value;
        NpgsqlParameter phMinP = upsertCmd.Parameters.Add("phMin", NpgsqlDbType.Numeric);
        phMinP.Value = soilPhMin.HasValue ? soilPhMin.Value : DBNull.Value;
        NpgsqlParameter phMaxP = upsertCmd.Parameters.Add("phMax", NpgsqlDbType.Numeric);
        phMaxP.Value = soilPhMax.HasValue ? soilPhMax.Value : DBNull.Value;
        NpgsqlParameter hmP = upsertCmd.Parameters.Add("harvestMoisture", NpgsqlDbType.Numeric);
        hmP.Value = harvestMoisture.HasValue ? harvestMoisture.Value : DBNull.Value;
        upsertCmd.Parameters.AddWithValue("expectedYield", expectedYield);
        upsertCmd.Parameters.AddWithValue("unitOfSale", unitOfSale);
        upsertCmd.Parameters.AddWithValue("kgPerUnit", kgPerUnit);
        upsertCmd.Parameters.AddWithValue("sourceCitation", sourceCitation);
        upsertCmd.Parameters.AddWithValue("reviewerId", reviewerId);

        object? scalar = await upsertCmd.ExecuteScalarAsync(cancellationToken);
        return (true, scalar is Guid g ? g : null, null);
    }
}
