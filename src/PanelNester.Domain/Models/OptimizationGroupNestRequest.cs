namespace PanelNester.Domain.Models;

public sealed record OptimizationGroupNestRequest
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public IReadOnlyList<string> OwnedPartRowIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();
}
