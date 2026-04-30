namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffLengthSummary
{
    public string Label { get; init; } = string.Empty;

    public decimal LengthInches { get; init; }

    public int PieceCount { get; init; }
}
