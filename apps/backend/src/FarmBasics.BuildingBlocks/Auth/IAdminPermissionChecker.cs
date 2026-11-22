namespace FarmBasics.BuildingBlocks.Auth;

/// <summary>
/// Live server-side permission lookup against persisted AdminRoleAssignment records.
/// Fail-closed by default in Sprint 0 until Sprint 1 WP-12 wires admin.admin_role_assignment.
/// </summary>
public interface IAdminPermissionChecker
{
    Task<bool> HasPermissionAsync(string subject, string permission, CancellationToken cancellationToken = default);
}

public sealed class FailClosedAdminPermissionChecker : IAdminPermissionChecker
{
    public Task<bool> HasPermissionAsync(string subject, string permission, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
