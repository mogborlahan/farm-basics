using FarmBasics.Api.Geography;
using Xunit;

namespace FarmBasics.UnitTests.Geography;

public sealed class GeographyCrossSourceValidatorTests
{
    [Fact]
    public void BaselineNigeriaDatasetHasAll37StatesAndValidatesWithNormalizedAliases()
    {
        GeographyDataset primary = GeographySeeder.LoadBaselineDataset();
        Assert.Equal(37, primary.States.Count);

        List<StateSeedEntry> secondaryStates = primary.States
            .Select(state => state.Code == "FC"
                ? state with { Name = "  fct   abuja  " }
                : state with { Name = state.Name.ToLowerInvariant() })
            .ToList();

        GeographyValidationReport report = GeographyCrossSourceValidator.Validate(
            primary,
            new GeographyDataset(secondaryStates));

        Assert.True(report.IsValid);
        Assert.Empty(report.Mismatches);
        Assert.Equal("Federal Capital Territory", GeographyCrossSourceValidator.NormalizeStateName("FCT Abuja"));
    }

    [Fact]
    public void CrossSourceValidatorFlagsMissingAndMismatchedEntries()
    {
        GeographyDataset primary = new([
            new StateSeedEntry("Lagos", "LA", [new LgaSeedEntry("Ikeja", "LA-IKE")]),
            new StateSeedEntry("Ogun", "OG", [new LgaSeedEntry("Abeokuta South", "OG-ABS")])
        ]);

        GeographyDataset secondary = new([
            new StateSeedEntry("Lagos", "LA", [new LgaSeedEntry("Ikeja", "LA-BAD")]),
            new StateSeedEntry("Oyo", "OY", [new LgaSeedEntry("Ibadan North", "OY-IBN")])
        ]);

        GeographyValidationReport report = GeographyCrossSourceValidator.Validate(primary, secondary);

        Assert.False(report.IsValid);
        Assert.Contains(report.Mismatches, m => m.Kind == GeographyMismatchKind.LgaCodeMismatch && m.Scope == "lga:LA:Ikeja");
        Assert.Contains(report.Mismatches, m => m.Kind == GeographyMismatchKind.MissingInSecondary && m.Scope == "state:OG");
        Assert.Contains(report.Mismatches, m => m.Kind == GeographyMismatchKind.MissingInPrimary && m.Scope == "state:OY");
    }
}
