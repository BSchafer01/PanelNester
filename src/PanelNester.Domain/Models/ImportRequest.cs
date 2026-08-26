namespace PanelNester.Domain.Models;

public sealed record ImportRequest
{
    public string FilePath { get; init; } = string.Empty;

    public ImportOptions Options { get; init; } = new();

    public string? WorksheetName { get; init; }

    public string? HeadingRange { get; init; }
}
