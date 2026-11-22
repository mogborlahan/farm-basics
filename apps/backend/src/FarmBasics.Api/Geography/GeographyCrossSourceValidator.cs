using System.Globalization;
using System.Text.RegularExpressions;

namespace FarmBasics.Api.Geography;

public sealed record LgaSeedEntry(string Name, string Code);

public sealed record StateSeedEntry(string Name, string Code, IReadOnlyList<LgaSeedEntry> Lgas);

public sealed record GeographyDataset(IReadOnlyList<StateSeedEntry> States);

public enum GeographyMismatchKind
{
    MissingInSecondary = 1,
    MissingInPrimary = 2,
    StateNameMismatch = 3,
    LgaCodeMismatch = 4
}

public sealed record GeographyMismatch(
    GeographyMismatchKind Kind,
    string Scope,
    string? PrimaryValue,
    string? SecondaryValue);

public sealed record GeographyValidationReport(
    int PrimaryStateCount,
    int SecondaryStateCount,
    int PrimaryLgaCount,
    int SecondaryLgaCount,
    IReadOnlyList<GeographyMismatch> Mismatches)
{
    public bool IsValid => Mismatches.Count == 0;
}

/// <summary>
/// Cross-validates Nigerian State/LGA datasets between primary (GRID3) and secondary (OCHA COD-AB)
/// sources per Doc/06-DATA §2.3 & §2.5, flagging discrepancies rather than silently overwriting.
/// </summary>
public static partial class GeographyCrossSourceValidator
{
    private static readonly Dictionary<string, string> CanonicalStateAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fct"] = "Federal Capital Territory",
        ["fct abuja"] = "Federal Capital Territory",
        ["abuja fct"] = "Federal Capital Territory",
        ["federal capital territory abuja"] = "Federal Capital Territory",
        ["nassarawa"] = "Nasarawa",
        ["plateu"] = "Plateau"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();

    public static string NormalizeStateName(string rawName)
    {
        string normalized = CollapseWhitespace(rawName);
        if (CanonicalStateAliases.TryGetValue(normalized, out string? canonical))
        {
            return canonical;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    public static string NormalizePlaceName(string rawName)
    {
        string normalized = CollapseWhitespace(rawName);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    public static GeographyValidationReport Validate(GeographyDataset primary, GeographyDataset secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        List<GeographyMismatch> mismatches = [];

        Dictionary<string, StateSeedEntry> primaryByCode = primary.States
            .ToDictionary(s => s.Code.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        Dictionary<string, StateSeedEntry> secondaryByCode = secondary.States
            .ToDictionary(s => s.Code.Trim().ToUpperInvariant(), StringComparer.Ordinal);

        foreach (KeyValuePair<string, StateSeedEntry> kvp in primaryByCode)
        {
            if (!secondaryByCode.TryGetValue(kvp.Key, out StateSeedEntry? secState))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.MissingInSecondary,
                    $"state:{kvp.Key}",
                    kvp.Value.Name,
                    null));
                continue;
            }

            string normPrimaryName = NormalizeStateName(kvp.Value.Name);
            string normSecondaryName = NormalizeStateName(secState.Name);
            if (!string.Equals(normPrimaryName, normSecondaryName, StringComparison.Ordinal))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.StateNameMismatch,
                    $"state:{kvp.Key}",
                    normPrimaryName,
                    normSecondaryName));
            }

            CompareStateLgas(kvp.Key, kvp.Value.Lgas, secState.Lgas, mismatches);
        }

        foreach (KeyValuePair<string, StateSeedEntry> kvp in secondaryByCode)
        {
            if (!primaryByCode.ContainsKey(kvp.Key))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.MissingInPrimary,
                    $"state:{kvp.Key}",
                    null,
                    kvp.Value.Name));
            }
        }

        int primaryLgas = primary.States.Sum(s => s.Lgas.Count);
        int secondaryLgas = secondary.States.Sum(s => s.Lgas.Count);
        return new GeographyValidationReport(
            primary.States.Count,
            secondary.States.Count,
            primaryLgas,
            secondaryLgas,
            mismatches);
    }

    private static void CompareStateLgas(
        string stateCode,
        IReadOnlyList<LgaSeedEntry> primaryLgas,
        IReadOnlyList<LgaSeedEntry> secondaryLgas,
        List<GeographyMismatch> mismatches)
    {
        Dictionary<string, LgaSeedEntry> pByName = primaryLgas
            .ToDictionary(l => NormalizePlaceName(l.Name), StringComparer.Ordinal);
        Dictionary<string, LgaSeedEntry> sByName = secondaryLgas
            .ToDictionary(l => NormalizePlaceName(l.Name), StringComparer.Ordinal);

        foreach (KeyValuePair<string, LgaSeedEntry> kvp in pByName)
        {
            if (!sByName.TryGetValue(kvp.Key, out LgaSeedEntry? secLga))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.MissingInSecondary,
                    $"lga:{stateCode}:{kvp.Key}",
                    kvp.Value.Code,
                    null));
                continue;
            }

            string pCode = kvp.Value.Code.Trim().ToUpperInvariant();
            string sCode = secLga.Code.Trim().ToUpperInvariant();
            if (!string.Equals(pCode, sCode, StringComparison.Ordinal))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.LgaCodeMismatch,
                    $"lga:{stateCode}:{kvp.Key}",
                    pCode,
                    sCode));
            }
        }

        foreach (KeyValuePair<string, LgaSeedEntry> kvp in sByName)
        {
            if (!pByName.ContainsKey(kvp.Key))
            {
                mismatches.Add(new GeographyMismatch(
                    GeographyMismatchKind.MissingInPrimary,
                    $"lga:{stateCode}:{kvp.Key}",
                    null,
                    kvp.Value.Code));
            }
        }
    }

    private static string CollapseWhitespace(string raw)
        => MultiWhitespaceRegex().Replace(raw.Trim(), " ");
}
