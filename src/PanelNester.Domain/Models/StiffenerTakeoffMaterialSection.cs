namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffMaterialSection
{
    public string MaterialName { get; init; } = string.Empty;

    public StiffenerTakeoffSectionSummary Summary { get; init; } = new();

    public IReadOnlyList<StiffenerTakeoffLengthSummary> Lengths { get; init; } = Array.Empty<StiffenerTakeoffLengthSummary>();
}
