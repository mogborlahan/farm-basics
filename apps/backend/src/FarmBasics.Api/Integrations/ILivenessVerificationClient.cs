using System.Security.Cryptography;

namespace FarmBasics.Api.Integrations;

public sealed record LivenessVerificationResult(bool Matched, decimal ConfidenceScore, bool PhotoDiscarded);

/// <summary>
/// Compares live capture against NIN verification photo and immediately discards biometric bytes
/// so no raw photograph or biometric vector is ever persisted (Doc/05-ENG §3.3, S3.1.M3).
/// </summary>
public interface ILivenessVerificationClient
{
    Task<LivenessVerificationResult> VerifyAndDiscardAsync(
        byte[] livePhotoBytes,
        string ninVerificationRef,
        CancellationToken cancellationToken = default);
}

internal sealed class SandboxLivenessVerificationClient : ILivenessVerificationClient
{
    public Task<LivenessVerificationResult> VerifyAndDiscardAsync(
        byte[] livePhotoBytes,
        string ninVerificationRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(livePhotoBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(ninVerificationRef);

        try
        {
            if (livePhotoBytes.Length < 8)
            {
                return Task.FromResult(new LivenessVerificationResult(false, 0.2500m, PhotoDiscarded: true));
            }

            return Task.FromResult(new LivenessVerificationResult(true, 0.9600m, PhotoDiscarded: true));
        }
        finally
        {
            // Wipe raw liveness image buffer immediately after comparison completes (Doc 05 §3.3).
            CryptographicOperations.ZeroMemory(livePhotoBytes);
        }
    }
}
