namespace FarmBasics.Api.Accounts;

internal sealed record PostingRuleDefinition(
    string EventType,
    string DebitAccountCode,
    string CreditAccountCode,
    string? CommissionDebitAccountCode = null);

internal sealed record LedgerEventPayload(
    Guid FarmIdentityId,
    DateOnly EntryDate,
    string Description,
    decimal AmountNgn,
    decimal? CommissionNgn = null,
    Guid? CropCycleId = null,
    Guid? ProductionUnitId = null,
    Guid? FarmOrganizationId = null,
    Guid? RecordedByStaffIdentityId = null);

internal sealed record PlannedJournalLine(
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    Guid? CropCycleId,
    Guid? ProductionUnitId);

/// <summary>
/// Config-driven double-entry posting rules mapping domain events to balanced ledger lines with cost-centre dimensions (01A §6.3).
/// </summary>
internal static class PostingRuleCatalog
{
    private static readonly Dictionary<string, PostingRuleDefinition> Rules = new(StringComparer.Ordinal)
    {
        ["operation.seed_cost"] = new("operation.seed_cost", "5000", "1000"),
        ["operation.fertilizer_cost"] = new("operation.fertilizer_cost", "5100", "1000"),
        ["operation.chemical_cost"] = new("operation.chemical_cost", "5200", "1000"),
        ["operation.labor_cost"] = new("operation.labor_cost", "5300", "1000"),
        ["soil.lab_fee"] = new("soil.lab_fee", "5400", "1000"),
        ["machinery.hire_cost"] = new("machinery.hire_cost", "5500", "1000"),
        ["marketplace.escrow_released"] = new("marketplace.escrow_released", "1000", "4000", CommissionDebitAccountCode: "5600"),
        ["owner.capital_injected"] = new("owner.capital_injected", "1000", "3000"),
        ["asset.acquired"] = new("asset.acquired", "1500", "1000")
    };

    public static bool TryBuildJournalLines(
        string eventType,
        LedgerEventPayload payload,
        out List<PlannedJournalLine> lines,
        out string? error)
    {
        lines = [];
        error = null;

        if (!Rules.TryGetValue(eventType, out PostingRuleDefinition? rule))
        {
            error = $"UNKNOWN_POSTING_RULE:{eventType}";
            return false;
        }

        if (payload.AmountNgn <= 0m)
        {
            error = "NON_POSITIVE_AMOUNT";
            return false;
        }

        decimal commission = payload.CommissionNgn ?? 0m;
        if (commission < 0m || (rule.CommissionDebitAccountCode is not null && commission >= payload.AmountNgn))
        {
            error = "INVALID_COMMISSION_AMOUNT";
            return false;
        }

        lines.Add(new PlannedJournalLine(
            AccountCode: rule.DebitAccountCode,
            DebitAmount: payload.AmountNgn,
            CreditAmount: 0m,
            CropCycleId: payload.CropCycleId,
            ProductionUnitId: payload.ProductionUnitId));

        lines.Add(new PlannedJournalLine(
            AccountCode: rule.CreditAccountCode,
            DebitAmount: 0m,
            CreditAmount: payload.AmountNgn,
            CropCycleId: payload.CropCycleId,
            ProductionUnitId: payload.ProductionUnitId));

        if (rule.CommissionDebitAccountCode is not null && commission > 0m)
        {
            lines.Add(new PlannedJournalLine(
                AccountCode: rule.CommissionDebitAccountCode,
                DebitAmount: commission,
                CreditAmount: 0m,
                CropCycleId: payload.CropCycleId,
                ProductionUnitId: payload.ProductionUnitId));

            lines.Add(new PlannedJournalLine(
                AccountCode: "1000",
                DebitAmount: 0m,
                CreditAmount: commission,
                CropCycleId: payload.CropCycleId,
                ProductionUnitId: payload.ProductionUnitId));
        }

        return true;
    }
}
