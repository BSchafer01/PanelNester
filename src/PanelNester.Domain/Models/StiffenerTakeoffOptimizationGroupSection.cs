namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffOptimizationGroupSection
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public StiffenerTakeoffSectionSummary Summary { get; init; } = new();

    public IReadOnlyList<StiffenerTakeoffLengthSummary> Lengths { get; init; } =
        Array.Empty<StiffenerTakeoffLengthSummary>();
}
