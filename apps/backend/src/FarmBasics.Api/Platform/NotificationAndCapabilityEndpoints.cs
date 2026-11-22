using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Platform;

public sealed record DispatchNotificationRequest(
    string TemplateCode,
    Guid RecipientIdentityId,
    string SourceModule,
    string? SourceRefType = null,
    Guid? SourceRefId = null,
    Dictionary<string, string>? Context = null,
    int DedupWindowMinutes = 60);

public sealed record UpsertCapabilityOverrideRequest(bool Enabled, string ScopeType = "global", Guid? ScopeRefId = null);

/// <summary>
/// Notification dispatch layer with template rendering + dedup (WP-43 / 02-ENG §3.3)
/// and Capability Modules infrastructure keeping CEA modules behind flags (PRECEDENCE P25).
/// </summary>
internal static class NotificationAndCapabilityEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapNotificationAndCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/admin/notifications/dispatch", HandleDispatchAsync)
            .RequireAuthorization()
            .RequireAdminPermission("notification", "dispatch");

        app.MapGet("/v1/platform/capabilities", HandleListCapabilitiesAsync);

        app.MapPut("/v1/admin/capabilities/{moduleCode}", HandleSetCapabilityOverrideAsync)
            .RequireAuthorization()
            .RequireAdminPermission("capability", "manage");
    }

    private static async Task<IResult> HandleDispatchAsync(
        DispatchNotificationRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        ISmsGatewayClient smsClient,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        if (string.IsNullOrWhiteSpace(request.TemplateCode)
            || request.RecipientIdentityId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.SourceModule)
            || request.DedupWindowMinutes < 0)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "TemplateCode, RecipientIdentityId, and SourceModule are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid templateId;
        string channel;
        string bodyTemplate;
        string[] requiredFields;
        await using (NpgsqlCommand tplCmd = conn.CreateCommand())
        {
            tplCmd.Transaction = tx;
            tplCmd.CommandText = """
                SELECT id, channel::text, body_template, required_context_fields
                FROM notify.notification_template
                WHERE code = @code;
                """;
            tplCmd.Parameters.AddWithValue("code", request.TemplateCode.Trim());

            await using NpgsqlDataReader reader = await tplCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Notification template was not found.");
            }

            templateId = reader.GetGuid(0);
            channel = reader.GetString(1);
            bodyTemplate = reader.GetString(2);
            requiredFields = reader.GetFieldValue<string[]>(3);
        }

        Dictionary<string, string> ctx = request.Context ?? new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string requiredField in requiredFields)
        {
            if (!ctx.TryGetValue(requiredField, out string? val) || string.IsNullOrWhiteSpace(val))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", $"Missing required notification template context field '{requiredField}'.");
            }
        }

        // Check dedup index (recipient_identity_id, source_module, source_ref_type, source_ref_id, created_at)
        await using (NpgsqlCommand dedupCmd = conn.CreateCommand())
        {
            dedupCmd.Transaction = tx;
            dedupCmd.CommandText = """
                SELECT id
                FROM notify.notification_dispatch
                WHERE recipient_identity_id = @recipientId
                  AND source_module = @sourceModule
                  AND source_ref_type IS NOT DISTINCT FROM @refType
                  AND source_ref_id IS NOT DISTINCT FROM @refId
                  AND template_id = @templateId
                  AND created_at >= now() - make_interval(mins => @windowMinutes)
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            dedupCmd.Parameters.AddWithValue("recipientId", request.RecipientIdentityId);
            dedupCmd.Parameters.AddWithValue("sourceModule", request.SourceModule.Trim());
            NpgsqlParameter rtParam = dedupCmd.Parameters.Add("refType", NpgsqlDbType.Text);
            rtParam.Value = string.IsNullOrWhiteSpace(request.SourceRefType) ? DBNull.Value : request.SourceRefType.Trim();
            NpgsqlParameter riParam = dedupCmd.Parameters.Add("refId", NpgsqlDbType.Uuid);
            riParam.Value = request.SourceRefId.HasValue ? request.SourceRefId.Value : DBNull.Value;
            dedupCmd.Parameters.AddWithValue("templateId", templateId);
            dedupCmd.Parameters.AddWithValue("windowMinutes", request.DedupWindowMinutes);

            if (await dedupCmd.ExecuteScalarAsync(cancellationToken) is Guid existingDispatchId)
            {
                await tx.RollbackAsync(cancellationToken);
                return Results.Ok(new ApiResponse<object>(
                    new { dispatchId = existingDispatchId, deduplicated = true, status = "skipped_duplicate" },
                    new ApiMeta(httpContext.TraceIdentifier)));
            }
        }

        string? recipientPhone;
        await using (NpgsqlCommand phoneCmd = conn.CreateCommand())
        {
            phoneCmd.Transaction = tx;
            phoneCmd.CommandText = "SELECT phone_primary FROM identity.identity WHERE id = @id;";
            phoneCmd.Parameters.AddWithValue("id", request.RecipientIdentityId);
            object? obj = await phoneCmd.ExecuteScalarAsync(cancellationToken);
            if (obj is null)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Recipient identity was not found.");
            }
            recipientPhone = obj as string;
        }

        string renderedBody = bodyTemplate;
        foreach (KeyValuePair<string, string> pair in ctx)
        {
            renderedBody = renderedBody.Replace($"{{{pair.Key}}}", pair.Value, StringComparison.Ordinal);
        }

        bool delivered = true;
        string dispatchStatus = "sent";
        if (string.Equals(channel, "sms", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(recipientPhone))
        {
            SmsSendResult smsRes = await smsClient.SendSmsAsync(new SmsSendRequest(recipientPhone, renderedBody), cancellationToken);
            delivered = smsRes.Succeeded;
            dispatchStatus = smsRes.Succeeded ? "sent" : "failed";
        }

        Guid dispatchId = Guid.NewGuid();
        await using (NpgsqlCommand insCmd = conn.CreateCommand())
        {
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT INTO notify.notification_dispatch (
                    id, template_id, recipient_identity_id, channel, source_module,
                    source_ref_type, source_ref_id, status, sent_at, failure_reason
                )
                VALUES (
                    @id, @templateId, @recipientId, @channel::notification_channel, @sourceModule,
                    @refType, @refId, @status::dispatch_status, now(), @failureReason
                );
                """;
            insCmd.Parameters.AddWithValue("id", dispatchId);
            insCmd.Parameters.AddWithValue("templateId", templateId);
            insCmd.Parameters.AddWithValue("recipientId", request.RecipientIdentityId);
            insCmd.Parameters.AddWithValue("channel", channel);
            insCmd.Parameters.AddWithValue("sourceModule", request.SourceModule.Trim());
            NpgsqlParameter rtParam = insCmd.Parameters.Add("refType", NpgsqlDbType.Text);
            rtParam.Value = string.IsNullOrWhiteSpace(request.SourceRefType) ? DBNull.Value : request.SourceRefType.Trim();
            NpgsqlParameter riParam = insCmd.Parameters.Add("refId", NpgsqlDbType.Uuid);
            riParam.Value = request.SourceRefId.HasValue ? request.SourceRefId.Value : DBNull.Value;
            insCmd.Parameters.AddWithValue("status", dispatchStatus);
            NpgsqlParameter frParam = insCmd.Parameters.Add("failureReason", NpgsqlDbType.Text);
            frParam.Value = delivered ? DBNull.Value : "TRANSPORT_FAILURE";
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "notify",
                "notification.dispatched",
                "notification_dispatch",
                dispatchId,
                adminId == Guid.Empty ? null : adminId,
                JsonSerializer.Serialize(new { templateCode = request.TemplateCode.Trim(), channel, dispatchStatus }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new { dispatchId, deduplicated = false, status = dispatchStatus, channel },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListCapabilitiesAsync(
        Guid? organizationId,
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
                cm.module_code,
                cm.display_name,
                cm.enabled_by_default,
                COALESCE(org_ov.enabled, global_ov.enabled, cm.enabled_by_default) AS effective_enabled
            FROM platform.capability_module cm
            LEFT JOIN platform.capability_module_override global_ov
              ON global_ov.module_code = cm.module_code AND global_ov.scope_type = 'global'
            LEFT JOIN platform.capability_module_override org_ov
              ON org_ov.module_code = cm.module_code AND org_ov.scope_type = 'organization' AND org_ov.scope_ref_id = @orgId
            ORDER BY cm.module_code ASC;
            """;
        NpgsqlParameter orgParam = cmd.Parameters.Add("orgId", NpgsqlDbType.Uuid);
        orgParam.Value = organizationId.HasValue ? organizationId.Value : DBNull.Value;

        List<object> modules = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            modules.Add(new
            {
                moduleCode = reader.GetString(0),
                displayName = reader.GetString(1),
                enabledByDefault = reader.GetBoolean(2),
                enabled = reader.GetBoolean(3)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(modules, new ApiMeta(httpContext.TraceIdentifier)));
    }

    internal static async Task<bool> IsCapabilityEnabledAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        string moduleCode,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COALESCE(org_ov.enabled, global_ov.enabled, cm.enabled_by_default)
            FROM platform.capability_module cm
            LEFT JOIN platform.capability_module_override global_ov
              ON global_ov.module_code = cm.module_code AND global_ov.scope_type = 'global'
            LEFT JOIN platform.capability_module_override org_ov
              ON org_ov.module_code = cm.module_code AND org_ov.scope_type = 'organization' AND org_ov.scope_ref_id = @orgId
            WHERE cm.module_code = @moduleCode;
            """;
        cmd.Parameters.AddWithValue("moduleCode", moduleCode);
        NpgsqlParameter orgParam = cmd.Parameters.Add("orgId", NpgsqlDbType.Uuid);
        orgParam.Value = organizationId.HasValue ? organizationId.Value : DBNull.Value;

        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is bool enabled && enabled;
    }

    private static async Task<IResult> HandleSetCapabilityOverrideAsync(
        string moduleCode,
        UpsertCapabilityOverrideRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        string scope = string.IsNullOrWhiteSpace(request.ScopeType) ? "global" : request.ScopeType.Trim().ToLowerInvariant();
        if (scope is not ("global" or "region" or "organization")
            || (scope == "global" && request.ScopeRefId.HasValue)
            || (scope != "global" && !request.ScopeRefId.HasValue))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Invalid capability override scope configuration.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO platform.capability_module_override (
                    module_code, scope_type, scope_ref_id, enabled, updated_by_admin_id, updated_at
                )
                VALUES (@code, @scope::scope_type, @refId, @enabled, @adminId, now())
                ON CONFLICT (module_code, scope_type, scope_ref_id) DO UPDATE
                SET enabled = EXCLUDED.enabled,
                    updated_by_admin_id = EXCLUDED.updated_by_admin_id,
                    updated_at = now();
                """;
            cmd.Parameters.AddWithValue("code", moduleCode.Trim());
            cmd.Parameters.AddWithValue("scope", scope);
            NpgsqlParameter refParam = cmd.Parameters.Add("refId", NpgsqlDbType.Uuid);
            refParam.Value = request.ScopeRefId.HasValue ? request.ScopeRefId.Value : DBNull.Value;
            cmd.Parameters.AddWithValue("enabled", request.Enabled);
            NpgsqlParameter admParam = cmd.Parameters.Add("adminId", NpgsqlDbType.Uuid);
            admParam.Value = adminId == Guid.Empty ? DBNull.Value : adminId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", $"Unknown capability module '{moduleCode}'.");
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "platform",
                "capability.override_updated",
                "capability_module",
                request.ScopeRefId ?? Guid.Empty,
                adminId == Guid.Empty ? null : adminId,
                JsonSerializer.Serialize(new { moduleCode = moduleCode.Trim(), scope, request.Enabled }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { moduleCode = moduleCode.Trim(), scopeType = scope, scopeRefId = request.ScopeRefId, enabled = request.Enabled },
            new ApiMeta(httpContext.TraceIdentifier)));
    }
}
