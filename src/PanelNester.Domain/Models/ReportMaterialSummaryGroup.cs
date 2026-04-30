namespace PanelNester.Domain.Models;

public sealed record ReportMaterialSummaryGroup
{
    public string GroupName { get; init; } = string.Empty;

    public IReadOnlyList<ReportMaterialSummaryRow> Materials { get; init; } = Array.Empty<ReportMaterialSummaryRow>();
}
