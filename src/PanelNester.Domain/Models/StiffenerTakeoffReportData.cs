namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffReportData
{
    public string? CompanyLogoPath { get; init; }

    public ProjectMetadata ProjectMetadata { get; init; } = new();

    public ReportSettings ReportSettings { get; init; } = new();

    public StiffenerTakeoffSettings Settings { get; init; } = new();

    public StiffenerTakeoffSectionSummary OverallSummary { get; init; } = new();

    public IReadOnlyList<StiffenerTakeoffLengthSummary> OverallLengths { get; init; } = Array.Empty<StiffenerTakeoffLengthSummary>();

    public IReadOnlyList<StiffenerTakeoffMaterialSection> Materials { get; init; } = Array.Empty<StiffenerTakeoffMaterialSection>();

    public bool HasTakeoff { get; init; }
}
