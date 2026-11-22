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
        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT proposed_by_admin_id, status::text FROM admin.master_data_change_request WHERE id = @id FOR UPDATE;";
            lockCmd.Parameters.AddWithValue("id", changeRequestId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Master data change request was not found.");
            }

            proposedById = reader.GetGuid(0);
            currentStatus = reader.GetString(1);
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

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("admin", "master_data.change_approved", "master_data_change_request", changeRequestId, reviewerId),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { changeRequestId, status = "applied", reviewedByAdminId = reviewerId },
            new ApiMeta(httpContext.TraceIdentifier)));
    }
}
