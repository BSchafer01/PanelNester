namespace PanelNester.Domain.Models;

public sealed record MaterialSummary
{
    public int TotalSheets { get; init; }

    public int TotalPlaced { get; init; }

    public int TotalUnplaced { get; init; }

    public decimal OverallUtilization { get; init; }
}
