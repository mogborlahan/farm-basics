using System.Diagnostics;
using System.Threading.RateLimiting;
using FarmBasics.Api.Sync;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using FarmBasics.BuildingBlocks.Logging;
using FarmBasics.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddScoped<IAdminPermissionChecker, FailClosedAdminPermissionChecker>();
builder.Services.AddSingleton(sp => new DatabaseConnectionSource(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<SyncSpikeSchemaInitializer>();

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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        ApiErrorResponse payload = new(
            new ApiErrorDetail("RATE_LIMIT_EXCEEDED", "Too many requests. Please wait before trying again."),
            new ApiMeta(context.HttpContext.TraceIdentifier));
        await context.HttpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        string partitionKey = httpContext.User.FindFirst("sub")?.Value
                              ?? httpContext.Connection.RemoteIpAddress?.ToString()
                              ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

WebApplication app = builder.Build();

app.Use(async (context, next) =>
{
    long startedAt = Stopwatch.GetTimestamp();
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        ILogger<Program> logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        ApiLog.UnhandledException(logger, context.TraceIdentifier, ex);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            ApiErrorResponse error = new(
                new ApiErrorDetail("INTERNAL_ERROR", "An unexpected error occurred."),
                new ApiMeta(context.TraceIdentifier));
            await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
        }
    }
    finally
    {
        double elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        string routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                              ?? context.Request.Path.Value
                              ?? "/";
        string method = context.Request.Method;
        string statusClass = PlatformMetrics.ClassifyStatusCode(context.Response.StatusCode);

        PlatformMetrics.HttpRequestDurationSeconds.WithLabels(method, routePattern).Observe(elapsedSeconds);
        PlatformMetrics.HttpRequestsTotal.WithLabels(method, routePattern, statusClass).Inc();
    }
});

app.UseAuthentication();
app.UseRateLimiter();
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

app.MapGet("/v1/auth/me", [Authorize] (HttpContext httpContext, ICurrentIdentityAccessor identityAccessor) =>
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

    return Results.Ok(new ApiResponse<object>(
        new
        {
            identity.Subject,
            identity.Issuer,
            permissionsSource = "live_database_recheck_required"
        },
        new ApiMeta(httpContext.TraceIdentifier)));
});

app.MapGet("/v1/admin/ping", [Authorize] async (
    HttpContext httpContext,
    ICurrentIdentityAccessor identityAccessor,
    IAdminPermissionChecker permissionChecker,
    CancellationToken cancellationToken) =>
{
    CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
    if (identity is null)
    {
        return Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("AUTHENTICATION_REQUIRED", "Authentication is required."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    bool allowed = await permissionChecker.HasPermissionAsync(identity.Subject, "admin:ping", cancellationToken);
    if (!allowed)
    {
        return Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("PERMISSION_DENIED", "You do not have permission to access this admin endpoint."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new ApiResponse<object>(
        new
        {
            status = "authorized",
            identity.Subject
        },
        new ApiMeta(httpContext.TraceIdentifier)));
});

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
