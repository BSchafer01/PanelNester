namespace PanelNester.Domain.Models;

public sealed record RequiredPieceChange
{
    public RequiredPieceChangeType Type { get; init; }

    public string? OptimizationGroupId { get; init; }

    public string? RequiredPieceId { get; init; }

    public string Quantity { get; init; } = string.Empty;

    public string Length { get; init; } = string.Empty;

    public string ProfileNumber { get; init; } = string.Empty;

    public string? PartName { get; init; }

    public string? Finish { get; init; }

    public string? PartNumber { get; init; }
}

public enum RequiredPieceChangeType
{
    Create,
    Update,
    Delete
}
