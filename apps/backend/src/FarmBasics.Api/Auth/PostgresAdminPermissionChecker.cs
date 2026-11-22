using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Auth;

/// <summary>
/// Live per-request permission verification against admin.admin_role_assignment (Doc/03-ENG §1.7, Rule 8).
/// Never caches assignments across requests and always fails closed when unconfigured or unmapped.
/// </summary>
internal sealed class PostgresAdminPermissionChecker(DatabaseConnectionSource dbSource) : IAdminPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string subject, string permission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        if (!Guid.TryParse(subject, out Guid identityId) || identityId == Guid.Empty)
        {
            return false;
        }

        string[] parts = permission.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
        {
            return false;
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return false;
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM admin.admin_role_assignment ara
                JOIN identity.identity i ON i.id = ara.identity_id
                JOIN admin.admin_role_permission arp ON arp.admin_role_id = ara.admin_role_id
                JOIN admin.admin_permission ap ON ap.id = arp.permission_id
                WHERE ara.identity_id = @identityId
                  AND i.status = 'active'
                  AND ara.revoked_at IS NULL
                  AND ap.resource = @resource
                  AND ap.action = @action
            );
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("resource", parts[0]);
        cmd.Parameters.AddWithValue("action", parts[1]);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool allowed && allowed;
    }
}
