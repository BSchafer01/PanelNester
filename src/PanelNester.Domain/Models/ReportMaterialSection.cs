namespace PanelNester.Domain.Models;

public sealed record ReportMaterialSection
{
    public string MaterialName { get; init; } = string.Empty;

    public string? MaterialId { get; init; }

    public decimal SheetLength { get; init; }

    public decimal SheetWidth { get; init; }

    public decimal? CostPerSheet { get; init; }

    public MaterialSummary Summary { get; init; } = new();

    public IReadOnlyList<ReportSheetDiagram> Sheets { get; init; } = Array.Empty<ReportSheetDiagram>();

    public IReadOnlyList<UnplacedItem> UnplacedItems { get; init; } = Array.Empty<UnplacedItem>();
}
