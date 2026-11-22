using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;

namespace FarmBasics.Api.Auth;

internal sealed class AdminPermissionEndpointFilter(string permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        ICurrentIdentityAccessor identityAccessor = httpContext.RequestServices.GetRequiredService<ICurrentIdentityAccessor>();
        IAdminPermissionChecker permissionChecker = httpContext.RequestServices.GetRequiredService<IAdminPermissionChecker>();

        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
        if (identity is null)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("AUTHENTICATION_REQUIRED", "Authentication is required to access this admin endpoint."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        bool allowed = await permissionChecker.HasPermissionAsync(identity.Subject, permission, httpContext.RequestAborted);
        if (!allowed)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("PERMISSION_DENIED", "You do not have permission to access this admin endpoint."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

internal static class AdminPermissionFilterExtensions
{
    public static RouteHandlerBuilder RequireAdminPermission(this RouteHandlerBuilder builder, string resource, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return builder.AddEndpointFilter(new AdminPermissionEndpointFilter($"{resource.Trim()}:{action.Trim()}"));
    }
}
