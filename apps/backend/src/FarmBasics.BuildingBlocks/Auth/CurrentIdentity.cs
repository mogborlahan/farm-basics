using System.Security.Claims;

namespace FarmBasics.BuildingBlocks.Auth;

/// <summary>
/// Identity-only representation extracted from a validated JWT.
/// Per Document 01A §3.2 and AGENTS.md Hard Rule 8, role/scope claims in the JWT
/// are explicitly ignored; authorization is always resolved fresh from the database.
/// </summary>
public sealed record CurrentIdentity(string Subject, string? Issuer, string? TokenId);

public interface ICurrentIdentityAccessor
{
    CurrentIdentity? GetCurrentIdentity(ClaimsPrincipal? principal);
}

public sealed class JwtCurrentIdentityAccessor : ICurrentIdentityAccessor
{
    public CurrentIdentity? GetCurrentIdentity(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? subject = principal.FindFirst("sub")?.Value
                          ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        string? issuer = principal.FindFirst("iss")?.Value;
        string? tokenId = principal.FindFirst("jti")?.Value;
        return new CurrentIdentity(subject, issuer, tokenId);
    }
}
