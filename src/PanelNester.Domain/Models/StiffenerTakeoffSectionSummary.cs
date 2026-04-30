namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffSectionSummary
{
    public int EligiblePanelCount { get; init; }

    public int TotalStiffenerCount { get; init; }

    public decimal TotalLinearFeet { get; init; }

    public decimal StockLengthFeet { get; init; }

    public int RequiredStockCount { get; init; }
}
