using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FarmBasics.Api.Integrations;

public enum NinLookupStatus
{
    Matched = 1,
    FuzzyMatch = 2,
    NotFound = 3,
    ProviderUnavailable = 4,
    MalformedResponse = 5
}

public sealed record NinLookupResult(
    NinLookupStatus Status,
    string? VerificationRef,
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    decimal ConfidenceScore);

public interface INinVerificationClient
{
    Task<NinLookupResult> VerifyNinAsync(string rawNin, CancellationToken cancellationToken = default);
}

public sealed class NinIntegrationOptions
{
    public const string SectionName = "Integrations:Nin";
    public string Mode { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = "https://api.prembly.com/";
    public string EndpointPath { get; set; } = "identitypass/verification/vnin";
    public string ApiKey { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string HashPepper { get; set; } = "farmbasics-nonprod-nin-pepper-v1";
}

/// <summary>
/// Derives an irreversible SHA-256 verification reference token from a raw NIN + server pepper
/// so no raw NIN is ever persisted in identity.identity or written to logs (Doc/05-ENG §3.2, Rule 1).
/// </summary>
public static class NinVerificationHashHelper
{
    public static string ComputeVerificationRef(string rawNin, string pepper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawNin);
        ArgumentException.ThrowIfNullOrWhiteSpace(pepper);

        byte[] payload = Encoding.UTF8.GetBytes($"{pepper}:{rawNin.Trim()}");
        byte[] digest = SHA256.HashData(payload);
        return $"nin-v1-{Convert.ToHexStringLower(digest)}";
    }

    public static bool IsValidNinFormat(string? rawNin)
    {
        if (string.IsNullOrWhiteSpace(rawNin))
        {
            return false;
        }

        string trimmed = rawNin.Trim();
        return trimmed.Length == 11 && trimmed.All(char.IsAsciiDigit);
    }
}

/// <summary>
/// Typed HTTP client for Prembly NIN verification per Doc/05-ENG §3 &amp; Doc/01A-ENG §10.1.
/// </summary>
internal sealed partial class NinVerificationHttpClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<NinVerificationHttpClient> logger) : INinVerificationClient
{
    public async Task<NinLookupResult> VerifyNinAsync(string rawNin, CancellationToken cancellationToken = default)
    {
        if (!NinVerificationHashHelper.IsValidNinFormat(rawNin))
        {
            return new NinLookupResult(NinLookupStatus.NotFound, null, null, null, null, 0m);
        }

        NinIntegrationOptions options = new();
        configuration.GetSection(NinIntegrationOptions.SectionName).Bind(options);

        string normalizedNin = rawNin.Trim();
        string endpointPath = string.IsNullOrWhiteSpace(options.EndpointPath)
            ? "identitypass/verification/vnin"
            : options.EndpointPath.TrimStart('/');

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpointPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new { number = normalizedNin })
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(options.AppId))
        {
            request.Headers.TryAddWithoutValidation("app-id", options.AppId);
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new NinLookupResult(NinLookupStatus.NotFound, null, null, null, null, 0m);
            }

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                NinClientLog.ProviderHttpFailure(logger, (int)response.StatusCode);
                return new NinLookupResult(NinLookupStatus.ProviderUnavailable, null, null, null, null, 0m);
            }

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                NinClientLog.MalformedProviderPayload(logger, "EmptyBody");
                return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
            }

            return ParseProviderResponse(rawJson, normalizedNin, options.HashPepper);
        }
        catch (CircuitBrokenException)
        {
            NinClientLog.CircuitOpen(logger);
            return new NinLookupResult(NinLookupStatus.ProviderUnavailable, null, null, null, null, 0m);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            NinClientLog.ProviderTransportFailure(logger, ex.GetType().Name);
            return new NinLookupResult(NinLookupStatus.ProviderUnavailable, null, null, null, null, 0m);
        }
    }

    private NinLookupResult ParseProviderResponse(string rawJson, string normalizedNin, string pepper)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawJson);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out JsonElement statusProp))
            {
                NinClientLog.MalformedProviderPayload(logger, "MissingStatus");
                return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
            }

            string statusText = statusProp.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (statusText is "not_found" or "failed")
            {
                return new NinLookupResult(NinLookupStatus.NotFound, null, null, null, null, 0m);
            }

            if (!root.TryGetProperty("verification", out JsonElement verification)
                || verification.ValueKind != JsonValueKind.Object)
            {
                NinClientLog.MalformedProviderPayload(logger, "MissingVerificationObject");
                return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
            }

            string? firstName = verification.TryGetProperty("firstName", out JsonElement fn) ? fn.GetString()?.Trim() : null;
            string? lastName = verification.TryGetProperty("lastName", out JsonElement ln) ? ln.GetString()?.Trim() : null;
            string? dobRaw = verification.TryGetProperty("dateOfBirth", out JsonElement dobProp) ? dobProp.GetString()?.Trim() : null;

            if (string.IsNullOrWhiteSpace(firstName)
                || string.IsNullOrWhiteSpace(lastName)
                || !DateOnly.TryParse(dobRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dob))
            {
                NinClientLog.MalformedProviderPayload(logger, "MissingVerificationFields");
                return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
            }

            decimal confidence = 1.0m;
            if (verification.TryGetProperty("confidence", out JsonElement confProp)
                && confProp.ValueKind == JsonValueKind.Number
                && confProp.TryGetDecimal(out decimal parsedConfidence))
            {
                confidence = parsedConfidence;
            }

            string verificationRef = NinVerificationHashHelper.ComputeVerificationRef(normalizedNin, pepper);
            if (statusText == "fuzzy" || confidence < 0.85m)
            {
                return new NinLookupResult(NinLookupStatus.FuzzyMatch, verificationRef, firstName, lastName, dob, confidence);
            }

            if (statusText != "success")
            {
                NinClientLog.MalformedProviderPayload(logger, "UnknownStatusValue");
                return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
            }

            return new NinLookupResult(NinLookupStatus.Matched, verificationRef, firstName, lastName, dob, confidence);
        }
        catch (JsonException)
        {
            NinClientLog.MalformedProviderPayload(logger, "InvalidJson");
            return new NinLookupResult(NinLookupStatus.MalformedResponse, null, null, null, null, 0m);
        }
    }
}

internal static partial class NinClientLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "NIN provider returned HTTP {StatusCode}")]
    public static partial void ProviderHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "NIN provider circuit breaker is open")]
    public static partial void CircuitOpen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "NIN provider request failed with {ErrorType}")]
    public static partial void ProviderTransportFailure(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "NIN provider returned unexpected payload shape ({Reason})")]
    public static partial void MalformedProviderPayload(ILogger logger, string reason);
}

/// <summary>
/// Deterministic offline mock NIN provider per PRECEDENCE P13-P15 and Sprint 2 S2.1.M3.
/// Test suffixes: ...0000 => NotFound, ...9999 => FuzzyMatch, ...5553 => ProviderUnavailable.
/// </summary>
internal sealed class SandboxNinVerificationClient(IConfiguration configuration) : INinVerificationClient
{
    private static readonly string[] SyntheticFirstNames = ["Amina", "Chinedu", "Yusuf", "Folake", "Babatunde", "Zainab"];
    private static readonly string[] SyntheticLastNames = ["Bello", "Okafor", "Danladi", "Adeyemi", "Olawale", "Suleiman"];

    public Task<NinLookupResult> VerifyNinAsync(string rawNin, CancellationToken cancellationToken = default)
    {
        if (!NinVerificationHashHelper.IsValidNinFormat(rawNin))
        {
            return Task.FromResult(new NinLookupResult(NinLookupStatus.NotFound, null, null, null, null, 0m));
        }

        string trimmed = rawNin.Trim();
        if (trimmed.EndsWith("0000", StringComparison.Ordinal))
        {
            return Task.FromResult(new NinLookupResult(NinLookupStatus.NotFound, null, null, null, null, 0m));
        }

        if (trimmed.EndsWith("5553", StringComparison.Ordinal))
        {
            return Task.FromResult(new NinLookupResult(NinLookupStatus.ProviderUnavailable, null, null, null, null, 0m));
        }

        string pepper = configuration[$"{NinIntegrationOptions.SectionName}:HashPepper"]
                        ?? "farmbasics-nonprod-nin-pepper-v1";
        string verificationRef = NinVerificationHashHelper.ComputeVerificationRef(trimmed, pepper);

        if (trimmed.EndsWith("9999", StringComparison.Ordinal))
        {
            return Task.FromResult(new NinLookupResult(
                NinLookupStatus.FuzzyMatch,
                verificationRef,
                "Chinedu",
                "Okafor",
                new DateOnly(1988, 4, 19),
                0.62m));
        }

        int seed = int.Parse(trimmed[^4..], CultureInfo.InvariantCulture);
        string firstName = SyntheticFirstNames[seed % SyntheticFirstNames.Length];
        string lastName = SyntheticLastNames[(seed / SyntheticFirstNames.Length) % SyntheticLastNames.Length];
        DateOnly dob = new DateOnly(1975 + (seed % 25), (seed % 12) + 1, (seed % 28) + 1);

        return Task.FromResult(new NinLookupResult(
            NinLookupStatus.Matched,
            verificationRef,
            firstName,
            lastName,
            dob,
            0.99m));
    }
}
