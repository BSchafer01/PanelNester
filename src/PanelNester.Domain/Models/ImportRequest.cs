namespace PanelNester.Domain.Models;

public sealed record ImportRequest
{
    public string FilePath { get; init; } = string.Empty;

    public ImportOptions Options { get; init; } = new();
}
