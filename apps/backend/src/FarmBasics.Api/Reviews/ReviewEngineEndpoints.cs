using System.Globalization;
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

namespace FarmBasics.Api.Reviews;

public sealed record RegisterReviewerRequest(Guid IdentityId, IReadOnlyList<string> CategoryCodes, int MaxConcurrentAssignments = 10, int ProficiencyLevel = 2);

public sealed record CreateReviewQueueItemRequest(
    string CategoryCode,
    string SubjectRefType,
    Guid SubjectRefId,
    Guid? SubmittedByIdentityId = null,
    string Priority = "normal");

public sealed record AssignReviewerRequest(Guid ReviewerId, string Reason);

public sealed record RecordVerdictRequest(string VerdictCode, string? Notes = null);

public sealed record AppealReviewItemRequest(Guid? PreferredReviewerId = null, string? Reason = null);

public sealed record CreateTrustOverrideRequest(
    string OverriddenTier,
    string Reason,
    DateOnly? ExpiryDate = null,
    bool IsPermanent = false);

internal static class ReviewEngineEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.Ordinal)
    {
        "normal", "elevated", "urgent"
    };

    private static readonly HashSet<string> AllowedTrustTiers = new(StringComparer.Ordinal)
    {
        "unverified", "verified", "trusted"
    };

    public static void MapReviewEngineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/admin/reviewers", HandleRegisterReviewerAsync)
            .RequireAuthorization()
            .RequireAdminPermission("review", "manage");

        app.MapPost("/v1/admin/review-queue", HandleEnqueueItemAsync)
            .RequireAuthorization()
            .RequireAdminPermission("review", "manage");

        app.MapGet("/v1/admin/review-queue", HandleListQueueAsync)
            .RequireAuthorization()
            .RequireAdminPermission("review", "manage");

        app.MapPost("/v1/admin/review-queue/{itemId:guid}/claim", HandleClaimItemAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/review-queue/{itemId:guid}/assign", HandleAssignItemAsync)
            .RequireAuthorization()
            .RequireAdminPermission("review", "manage");

        app.MapPost("/v1/admin/review-queue/{itemId:guid}/verdict", HandleSubmitVerdictAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/review-queue/{itemId:guid}/appeal", HandleAppealItemAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/review-queue/sla-sweep", HandleSlaSweepAsync)
            .RequireAuthorization()
            .RequireAdminPermission("review", "manage");

        app.MapPost("/v1/admin/identities/{identityId:guid}/trust-override", HandleCreateTrustOverrideAsync)
            .RequireAuthorization()
            .RequireAdminPermission("trust", "override");
    }

    internal static async Task<Guid> EnqueueReviewItemAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string categoryCode,
        string subjectRefType,
        Guid subjectRefId,
        Guid? submittedByIdentityId,
        string priority,
        CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand existingCmd = conn.CreateCommand())
        {
            existingCmd.Transaction = tx;
            existingCmd.CommandText = """
                SELECT rqi.id
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rc.code = @categoryCode
                  AND rqi.subject_ref_type = @subjectRefType
                  AND rqi.subject_ref_id = @subjectRefId
                  AND rqi.status <> 'resolved'
                ORDER BY rqi.created_at DESC
                LIMIT 1;
                """;
            existingCmd.Parameters.AddWithValue("categoryCode", categoryCode);
            existingCmd.Parameters.AddWithValue("subjectRefType", subjectRefType);
            existingCmd.Parameters.AddWithValue("subjectRefId", subjectRefId);

            if (await existingCmd.ExecuteScalarAsync(cancellationToken) is Guid existingId)
            {
                return existingId;
            }
        }

        Guid newId = Guid.NewGuid();
        await using (NpgsqlCommand insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO review.review_queue_item (
                    id, category_id, subject_ref_type, subject_ref_id, submitted_by_identity_id, priority, status, due_at
                )
                SELECT
                    @id, rc.id, @subjectRefType, @subjectRefId, @submittedById, @priority::review_priority, 'unassigned',
                    now() + make_interval(hours => rc.sla_hours)
                FROM review.review_category rc
                WHERE rc.code = @categoryCode;
                """;
            insertCmd.Parameters.AddWithValue("id", newId);
            insertCmd.Parameters.AddWithValue("categoryCode", categoryCode);
            insertCmd.Parameters.AddWithValue("subjectRefType", subjectRefType);
            insertCmd.Parameters.AddWithValue("subjectRefId", subjectRefId);
            NpgsqlParameter subParam = insertCmd.Parameters.Add("submittedById", NpgsqlDbType.Uuid);
            subParam.Value = submittedByIdentityId.HasValue ? submittedByIdentityId.Value : DBNull.Value;
            insertCmd.Parameters.AddWithValue("priority", priority);

            int rows = await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            if (rows != 1)
            {
                throw new InvalidOperationException($"Unknown review category '{categoryCode}'.");
            }
        }

        await WriteReviewAuditAsync(
            conn,
            tx,
            newId,
            "review_item.enqueued",
            submittedByIdentityId,
            JsonSerializer.Serialize(new { categoryCode, subjectRefType, subjectRefId, priority }, JsonOptions),
            cancellationToken);

        return newId;
    }

    internal static async Task WriteReviewAuditAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid reviewQueueItemId,
        string eventType,
        Guid? actorId,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime monthEnd = monthStart.AddMonths(1);
        string partitionName = $"review_audit_log_{monthStart.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";
        string fromIso = monthStart.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string toIso = monthEnd.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

        await using (NpgsqlCommand partCmd = conn.CreateCommand())
        {
            partCmd.Transaction = tx;
            partCmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS review.{partitionName} PARTITION OF review.review_audit_log
                FOR VALUES FROM ('{fromIso}') TO ('{toIso}');
                """;
            await partCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO review.review_audit_log (review_queue_item_id, event_type, actor_id, payload)
            VALUES (@itemId, @eventType, @actorId, CAST(@payload AS jsonb));
            """;
        cmd.Parameters.AddWithValue("itemId", reviewQueueItemId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        NpgsqlParameter actorParam = cmd.Parameters.Add("actorId", NpgsqlDbType.Uuid);
        actorParam.Value = actorId.HasValue ? actorId.Value : DBNull.Value;
        NpgsqlParameter payloadParam = cmd.Parameters.Add("payload", NpgsqlDbType.Text);
        payloadParam.Value = payloadJson is not null ? payloadJson : DBNull.Value;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IResult> HandleRegisterReviewerAsync(
        RegisterReviewerRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        if (request.IdentityId == Guid.Empty || request.CategoryCodes is null || request.CategoryCodes.Count == 0 || request.MaxConcurrentAssignments < 1)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "IdentityId, at least one categoryCode, and positive maxConcurrentAssignments are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand profCmd = conn.CreateCommand())
        {
            profCmd.Transaction = tx;
            profCmd.CommandText = """
                INSERT INTO review.reviewer_profile (identity_id, status, max_concurrent_assignments)
                VALUES (@id, 'active', @maxConcurrent)
                ON CONFLICT (identity_id) DO UPDATE
                SET status = 'active',
                    max_concurrent_assignments = EXCLUDED.max_concurrent_assignments;
                """;
            profCmd.Parameters.AddWithValue("id", request.IdentityId);
            profCmd.Parameters.AddWithValue("maxConcurrent", request.MaxConcurrentAssignments);
            await profCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (string code in request.CategoryCodes)
        {
            await using NpgsqlCommand qualCmd = conn.CreateCommand();
            qualCmd.Transaction = tx;
            qualCmd.CommandText = """
                INSERT INTO review.reviewer_qualification (reviewer_id, category_id, proficiency_level)
                SELECT @reviewerId, rc.id, @level
                FROM review.review_category rc
                WHERE rc.code = @code
                ON CONFLICT (reviewer_id, category_id) DO UPDATE
                SET proficiency_level = EXCLUDED.proficiency_level;
                """;
            qualCmd.Parameters.AddWithValue("reviewerId", request.IdentityId);
            qualCmd.Parameters.AddWithValue("code", code.Trim());
            qualCmd.Parameters.AddWithValue("level", request.ProficiencyLevel);
            await qualCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("review", "reviewer.enrolled", "reviewer_profile", request.IdentityId, adminId == Guid.Empty ? null : adminId),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { reviewerId = request.IdentityId, categories = request.CategoryCodes }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleEnqueueItemAsync(
        CreateReviewQueueItemRequest request,
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        string priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.CategoryCode)
            || string.IsNullOrWhiteSpace(request.SubjectRefType)
            || request.SubjectRefId == Guid.Empty
            || !AllowedPriorities.Contains(priority))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "CategoryCode, SubjectRefType, SubjectRefId, and valid priority are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid itemId;
        try
        {
            itemId = await EnqueueReviewItemAsync(
                conn,
                tx,
                request.CategoryCode.Trim(),
                request.SubjectRefType.Trim(),
                request.SubjectRefId,
                request.SubmittedByIdentityId,
                priority,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message);
        }

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { reviewQueueItemId = itemId, categoryCode = request.CategoryCode.Trim(), status = "unassigned" }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListQueueAsync(
        string? category,
        string? status,
        string? priority,
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
                rqi.id, rc.code, rc.display_name, rqi.subject_ref_type, rqi.subject_ref_id,
                rqi.status::text, rqi.priority::text, rqi.assigned_reviewer_id,
                rqi.due_at, rqi.verdict_code, rqi.appeal_of_id, rqi.created_at
            FROM review.review_queue_item rqi
            JOIN review.review_category rc ON rc.id = rqi.category_id
            WHERE (@category IS NULL OR rc.code = @category)
              AND (@status IS NULL OR rqi.status::text = @status)
              AND (@priority IS NULL OR rqi.priority::text = @priority)
            ORDER BY rqi.priority DESC, rqi.due_at ASC
            LIMIT 100;
            """;

        NpgsqlParameter catParam = cmd.Parameters.Add("category", NpgsqlDbType.Text);
        catParam.Value = string.IsNullOrWhiteSpace(category) ? DBNull.Value : category.Trim();
        NpgsqlParameter statusParam = cmd.Parameters.Add("status", NpgsqlDbType.Text);
        statusParam.Value = string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim();
        NpgsqlParameter prioParam = cmd.Parameters.Add("priority", NpgsqlDbType.Text);
        prioParam.Value = string.IsNullOrWhiteSpace(priority) ? DBNull.Value : priority.Trim();

        List<object> items = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetGuid(0),
                categoryCode = reader.GetString(1),
                categoryName = reader.GetString(2),
                subjectRefType = reader.GetString(3),
                subjectRefId = reader.GetGuid(4),
                status = reader.GetString(5),
                priority = reader.GetString(6),
                assignedReviewerId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                dueAt = reader.GetFieldValue<DateTimeOffset>(8),
                verdictCode = reader.IsDBNull(9) ? null : reader.GetString(9),
                appealOfId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                createdAt = reader.GetFieldValue<DateTimeOffset>(11)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(items, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleClaimItemAsync(
        Guid itemId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
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

        IResult? assignResult = await TryAssignReviewerInternalAsync(
            conn,
            tx,
            itemId,
            reviewerId,
            assignedByAdminId: reviewerId,
            httpContext,
            adminPermissionChecker,
            cancellationToken);

        if (assignResult is not null)
        {
            return assignResult;
        }

        await tx.CommitAsync(cancellationToken);
        return Results.Ok(new ApiResponse<object>(
            new { reviewQueueItemId = itemId, assignedReviewerId = reviewerId, status = "assigned" },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleAssignItemAsync(
        Guid itemId,
        AssignReviewerRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        if (request.ReviewerId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "ReviewerId and reason are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        IResult? assignResult = await TryAssignReviewerInternalAsync(
            conn,
            tx,
            itemId,
            request.ReviewerId,
            adminId == Guid.Empty ? request.ReviewerId : adminId,
            httpContext,
            adminPermissionChecker,
            cancellationToken);

        if (assignResult is not null)
        {
            return assignResult;
        }

        await tx.CommitAsync(cancellationToken);
        return Results.Ok(new ApiResponse<object>(
            new { reviewQueueItemId = itemId, assignedReviewerId = request.ReviewerId, status = "assigned" },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult?> TryAssignReviewerInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid itemId,
        Guid reviewerId,
        Guid assignedByAdminId,
        HttpContext httpContext,
        IAdminPermissionChecker adminPermissionChecker,
        CancellationToken cancellationToken)
    {
        Guid categoryId;
        int minLevel;
        Guid subjectRefId;
        Guid? submittedById;
        string currentStatus;

        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = """
                SELECT rqi.category_id, rc.min_qualification_level, rqi.subject_ref_id, rqi.submitted_by_identity_id, rqi.status::text
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rqi.id = @id
                FOR UPDATE OF rqi;
                """;
            lockCmd.Parameters.AddWithValue("id", itemId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Review queue item was not found.");
            }

            categoryId = reader.GetGuid(0);
            minLevel = reader.GetInt32(1);
            subjectRefId = reader.GetGuid(2);
            submittedById = reader.IsDBNull(3) ? null : reader.GetGuid(3);
            currentStatus = reader.GetString(4);
        }

        // Concurrency-safe lock guard: only unassigned (or escalated returning to pool) items may be claimed
        if (currentStatus is not ("unassigned" or "escalated"))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "REVIEW_ITEM_ALREADY_ASSIGNED", "Review item is already claimed or resolved.");
        }

        // COI exclusion: submitter or subject cannot review their own item
        if (reviewerId == subjectRefId || (submittedById.HasValue && submittedById.Value == reviewerId))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "COI_SELF_REVIEW_BLOCKED", "Conflict-of-interest exclusion forbids reviewing an item you submitted or are the subject of.");
        }

        // Verify reviewer eligibility (active qualified reviewer_profile, or review:manage admin with active reviewer_profile)
        bool hasManagePerm = await adminPermissionChecker.HasPermissionAsync(reviewerId.ToString(), "review:manage", cancellationToken);
        await using (NpgsqlCommand qualCmd = conn.CreateCommand())
        {
            qualCmd.Transaction = tx;
            qualCmd.CommandText = """
                SELECT
                    rp.status::text,
                    rp.max_concurrent_assignments,
                    COALESCE((
                        SELECT rq.proficiency_level
                        FROM review.reviewer_qualification rq
                        WHERE rq.reviewer_id = rp.identity_id AND rq.category_id = @catId
                    ), 0) AS proficiency_level,
                    (
                        SELECT count(*) FROM review.review_queue_item active_items
                        WHERE active_items.assigned_reviewer_id = rp.identity_id
                          AND active_items.status IN ('assigned', 'in_review')
                    ) AS active_count
                FROM review.reviewer_profile rp
                WHERE rp.identity_id = @reviewerId;
                """;
            qualCmd.Parameters.AddWithValue("reviewerId", reviewerId);
            qualCmd.Parameters.AddWithValue("catId", categoryId);

            await using NpgsqlDataReader reader = await qualCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Reviewer profile is not enrolled or active.");
            }

            string reviewerStatus = reader.GetString(0);
            int maxConcurrent = reader.GetInt32(1);
            int proficiencyLevel = reader.GetInt32(2);
            long activeCount = reader.GetInt64(3);

            if (!string.Equals(reviewerStatus, "active", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Reviewer status is not active.");
            }

            if (!hasManagePerm && proficiencyLevel < minLevel)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Reviewer lacks qualification for this review category.");
            }

            if (activeCount >= maxConcurrent)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Reviewer has reached maximum concurrent assignments.");
            }
        }

        try
        {
            await using NpgsqlCommand assignCmd = conn.CreateCommand();
            assignCmd.Transaction = tx;
            assignCmd.CommandText = """
                UPDATE review.review_queue_item
                SET status = 'assigned',
                    assigned_reviewer_id = @reviewerId,
                    assigned_at = now()
                WHERE id = @id;
                """;
            assignCmd.Parameters.AddWithValue("id", itemId);
            assignCmd.Parameters.AddWithValue("reviewerId", reviewerId);
            await assignCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "APPEAL_SAME_REVIEWER_BLOCKED",
                "Appeals must be assigned to a different reviewer from the original decision.");
        }

        await WriteReviewAuditAsync(
            conn,
            tx,
            itemId,
            "review_item.assigned",
            assignedByAdminId,
            JsonSerializer.Serialize(new { assignedReviewerId = reviewerId }, JsonOptions),
            cancellationToken);

        return null;
    }

    private static async Task<IResult> HandleSubmitVerdictAsync(
        Guid itemId,
        RecordVerdictRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        ReviewVerdictWriteBackRegistry writeBackRegistry,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid reviewerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated reviewer is required.");
        }

        if (string.IsNullOrWhiteSpace(request.VerdictCode))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "VerdictCode is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        string categoryCode;
        bool requiresSecondary;
        string subjectRefType;
        Guid subjectRefId;
        string currentStatus;
        Guid? assignedReviewerId;

        await using (NpgsqlCommand lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = """
                SELECT rc.code, rc.requires_secondary_review, rqi.subject_ref_type, rqi.subject_ref_id, rqi.status::text, rqi.assigned_reviewer_id
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rqi.id = @id
                FOR UPDATE OF rqi;
                """;
            lockCmd.Parameters.AddWithValue("id", itemId);

            await using NpgsqlDataReader reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Review queue item was not found.");
            }

            categoryCode = reader.GetString(0);
            requiresSecondary = reader.GetBoolean(1);
            subjectRefType = reader.GetString(2);
            subjectRefId = reader.GetGuid(3);
            currentStatus = reader.GetString(4);
            assignedReviewerId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
        }

        // Strict Doc 03 §10 check: assigned reviewer only
        if (assignedReviewerId != reviewerId)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Only the currently assigned reviewer may submit a verdict.");
        }

        if (string.Equals(currentStatus, "resolved", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Review item is already resolved.");
        }

        string verdictCode = request.VerdictCode.Trim();
        if (!ReviewVerdictWriteBackRegistry.IsAllowedVerdict(categoryCode, verdictCode))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_VERDICT", $"Verdict code '{verdictCode}' is not valid for category '{categoryCode}'.");
        }

        if (requiresSecondary && !string.Equals(currentStatus, "awaiting_secondary_review", StringComparison.Ordinal))
        {
            await using NpgsqlCommand secCmd = conn.CreateCommand();
            secCmd.Transaction = tx;
            secCmd.CommandText = """
                UPDATE review.review_queue_item
                SET status = 'awaiting_secondary_review',
                    assigned_reviewer_id = NULL,
                    verdict_notes = @notes
                WHERE id = @id;
                """;
            secCmd.Parameters.AddWithValue("id", itemId);
            NpgsqlParameter nParam = secCmd.Parameters.Add("notes", NpgsqlDbType.Text);
            nParam.Value = string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim();
            await secCmd.ExecuteNonQueryAsync(cancellationToken);

            await WriteReviewAuditAsync(
                conn,
                tx,
                itemId,
                "review_item.awaiting_secondary",
                reviewerId,
                JsonSerializer.Serialize(new { primaryVerdict = verdictCode }, JsonOptions),
                cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Results.Ok(new ApiResponse<object>(
                new { reviewQueueItemId = itemId, status = "awaiting_secondary_review", verdictCode },
                new ApiMeta(httpContext.TraceIdentifier)));
        }

        object? writeBackResult = await writeBackRegistry.ExecuteWriteBackAsync(
            conn,
            tx,
            new ReviewVerdictContext(itemId, categoryCode, subjectRefType, subjectRefId, verdictCode, request.Notes, reviewerId),
            cancellationToken);

        string payloadJson = JsonSerializer.Serialize(writeBackResult ?? new { }, JsonOptions);

        await using (NpgsqlCommand resolveCmd = conn.CreateCommand())
        {
            resolveCmd.Transaction = tx;
            resolveCmd.CommandText = """
                UPDATE review.review_queue_item
                SET status = 'resolved',
                    resolved_at = now(),
                    verdict_code = @verdictCode,
                    verdict_notes = @notes,
                    verdict_payload = CAST(@payload AS jsonb)
                WHERE id = @id;
                """;
            resolveCmd.Parameters.AddWithValue("id", itemId);
            resolveCmd.Parameters.AddWithValue("verdictCode", verdictCode);
            NpgsqlParameter notesParam = resolveCmd.Parameters.Add("notes", NpgsqlDbType.Text);
            notesParam.Value = string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim();
            resolveCmd.Parameters.AddWithValue("payload", payloadJson);
            await resolveCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteReviewAuditAsync(
            conn,
            tx,
            itemId,
            "review_item.resolved",
            reviewerId,
            JsonSerializer.Serialize(new { verdictCode, writeBackResult }, JsonOptions),
            cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "review",
                "review_item.resolved",
                "review_queue_item",
                itemId,
                reviewerId,
                JsonSerializer.Serialize(new { categoryCode, subjectRefId, verdictCode }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { reviewQueueItemId = itemId, status = "resolved", verdictCode, writeBackResult },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleAppealItemAsync(
        Guid itemId,
        AppealReviewItemRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid categoryId;
        int slaHours;
        bool allowsAppeal;
        string subjectRefType;
        Guid subjectRefId;
        Guid? submittedById;
        string currentStatus;

        await using (NpgsqlCommand readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = tx;
            readCmd.CommandText = """
                SELECT rqi.category_id, rc.sla_hours, rc.allows_appeal, rqi.subject_ref_type, rqi.subject_ref_id, rqi.submitted_by_identity_id, rqi.status::text
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rqi.id = @id;
                """;
            readCmd.Parameters.AddWithValue("id", itemId);

            await using NpgsqlDataReader reader = await readCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Original review item was not found.");
            }

            categoryId = reader.GetGuid(0);
            slaHours = reader.GetInt32(1);
            allowsAppeal = reader.GetBoolean(2);
            subjectRefType = reader.GetString(3);
            subjectRefId = reader.GetGuid(4);
            submittedById = reader.IsDBNull(5) ? null : reader.GetGuid(5);
            currentStatus = reader.GetString(6);
        }

        if (!allowsAppeal)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "APPEALS_NOT_ALLOWED", "Appeals are disabled for this review category.");
        }

        if (!string.Equals(currentStatus, "resolved", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Only resolved review items may be appealed.");
        }

        Guid appealId = Guid.NewGuid();
        try
        {
            await using NpgsqlCommand insertAppeal = conn.CreateCommand();
            insertAppeal.Transaction = tx;
            insertAppeal.CommandText = """
                INSERT INTO review.review_queue_item (
                    id, category_id, subject_ref_type, subject_ref_id, submitted_by_identity_id,
                    priority, status, assigned_reviewer_id, assigned_at, due_at, appeal_of_id
                )
                VALUES (
                    @id, @catId, @refType, @refId, @subId,
                    'elevated', CASE WHEN @prefReviewer IS NULL THEN 'unassigned'::review_item_status ELSE 'assigned'::review_item_status END,
                    @prefReviewer, CASE WHEN @prefReviewer IS NULL THEN NULL ELSE now() END,
                    now() + make_interval(hours => @slaHours), @appealOfId
                );
                """;
            insertAppeal.Parameters.AddWithValue("id", appealId);
            insertAppeal.Parameters.AddWithValue("catId", categoryId);
            insertAppeal.Parameters.AddWithValue("refType", subjectRefType);
            insertAppeal.Parameters.AddWithValue("refId", subjectRefId);
            NpgsqlParameter subParam = insertAppeal.Parameters.Add("subId", NpgsqlDbType.Uuid);
            subParam.Value = submittedById.HasValue ? submittedById.Value : DBNull.Value;
            NpgsqlParameter revParam = insertAppeal.Parameters.Add("prefReviewer", NpgsqlDbType.Uuid);
            revParam.Value = request.PreferredReviewerId.HasValue ? request.PreferredReviewerId.Value : DBNull.Value;
            insertAppeal.Parameters.AddWithValue("slaHours", slaHours);
            insertAppeal.Parameters.AddWithValue("appealOfId", itemId);
            await insertAppeal.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "APPEAL_SAME_REVIEWER_BLOCKED",
                "Appeals must be assigned to a different reviewer from the original decision.");
        }

        await WriteReviewAuditAsync(
            conn,
            tx,
            appealId,
            "review_item.appeal_opened",
            callerId,
            JsonSerializer.Serialize(new { appealOfId = itemId, reason = request.Reason }, JsonOptions),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { appealId, appealOfId = itemId, status = request.PreferredReviewerId.HasValue ? "assigned" : "unassigned" }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleSlaSweepAsync(
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        List<Guid> escalatedIds = [];
        await using (NpgsqlCommand sweepCmd = conn.CreateCommand())
        {
            sweepCmd.Transaction = tx;
            sweepCmd.CommandText = """
                UPDATE review.review_queue_item
                SET status = 'escalated',
                    priority = 'urgent'
                WHERE status NOT IN ('resolved', 'escalated')
                  AND due_at < now()
                RETURNING id;
                """;

            await using NpgsqlDataReader reader = await sweepCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                escalatedIds.Add(reader.GetGuid(0));
            }
        }

        foreach (Guid escId in escalatedIds)
        {
            await WriteReviewAuditAsync(
                conn,
                tx,
                escId,
                "review_item.sla_escalated",
                adminId == Guid.Empty ? null : adminId,
                null,
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { escalatedCount = escalatedIds.Count, escalatedIds },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleCreateTrustOverrideAsync(
        Guid identityId,
        CreateTrustOverrideRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        IAdminPermissionChecker adminPermissionChecker,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated administrator is required.");
        }

        string tier = request.OverriddenTier?.Trim().ToLowerInvariant() ?? string.Empty;
        if (identityId == Guid.Empty || !AllowedTrustTiers.Contains(tier) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "IdentityId, valid overriddenTier, and reason are required.");
        }

        if (!request.IsPermanent && !request.ExpiryDate.HasValue)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Either expiryDate or isPermanent=true must be specified (BR-D-04).");
        }

        if (request.IsPermanent)
        {
            bool canGrantPermanent = await adminPermissionChecker.HasPermissionAsync(adminId.ToString(), "trust:override_permanent", cancellationToken);
            if (!canGrantPermanent)
            {
                return SelfServiceRegistrationEndpoints.ErrorResult(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "PERMISSION_DENIED",
                    "Permanent trust overrides require SuperAdmin (trust:override_permanent) permission.");
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

        Guid overrideId = Guid.NewGuid();
        try
        {
            await using NpgsqlCommand ovCmd = conn.CreateCommand();
            ovCmd.Transaction = tx;
            ovCmd.CommandText = """
                INSERT INTO admin.trust_profile_override (
                    id, identity_id, organization_id, overridden_tier, reason, granted_by_admin_id, expiry_date, is_permanent
                )
                VALUES (@id, @identityId, NULL, @tier::trust_tier, @reason, @adminId, @expiryDate, @isPermanent);
                """;
            ovCmd.Parameters.AddWithValue("id", overrideId);
            ovCmd.Parameters.AddWithValue("identityId", identityId);
            ovCmd.Parameters.AddWithValue("tier", tier);
            ovCmd.Parameters.AddWithValue("reason", request.Reason.Trim());
            ovCmd.Parameters.AddWithValue("adminId", adminId);
            NpgsqlParameter expiryParam = ovCmd.Parameters.Add("expiryDate", NpgsqlDbType.Date);
            expiryParam.Value = request.ExpiryDate.HasValue ? request.ExpiryDate.Value : DBNull.Value;
            ovCmd.Parameters.AddWithValue("isPermanent", request.IsPermanent);
            await ovCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Target identity was not found.");
        }

        await using (NpgsqlCommand updTrust = conn.CreateCommand())
        {
            updTrust.Transaction = tx;
            updTrust.CommandText = """
                INSERT INTO identity.trust_profile (identity_id, organization_id, verification_tier, updated_at)
                VALUES (@identityId, NULL, @tier::trust_tier, now())
                ON CONFLICT (identity_id) DO UPDATE
                SET verification_tier = EXCLUDED.verification_tier,
                    updated_at = now();
                """;
            updTrust.Parameters.AddWithValue("identityId", identityId);
            updTrust.Parameters.AddWithValue("tier", tier);
            await updTrust.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "admin",
                "trust.override_granted",
                "trust_profile_override",
                overrideId,
                adminId,
                JsonSerializer.Serialize(new { identityId, overriddenTier = tier, request.IsPermanent, request.ExpiryDate }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    overrideId,
                    identityId,
                    overriddenTier = tier,
                    expiryDate = request.ExpiryDate,
                    isPermanent = request.IsPermanent
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }
}
