using System.Text.RegularExpressions;

namespace FarmBasics.BuildingBlocks.Logging;

/// <summary>
/// Canonical sensitive-field denylist and pattern scrubber required by
/// Document 04 §7.3, Document 08 §3, Document 01A §10.3, and AGENTS.md Hard Rule 1.
/// </summary>
public static partial class SensitiveFieldDenylist
{
    public const string RedactedMarker = "[REDACTED]";

    private static readonly HashSet<string> ExactFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nin",
        "rawnin",
        "raw_nin",
        "nationalid",
        "national_id",
        "nationalidentitynumber",
        "national_identity_number",
        "otp",
        "voiceotp",
        "voice_otp",
        "pin",
        "splitpin",
        "split_pin",
        "password",
        "passwordhash",
        "password_hash",
        "passwd",
        "secret",
        "clientsecret",
        "client_secret",
        "webhooksecret",
        "webhook_secret",
        "hmacsecret",
        "hmac_secret",
        "apikey",
        "api_key",
        "token",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "idtoken",
        "id_token",
        "authorization",
        "authheader",
        "bvn",
        "bankaccount",
        "bank_account",
        "accountnumber",
        "account_number",
        "iban",
        "cardnumber",
        "card_number",
        "cvv",
        "privatekey",
        "private_key",
        "connectionstring",
        "connection_string"
    };

    private static readonly string[] SensitiveSubstrings =
    [
        "password",
        "passwd",
        "secret",
        "rawnin",
        "raw_nin",
        "voiceotp",
        "voice_otp",
        "splitpin",
        "split_pin",
        "privatekey",
        "private_key",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "accountnumber",
        "account_number",
        "connectionstring",
        "connection_string"
    ];

    public static bool IsSensitivePropertyName(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        string trimmed = propertyName.Trim();
        if (ExactFieldNames.Contains(trimmed))
        {
            return true;
        }

        string normalized = trimmed.Replace("-", string.Empty, StringComparison.Ordinal)
                                   .Replace("_", string.Empty, StringComparison.Ordinal);

        if (ExactFieldNames.Contains(normalized))
        {
            return true;
        }

        foreach (string token in SensitiveSubstrings)
        {
            if (trimmed.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string ScrubSensitivePatterns(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        string scrubbed = BearerJwtRegex().Replace(input, $"Bearer {RedactedMarker}");
        scrubbed = KeyValueSecretRegex().Replace(scrubbed, m => $"{m.Groups["key"].Value}{m.Groups["sep"].Value}{RedactedMarker}");
        return scrubbed;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerJwtRegex();

    [GeneratedRegex(@"(?<key>\b(?:nin|raw_?nin|otp|voice_?otp|pin|split_?pin|password|password_?hash|passwd|secret|client_?secret|api_?key|access_?token|refresh_?token|bvn|iban|cvv|account_?number)\b)(?<sep>\s*[:=]\s*)[""']?[^\s""',;\}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretRegex();
}
