using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

public sealed class OversizedStockAssignmentService : IOversizedStockAssignmentService
{
    public Task<ProjectOperationResult> SetAsync(
        Project project,
        string optimizationGroupId,
        string? oversizedStockLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        if (project.ProjectKind != ProjectKind.StockLength)
        {
            return Task.FromResult(Failure("oversized-stock-project-kind-invalid", "Oversized Stock can be assigned only in a Stock-Length Project."));
        }

        var groups = project.State.OptimizationGroups.ToArray();
        var index = Array.FindIndex(groups, group =>
            string.Equals(group.OptimizationGroupId, optimizationGroupId, StringComparison.Ordinal));
        if (index < 0)
        {
            return Task.FromResult(Failure("optimization-group-not-found", "The Optimization Group was not found."));
        }

        var group = groups[index];
        var current = group.LastStockLengthOptimizationResult;
        if (group.ResultStatus != OptimizationResultStatus.Valid || current is null)
        {
            return Task.FromResult(Failure("oversized-stock-current-result-required", "Generate a current Cut Plan before assigning Oversized Stock."));
        }

        decimal? requestedLength = null;
        if (!string.IsNullOrWhiteSpace(oversizedStockLength))
        {
            if (!InchMeasurementParser.TryParse(oversizedStockLength, out var parsed) || parsed <= (group.StockLength ?? 0m))
            {
                return Task.FromResult(Failure("oversized-stock-length-invalid", "Oversized Stock Length must be greater than the regular Stock Length."));
            }
            requestedLength = parsed;
        }

        var eligibleCount = current.CutPlans.Sum(plan =>
            plan.UnplacedPieceInstances.Count(IsEligible) +
            plan.StockItems.Where(item => item.Kind == StockItemKind.Oversized).Sum(item => item.CutSequence.Count));
        if (requestedLength is not null && eligibleCount == 0)
        {
            return Task.FromResult(Failure("oversized-stock-pieces-required", "The Cut Plan has no overlong Piece Instances to assign."));
        }

        var assignedAny = false;
        var plans = current.CutPlans.Select(plan =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regularItems = plan.StockItems.Where(item => item.Kind != StockItemKind.Oversized).ToArray();
            var candidates = plan.UnplacedPieceInstances
                .Where(IsEligible)
                .Select(item => item.PieceInstance)
                .Concat(plan.StockItems
                    .Where(item => item.Kind == StockItemKind.Oversized)
                    .SelectMany(item => item.CutSequence))
                .OrderBy(FirstSourceOrder)
                .ThenBy(instance => instance.PieceInstanceId, StringComparer.Ordinal)
                .ToArray();
            var otherUnplaced = plan.UnplacedPieceInstances.Where(item => !IsEligible(item)).ToList();
            var oversizedItems = new List<StockItem>();
            var nextNumber = regularItems.Select(item => item.StockItemNumber).DefaultIfEmpty(0).Max() + 1;

            foreach (var instance in candidates)
            {
                if (requestedLength is { } length && instance.Length <= length)
                {
                    assignedAny = true;
                    oversizedItems.Add(new StockItem
                    {
                        StockItemId = $"{plan.CutPlanId}:oversized:{instance.PieceInstanceId}",
                        StockItemNumber = nextNumber++,
                        Kind = StockItemKind.Oversized,
                        StockLength = length,
                        PieceLength = instance.Length,
                        SawLoss = 0m,
                        Remainder = length - instance.Length,
                        UtilizationPercent = ToPercent(instance.Length, length),
                        CutSequence = [instance]
                    });
                }
                else
                {
                    otherUnplaced.Add(new UnplacedPieceInstance
                    {
                        PieceInstance = instance,
                        ReasonCode = "exceeds-stock-length",
                        ReasonDescription = "Piece Instance exceeds Stock Length."
                    });
                }
            }

            var stockItems = regularItems.Concat(oversizedItems).ToArray();
            var status = Classify(stockItems.Sum(item => item.CutSequence.Count), otherUnplaced.Count);
            return plan with
            {
                Status = status,
                StockItems = stockItems,
                UnplacedPieceInstances = otherUnplaced.ToArray()
            };
        }).ToArray();

        if (requestedLength is not null && !assignedAny)
        {
            return Task.FromResult(Failure("oversized-stock-length-too-short", "Oversized Stock Length must fit at least one overlong Piece Instance."));
        }

        var result = current with
        {
            OversizedStockLength = requestedLength,
            CutPlans = plans,
            Status = Classify(
                plans.Sum(plan => plan.StockItems.Sum(item => item.CutSequence.Count)),
                plans.Sum(plan => plan.UnplacedPieceInstances.Count))
        };
        groups[index] = group with { LastStockLengthOptimizationResult = result };
        return Task.FromResult(new ProjectOperationResult
        {
            Success = true,
            Project = project with { State = project.State with { OptimizationGroups = groups } }
        });
    }

    private static bool IsEligible(UnplacedPieceInstance item) =>
        string.Equals(item.ReasonCode, "exceeds-stock-length", StringComparison.Ordinal);

    private static int FirstSourceOrder(PieceInstance piece) =>
        piece.SourceReferences.OrderBy(reference => reference.WorksheetPosition)
            .ThenBy(reference => reference.PhysicalRow)
            .FirstOrDefault()?.PhysicalRow ?? int.MaxValue;

    private static CutPlanStatus Classify(int placed, int unplaced) =>
        unplaced == 0 ? CutPlanStatus.Complete : placed == 0 ? CutPlanStatus.Failed : CutPlanStatus.Partial;

    private static decimal ToPercent(decimal numerator, decimal denominator) =>
        denominator <= 0m ? 0m : decimal.Round(numerator / denominator * 100m, 2, MidpointRounding.AwayFromZero);

    private static ProjectOperationResult Failure(string code, string message) => new()
    {
        Success = false,
        Errors = [new ValidationError(code, message)]
    };
}
