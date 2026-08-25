using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Nesting;

public sealed class BatchNestingService : IBatchNestingService
{
    private readonly INestingService _nestingService;
    private readonly Func<string> _idGenerator;

    public BatchNestingService(INestingService nestingService, Func<string>? idGenerator = null)
    {
        _nestingService = nestingService ?? throw new ArgumentNullException(nameof(nestingService));
        _idGenerator = idGenerator ?? (() => Guid.NewGuid().ToString("N"));
    }

    public async Task<BatchNestResponse> NestBatchAsync(
        BatchNestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executionId = CreateExecutionId();
        var optimizationGroups = request.OptimizationGroups ?? Array.Empty<OptimizationGroupNestRequest>();
        if (optimizationGroups.Count > 0)
        {
            ValidateOptimizationGroupIds(optimizationGroups);
            return await NestOptimizationGroupsAsync(request, optimizationGroups, executionId, cancellationToken)
                .ConfigureAwait(false);
        }

        var legacyResponse = await NestMaterialsAsync(
                request.Parts ?? Array.Empty<PartRow>(),
                request.Materials ?? Array.Empty<Material>(),
                request.KerfWidth,
                request.SelectedMaterialId,
                cancellationToken)
            .ConfigureAwait(false);

        return legacyResponse with { ExecutionId = executionId };
    }

    private async Task<BatchNestResponse> NestOptimizationGroupsAsync(
        BatchNestRequest request,
        IReadOnlyList<OptimizationGroupNestRequest> optimizationGroups,
        string executionId,
        CancellationToken cancellationToken)
    {
        var groupResults = new List<OptimizationGroupNestResult>();

        foreach (var group in optimizationGroups.OrderBy(group => group.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupId = group.OptimizationGroupId;
            var resultId = $"{executionId}:{groupId}";
            var groupParts = group.Parts ?? Array.Empty<PartRow>();
            var ownedPartRowIds = group.OwnedPartRowIds?.Count > 0
                ? group.OwnedPartRowIds
                : groupParts.Select(part => part.RowId).ToArray();

            try
            {
                var batch = await NestMaterialsAsync(
                        groupParts,
                        request.Materials ?? Array.Empty<Material>(),
                        request.KerfWidth,
                        request.SelectedMaterialId,
                        cancellationToken)
                    .ConfigureAwait(false);
                var materialResults = batch.MaterialResults
                    .Select((result, index) => RewriteIdentities(resultId, index, result))
                    .ToArray();
                var legacyResult = ResolveLegacyResult(
                    materialResults,
                    ResolveSelectedMaterialName(request.Materials ?? Array.Empty<Material>(), request.SelectedMaterialId));
                var groupSucceeded =
                    materialResults.Length > 0 &&
                    materialResults.All(result => result.Result.Success);

                groupResults.Add(
                    new OptimizationGroupNestResult
                    {
                        OptimizationResultId = resultId,
                        OptimizationGroupId = groupId,
                        Name = group.Name,
                        Order = group.Order,
                        Success = groupSucceeded,
                        FailureMessage = groupSucceeded ? null : DescribeGroupFailure(batch),
                        InputPartRowIds = groupParts.Select(part => part.RowId).ToArray(),
                        OwnedPartRowIds = ownedPartRowIds,
                        LegacyResult = legacyResult,
                        MaterialResults = materialResults
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                groupResults.Add(
                    new OptimizationGroupNestResult
                    {
                        OptimizationResultId = resultId,
                        OptimizationGroupId = groupId,
                        Name = group.Name,
                        Order = group.Order,
                        Success = false,
                        FailureMessage = ex.Message,
                        InputPartRowIds = groupParts.Select(part => part.RowId).ToArray(),
                        OwnedPartRowIds = ownedPartRowIds
                    });
            }
        }

        var successfulGroupCount = groupResults.Count(result => result.Success);
        var primaryGroup = groupResults.FirstOrDefault();
        return new BatchNestResponse
        {
            ExecutionId = executionId,
            Success = groupResults.Count > 0 && successfulGroupCount == groupResults.Count,
            PartialSuccess = successfulGroupCount > 0 && successfulGroupCount < groupResults.Count,
            LegacyResult = primaryGroup?.LegacyResult,
            MaterialResults = primaryGroup?.MaterialResults ?? Array.Empty<MaterialNestResult>(),
            OptimizationGroupResults = groupResults
        };
    }

    private async Task<BatchNestResponse> NestMaterialsAsync(
        IReadOnlyList<PartRow> parts,
        IReadOnlyList<Material> materials,
        decimal kerfWidth,
        string? selectedMaterialId,
        CancellationToken cancellationToken)
    {
        if (parts.Count == 0)
        {
            var emptyResponse = CreateEmptyRunResponse();
            return new BatchNestResponse
            {
                Success = false,
                LegacyResult = emptyResponse,
                MaterialResults = Array.Empty<MaterialNestResult>()
            };
        }

        var materialsByName = BuildMaterialLookup(materials);
        var selectedMaterialName = ResolveSelectedMaterialName(materials, selectedMaterialId);

        var groupedParts = parts
            .GroupBy(part => part.MaterialName ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var materialResults = new List<MaterialNestResult>();

        foreach (var group in groupedParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (materialsByName.TryGetValue(group.Key, out var material))
            {
                var response = await _nestingService
                    .NestAsync(
                        new NestRequest
                        {
                            Parts = group.ToArray(),
                            Material = material,
                            KerfWidth = kerfWidth
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                materialResults.Add(
                    new MaterialNestResult
                    {
                        MaterialName = material.Name,
                        MaterialId = material.MaterialId,
                        Result = response
                    });
            }
            else
            {
                var unplacedItems = BuildMissingMaterialUnplacedItems(group);
                materialResults.Add(
                    new MaterialNestResult
                    {
                        MaterialName = group.Key,
                        Result = CreateFailureResponse(unplacedItems)
                    });
            }
        }

        var legacyResult = ResolveLegacyResult(materialResults, selectedMaterialName);
        return new BatchNestResponse
        {
            Success = materialResults.Any(result => result.Result.Success),
            LegacyResult = legacyResult,
            MaterialResults = materialResults
        };
    }

    private string CreateExecutionId()
    {
        var generated = _idGenerator()?.Trim();
        return string.IsNullOrWhiteSpace(generated) ? Guid.NewGuid().ToString("N") : generated;
    }

    private static void ValidateOptimizationGroupIds(
        IReadOnlyList<OptimizationGroupNestRequest> optimizationGroups)
    {
        var ids = optimizationGroups.Select(group => group.OptimizationGroupId).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException(
                "Every Optimization Group run requires a stable unique Optimization Group ID.",
                nameof(optimizationGroups));
        }
    }

    private static string DescribeGroupFailure(BatchNestResponse batch) =>
        batch.MaterialResults
            .Where(result => !result.Result.Success)
            .SelectMany(result => result.Result.UnplacedItems)
            .FirstOrDefault()
            ?.ReasonDescription ??
        batch.LegacyResult?.UnplacedItems.FirstOrDefault()?.ReasonDescription ??
        "The Optimization Group did not produce a successful layout.";

    private static MaterialNestResult RewriteIdentities(
        string optimizationResultId,
        int materialIndex,
        MaterialNestResult materialResult)
    {
        var identityPrefix = $"{optimizationResultId}:material-{materialIndex}";
        var sheetIds = materialResult.Result.Sheets.ToDictionary(
            sheet => sheet.SheetId,
            sheet => $"{identityPrefix}:{sheet.SheetId}",
            StringComparer.Ordinal);
        var rewrittenSheets = materialResult.Result.Sheets
            .Select(sheet => sheet with { SheetId = sheetIds[sheet.SheetId] })
            .ToArray();
        var rewrittenPlacements = materialResult.Result.Placements
            .Select(placement => placement with
            {
                PlacementId = $"{identityPrefix}:{placement.PlacementId}",
                SheetId = sheetIds.GetValueOrDefault(
                    placement.SheetId,
                    $"{identityPrefix}:{placement.SheetId}")
            })
            .ToArray();

        return materialResult with
        {
            Result = materialResult.Result with
            {
                Sheets = rewrittenSheets,
                Placements = rewrittenPlacements
            }
        };
    }

    private static Dictionary<string, Material> BuildMaterialLookup(IEnumerable<Material> materials) =>
        materials
            .Where(material => !string.IsNullOrWhiteSpace(material.Name))
            .GroupBy(material => material.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(material => material.MaterialId, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);

    private static string? ResolveSelectedMaterialName(
        IEnumerable<Material> materials,
        string? selectedMaterialId)
    {
        if (string.IsNullOrWhiteSpace(selectedMaterialId))
        {
            return null;
        }

        return materials
            .FirstOrDefault(material => string.Equals(material.MaterialId, selectedMaterialId, StringComparison.Ordinal))
            ?.Name;
    }

    private static NestResponse ResolveLegacyResult(
        IReadOnlyList<MaterialNestResult> results,
        string? selectedMaterialName)
    {
        if (!string.IsNullOrWhiteSpace(selectedMaterialName))
        {
            var selected = results.FirstOrDefault(result =>
                string.Equals(result.MaterialName, selectedMaterialName, StringComparison.Ordinal));
            if (selected is not null)
            {
                return selected.Result;
            }
        }

        if (results.Count == 1)
        {
            return results[0].Result;
        }

        return results.Count > 0 ? results[0].Result : CreateEmptyRunResponse();
    }

    private static IReadOnlyList<UnplacedItem> BuildMissingMaterialUnplacedItems(IEnumerable<PartRow> rows)
    {
        var unplacedItems = new List<UnplacedItem>();

        foreach (var row in rows)
        {
            AddRowUnplacedItems(
                row,
                NestingFailureCodes.InvalidInput,
                DescribeMissingMaterialRow(row),
                unplacedItems);
        }

        return unplacedItems;
    }

    private static string DescribeMissingMaterialRow(PartRow row)
    {
        if (IsRowError(row))
        {
            return DescribeRow(row);
        }

        return string.IsNullOrWhiteSpace(row.MaterialName)
            ? "Row is missing a material name."
            : $"Row material '{row.MaterialName}' does not match any configured material.";
    }

    private static bool IsRowError(PartRow row) =>
        string.Equals(row.ValidationStatus, ValidationStatuses.Error, StringComparison.OrdinalIgnoreCase);

    private static string DescribeRow(PartRow row)
    {
        if (row.ValidationMessages.Count == 0)
        {
            return "Part row failed validation before nesting.";
        }

        return string.Join("; ", row.ValidationMessages);
    }

    private static void AddRowUnplacedItems(
        PartRow row,
        string reasonCode,
        string reasonDescription,
        ICollection<UnplacedItem> unplacedItems)
    {
        var partCount = row.Quantity > 0 ? row.Quantity : 1;
        var basePartId = string.IsNullOrWhiteSpace(row.ImportedId) ? row.RowId : row.ImportedId;

        for (var instanceNumber = 1; instanceNumber <= partCount; instanceNumber++)
        {
            var partId = partCount == 1 ? basePartId : $"{basePartId}#{instanceNumber}";
            unplacedItems.Add(
                new UnplacedItem
                {
                    PartId = partId,
                    ReasonCode = reasonCode,
                    ReasonDescription = reasonDescription
                });
        }
    }

    private static NestResponse CreateEmptyRunResponse() =>
        CreateFailureResponse(
            [
                new UnplacedItem
                {
                    PartId = string.Empty,
                    ReasonCode = NestingFailureCodes.EmptyRun,
                    ReasonDescription = "No part rows were supplied for nesting."
                }
            ]);

    private static NestResponse CreateFailureResponse(IReadOnlyList<UnplacedItem> unplacedItems) =>
        new()
        {
            Success = false,
            UnplacedItems = unplacedItems,
            Summary = new MaterialSummary
            {
                TotalSheets = 0,
                TotalPlaced = 0,
                TotalUnplaced = unplacedItems.Count,
                OverallUtilization = 0m
            }
        };
}
