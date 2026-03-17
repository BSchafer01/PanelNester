namespace PanelNester.Domain.Models;

public sealed record ReportDataRequest
{
    public Project Project { get; init; } = new();

    public BatchNestResponse? BatchResult { get; init; }
}
