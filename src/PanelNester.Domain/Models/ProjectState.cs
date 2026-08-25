namespace PanelNester.Domain.Models;

public sealed record ProjectState
{
    public string? SourceFilePath { get; init; }

    public IReadOnlyList<OptimizationGroup> OptimizationGroups { get; init; } = Array.Empty<OptimizationGroup>();

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public string? SelectedMaterialId { get; init; }

    public NestResponse? LastNestingResult { get; init; }

    public BatchNestResponse? LastBatchNestingResult { get; init; }

    public ExtrusionLayoutState ExtrusionLayout { get; init; } = new();
}
