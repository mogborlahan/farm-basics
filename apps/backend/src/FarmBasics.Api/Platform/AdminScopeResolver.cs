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

    public static async Task<bool> IsIdentityInScopeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        AdminPermissionScope scope,
        Guid targetIdentityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.HasGlobal)
        {
            return true;
        }

        bool hasRegions = scope.RegionIds.Count > 0;
        bool hasOrgs = scope.OrganizationIds.Count > 0;
        if (!hasRegions && !hasOrgs)
        {
            return false;
        }

        await using NpgsqlCommand cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.identity i
                LEFT JOIN identity.lga l ON l.id = i.lga_id
                WHERE i.id = @targetIdentityId
                  AND (
                      (@hasRegions AND (i.state_id = ANY(@regionIds) OR i.lga_id = ANY(@regionIds) OR l.state_id = ANY(@regionIds)))
                      OR (@hasOrgs AND EXISTS (
                          SELECT 1
                          FROM identity.role_profile rp
                          WHERE rp.identity_id = i.id
                            AND rp.linked_organization_id = ANY(@orgIds)
                      ))
                  )
            );
            """;
        cmd.Parameters.AddWithValue("targetIdentityId", targetIdentityId);
        cmd.Parameters.AddWithValue("hasRegions", hasRegions);
        cmd.Parameters.AddWithValue("regionIds", scope.RegionIds.ToArray());
        cmd.Parameters.AddWithValue("hasOrgs", hasOrgs);
        cmd.Parameters.AddWithValue("orgIds", scope.OrganizationIds.ToArray());

        object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is bool inScope && inScope;
    }
}
