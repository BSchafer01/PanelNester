using PanelNester.Domain.Models;

namespace PanelNester.Services.Reporting;

public sealed class StockLengthReportDataService
{
    public Task<StockLengthReportData> BuildAsync(
        StockLengthReportDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Project.ProjectKind != ProjectKind.StockLength)
        {
            throw new ArgumentException("Stock-Length report data requires a Stock-Length Project.", nameof(request));
        }

        var groups = request.Project.State.OptimizationGroups
            .Where(group => string.IsNullOrWhiteSpace(request.Scope.OptimizationGroupId) ||
                string.Equals(group.OptimizationGroupId, request.Scope.OptimizationGroupId, StringComparison.Ordinal))
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildGroup(group, request.Scope))
            .ToArray();

        var unplaced = groups
            .SelectMany(group => group.StockGroupProjections.SelectMany(stockGroup => stockGroup.UnplacedPieceInstances.Select(item =>
                new StockLengthReportUnplacedPieceInstance
                {
                    OptimizationGroupId = group.ReportOptimizationGroup.OptimizationGroupId,
                    OptimizationGroupName = group.ReportOptimizationGroup.Name,
                    OptimizationGroupOrder = group.ReportOptimizationGroup.Order,
                    ProfileNumber = stockGroup.ReportStockGroup.ProfileNumber,
                    Finish = stockGroup.ReportStockGroup.Finish,
                    State = stockGroup.ReportStockGroup.State,
                    PieceInstance = ToReportPiece(item.PieceInstance, sequence: 0),
                    ReasonCode = item.ReasonCode,
                    ReasonDescription = item.ReasonDescription
                })))
            .OrderBy(item => item.OptimizationGroupOrder)
            .ThenBy(item => item.ProfileNumber, NaturalLabelComparer.Instance)
            .ThenBy(item => item.Finish ?? string.Empty, NaturalLabelComparer.Instance)
            .ThenBy(item => item.PieceInstance.SourceReferences.FirstOrDefault()?.WorksheetPosition ?? int.MaxValue)
            .ThenBy(item => item.PieceInstance.SourceReferences.FirstOrDefault()?.PhysicalRow ?? int.MaxValue)
            .ThenBy(item => item.PieceInstance.PieceInstanceId, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(new StockLengthReportData
        {
            Settings = request.Project.Settings.ReportSettings,
            ProjectMetadata = request.Project.Metadata,
            InchDisplayFormat = request.Project.Settings.InchDisplayFormat,
            Scope = request.Scope,
            Summary = Summarize(groups.Select(group => group.ReportOptimizationGroup.Summary)),
            OptimizationGroups = groups.Select(group => group.ReportOptimizationGroup).ToArray(),
            UnplacedPieceInstances = unplaced
        });
    }

    private static ReportGroupProjection BuildGroup(OptimizationGroup group, StockLengthReportScope scope)
    {
        var groupState = GetState(group);
        var stockGroups = group.ResultStatus == OptimizationResultStatus.Valid
            ? (group.LastStockLengthOptimizationResult?.CutPlans ?? Array.Empty<CutPlan>())
                .Where(plan => MatchesScope(plan.StockGroup, scope))
                .OrderBy(plan => plan.StockGroup.ProfileNumber, NaturalLabelComparer.Instance)
                .ThenBy(plan => plan.StockGroup.Finish ?? string.Empty, NaturalLabelComparer.Instance)
                .Select(BuildStockGroup)
                .ToArray()
            : Array.Empty<ReportStockGroupProjection>();

        var summary = stockGroups.Length > 0
            ? Summarize(stockGroups.Select(stockGroup => stockGroup.ReportStockGroup.Summary))
            : new StockLengthReportSummary
            {
                AcceptedPieceInstanceCount = group.RequiredPieces
                    .Where(piece => piece.ValidationStatus != ValidationStatuses.Error && piece.Quantity > 0)
                    .Sum(piece => piece.Quantity)
            };

        return new ReportGroupProjection(
            new StockLengthReportOptimizationGroup
            {
                OptimizationGroupId = group.OptimizationGroupId,
                Name = group.Name,
                Order = group.Order,
                State = groupState,
                FailureMessage = group.LastStockLengthGenerationError?.Message,
                Summary = summary,
                StockGroups = stockGroups.Select(stockGroup => stockGroup.ReportStockGroup).ToArray()
            },
            stockGroups);
    }

    private static ReportStockGroupProjection BuildStockGroup(CutPlan plan)
    {
        var stockItems = plan.StockItems
            .OrderBy(item => item.StockItemNumber)
            .ThenBy(item => item.StockItemId, StringComparer.Ordinal)
            .Select(item => new StockLengthReportStockItem
            {
                StockItemNumber = item.StockItemNumber,
                Kind = item.Kind,
                StockLength = item.StockLength,
                PieceLength = item.PieceLength,
                SawLoss = item.SawLoss,
                Remainder = item.Remainder,
                UtilizationPercent = item.UtilizationPercent,
                CutSequence = item.CutSequence
                    .Select((piece, index) => ToReportPiece(piece, index + 1))
                    .ToArray()
            })
            .ToArray();
        var unplaced = plan.UnplacedPieceInstances
            .OrderBy(item => FirstSourceOrder(item.PieceInstance))
            .ThenBy(item => FirstSourceLabel(item.PieceInstance), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PieceInstance.PieceInstanceId, StringComparer.Ordinal)
            .ToArray();
        var stockLength = stockItems.Sum(item => item.StockLength);

        return new ReportStockGroupProjection(
            new StockLengthReportStockGroup
            {
                ProfileNumber = plan.StockGroup.ProfileNumber,
                Finish = plan.StockGroup.Finish,
                State = ToReportState(plan.Status),
                Summary = new StockLengthReportSummary
                {
                    AcceptedPieceInstanceCount = stockItems.Sum(item => item.CutSequence.Count) + unplaced.Length,
                    PlacedPieceInstanceCount = stockItems.Sum(item => item.CutSequence.Count),
                    UnplacedPieceInstanceCount = unplaced.Length,
                    StockLength = stockLength,
                    PieceLength = stockItems.Sum(item => item.PieceLength),
                    SawLoss = stockItems.Sum(item => item.SawLoss),
                    Remainder = stockItems.Sum(item => item.Remainder),
                    UtilizationPercent = stockLength == 0m
                        ? 0m
                        : stockItems.Sum(item => item.PieceLength) / stockLength * 100m
                },
                StockItems = stockItems
            },
            unplaced);
    }

    private static bool MatchesScope(StockGroup stockGroup, StockLengthReportScope scope) =>
        !scope.HasStockGroupFilter ||
        (string.Equals(stockGroup.ProfileNumber.Trim(), scope.StockGroupProfileNumber?.Trim(), StringComparison.OrdinalIgnoreCase) &&
         string.Equals((stockGroup.Finish ?? string.Empty).Trim(), (scope.StockGroupFinish ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    private static StockLengthReportSummary Summarize(IEnumerable<StockLengthReportSummary> summaries)
    {
        var items = summaries.ToArray();
        var stockLength = items.Sum(item => item.StockLength);
        var pieceLength = items.Sum(item => item.PieceLength);
        return new StockLengthReportSummary
        {
            AcceptedPieceInstanceCount = items.Sum(item => item.AcceptedPieceInstanceCount),
            PlacedPieceInstanceCount = items.Sum(item => item.PlacedPieceInstanceCount),
            UnplacedPieceInstanceCount = items.Sum(item => item.UnplacedPieceInstanceCount),
            StockLength = stockLength,
            PieceLength = pieceLength,
            SawLoss = items.Sum(item => item.SawLoss),
            Remainder = items.Sum(item => item.Remainder),
            UtilizationPercent = stockLength == 0m ? 0m : pieceLength / stockLength * 100m
        };
    }

    private static StockLengthReportState GetState(OptimizationGroup group)
    {
        if (group.LastStockLengthGenerationError is not null)
        {
            return StockLengthReportState.ApplicationError;
        }

        if (group.ResultStatus == OptimizationResultStatus.Valid && group.LastStockLengthOptimizationResult is not null)
        {
            return ToReportState(group.LastStockLengthOptimizationResult.Status);
        }

        return group.RequiredPieces.Count == 0
            ? StockLengthReportState.Empty
            : StockLengthReportState.NeedsGeneration;
    }

    private static StockLengthReportState ToReportState(CutPlanStatus status) => status switch
    {
        CutPlanStatus.Complete => StockLengthReportState.Complete,
        CutPlanStatus.Partial => StockLengthReportState.Partial,
        _ => StockLengthReportState.Failed
    };

    private static StockLengthReportPieceInstance ToReportPiece(PieceInstance piece, int sequence) => new()
    {
        PieceInstanceId = piece.PieceInstanceId,
        RequiredPieceId = piece.RequiredPieceId,
        QuantityInstance = piece.InstanceNumber,
        Sequence = sequence,
        Length = piece.Length,
        ProfileNumber = piece.ProfileNumber,
        Finish = piece.Finish,
        PartNumber = piece.PartNumber,
        PartName = piece.PartName,
        SourceReferences = piece.SourceReferences
            .OrderBy(reference => reference.WorksheetPosition)
            .ThenBy(reference => reference.PhysicalRow)
            .ToArray()
    };

    private static int FirstSourceOrder(PieceInstance piece) =>
        piece.SourceReferences.OrderBy(reference => reference.WorksheetPosition).FirstOrDefault()?.WorksheetPosition ?? int.MaxValue;

    private static string FirstSourceLabel(PieceInstance piece) =>
        piece.SourceReferences
            .OrderBy(reference => reference.WorksheetPosition)
            .ThenBy(reference => reference.PhysicalRow)
            .Select(reference => $"{reference.WorksheetName}!{reference.PhysicalRow:D10}")
            .FirstOrDefault() ?? string.Empty;

    private sealed record ReportGroupProjection(
        StockLengthReportOptimizationGroup ReportOptimizationGroup,
        IReadOnlyList<ReportStockGroupProjection> StockGroupProjections);

    private sealed record ReportStockGroupProjection(
        StockLengthReportStockGroup ReportStockGroup,
        IReadOnlyList<UnplacedPieceInstance> UnplacedPieceInstances);

    private sealed class NaturalLabelComparer : IComparer<string>
    {
        public static NaturalLabelComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftEnd = DigitRunEnd(left, leftIndex);
                    var rightEnd = DigitRunEnd(right, rightIndex);
                    var leftSignificant = SkipLeadingZeroes(left, leftIndex, leftEnd);
                    var rightSignificant = SkipLeadingZeroes(right, rightIndex, rightEnd);
                    var lengthComparison = (leftEnd - leftSignificant).CompareTo(rightEnd - rightSignificant);
                    if (lengthComparison != 0)
                    {
                        return lengthComparison;
                    }

                    var numberComparison = string.Compare(
                        left,
                        leftSignificant,
                        right,
                        rightSignificant,
                        leftEnd - leftSignificant,
                        StringComparison.Ordinal);
                    if (numberComparison != 0)
                    {
                        return numberComparison;
                    }

                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0)
                {
                    return characterComparison;
                }

                leftIndex++;
                rightIndex++;
            }

            var remainingComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
            return remainingComparison != 0
                ? remainingComparison
                : string.Compare(left, right, StringComparison.Ordinal);
        }

        private static int DigitRunEnd(string value, int start)
        {
            var index = start;
            while (index < value.Length && char.IsDigit(value[index]))
            {
                index++;
            }

            return index;
        }

        private static int SkipLeadingZeroes(string value, int start, int end)
        {
            var index = start;
            while (index < end - 1 && value[index] == '0')
            {
                index++;
            }

            return index;
        }
    }
}
