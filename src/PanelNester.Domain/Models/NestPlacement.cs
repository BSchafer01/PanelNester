namespace PanelNester.Domain.Models;

public sealed record NestPlacement
{
    public string PlacementId { get; init; } = string.Empty;

    public string SheetId { get; init; } = string.Empty;

    public string PartId { get; init; } = string.Empty;

    public string? Group { get; init; }

    public decimal X { get; init; }

    public decimal Y { get; init; }

    public decimal Width { get; init; }

    public decimal Height { get; init; }

    public bool Rotated90 { get; init; }
}
