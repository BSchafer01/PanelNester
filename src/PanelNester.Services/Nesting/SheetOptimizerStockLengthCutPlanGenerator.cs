using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Nesting;

public sealed class SheetOptimizerStockLengthCutPlanGenerator(INestingService nestingService)
    : IStockLengthCutPlanGenerator
{
    private const decimal SyntheticWidth = 1m;
    private const decimal FitTolerance = 0.0001m;
    private readonly INestingService _nestingService =
        nestingService ?? throw new ArgumentNullException(nameof(nestingService));

    public async Task<StockLengthOptimizationResult> GenerateAsync(
        StockLengthCutPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var groups = BuildStockGroups(request.RequiredPieces);
        var cutPlans = new List<CutPlan>(groups.Count);

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cutPlans.Add(await GenerateStockGroupAsync(
                request,
                groups[groupIndex],
                groupIndex + 1,
                cancellationToken).ConfigureAwait(false));
        }

        var placed = cutPlans.Sum(plan => plan.StockItems.Sum(item => item.CutSequence.Count));
        var unplaced = cutPlans.Sum(plan => plan.UnplacedPieceInstances.Count);
        return new StockLengthOptimizationResult
        {
            OptimizationGroupId = request.OptimizationGroupId,
            Status = Classify(placed, unplaced),
            CutPlans = cutPlans
        };
    }

    private async Task<CutPlan> GenerateStockGroupAsync(
        StockLengthCutPlanRequest request,
        StockGroupInput group,
        int groupNumber,
        CancellationToken cancellationToken)
    {
        var syntheticKey = BuildSyntheticMaterialKey(group.ProfileNumber, group.Finish);
        var instancesByEnginePartId = BuildInstances(group.Pieces);
        var engineRequest = new NestRequest
        {
            Material = new Material
            {
                MaterialId = syntheticKey,
                Name = syntheticKey,
                SheetLength = request.StockLength,
                SheetWidth = SyntheticWidth,
                AllowRotation = false,
                DefaultEdgeMargin = 0m,
                DefaultSpacing = 0m
            },
            KerfWidth = request.SawKerf,
            Parts = group.Pieces.Select((piece, index) => new PartRow
            {
                RowId = BuildSyntheticPieceKey(piece.RequiredPieceId),
                ImportedId = BuildSyntheticPieceKey(piece.RequiredPieceId),
                Length = piece.Length,
                Width = SyntheticWidth,
                Quantity = piece.Quantity,
                MaterialName = syntheticKey,
                Group = null,
                ValidationStatus = ValidationStatuses.Valid
            }).ToArray()
        };
        var engineResult = await _nestingService.NestAsync(engineRequest, cancellationToken).ConfigureAwait(false);
        ValidateEngineResult(engineRequest, engineResult, instancesByEnginePartId);

        var cutPlanId = $"{request.OptimizationGroupId}:stock-group-{groupNumber}";
        var placementsBySheet = engineResult.Placements
            .GroupBy(placement => placement.SheetId, StringComparer.Ordinal)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.OrderBy(item => item.X).ToArray(), StringComparer.Ordinal);
        var stockItems = engineResult.Sheets
            .OrderBy(sheet => sheet.SheetNumber)
            .Select((sheet, index) =>
            {
                placementsBySheet.TryGetValue(sheet.SheetId, out var placements);
                placements ??= Array.Empty<NestPlacement>();
                var sequence = placements.Select(placement => instancesByEnginePartId[placement.PartId]).ToArray();
                var pieceLength = sequence.Sum(instance => instance.Length);
                var sawLoss = Math.Max(sequence.Length - 1, 0) * request.SawKerf;
                var remainder = request.StockLength - pieceLength - sawLoss;
                if (remainder < -FitTolerance)
                {
                    throw Invariant("Engine placements exceed the available Stock Length.");
                }

                return new StockItem
                {
                    StockItemId = $"{cutPlanId}:stock-item-{index + 1}",
                    StockItemNumber = index + 1,
                    StockLength = request.StockLength,
                    PieceLength = pieceLength,
                    SawLoss = sawLoss,
                    Remainder = Math.Abs(remainder) <= FitTolerance ? 0m : remainder,
                    UtilizationPercent = ToPercent(pieceLength, request.StockLength),
                    CutSequence = sequence
                };
            }).ToArray();
        var unplaced = engineResult.UnplacedItems.Select(item =>
        {
            var instance = instancesByEnginePartId[item.PartId];
            if (instance.Length <= request.StockLength + FitTolerance)
            {
                throw Invariant("Engine returned an Unplaced Piece Instance that fits unlimited Stock Items.");
            }

            return new UnplacedPieceInstance
            {
                PieceInstance = instance,
                ReasonCode = "exceeds-stock-length",
                ReasonDescription = "Piece Instance exceeds Stock Length."
            };
        }).ToArray();

        return new CutPlan
        {
            CutPlanId = cutPlanId,
            StockGroup = new StockGroup
            {
                ProfileNumber = group.ProfileNumber,
                Finish = group.Finish,
                RequiredPieceIds = group.Pieces.Select(piece => piece.RequiredPieceId).ToArray()
            },
            Status = Classify(stockItems.Sum(item => item.CutSequence.Count), unplaced.Length),
            StockItems = stockItems,
            UnplacedPieceInstances = unplaced
        };
    }

    private static void ValidateRequest(StockLengthCutPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OptimizationGroupId) || request.StockLength <= 0 || request.SawKerf < 0)
        {
            throw new CutPlanGenerationException("cut-plan-invalid-input", "Cut Plan inputs are invalid.");
        }

        if (request.RequiredPieces is null || request.RequiredPieces.Count == 0)
        {
            throw new CutPlanGenerationException("cut-plan-empty-group", "Empty Optimization Groups cannot generate a Cut Plan.");
        }

        if (request.RequiredPieces.Any(piece => piece.Quantity <= 0 || piece.Length <= 0 ||
                string.IsNullOrWhiteSpace(piece.RequiredPieceId) || string.IsNullOrWhiteSpace(piece.ProfileNumber)) ||
            request.RequiredPieces.Select(piece => piece.RequiredPieceId).Distinct(StringComparer.Ordinal).Count() != request.RequiredPieces.Count)
        {
            throw new CutPlanGenerationException("cut-plan-invalid-input", "Required Pieces are invalid.");
        }
    }

    private static IReadOnlyList<StockGroupInput> BuildStockGroups(IReadOnlyList<RequiredPiece> pieces) =>
        pieces
            .GroupBy(piece => (Normalize(piece.ProfileNumber).ToUpperInvariant(), Normalize(piece.Finish).ToUpperInvariant()))
            .Select(group => new StockGroupInput(
                Normalize(group.First().ProfileNumber),
                NormalizeOptional(group.First().Finish),
                group.ToArray()))
            .ToArray();

    private static Dictionary<string, PieceInstance> BuildInstances(IReadOnlyList<RequiredPiece> pieces)
    {
        var instances = new Dictionary<string, PieceInstance>(StringComparer.Ordinal);
        foreach (var piece in pieces)
        {
            var syntheticPieceKey = BuildSyntheticPieceKey(piece.RequiredPieceId);
            for (var instanceNumber = 1; instanceNumber <= piece.Quantity; instanceNumber++)
            {
                var enginePartId = piece.Quantity == 1 ? syntheticPieceKey : $"{syntheticPieceKey}#{instanceNumber}";
                instances.Add(enginePartId, new PieceInstance
                {
                    PieceInstanceId = $"{piece.RequiredPieceId}:instance-{instanceNumber}",
                    RequiredPieceId = piece.RequiredPieceId,
                    InstanceNumber = instanceNumber,
                    Length = piece.Length,
                    ProfileNumber = Normalize(piece.ProfileNumber),
                    Finish = NormalizeOptional(piece.Finish),
                    PartNumber = NormalizeOptional(piece.PartNumber),
                    PartName = NormalizeOptional(piece.PartName),
                    SourceReferences = piece.SourceReferences ?? Array.Empty<SourceReference>()
                });
            }
        }

        return instances;
    }

    private static void ValidateEngineResult(
        NestRequest request,
        NestResponse result,
        IReadOnlyDictionary<string, PieceInstance> instances)
    {
        var sheetIds = result.Sheets.Select(sheet => sheet.SheetId).ToHashSet(StringComparer.Ordinal);
        if (sheetIds.Count != result.Sheets.Count ||
            result.Sheets.Any(sheet => string.IsNullOrWhiteSpace(sheet.SheetId) ||
                sheet.SheetWidth != SyntheticWidth || sheet.SheetLength != request.Material.SheetLength) ||
            result.Placements.Any(placement => !IsValidPlacement(placement, request, sheetIds, instances)) ||
            result.Placements.GroupBy(placement => placement.SheetId, StringComparer.Ordinal)
                .Any(group => HasInvalidSpacing(group, request.KerfWidth)) ||
            result.UnplacedItems.Any(item => !instances.ContainsKey(item.PartId)) ||
            result.Placements.Select(item => item.PartId).Concat(result.UnplacedItems.Select(item => item.PartId))
                .GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            result.Placements.Count + result.UnplacedItems.Count != instances.Count)
        {
            throw Invariant("The sheet optimizer returned output that violates Stock-Length adapter invariants.");
        }
    }

    private static bool IsValidPlacement(
        NestPlacement placement,
        NestRequest request,
        IReadOnlySet<string> sheetIds,
        IReadOnlyDictionary<string, PieceInstance> instances)
    {
        if (placement.Rotated90 || placement.Height != SyntheticWidth ||
            !sheetIds.Contains(placement.SheetId) || !instances.TryGetValue(placement.PartId, out var instance))
        {
            return false;
        }

        return Math.Abs(placement.Width - instance.Length) <= FitTolerance &&
            Math.Abs(placement.Y) <= FitTolerance &&
            placement.X >= -FitTolerance &&
            placement.X + placement.Width <= request.Material.SheetLength + FitTolerance;
    }

    private static bool HasInvalidSpacing(IEnumerable<NestPlacement> placements, decimal sawKerf)
    {
        NestPlacement? previous = null;
        foreach (var placement in placements.OrderBy(item => item.X))
        {
            if (previous is not null &&
                placement.X - (previous.X + previous.Width) < sawKerf - FitTolerance)
            {
                return true;
            }

            previous = placement;
        }

        return false;
    }

    private static CutPlanStatus Classify(int placed, int unplaced) =>
        placed > 0 && unplaced == 0 ? CutPlanStatus.Complete :
        placed > 0 ? CutPlanStatus.Partial : CutPlanStatus.Failed;

    private static string BuildSyntheticMaterialKey(string profileNumber, string? finish)
    {
        var profile = Normalize(profileNumber);
        var normalizedFinish = Normalize(finish);
        return $"__stock__{profile.Length}:{profile}{normalizedFinish.Length}:{normalizedFinish}";
    }

    private static string BuildSyntheticPieceKey(string requiredPieceId) =>
        $"__piece__{requiredPieceId.Length}:{requiredPieceId}";

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static decimal ToPercent(decimal numerator, decimal denominator) =>
        denominator <= 0 ? 0m : decimal.Round(numerator / denominator * 100m, 2, MidpointRounding.AwayFromZero);

    private static CutPlanGenerationException Invariant(string message) =>
        new("cut-plan-adapter-invariant", message);

    private sealed record StockGroupInput(
        string ProfileNumber,
        string? Finish,
        IReadOnlyList<RequiredPiece> Pieces);
}
