namespace PanelNester.Domain.Models;

public enum CutPlanStatus
{
    Complete,
    Partial,
    Failed
}

public sealed record StockLengthCutPlanRequest
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public IReadOnlyList<RequiredPiece> RequiredPieces { get; init; } = Array.Empty<RequiredPiece>();

    public decimal StockLength { get; init; }

    public decimal SawKerf { get; init; }
}

public sealed record StockLengthOptimizationResult
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public CutPlanStatus Status { get; init; }

    public string Description { get; init; } = "Deterministic heuristic Cut Plan";

    public IReadOnlyList<CutPlan> CutPlans { get; init; } = Array.Empty<CutPlan>();
}

public sealed record StockLengthGenerationFailure
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed record StockLengthGenerationProgress
{
    public StockLengthGenerationProgressPhase Phase { get; init; }

    public int CompletedOptimizationGroups { get; init; }

    public int TotalOptimizationGroups { get; init; }

    public string? OptimizationGroupId { get; init; }

    public int CompletedStockGroups { get; init; }

    public int TotalStockGroups { get; init; }

    public long CompletedPieceInstanceSteps { get; init; }

    public long TotalPieceInstanceSteps { get; init; }

    public string Label { get; init; } = string.Empty;
}

public enum StockLengthGenerationProgressPhase
{
    OptimizationGroups,
    StockGroups,
    PieceInstances
}

public sealed record StockLengthProjectGenerationResult
{
    public bool Success => Failures.Count == 0;

    public Project Project { get; init; } = new();

    public IReadOnlyList<StockLengthGenerationFailure> Failures { get; init; } =
        Array.Empty<StockLengthGenerationFailure>();
}

public sealed record CutPlan
{
    public string CutPlanId { get; init; } = string.Empty;

    public StockGroup StockGroup { get; init; } = new();

    public CutPlanStatus Status { get; init; }

    public IReadOnlyList<StockItem> StockItems { get; init; } = Array.Empty<StockItem>();

    public IReadOnlyList<UnplacedPieceInstance> UnplacedPieceInstances { get; init; } =
        Array.Empty<UnplacedPieceInstance>();
}

public sealed record StockItem
{
    public string StockItemId { get; init; } = string.Empty;

    public int StockItemNumber { get; init; }

    public decimal StockLength { get; init; }

    public decimal PieceLength { get; init; }

    public decimal SawLoss { get; init; }

    public decimal Remainder { get; init; }

    public decimal UtilizationPercent { get; init; }

    public IReadOnlyList<PieceInstance> CutSequence { get; init; } = Array.Empty<PieceInstance>();
}

public sealed record PieceInstance
{
    public string PieceInstanceId { get; init; } = string.Empty;

    public string RequiredPieceId { get; init; } = string.Empty;

    public int InstanceNumber { get; init; }

    public decimal Length { get; init; }

    public string ProfileNumber { get; init; } = string.Empty;

    public string? Finish { get; init; }

    public string? PartNumber { get; init; }

    public string? PartName { get; init; }

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } = Array.Empty<SourceReference>();
}

public sealed record UnplacedPieceInstance
{
    public PieceInstance PieceInstance { get; init; } = new();

    public string ReasonCode { get; init; } = string.Empty;

    public string ReasonDescription { get; init; } = string.Empty;
}

public sealed class CutPlanGenerationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
