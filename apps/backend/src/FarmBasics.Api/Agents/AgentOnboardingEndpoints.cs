using System.Text.Json;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Agents;

public sealed record CreateOrganizationRequest(string Name, string Type, string? RegistrationNumber = null);

public sealed record AddOrganizationAgentRequest(Guid AgentIdentityId);

public sealed record BindTabletRequest(
    string DeviceSerial,
    string DeviceFingerprint,
    Guid OrganizationId,
    Guid AssignedAgentId);

public sealed record StartAgentSessionRequest(Guid TabletId, string DeviceFingerprint, int LifetimeHours = 72);

public sealed record AgentAssistedRegistrationRequest(
    string Nin,
    string Phone,
    string? LivenessPhotoBase64 = null,
    string? Email = null,
    Guid? StateId = null,
    Guid? LgaId = null,
    Guid? WardId = null,
    Guid? CommunityId = null,
    Guid? AgentSessionId = null);

public sealed record ConfirmVoiceOtpRequest(string Otp, Guid? AgentSessionId = null);

internal sealed record AgentSessionValidationResult(
    bool IsValid,
    Guid OrganizationId,
    Guid TabletDeviceId,
    int ErrorStatusCode = StatusCodes.Status403Forbidden,
    string? ErrorCode = null,
    string? ErrorMessage = null);

internal static class AgentOnboardingEndpoints
{
    public const string AgentSessionHeaderName = "X-Agent-Session-Id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedOrganizationTypes = new(StringComparer.Ordinal)
    {
        "ngo",
        "government_agency",
        "private_agribusiness",
        "cooperative",
        "vendor_company",
        "buyer_company",
        "agency",
        "soil_test_lab",
        "commercial_farm"
    };

    public static void MapAgentOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/organizations", HandleCreateOrganizationAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/organizations/{organizationId:guid}/verify", HandleVerifyOrganizationAsync)
            .RequireAuthorization()
            .RequireAdminPermission("organization", "verify");

        app.MapPost("/v1/organizations/{organizationId:guid}/agents", HandleAddAgentAsync)
            .RequireAuthorization();

        app.MapPost("/v1/tablets/bind", HandleBindTabletAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/tablets/{tabletId:guid}/deactivate", HandleDeactivateTabletAsync)
            .RequireAuthorization()
            .RequireAdminPermission("tablet", "deactivate");

        app.MapPost("/v1/agents/sessions", HandleStartAgentSessionAsync)
            .RequireAuthorization();

        app.MapPost("/v1/registrations/agent-assisted", HandleAgentAssistedRegisterAsync)
            .RequireAuthorization();

        app.MapPost("/v1/registrations/agent-assisted/{identityId:guid}/confirm-voice-otp", HandleConfirmVoiceOtpAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.OtpVerify);
    }

    internal static bool TryResolveSessionId(HttpRequest httpRequest, Guid? bodySessionId, out Guid sessionId)
    {
        if (bodySessionId.HasValue && bodySessionId.Value != Guid.Empty)
        {
            sessionId = bodySessionId.Value;
            return true;
        }

        string? rawHeader = httpRequest.Headers[AgentSessionHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rawHeader) && Guid.TryParse(rawHeader, out sessionId) && sessionId != Guid.Empty)
        {
            return true;
        }

        sessionId = Guid.Empty;
        return false;
    }

    internal static bool TryGetCallerGuid(HttpContext httpContext, ICurrentIdentityAccessor identityAccessor, out Guid callerId)
    {
        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
        if (identity is not null && Guid.TryParse(identity.Subject, out callerId) && callerId != Guid.Empty)
        {
            return true;
        }

        callerId = Guid.Empty;
        return false;
    }

    internal static async Task<AgentSessionValidationResult> ValidateActiveAgentSessionAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid agentIdentityId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT
                s.organization_id,
                s.tablet_device_id,
                s.ended_at,
                s.expires_at,
                td.status::text,
                o.verification_status::text,
                i.status::text,
                EXISTS (
                    SELECT 1
                    FROM identity.role_profile rp
                    WHERE rp.identity_id = @agentId
                      AND rp.role_type = 'agent'
                      AND rp.linked_organization_id = s.organization_id
                ) AS has_agent_role
            FROM identity.agent_session s
            JOIN identity.tablet_device td ON td.id = s.tablet_device_id
            JOIN identity.organization o ON o.id = s.organization_id
            JOIN identity.identity i ON i.id = s.agent_identity_id
            WHERE s.id = @sessionId
              AND s.agent_identity_id = @agentId;
            """;
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("agentId", agentIdentityId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AgentSessionValidationResult(
                false,
                Guid.Empty,
                Guid.Empty,
                StatusCodes.Status403Forbidden,
                "AGENT_SESSION_INVALID",
                "A valid active agent tablet session is required.");
        }

        Guid orgId = reader.GetGuid(0);
        Guid tabletId = reader.GetGuid(1);
        DateTimeOffset? endedAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        DateTimeOffset expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        string tabletStatus = reader.GetString(4);
        string orgVerificationStatus = reader.GetString(5);
        string identityStatus = reader.GetString(6);
        bool hasAgentRole = reader.GetBoolean(7);

        if (!string.Equals(identityStatus, "active", StringComparison.Ordinal) || !hasAgentRole)
        {
            return new AgentSessionValidationResult(
                false,
                orgId,
                tabletId,
                StatusCodes.Status403Forbidden,
                "PERMISSION_DENIED",
                "Caller is not an active extension agent for this organization.");
        }

        if (!string.Equals(orgVerificationStatus, "verified", StringComparison.Ordinal))
        {
            return new AgentSessionValidationResult(
                false,
                orgId,
                tabletId,
                StatusCodes.Status403Forbidden,
                "AGENT_ORGANIZATION_NOT_VERIFIED",
                "Agent organization must be verified before performing field operations.");
        }

        if (endedAt.HasValue || !string.Equals(tabletStatus, "active", StringComparison.Ordinal))
        {
            return new AgentSessionValidationResult(
                false,
                orgId,
                tabletId,
                StatusCodes.Status403Forbidden,
                "TABLET_DEVICE_INACTIVE",
                "This tablet device or session has been deactivated.");
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return new AgentSessionValidationResult(
                false,
                orgId,
                tabletId,
                StatusCodes.Status401Unauthorized,
                "AGENT_SESSION_EXPIRED",
                "Offline agent session expired (72h limit reached). Re-authenticate online to start a new session.");
        }

        return new AgentSessionValidationResult(true, orgId, tabletId);
    }

    private static async Task<IResult> HandleCreateOrganizationAsync(
        CreateOrganizationRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        string orgType = request.Type?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Name) || !AllowedOrganizationTypes.Contains(orgType))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Organization name and valid organization type are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand checkCaller = conn.CreateCommand())
        {
            checkCaller.Transaction = tx;
            checkCaller.CommandText = "SELECT status::text FROM identity.identity WHERE id = @id;";
            checkCaller.Parameters.AddWithValue("id", callerId);
            string? status = await checkCaller.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(status, "active", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Caller identity must be active before registering an organization.");
            }
        }

        Guid orgId = Guid.NewGuid();
        await using (NpgsqlCommand insertOrg = conn.CreateCommand())
        {
            insertOrg.Transaction = tx;
            insertOrg.CommandText = """
                INSERT INTO identity.organization (id, name, type, registration_number, verification_status, primary_contact_identity_id)
                VALUES (@id, @name, @type::organization_type, @regNo, 'pending_verification', @contactId);
                """;
            insertOrg.Parameters.AddWithValue("id", orgId);
            insertOrg.Parameters.AddWithValue("name", request.Name.Trim());
            insertOrg.Parameters.AddWithValue("type", orgType);
            NpgsqlParameter regNoParam = insertOrg.Parameters.Add("regNo", NpgsqlDbType.Text);
            regNoParam.Value = string.IsNullOrWhiteSpace(request.RegistrationNumber) ? DBNull.Value : request.RegistrationNumber.Trim();
            insertOrg.Parameters.AddWithValue("contactId", callerId);
            await insertOrg.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand roleCmd = conn.CreateCommand())
        {
            roleCmd.Transaction = tx;
            roleCmd.CommandText = """
                INSERT INTO identity.role_profile (identity_id, role_type, linked_organization_id)
                VALUES (@identityId, 'org_admin', @orgId)
                ON CONFLICT (identity_id, role_type, linked_organization_id) DO NOTHING;
                """;
            roleCmd.Parameters.AddWithValue("identityId", callerId);
            roleCmd.Parameters.AddWithValue("orgId", orgId);
            await roleCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "organization.created",
                "organization",
                orgId,
                callerId,
                JsonSerializer.Serialize(new { orgType }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    organizationId = orgId,
                    name = request.Name.Trim(),
                    type = orgType,
                    verificationStatus = "pending_verification"
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleVerifyOrganizationAsync(
        Guid organizationId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid? updated;
        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE identity.organization
                SET verification_status = 'verified',
                    updated_at = now()
                WHERE id = @id
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("id", organizationId);
            updated = await cmd.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (!updated.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Organization was not found.");
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("admin", "organization.verified", "organization", organizationId, adminId == Guid.Empty ? null : adminId),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { organizationId, verificationStatus = "verified" },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleAddAgentAsync(
        Guid organizationId,
        AddOrganizationAgentRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        if (request.AgentIdentityId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "AgentIdentityId is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        string? verificationStatus = null;
        bool isOrgAdmin = false;
        await using (NpgsqlCommand orgCmd = conn.CreateCommand())
        {
            orgCmd.Transaction = tx;
            orgCmd.CommandText = """
                SELECT
                    o.verification_status::text,
                    EXISTS (
                        SELECT 1
                        FROM identity.role_profile rp
                        JOIN identity.identity caller ON caller.id = rp.identity_id
                        WHERE rp.identity_id = @callerId
                          AND caller.status = 'active'
                          AND rp.role_type = 'org_admin'
                          AND rp.linked_organization_id = o.id
                    )
                FROM identity.organization o
                WHERE o.id = @orgId;
                """;
            orgCmd.Parameters.AddWithValue("orgId", organizationId);
            orgCmd.Parameters.AddWithValue("callerId", callerId);

            await using NpgsqlDataReader reader = await orgCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                verificationStatus = reader.GetString(0);
                isOrgAdmin = reader.GetBoolean(1);
            }
        }

        if (verificationStatus is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Organization was not found.");
        }

        if (!isOrgAdmin)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Only an active organization administrator may onboard agents.");
        }

        // BR-A-03: No agent may be added until Organization.verification_status = verified.
        if (!string.Equals(verificationStatus, "verified", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AGENT_ORGANIZATION_NOT_VERIFIED",
                "Agents can only be added after the organization is verified.");
        }

        await using (NpgsqlCommand targetCmd = conn.CreateCommand())
        {
            targetCmd.Transaction = tx;
            targetCmd.CommandText = "SELECT status::text FROM identity.identity WHERE id = @agentId;";
            targetCmd.Parameters.AddWithValue("agentId", request.AgentIdentityId);
            string? targetStatus = await targetCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(targetStatus, "active", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Target agent identity must exist and be active.");
            }
        }

        Guid roleProfileId;
        await using (NpgsqlCommand roleCmd = conn.CreateCommand())
        {
            roleCmd.Transaction = tx;
            roleCmd.CommandText = """
                INSERT INTO identity.role_profile (identity_id, role_type, linked_organization_id)
                VALUES (@agentId, 'agent', @orgId)
                ON CONFLICT (identity_id, role_type, linked_organization_id) DO UPDATE
                SET role_type = EXCLUDED.role_type
                RETURNING id;
                """;
            roleCmd.Parameters.AddWithValue("agentId", request.AgentIdentityId);
            roleCmd.Parameters.AddWithValue("orgId", organizationId);
            roleProfileId = (Guid)(await roleCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "agent.onboarded",
                "role_profile",
                roleProfileId,
                callerId,
                JsonSerializer.Serialize(new { organizationId, agentIdentityId = request.AgentIdentityId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    roleProfileId,
                    organizationId,
                    agentIdentityId = request.AgentIdentityId,
                    roleType = "agent"
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleBindTabletAsync(
        BindTabletRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid callerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated caller is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceSerial)
            || string.IsNullOrWhiteSpace(request.DeviceFingerprint)
            || request.OrganizationId == Guid.Empty
            || request.AssignedAgentId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "DeviceSerial, DeviceFingerprint, OrganizationId, and AssignedAgentId are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand authCmd = conn.CreateCommand())
        {
            authCmd.Transaction = tx;
            authCmd.CommandText = """
                SELECT
                    o.verification_status::text,
                    EXISTS (
                        SELECT 1
                        FROM identity.role_profile rp
                        WHERE rp.identity_id = @agentId
                          AND rp.role_type = 'agent'
                          AND rp.linked_organization_id = o.id
                    ) AS target_is_agent,
                    (
                        @callerId = @agentId
                        OR EXISTS (
                            SELECT 1 FROM identity.role_profile cp
                            WHERE cp.identity_id = @callerId
                              AND cp.role_type = 'org_admin'
                              AND cp.linked_organization_id = o.id
                        )
                    ) AS caller_authorized
                FROM identity.organization o
                WHERE o.id = @orgId;
                """;
            authCmd.Parameters.AddWithValue("orgId", request.OrganizationId);
            authCmd.Parameters.AddWithValue("agentId", request.AssignedAgentId);
            authCmd.Parameters.AddWithValue("callerId", callerId);

            await using NpgsqlDataReader reader = await authCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Organization was not found.");
            }

            string orgStatus = reader.GetString(0);
            bool targetIsAgent = reader.GetBoolean(1);
            bool callerAuthorized = reader.GetBoolean(2);

            if (!string.Equals(orgStatus, "verified", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "AGENT_ORGANIZATION_NOT_VERIFIED", "Organization must be verified before binding a tablet device.");
            }

            if (!targetIsAgent || !callerAuthorized)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Caller or assigned identity lacks agent membership in this organization.");
            }
        }

        Guid tabletId = Guid.NewGuid();
        try
        {
            await using NpgsqlCommand insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO identity.tablet_device (
                    id, device_serial, device_fingerprint, assigned_agent_id, bound_organization_id, status, bound_at
                )
                VALUES (@id, @serial, @fp, @agentId, @orgId, 'active', now());
                """;
            insertCmd.Parameters.AddWithValue("id", tabletId);
            insertCmd.Parameters.AddWithValue("serial", request.DeviceSerial.Trim());
            insertCmd.Parameters.AddWithValue("fp", request.DeviceFingerprint.Trim());
            insertCmd.Parameters.AddWithValue("agentId", request.AssignedAgentId);
            insertCmd.Parameters.AddWithValue("orgId", request.OrganizationId);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Tablet serial or device fingerprint is already bound.");
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "tablet.bound",
                "tablet_device",
                tabletId,
                callerId,
                JsonSerializer.Serialize(new { request.OrganizationId, request.AssignedAgentId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    tabletId,
                    deviceSerial = request.DeviceSerial.Trim(),
                    organizationId = request.OrganizationId,
                    assignedAgentId = request.AssignedAgentId,
                    status = "active"
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleDeactivateTabletAsync(
        Guid tabletId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid? deactivatedId;
        await using (NpgsqlCommand updCmd = conn.CreateCommand())
        {
            updCmd.Transaction = tx;
            updCmd.CommandText = """
                UPDATE identity.tablet_device
                SET status = 'deactivated',
                    deactivated_at = now(),
                    deactivated_by_admin_id = @adminId
                WHERE id = @tabletId
                RETURNING id;
                """;
            updCmd.Parameters.AddWithValue("tabletId", tabletId);
            NpgsqlParameter adminParam = updCmd.Parameters.Add("adminId", NpgsqlDbType.Uuid);
            adminParam.Value = adminId == Guid.Empty ? DBNull.Value : adminId;
            deactivatedId = await updCmd.ExecuteScalarAsync(cancellationToken) as Guid?;
        }

        if (!deactivatedId.HasValue)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Tablet device was not found.");
        }

        int endedSessions;
        await using (NpgsqlCommand endSessCmd = conn.CreateCommand())
        {
            endSessCmd.Transaction = tx;
            endSessCmd.CommandText = """
                UPDATE identity.agent_session
                SET ended_at = now()
                WHERE tablet_device_id = @tabletId AND ended_at IS NULL;
                """;
            endSessCmd.Parameters.AddWithValue("tabletId", tabletId);
            endedSessions = await endSessCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "admin",
                "tablet.deactivated",
                "tablet_device",
                tabletId,
                adminId == Guid.Empty ? null : adminId,
                JsonSerializer.Serialize(new { endedSessions }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { tabletId, status = "deactivated", endedSessions },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleStartAgentSessionAsync(
        StartAgentSessionRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent is required.");
        }

        if (request.TabletId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceFingerprint))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "TabletId and DeviceFingerprint are required.");
        }

        if (request.LifetimeHours < 1 || request.LifetimeHours > 72)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Offline agent session lifetime must be between 1 and 72 hours (ADR-009).");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid orgId;
        await using (NpgsqlCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT
                    td.assigned_agent_id,
                    td.device_fingerprint,
                    td.status::text,
                    td.bound_organization_id,
                    o.verification_status::text,
                    EXISTS (
                        SELECT 1 FROM identity.role_profile rp
                        WHERE rp.identity_id = @agentId
                          AND rp.role_type = 'agent'
                          AND rp.linked_organization_id = td.bound_organization_id
                    ) AS has_agent_role
                FROM identity.tablet_device td
                JOIN identity.organization o ON o.id = td.bound_organization_id
                WHERE td.id = @tabletId;
                """;
            cmd.Parameters.AddWithValue("tabletId", request.TabletId);
            cmd.Parameters.AddWithValue("agentId", agentId);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "TABLET_DEVICE_INACTIVE", "Bound tablet device was not found.");
            }

            Guid? assignedAgentId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            string? fp = reader.IsDBNull(1) ? null : reader.GetString(1);
            string tabletStatus = reader.GetString(2);
            orgId = reader.GetGuid(3);
            string orgStatus = reader.GetString(4);
            bool hasAgentRole = reader.GetBoolean(5);

            if (assignedAgentId != agentId
                || !string.Equals(fp, request.DeviceFingerprint.Trim(), StringComparison.Ordinal)
                || !string.Equals(tabletStatus, "active", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "TABLET_DEVICE_INACTIVE", "Tablet binding does not match or tablet is inactive.");
            }

            if (!string.Equals(orgStatus, "verified", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "AGENT_ORGANIZATION_NOT_VERIFIED", "Agent organization is not verified.");
            }

            if (!hasAgentRole)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Caller does not hold an active agent role in this organization.");
            }
        }

        Guid sessionId = Guid.NewGuid();
        DateTimeOffset startedAt;
        DateTimeOffset expiresAt;
        await using (NpgsqlCommand sessCmd = conn.CreateCommand())
        {
            sessCmd.Transaction = tx;
            sessCmd.CommandText = """
                INSERT INTO identity.agent_session (id, agent_identity_id, tablet_device_id, organization_id, expires_at)
                VALUES (@id, @agentId, @tabletId, @orgId, now() + make_interval(hours => @hours))
                RETURNING started_at, expires_at;
                """;
            sessCmd.Parameters.AddWithValue("id", sessionId);
            sessCmd.Parameters.AddWithValue("agentId", agentId);
            sessCmd.Parameters.AddWithValue("tabletId", request.TabletId);
            sessCmd.Parameters.AddWithValue("orgId", orgId);
            sessCmd.Parameters.AddWithValue("hours", request.LifetimeHours);

            await using NpgsqlDataReader r = await sessCmd.ExecuteReaderAsync(cancellationToken);
            await r.ReadAsync(cancellationToken);
            startedAt = r.GetFieldValue<DateTimeOffset>(0);
            expiresAt = r.GetFieldValue<DateTimeOffset>(1);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "agent.session_started",
                "agent_session",
                sessionId,
                agentId,
                JsonSerializer.Serialize(new { request.TabletId, orgId, request.LifetimeHours }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    sessionId,
                    tabletId = request.TabletId,
                    organizationId = orgId,
                    startedAt,
                    expiresAt,
                    lifetimeHours = request.LifetimeHours
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleAgentAssistedRegisterAsync(
        AgentAssistedRegistrationRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        INinVerificationClient ninClient,
        ILivenessVerificationClient livenessClient,
        IVoiceOtpGatewayClient voiceOtpClient,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent is required.");
        }

        if (!TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid sessionId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status403Forbidden, "AGENT_SESSION_REQUIRED", "An active bound-tablet agent session is required.");
        }

        if (!NinVerificationHashHelper.IsValidNinFormat(request.Nin))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "NIN must be exactly 11 digits.");
        }

        if (!SelfServiceRegistrationEndpoints.TryNormalizeNigerianPhone(request.Phone, out string normalizedPhone))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Phone number must be a valid Nigerian mobile number.");
        }

        byte[]? decodedPhotoBytes = null;
        if (!string.IsNullOrWhiteSpace(request.LivenessPhotoBase64))
        {
            try
            {
                decodedPhotoBytes = Convert.FromBase64String(request.LivenessPhotoBase64.Trim());
            }
            catch (FormatException)
            {
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "Liveness photo payload must be valid base64.");
            }
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection gateConn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction gateTx = await gateConn.BeginTransactionAsync(cancellationToken);
        AgentSessionValidationResult sessionGate = await ValidateActiveAgentSessionAsync(gateConn, gateTx, agentId, sessionId, cancellationToken);
        await gateTx.RollbackAsync(cancellationToken);

        if (!sessionGate.IsValid)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(
                httpContext,
                sessionGate.ErrorStatusCode,
                sessionGate.ErrorCode ?? "PERMISSION_DENIED",
                sessionGate.ErrorMessage ?? "Agent session is not authorized.");
        }

        NinLookupResult lookup = await ninClient.VerifyNinAsync(request.Nin, cancellationToken);
        switch (lookup.Status)
        {
            case NinLookupStatus.NotFound:
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "NIN_NOT_FOUND", "No national identity record matched the provided NIN.");

            case NinLookupStatus.ProviderUnavailable:
            case NinLookupStatus.MalformedResponse:
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "NIN_PROVIDER_UNAVAILABLE", "Identity verification provider is unavailable.");

            case NinLookupStatus.FuzzyMatch:
                {
                    Guid reviewQueueItemId = Guid.NewGuid();
                    await using NpgsqlConnection fuzzyConn = await dataSource.OpenConnectionAsync(cancellationToken);
                    await using NpgsqlTransaction fuzzyTx = await fuzzyConn.BeginTransactionAsync(cancellationToken);
                    await AuditWriter.WriteAsync(
                        fuzzyConn,
                        fuzzyTx,
                        new AuditRecord(
                            "identity",
                            "identity.nin_mismatch_queued",
                            "review_queue_item",
                            reviewQueueItemId,
                            agentId,
                            JsonSerializer.Serialize(new { reviewCategory = "IDENTITY_NIN_MISMATCH", confidenceScore = lookup.ConfidenceScore }, JsonOptions)),
                        cancellationToken);
                    await fuzzyTx.CommitAsync(cancellationToken);

                    return Results.Json(
                        new ApiResponse<object>(
                            new
                            {
                                status = "under_review",
                                code = "NIN_LOOKUP_UNDER_REVIEW",
                                reviewQueueItemId
                            },
                            new ApiMeta(httpContext.TraceIdentifier)),
                        statusCode: StatusCodes.Status202Accepted);
                }

            case NinLookupStatus.Matched:
                break;

            default:
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "NIN_PROVIDER_UNAVAILABLE", "Identity verification could not be completed.");
        }

        LivenessVerificationResult? livenessResult = null;
        if (decodedPhotoBytes is not null)
        {
            // Compare liveness photo then zero out the transient byte buffer immediately (Doc 05 §3.3, S3.1.M3).
            livenessResult = await livenessClient.VerifyAndDiscardAsync(decodedPhotoBytes, lookup.VerificationRef!, cancellationToken);
        }

        Guid candidateId = Guid.NewGuid();
        Guid effectiveIdentityId;
        bool resumed = false;
        string? normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid? insertedId = null;
        await SelfServiceRegistrationEndpoints.ExecuteTxSqlAsync(conn, tx, "SAVEPOINT sp_agent_reg;", cancellationToken);
        try
        {
            insertedId = await SelfServiceRegistrationEndpoints.TryInsertPendingIdentityAsync(
                conn,
                tx,
                candidateId,
                lookup,
                normalizedPhone,
                normalizedEmail,
                request.StateId,
                request.LgaId,
                request.WardId,
                request.CommunityId,
                cancellationToken);
            await SelfServiceRegistrationEndpoints.ExecuteTxSqlAsync(conn, tx, "RELEASE SAVEPOINT sp_agent_reg;", cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await SelfServiceRegistrationEndpoints.ExecuteTxSqlAsync(conn, tx, "ROLLBACK TO SAVEPOINT sp_agent_reg;", cancellationToken);
            await SelfServiceRegistrationEndpoints.ExecuteTxSqlAsync(conn, tx, "RELEASE SAVEPOINT sp_agent_reg;", cancellationToken);
        }

        if (insertedId.HasValue)
        {
            effectiveIdentityId = insertedId.Value;
        }
        else
        {
            SelfServiceRegistrationEndpoints.ExistingIdentityState? existing =
                await SelfServiceRegistrationEndpoints.FindByNinVerificationRefAsync(conn, tx, lookup.VerificationRef!, cancellationToken);
            if (existing is null)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONTACT_ALREADY_REGISTERED", "Unable to complete registration with provided contact details.");
            }

            if (!string.Equals(existing.Status, "pending_verification", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "NIN_ALREADY_REGISTERED", "This identity is already registered.");
            }

            effectiveIdentityId = existing.Id;
            resumed = true;
        }

        if (livenessResult is not null)
        {
            await using NpgsqlCommand liveCmd = conn.CreateCommand();
            liveCmd.Transaction = tx;
            liveCmd.CommandText = """
                INSERT INTO identity.liveness_check_record (
                    identity_id, agent_identity_id, matched, confidence_score, photo_discarded
                )
                VALUES (@identityId, @agentId, @matched, @confidence, @discarded);
                """;
            liveCmd.Parameters.AddWithValue("identityId", effectiveIdentityId);
            liveCmd.Parameters.AddWithValue("agentId", agentId);
            liveCmd.Parameters.AddWithValue("matched", livenessResult.Matched);
            liveCmd.Parameters.AddWithValue("confidence", livenessResult.ConfidenceScore);
            liveCmd.Parameters.AddWithValue("discarded", livenessResult.PhotoDiscarded);
            await liveCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        string voiceOtpPlaintext = SelfServiceRegistrationEndpoints.GenerateSixDigitOtp();
        string otpHash = SelfServiceRegistrationEndpoints.ComputeOtpHash(effectiveIdentityId, voiceOtpPlaintext);
        await SelfServiceRegistrationEndpoints.InsertOtpChallengeAsync(conn, tx, effectiveIdentityId, "voice", otpHash, cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "identity.agent_assisted_registered",
                "identity",
                effectiveIdentityId,
                agentId,
                JsonSerializer.Serialize(new
                {
                    actingAgentId = agentId,
                    beneficiaryFarmerId = effectiveIdentityId,
                    sessionGate.OrganizationId,
                    sessionGate.TabletDeviceId,
                    resumed,
                    livenessPerformed = livenessResult is not null,
                    livenessMatched = livenessResult?.Matched
                }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        VoiceOtpCallResult voiceResult = await voiceOtpClient.SendVoiceOtpAsync(
            new VoiceOtpCallRequest(normalizedPhone, voiceOtpPlaintext),
            cancellationToken);

        return Results.Json(
            new ApiResponse<object>(
                new
                {
                    identityId = effectiveIdentityId,
                    voiceOtpCallStatus = voiceResult.Initiated ? "initiated" : "failed",
                    resumed,
                    livenessMatched = livenessResult?.Matched,
                    livenessConfidence = livenessResult?.ConfidenceScore,
                    livenessPhotoDiscarded = livenessResult?.PhotoDiscarded ?? false
                },
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleConfirmVoiceOtpAsync(
        Guid identityId,
        ConfirmVoiceOtpRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerGuid(httpContext, identityAccessor, out Guid agentId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated agent is required.");
        }

        if (identityId == Guid.Empty || !SelfServiceRegistrationEndpoints.IsSixDigitOtp(request.Otp))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "Enter the 6-digit VoiceOTP verification code.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        if (TryResolveSessionId(httpContext.Request, request.AgentSessionId, out Guid explicitSessionId))
        {
            AgentSessionValidationResult gate = await ValidateActiveAgentSessionAsync(conn, tx, agentId, explicitSessionId, cancellationToken);
            if (!gate.IsValid)
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, gate.ErrorStatusCode, gate.ErrorCode ?? "PERMISSION_DENIED", gate.ErrorMessage ?? "Agent session invalid.");
            }
        }

        SelfServiceRegistrationEndpoints.OtpChallengeRow? challenge =
            await SelfServiceRegistrationEndpoints.LoadLatestOpenChallengeForUpdateAsync(conn, tx, identityId, "voice", cancellationToken);
        if (challenge is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "INVALID_OTP", "No active VoiceOTP challenge found.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (challenge.LockedUntil.HasValue && challenge.LockedUntil.Value > nowUtc)
        {
            int retryAfter = Math.Max(1, (int)Math.Ceiling((challenge.LockedUntil.Value - nowUtc).TotalSeconds));
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.LockedResult(httpContext, retryAfter);
        }

        if (challenge.ExpiresAt <= nowUtc)
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "OTP_EXPIRED", "VoiceOTP code expired.");
        }

        string candidateHash = SelfServiceRegistrationEndpoints.ComputeOtpHash(identityId, request.Otp.Trim());
        bool matched = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(candidateHash),
            System.Text.Encoding.ASCII.GetBytes(challenge.OtpHash));

        if (!matched)
        {
            int nextAttempts = challenge.AttemptCount + 1;
            int lockoutSeconds = 0;
            if (nextAttempts >= 5)
            {
                int power = Math.Min(nextAttempts - 5, 10);
                lockoutSeconds = Math.Min(900, 60 * (1 << power));
            }

            await SelfServiceRegistrationEndpoints.RecordFailedOtpAttemptAsync(conn, tx, challenge.Id, nextAttempts, lockoutSeconds, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            if (lockoutSeconds > 0)
            {
                return SelfServiceRegistrationEndpoints.LockedResult(httpContext, lockoutSeconds);
            }

            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("INVALID_OTP", "VoiceOTP code entered is incorrect.", new { attemptCount = nextAttempts, attemptsRemaining = 5 - nextAttempts }),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        await SelfServiceRegistrationEndpoints.MarkChallengeVerifiedAsync(conn, tx, challenge.Id, cancellationToken);
        string farmerId = await SelfServiceRegistrationEndpoints.ActivateIdentityAndCreateFarmerProfileAsync(conn, tx, identityId, cancellationToken);

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord(
                "identity",
                "identity.activated",
                "identity",
                identityId,
                agentId,
                JsonSerializer.Serialize(new { channel = "voice", farmerId, actingAgentId = agentId }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { identityId, status = "active", farmerId },
            new ApiMeta(httpContext.TraceIdentifier)));
    }
}
