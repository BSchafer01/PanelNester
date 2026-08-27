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

    public StockLengthOptimizationResult RefreshRequiredPieceMetadata(
        IReadOnlyList<RequiredPiece> previousRequiredPieces,
        IReadOnlyList<RequiredPiece> requiredPieces)
    {
        ArgumentNullException.ThrowIfNull(previousRequiredPieces);
        ArgumentNullException.ThrowIfNull(requiredPieces);
        if (previousRequiredPieces.Count != requiredPieces.Count)
        {
            throw new ArgumentException("Previous and current Required Pieces must have matching correspondence.");
        }

        var piecesByPreviousId = previousRequiredPieces.Zip(requiredPieces).ToDictionary(
            pair => pair.First.RequiredPieceId,
            pair => pair.Second,
            StringComparer.Ordinal);
        PieceInstance Refresh(PieceInstance instance) =>
            !piecesByPreviousId.TryGetValue(instance.RequiredPieceId, out var piece)
                ? instance
                : instance with
                {
                    PieceInstanceId = $"{piece.RequiredPieceId}:instance-{instance.InstanceNumber}",
                    RequiredPieceId = piece.RequiredPieceId,
                    Length = piece.Length,
                    ProfileNumber = piece.ProfileNumber,
                    Finish = piece.Finish,
                    PartName = piece.PartName,
                    PartNumber = piece.PartNumber,
                    SourceReferences = piece.SourceReferences
                };

        return this with
        {
            CutPlans = CutPlans.Select(plan =>
            {
                var requiredPieceIds = plan.StockGroup.RequiredPieceIds
                    .Select(id => piecesByPreviousId.TryGetValue(id, out var piece) ? piece.RequiredPieceId : id)
                    .ToArray();
                var representative = plan.StockGroup.RequiredPieceIds
                    .Select(id => piecesByPreviousId.GetValueOrDefault(id))
                    .FirstOrDefault(piece => piece is not null);
                return plan with
                {
                    StockGroup = plan.StockGroup with
                    {
                        ProfileNumber = representative?.ProfileNumber ?? plan.StockGroup.ProfileNumber,
                        Finish = representative?.Finish ?? plan.StockGroup.Finish,
                        RequiredPieceIds = requiredPieceIds
                    },
                    StockItems = plan.StockItems
                        .Select(item => item with
                        {
                            CutSequence = item.CutSequence.Select(Refresh).ToArray()
                        })
                        .ToArray(),
                    UnplacedPieceInstances = plan.UnplacedPieceInstances
                        .Select(unplaced => unplaced with
                        {
                            PieceInstance = Refresh(unplaced.PieceInstance)
                        })
                        .ToArray()
                };
            }).ToArray()
        };
    }
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
