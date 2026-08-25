namespace PanelNester.Domain.Models;

public sealed record OptimizationGroupChange
{
    public OptimizationGroupChangeType Type { get; init; }

    public string? OptimizationGroupId { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<string> OrderedOptimizationGroupIds { get; init; } = Array.Empty<string>();

    public string? PartRowId { get; init; }

    public string? TargetOptimizationGroupId { get; init; }

    public bool RemoveOwnedContent { get; init; }
}

public enum OptimizationGroupChangeType
{
    Create,
    Rename,
    Reorder,
    MovePart,
    Delete
}
