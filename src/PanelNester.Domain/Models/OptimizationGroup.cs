namespace PanelNester.Domain.Models;

public sealed record OptimizationGroup
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public NestResponse? LastNestingResult { get; init; }

    public BatchNestResponse? LastBatchNestingResult { get; init; }

    public string ResultStatus { get; init; } = OptimizationResultStatuses.None;
}

public static class OptimizationResultStatuses
{
    public const string None = "none";

    public const string Valid = "valid";

    public const string Stale = "stale";
}
