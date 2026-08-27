namespace PanelNester.Domain.Models;

public sealed record OptimizationGroup
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public OptimizationGroupOrigin Origin { get; init; } = OptimizationGroupOrigin.Project;

    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public decimal? StockLength { get; init; }

    public IReadOnlyList<RequiredPiece> RequiredPieces { get; init; } = Array.Empty<RequiredPiece>();

    public IReadOnlyList<StockGroup> StockGroups { get; init; } = Array.Empty<StockGroup>();

    public StockLengthOptimizationResult? LastStockLengthOptimizationResult { get; init; }

    public ValidationError? LastStockLengthGenerationError { get; init; }

    public NestResponse? LastNestingResult { get; init; }

    public BatchNestResponse? LastBatchNestingResult { get; init; }

    public OptimizationResultStatus ResultStatus { get; init; } = OptimizationResultStatus.None;
}

public enum OptimizationGroupOrigin
{
    Project,
    ImportSource
}

public enum OptimizationResultStatus
{
    None,
    Valid,
    Stale
}
