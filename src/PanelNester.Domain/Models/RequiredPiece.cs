namespace PanelNester.Domain.Models;

public sealed record RequiredPiece
{
    public string RequiredPieceId { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal Length { get; init; }

    public string ProfileNumber { get; init; } = string.Empty;

    public string? PartName { get; init; }

    public string? Finish { get; init; }

    public string? PartNumber { get; init; }

    public bool IsManual { get; init; } = true;

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } = Array.Empty<SourceReference>();
}
