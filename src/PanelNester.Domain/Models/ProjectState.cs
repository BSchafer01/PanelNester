namespace PanelNester.Domain.Models;

public sealed record ProjectState
{
    public string? SourceFilePath { get; init; }

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public string? SelectedMaterialId { get; init; }

    public NestResponse? LastNestingResult { get; init; }

    public BatchNestResponse? LastBatchNestingResult { get; init; }
}
