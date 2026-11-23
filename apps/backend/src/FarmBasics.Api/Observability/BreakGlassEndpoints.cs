using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Observability;

public sealed record InvokeBreakGlassRequest(
    string ActionDescription,
    string Justification,
    bool Retroactive = true);

public sealed record ReviewBreakGlassRequest(string? Notes = null);

/// <summary>
/// Break-Glass Emergency Action & mandatory distinct second-SuperAdmin review workflow (WP-B2 / S11.3.M1).
/// Enforces FR-SC-003 / BR-D-08: zero silent 48h expiration and structural refusal of self-review.
/// </summary>
internal static class BreakGlassEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapBreakGlassEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/admin/break-glass", HandleInvokeAsync)
            .RequireAuthorization()
            .RequireAdminPermission("break_glass", "invoke");

        app.MapPost("/v1/admin/break-glass/{actionId:guid}/review", HandleReviewAsync)
            .RequireAuthorization()
            .RequireAdminPermission("break_glass", "review");

        app.MapGet("/v1/admin/break-glass", HandleListAsync)
            .RequireAuthorization()
            .RequireAdminPermission("break_glass", "review");
    }

    private static async Task<IResult> HandleInvokeAsync(
        InvokeBreakGlassRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated SuperAdmin identity is required.");
        }

        string actionDescription = request.ActionDescription?.Trim() ?? string.Empty;
        string justification = request.Justification?.Trim() ?? string.Empty;
        if (actionDescription.Length < 10 || justification.Length < 10)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "ActionDescription and Justification must each be at least 10 characters.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid actionId = Guid.NewGuid();
        DateTimeOffset invokedAt;
        DateTimeOffset reviewDueAt;

        await using (NpgsqlCommand insCmd = conn.CreateCommand())
        {
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO admin.break_glass_action (
                    id, invoked_by_admin_id, action_description, justification, retroactive, status
                )
                VALUES (@id, @adminId, @desc, @just, @retro, 'pending_review')
                RETURNING invoked_at, review_due_at;
                """;
            insCmd.Parameters.AddWithValue("id", actionId);
            insCmd.Parameters.AddWithValue("adminId", adminId);
            insCmd.Parameters.AddWithValue("desc", actionDescription);
            insCmd.Parameters.AddWithValue("just", justification);
            insCmd.Parameters.AddWithValue("retro", request.Retroactive);

            await using NpgsqlDataReader r = await insCmd.ExecuteReaderAsync(cancellationToken);
            await r.ReadAsync(cancellationToken);
            invokedAt = r.GetFieldValue<DateTimeOffset>(0);
            reviewDueAt = r.GetFieldValue<DateTimeOffset>(1);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "admin",
                EventType: "admin.break_glass_invoked",
                EntityType: "break_glass_action",
                EntityId: actionId,
                ActorId: adminId,
                PayloadJson: JsonSerializer.Serialize(new { actionDescription, request.Retroactive, reviewDueAt }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    breakGlassActionId = actionId,
                    status = "pending_review",
                    retroactive = request.Retroactive,
                    invokedAt,
                    reviewDueAt
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleReviewAsync(
        Guid actionId,
        ReviewBreakGlassRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid reviewerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated SuperAdmin identity is required.");
        }

        if (actionId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Break-glass action identifier is invalid.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid invokedBy = Guid.Empty;
        string currentStatus = string.Empty;
        bool found = false;

        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = """
                SELECT invoked_by_admin_id, status
                FROM admin.break_glass_action
                WHERE id = @id
                FOR UPDATE;
                """;
            lockCmd.Parameters.AddWithValue("id", actionId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                found = true;
                invokedBy = reader.GetGuid(0);
                currentStatus = reader.GetString(1);
            }
        }

        if (!found)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Break-glass action was not found.");
        }

        if (invokedBy == reviewerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "SELF_APPROVAL_BLOCKED",
                "A second, different SuperAdmin must review a break-glass action (FR-SC-003 / BR-D-08).");
        }

        if (!string.Equals(currentStatus, "pending_review", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Break-glass action has already been reviewed.");
        }

        string? notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE admin.break_glass_action
                SET status = 'reviewed',
                    reviewed_by_admin_id = @reviewerId,
                    review_notes = @notes,
                    reviewed_at = now()
                WHERE id = @id;
                """;
            updCmd.Parameters.AddWithValue("id", actionId);
            updCmd.Parameters.AddWithValue("reviewerId", reviewerId);
            updCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                Domain: "admin",
                EventType: "admin.break_glass_reviewed",
                EntityType: "break_glass_action",
                EntityId: actionId,
                ActorId: reviewerId,
                PayloadJson: JsonSerializer.Serialize(new { invokedByAdminId = invokedBy, notes }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                breakGlassActionId = actionId,
                status = "reviewed",
                invokedByAdminId = invokedBy,
                reviewedByAdminId = reviewerId
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleListAsync(
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
                id, invoked_by_admin_id, action_description, justification, retroactive,
                status, invoked_at, review_due_at, reviewed_by_admin_id, reviewed_at,
                (status = 'pending_review' AND review_due_at < now()) AS is_overdue
            FROM admin.break_glass_action
            ORDER BY invoked_at DESC
            LIMIT 100;
            """;

        List<object> items = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                breakGlassActionId = reader.GetGuid(0),
                invokedByAdminId = reader.GetGuid(1),
                actionDescription = reader.GetString(2),
                justification = reader.GetString(3),
                retroactive = reader.GetBoolean(4),
                status = reader.GetString(5),
                invokedAt = reader.GetFieldValue<DateTimeOffset>(6),
                reviewDueAt = reader.GetFieldValue<DateTimeOffset>(7),
                reviewedByAdminId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
                reviewedAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
                isOverdue = reader.GetBoolean(10)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(items, new ApiMeta(httpContext.TraceIdentifier)));
    }
}
