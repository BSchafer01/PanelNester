using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Nesting;

public sealed class BatchNestingService : IBatchNestingService
{
    private readonly INestingService _nestingService;

    public BatchNestingService(INestingService nestingService)
    {
        _nestingService = nestingService ?? throw new ArgumentNullException(nameof(nestingService));
    }

    public async Task<BatchNestResponse> NestBatchAsync(
        BatchNestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parts = request.Parts ?? Array.Empty<PartRow>();
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

        var materials = request.Materials ?? Array.Empty<Material>();
        var materialsByName = BuildMaterialLookup(materials);
        var selectedMaterialName = ResolveSelectedMaterialName(materials, request.SelectedMaterialId);

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
                            KerfWidth = request.KerfWidth
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
