namespace PanelNester.Domain.Models;

public sealed record ReportMaterialSummaryRow
{
    public string MaterialName { get; init; } = string.Empty;

    public string? MaterialId { get; init; }

    public decimal SheetLength { get; init; }

    public decimal SheetWidth { get; init; }

    public MaterialSummary Summary { get; init; } = new();
}
