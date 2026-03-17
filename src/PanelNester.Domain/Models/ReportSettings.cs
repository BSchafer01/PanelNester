namespace PanelNester.Domain.Models;

public sealed record ReportSettings
{
    public string? CompanyName { get; init; }

    public string? ReportTitle { get; init; }

    public string? ProjectJobName { get; init; }

    public string? ProjectJobNumber { get; init; }

    public DateTime? ReportDate { get; init; }

    public string? Notes { get; init; }
}
