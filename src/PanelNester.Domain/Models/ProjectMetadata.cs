namespace PanelNester.Domain.Models;

public sealed record ProjectMetadata
{
    public string ProjectName { get; init; } = string.Empty;

    public string? ProjectNumber { get; init; }

    public string? CustomerName { get; init; }

    public string? Estimator { get; init; }

    public string? Drafter { get; init; }

    public string? Pm { get; init; }

    public DateTime? Date { get; init; }

    public string? Revision { get; init; }

    public string? Notes { get; init; }
}
