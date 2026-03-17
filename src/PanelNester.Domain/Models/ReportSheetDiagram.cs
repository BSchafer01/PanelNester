namespace PanelNester.Domain.Models;

public sealed record ReportSheetDiagram
{
    public string SheetId { get; init; } = string.Empty;

    public int SheetNumber { get; init; }

    public decimal SheetLength { get; init; }

    public decimal SheetWidth { get; init; }

    public decimal UtilizationPercent { get; init; }

    public IReadOnlyList<NestPlacement> Placements { get; init; } = Array.Empty<NestPlacement>();
}
