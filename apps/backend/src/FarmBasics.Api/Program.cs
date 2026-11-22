using System.Diagnostics;
using FarmBasics.Api.Accounts;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Analytics;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.CropCycles;
using FarmBasics.Api.FieldOps;
using FarmBasics.Api.Fobs;
using FarmBasics.Api.Geography;
using FarmBasics.Api.Identity;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.MasterData;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.Api.ProductionUnits;
using FarmBasics.Api.Registration;
using FarmBasics.Api.Reviews;
using FarmBasics.Api.Support;
using FarmBasics.Api.Sync;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using FarmBasics.BuildingBlocks.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((_, loggerConfig) =>
    loggerConfig.ConfigureFarmBasicsLogging(serviceName: "FarmBasics.Api"));

builder.Services.AddSingleton<ICurrentIdentityAccessor, JwtCurrentIdentityAccessor>();
builder.Services.AddScoped<IAdminPermissionChecker, PostgresAdminPermissionChecker>();
builder.Services.AddSingleton(sp => new DatabaseConnectionSource(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<SchemaMigrationRunner>();
builder.Services.AddHostedService<DatabaseBootstrapAndPartitionService>();
builder.Services.AddHostedService<OutboxLedgerBackgroundService>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        string? authority = builder.Configuration["Auth:Authority"];
        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
        }

        options.Audience = builder.Configuration["Auth:Audience"] ?? "farm-basics-api";
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
            ValidateAudience = true,
            ValidateLifetime = true,
            NameClaimType = "sub",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    ApiErrorResponse payload = new(
                        new ApiErrorDetail("AUTHENTICATION_REQUIRED", "Authentication is required to access this resource."),
                        new ApiMeta(context.HttpContext.TraceIdentifier));
                    await context.Response.WriteAsJsonAsync(payload, context.HttpContext.RequestAborted);
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddFarmBasicsRateLimiting();
builder.Services.AddFarmBasicsIntegrations(builder.Configuration);
builder.Services.AddSingleton<IReviewVerdictHandler, IdentityNinMismatchVerdictHandler>();
builder.Services.AddSingleton<ReviewVerdictWriteBackRegistry>();

WebApplication app = builder.Build();

app.UseFarmBasicsRequestPipeline();
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    NpgsqlDataSource? dataSource = context.RequestServices.GetRequiredService<DatabaseConnectionSource>().DataSource;
    if (dataSource is not null && context.User.Identity?.IsAuthenticated == true)
    {
        ICurrentIdentityAccessor identityAccessor = context.RequestServices.GetRequiredService<ICurrentIdentityAccessor>();
        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(context.User);
        if (identity is not null && Guid.TryParse(identity.Subject, out Guid subjectGuid) && subjectGuid != Guid.Empty)
        {
            SchemaMigrationRunner migrationRunner = context.RequestServices.GetRequiredService<SchemaMigrationRunner>();
            await migrationRunner.EnsureMigratedAsync(dataSource, context.RequestAborted);
            JitProvisioningOutcome jitOutcome = await IdentityJitProvisioningService.EnsureProvisionedAsync(
                dataSource,
                subjectGuid,
                context.User,
                context.RequestAborted);
            context.Items[nameof(JitProvisioningOutcome)] = jitOutcome;
        }
    }

    await next();
});
app.UseAuthorization();

app.MapGet("/health/live", () =>
    Results.Ok(new
    {
        status = "live",
        timestampUtc = DateTimeOffset.UtcNow
    }));

app.MapGet("/health/ready", async (DatabaseConnectionSource dbSource, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    NpgsqlDataSource? dataSource = dbSource.DataSource;
    if (dataSource is null)
    {
        return Results.Json(
            new
            {
                status = "unhealthy",
                database = "unconfigured",
                timestampUtc = DateTimeOffset.UtcNow
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        await using NpgsqlCommand command = dataSource.CreateCommand("SELECT 1, PostGIS_Version();");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(timeoutCts.Token);
        if (!await reader.ReadAsync(timeoutCts.Token))
        {
            return Results.Json(
                new
                {
                    status = "unhealthy",
                    database = "no_result",
                    timestampUtc = DateTimeOffset.UtcNow
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        string postgisVersion = reader.GetString(1);
        return Results.Ok(new
        {
            status = "ready",
            database = "reachable",
            postgis = postgisVersion,
            timestampUtc = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        ApiLog.DatabaseReadinessFailed(logger, ex.GetType().Name);
        return Results.Json(
            new
            {
                status = "unhealthy",
                database = "unreachable",
                timestampUtc = DateTimeOffset.UtcNow
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/v1/auth/me", [Authorize] (
    HttpContext httpContext,
    ICurrentIdentityAccessor identityAccessor) =>
{
    CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
    if (identity is null)
    {
        return Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("AUTHENTICATION_REQUIRED", "Authenticated subject claim is missing."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    JitProvisioningOutcome? jitOutcome =
        httpContext.Items.TryGetValue(nameof(JitProvisioningOutcome), out object? rawOutcome)
        && rawOutcome is JitProvisioningOutcome parsedOutcome
            ? parsedOutcome
            : null;

    return Results.Ok(new ApiResponse<object>(
        new
        {
            identity.Subject,
            identity.Issuer,
            identityId = jitOutcome?.IdentityId,
            jitProvisioned = jitOutcome?.Provisioned ?? false,
            permissionsSource = "live_database_recheck_required"
        },
        new ApiMeta(httpContext.TraceIdentifier)));
});

app.MapGet("/v1/admin/ping", [Authorize] (HttpContext httpContext, ICurrentIdentityAccessor identityAccessor) =>
{
    CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
    return Results.Ok(new ApiResponse<object>(
        new
        {
            status = "authorized",
            subject = identity?.Subject
        },
        new ApiMeta(httpContext.TraceIdentifier)));
}).RequireAdminPermission("admin", "ping");

app.MapGeographyEndpoints();
app.MapAuditEndpoints();
app.MapSelfServiceRegistrationEndpoints();
app.MapAgentOnboardingEndpoints();
app.MapFobEndpoints();
app.MapRegistrationAttestationEndpoints();
app.MapReviewEngineEndpoints();
app.MapMasterDataEndpoints();
app.MapNotificationAndCapabilityEndpoints();
app.MapProductionUnitEndpoints();
app.MapCropCycleEndpoints();
app.MapAccountsEndpoints();
app.MapFieldOpsEndpoints();
app.MapAnalyticsEndpoints();
app.MapSupportEndpoints();
app.MapPhoneChangeEndpoints();
app.MapSyncSpikeEndpoints();
app.MapMetrics("/metrics");

app.Run();

internal sealed class DatabaseConnectionSource(IConfiguration configuration) : IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource?> _dataSource = new(() =>
    {
        string? cs = configuration.GetConnectionString("Default");
        return string.IsNullOrWhiteSpace(cs) ? null : NpgsqlDataSource.Create(cs);
    });

    public NpgsqlDataSource? DataSource => _dataSource.Value;

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated && _dataSource.Value is not null)
        {
            await _dataSource.Value.DisposeAsync();
        }
    }
}

internal static partial class ApiLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception processing request {RequestId}")]
    public static partial void UnhandledException(Microsoft.Extensions.Logging.ILogger logger, string requestId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database readiness probe failed with {ErrorType}")]
    public static partial void DatabaseReadinessFailed(Microsoft.Extensions.Logging.ILogger logger, string errorType);
}

public sealed partial class Program;
