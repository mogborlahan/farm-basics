namespace FarmBasics.Api.Identity;

public sealed record RegistrationProvisioningRequest(
    string? NinVerificationRef,
    string PhonePrimary,
    string FirstName,
    string LastName,
    string? EmailPrimary,
    Guid? StateId,
    Guid? LgaId);

public sealed record RegistrationProvisioningResult(
    Guid IdentityId,
    string KeycloakSubject,
    bool Created);

/// <summary>
/// API-driven registration & Keycloak credential provisioning contract locked by ADR-008 (P1).
/// Concrete registration endpoints in Sprint 2 (WP-21) implement against this boundary.
/// </summary>
public interface IIdentityRegistrationProvider
{
    Task<RegistrationProvisioningResult> ProvisionUserAsync(
        RegistrationProvisioningRequest request,
        CancellationToken cancellationToken = default);
}
