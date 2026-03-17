namespace PanelNester.Domain.Models;

public sealed record ProjectSettings
{
    public decimal KerfWidth { get; init; }

    public ReportSettings ReportSettings { get; init; } = new();
}
