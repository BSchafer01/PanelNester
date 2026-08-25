namespace PanelNester.Domain.Models;

public sealed record ReportOptimizationGroupSection
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public bool Success { get; init; }

    public string? FailureMessage { get; init; }

    public IReadOnlyList<ReportMaterialSection> Materials { get; init; } =
        Array.Empty<ReportMaterialSection>();

    public IReadOnlyList<ReportMaterialSummaryGroup> PartGroups { get; init; } =
        Array.Empty<ReportMaterialSummaryGroup>();

    public IReadOnlyList<UnplacedItem> UnplacedItems { get; init; } =
        Array.Empty<UnplacedItem>();
}
