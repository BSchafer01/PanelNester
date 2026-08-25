namespace PanelNester.Domain.Models;

public sealed record ReportData
{
    public string? CompanyLogoPath { get; init; }

    public ReportSettings Settings { get; init; } = new();

    public ProjectMetadata ProjectMetadata { get; init; } = new();

    public IReadOnlyList<ReportMaterialSection> Materials { get; init; } = Array.Empty<ReportMaterialSection>();

    public IReadOnlyList<ReportOptimizationGroupSection> OptimizationGroups { get; init; } =
        Array.Empty<ReportOptimizationGroupSection>();

    public IReadOnlyList<ReportMaterialSummaryGroup> MaterialSummaryGroups { get; init; } = Array.Empty<ReportMaterialSummaryGroup>();

    public IReadOnlyList<UnplacedItem> UnplacedItems { get; init; } = Array.Empty<UnplacedItem>();

    public bool HasResults { get; init; }
}
