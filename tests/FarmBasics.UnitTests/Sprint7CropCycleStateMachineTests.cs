using System.Text.Json;
using FarmBasics.Api.CropCycles;
using Xunit;

namespace FarmBasics.UnitTests;

public sealed class Sprint7CropCycleStateMachineTests
{
    private static readonly (string From, string To)[] ExpectedValidTransitions =
    [
        ("planning", "pre_planting"),
        ("planning", "abandoned"),
        ("pre_planting", "planting"),
        ("pre_planting", "abandoned"),
        ("planting", "crop_care"),
        ("planting", "abandoned"),
        ("crop_care", "harvest"),
        ("crop_care", "abandoned"),
        ("harvest", "post_harvest"),
        ("harvest", "abandoned"),
        ("post_harvest", "closed"),
        ("post_harvest", "abandoned")
    ];

    [Fact]
    public void S72FullStageTransitionMatrixPermitsOnlyForwardChainAndPreTerminalAbandon()
    {
        HashSet<(string, string)> validSet = [.. ExpectedValidTransitions];
        int checkedPairs = 0;

        foreach (string from in CropCycleStateMachine.AllStages)
        {
            foreach (string to in CropCycleStateMachine.AllStages)
            {
                bool expected = validSet.Contains((from, to));
                bool actual = CropCycleStateMachine.CanTransition(from, to);
                Assert.Equal(expected, actual);
                checkedPairs++;
            }
        }

        Assert.Equal(64, checkedPairs);
        Assert.False(CropCycleStateMachine.CanTransition("unknown_stage", "planning"));
        Assert.False(CropCycleStateMachine.CanTransition("planning", "unknown_stage"));
        Assert.Null(CropCycleStateMachine.GetNextSequentialStage("closed"));
        Assert.Null(CropCycleStateMachine.GetNextSequentialStage("abandoned"));
    }

    [Theory]
    [InlineData(6.2, "ok")]
    [InlineData(5.5, "ok")]
    [InlineData(7.0, "ok")]
    [InlineData(5.2, "warning")]
    [InlineData(7.5, "warning")]
    [InlineData(4.0, "flag")]
    [InlineData(9.2, "flag")]
    public void S73OperationLogValidatorEvaluatesOkWarningTenPercentAndFlag(decimal soilPh, string expectedStatus)
    {
        CropProfileValidationRanges maizeRanges = new(
            CommonName: "Maize",
            OptimalSoilPhMin: 5.5m,
            OptimalSoilPhMax: 7.0m,
            SeedDepthCmMin: 3.0m,
            SeedDepthCmMax: 5.0m,
            RowSpacingCmMin: 75.0m,
            RowSpacingCmMax: 90.0m,
            HarvestMoistureContentPct: 13.5m);

        using JsonDocument doc = JsonDocument.Parse($"{{\"soilPh\": {soilPh.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        OperationValidationResult result = OperationLogValidator.Validate("soilPh", maizeRanges, doc.RootElement);

        Assert.True(result.IsInputValid);
        Assert.Equal(expectedStatus, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
