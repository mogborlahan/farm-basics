using FarmBasics.Api.Accounts;
using Xunit;

namespace FarmBasics.UnitTests;

public sealed class Sprint8PostingRulesAndStatementsTests
{
    [Theory]
    [InlineData("operation.seed_cost", "5000", "1000")]
    [InlineData("operation.fertilizer_cost", "5100", "1000")]
    [InlineData("operation.chemical_cost", "5200", "1000")]
    [InlineData("operation.labor_cost", "5300", "1000")]
    [InlineData("soil.lab_fee", "5400", "1000")]
    [InlineData("machinery.hire_cost", "5500", "1000")]
    [InlineData("owner.capital_injected", "1000", "3000")]
    [InlineData("asset.acquired", "1500", "1000")]
    public void S82ConfiguredPostingRulesProduceBalancedLinesWithCostCentres(
        string eventType,
        string expectedDebitCode,
        string expectedCreditCode)
    {
        Guid cycleId = Guid.NewGuid();
        Guid unitId = Guid.NewGuid();
        LedgerEventPayload payload = new(
            FarmIdentityId: Guid.NewGuid(),
            EntryDate: new DateOnly(2026, 11, 22),
            Description: $"Test {eventType}",
            AmountNgn: 15000m,
            CropCycleId: cycleId,
            ProductionUnitId: unitId);

        bool built = PostingRuleCatalog.TryBuildJournalLines(eventType, payload, out List<PlannedJournalLine> lines, out string? error);

        Assert.True(built);
        Assert.Null(error);
        Assert.Equal(2, lines.Count);
        Assert.Equal(lines.Sum(l => l.DebitAmount), lines.Sum(l => l.CreditAmount));
        Assert.Contains(lines, l => l.AccountCode == expectedDebitCode && l.DebitAmount == 15000m);
        Assert.Contains(lines, l => l.AccountCode == expectedCreditCode && l.CreditAmount == 15000m);
        Assert.All(lines, l =>
        {
            Assert.Equal(cycleId, l.CropCycleId);
            Assert.Equal(unitId, l.ProductionUnitId);
        });
    }

    [Fact]
    public void S82EscrowReleaseWithCommissionBuildsFourBalancedLines()
    {
        LedgerEventPayload payload = new(
            FarmIdentityId: Guid.NewGuid(),
            EntryDate: new DateOnly(2026, 11, 22),
            Description: "Produce sale release",
            AmountNgn: 40000m,
            CommissionNgn: 2000m,
            CropCycleId: Guid.NewGuid());

        bool built = PostingRuleCatalog.TryBuildJournalLines("marketplace.escrow_released", payload, out List<PlannedJournalLine> lines, out string? error);

        Assert.True(built);
        Assert.Null(error);
        Assert.Equal(4, lines.Count);
        Assert.Equal(42000m, lines.Sum(l => l.DebitAmount));
        Assert.Equal(42000m, lines.Sum(l => l.CreditAmount));
        Assert.Contains(lines, l => l.AccountCode == "5600" && l.DebitAmount == 2000m);
    }

    [Fact]
    public void S82UnknownRuleAndNonPositiveAmountsAreRejected()
    {
        LedgerEventPayload zeroPayload = new(Guid.NewGuid(), new DateOnly(2026, 11, 22), "Zero", 0m);
        Assert.False(PostingRuleCatalog.TryBuildJournalLines("operation.seed_cost", zeroPayload, out _, out string? errZero));
        Assert.Equal("NON_POSITIVE_AMOUNT", errZero);

        LedgerEventPayload validPayload = new(Guid.NewGuid(), new DateOnly(2026, 11, 22), "Bad", 500m);
        Assert.False(PostingRuleCatalog.TryBuildJournalLines("unconfigured.event", validPayload, out _, out string? errUnknown));
        Assert.StartsWith("UNKNOWN_POSTING_RULE:", errUnknown, StringComparison.Ordinal);
    }
}
