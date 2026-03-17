namespace PanelNester.Domain.Models;

public sealed record Material
{
    public string MaterialId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal SheetLength { get; init; }

    public decimal SheetWidth { get; init; }

    public bool AllowRotation { get; init; }

    public decimal DefaultSpacing { get; init; }

    public decimal DefaultEdgeMargin { get; init; }

    public string? ColorFinish { get; init; }

    public string? Notes { get; init; }

    public decimal? CostPerSheet { get; init; }
}
