namespace PanelNester.Domain.Models;

public sealed record StockGroup
{
    public string ProfileNumber { get; init; } = string.Empty;

    public string? Finish { get; init; }

    public IReadOnlyList<string> RequiredPieceIds { get; init; } = Array.Empty<string>();
}
