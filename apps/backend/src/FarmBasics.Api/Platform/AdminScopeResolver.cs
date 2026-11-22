using Npgsql;

namespace FarmBasics.Api.Platform;

internal sealed record AdminPermissionScope(
    bool HasGlobal,
    IReadOnlyList<Guid> RegionIds,
    IReadOnlyList<Guid> OrganizationIds);

/// <summary>
/// Resolves a caller's active admin_role_assignment scopes strictly for a specific (resource, action) permission (Rule 8).
/// Prevents privilege bleed across multiple role assignments held by one identity.
/// </summary>
internal static class AdminScopeResolver
{
    public static async Task<AdminPermissionScope> ResolveScopesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Guid callerId,
        string resource,
        string action,
        CancellationToken cancellationToken)
    {
        bool hasGlobal = false;
        HashSet<Guid> regions = [];
        HashSet<Guid> organizations = [];

        await using NpgsqlCommand cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = """
            SELECT ara.scope_type::text, ara.scope_ref_id
            FROM admin.admin_role_assignment ara
            JOIN identity.identity i ON i.id = ara.identity_id
            JOIN admin.admin_role_permission arp ON arp.admin_role_id = ara.admin_role_id
            JOIN admin.admin_permission ap ON ap.id = arp.permission_id
            WHERE ara.identity_id = @callerId
              AND i.status = 'active'
              AND ara.revoked_at IS NULL
              AND ap.resource = @resource
              AND ap.action = @action;
            """;
        cmd.Parameters.AddWithValue("callerId", callerId);
        cmd.Parameters.AddWithValue("resource", resource);
        cmd.Parameters.AddWithValue("action", action);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string scopeType = reader.GetString(0);
            Guid? scopeRefId = reader.IsDBNull(1) ? null : reader.GetGuid(1);

            switch (scopeType)
            {
                case "global":
                    hasGlobal = true;
                    break;
                case "region" when scopeRefId.HasValue:
                    regions.Add(scopeRefId.Value);
                    break;
                case "organization" when scopeRefId.HasValue:
                    organizations.Add(scopeRefId.Value);
                    break;
            }
        }

        return new AdminPermissionScope(
            HasGlobal: hasGlobal,
            RegionIds: regions.ToArray(),
            OrganizationIds: organizations.ToArray());
    }
}
