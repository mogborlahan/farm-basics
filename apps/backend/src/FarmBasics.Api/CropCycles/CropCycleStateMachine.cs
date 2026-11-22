using System.Text.Json;

namespace FarmBasics.Api.CropCycles;

internal sealed record CropProfileValidationRanges(
    string CommonName,
    decimal? OptimalSoilPhMin,
    decimal? OptimalSoilPhMax,
    decimal? SeedDepthCmMin,
    decimal? SeedDepthCmMax,
    decimal? RowSpacingCmMin,
    decimal? RowSpacingCmMax,
    decimal? HarvestMoistureContentPct);

internal sealed record OperationValidationResult(
    bool IsInputValid,
    string Status,
    string? Message,
    string? ErrorCode = null);

/// <summary>
/// Forward-only CropCycle stage state machine implementing Doc/PRD-Volume-A §3.6 and Doc/12-QA §4.3.
/// </summary>
internal static class CropCycleStateMachine
{
    public static readonly string[] AllStages =
    [
        "planning",
        "pre_planting",
        "planting",
        "crop_care",
        "harvest",
        "post_harvest",
        "closed",
        "abandoned"
    ];

    private static readonly Dictionary<string, string> NextStageMap = new(StringComparer.Ordinal)
    {
        ["planning"] = "pre_planting",
        ["pre_planting"] = "planting",
        ["planting"] = "crop_care",
        ["crop_care"] = "harvest",
        ["harvest"] = "post_harvest",
        ["post_harvest"] = "closed"
    };

    private static readonly HashSet<string> ValidStageSet = new(AllStages, StringComparer.Ordinal);

    public static string? GetNextSequentialStage(string currentStage)
        => NextStageMap.TryGetValue(currentStage, out string? next) ? next : null;

    public static bool CanTransition(string fromStage, string toStage)
    {
        if (!ValidStageSet.Contains(fromStage) || !ValidStageSet.Contains(toStage))
        {
            return false;
        }

        if (fromStage is "closed" or "abandoned")
        {
            return false;
        }

        if (string.Equals(toStage, "abandoned", StringComparison.Ordinal))
        {
            return true;
        }

        return NextStageMap.TryGetValue(fromStage, out string? expectedNext)
            && string.Equals(expectedNext, toStage, StringComparison.Ordinal);
    }
}

/// <summary>
/// Validates OperationLog numeric measurements against CropProfile agronomic ranges (PRD Volume A §3.7).
/// Within range -> ok; within +-10% tolerance band -> warning; outside tolerance -> flag.
/// </summary>
internal static class OperationLogValidator
{
    public static OperationValidationResult Validate(
        string? validationField,
        CropProfileValidationRanges ranges,
        JsonElement? data)
    {
        if (string.IsNullOrWhiteSpace(validationField))
        {
            return new OperationValidationResult(true, "ok", "Task logged cleanly.");
        }

        if (!TryExtractDecimal(data, validationField, out decimal value))
        {
            return new OperationValidationResult(
                false,
                "invalid",
                $"Numeric field '{validationField}' is required for this task.",
                "VALIDATION_ERROR");
        }

        return validationField switch
        {
            "soilPh" => EvaluateRange(value, ranges.OptimalSoilPhMin, ranges.OptimalSoilPhMax, "pH", ranges.CommonName),
            "seedDepthCm" => EvaluateRange(value, ranges.SeedDepthCmMin, ranges.SeedDepthCmMax, "cm", ranges.CommonName),
            "rowSpacingCm" => EvaluateRange(value, ranges.RowSpacingCmMin, ranges.RowSpacingCmMax, "cm", ranges.CommonName),
            "harvestMoisturePct" => EvaluateRange(
                value,
                ranges.HarvestMoistureContentPct.HasValue ? Math.Round(ranges.HarvestMoistureContentPct.Value * 0.85m, 1) : null,
                ranges.HarvestMoistureContentPct,
                "%",
                ranges.CommonName),
            _ => new OperationValidationResult(true, "ok", "Task logged cleanly.")
        };
    }

    internal static OperationValidationResult EvaluateRange(
        decimal value,
        decimal? min,
        decimal? max,
        string unit,
        string cropName)
    {
        if (!min.HasValue || !max.HasValue)
        {
            return new OperationValidationResult(true, "ok", "Recorded measurement.");
        }

        if (value >= min.Value && value <= max.Value)
        {
            return new OperationValidationResult(
                true,
                "ok",
                $"Measurement {value}{unit} is within recommended range for {cropName}.");
        }

        decimal warningMin = Math.Round(min.Value * 0.90m, 2);
        decimal warningMax = Math.Round(max.Value * 1.10m, 2);
        if (value >= warningMin && value <= warningMax)
        {
            return new OperationValidationResult(
                true,
                "warning",
                $"Slightly outside the usual {cropName} range ({min.Value}–{max.Value}{unit}); recorded as advisory warning.");
        }

        return new OperationValidationResult(
            true,
            "flag",
            $"Attention needed: recorded value is outside the safe range for {cropName} and may affect yield or survival. Double-check or provide a reason to continue.");
    }

    private static bool TryExtractDecimal(JsonElement? data, string propertyName, out decimal value)
    {
        value = 0m;
        if (!data.HasValue || data.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (data.Value.TryGetProperty(propertyName, out JsonElement direct) && direct.ValueKind == JsonValueKind.Number)
        {
            return direct.TryGetDecimal(out value);
        }

        foreach (JsonProperty prop in data.Value.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.Number
                && prop.Value.TryGetDecimal(out value))
            {
                return true;
            }
        }

        return false;
    }
}
