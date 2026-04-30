using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Reporting;

public sealed class ReportDataService : IReportDataService
{
    public Task<ReportData> BuildReportDataAsync(
        ReportDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        cancellationToken.ThrowIfCancellationRequested();

        var project = request.Project;
        var batchResult = NormalizeBatchResult(request.BatchResult, project);
        var materialsByName = BuildMaterialLookup(project.MaterialSnapshots);
        var materialsById = BuildMaterialIdLookup(project.MaterialSnapshots);

        var materialSections = batchResult.MaterialResults
            .OrderBy(result => result.MaterialName, StringComparer.Ordinal)
            .Select(result =>
            {
                var material = ResolveMaterial(result, materialsByName, materialsById);
                var sheets = BuildSheetDiagrams(result.Result);

                return new ReportMaterialSection
                {
                    MaterialName = result.MaterialName,
                    MaterialId = material?.MaterialId ?? result.MaterialId,
                    SheetLength = material?.SheetLength ?? GetSheetLength(result.Result),
                    SheetWidth = material?.SheetWidth ?? GetSheetWidth(result.Result),
                    CostPerSheet = material?.CostPerSheet,
                    Summary = result.Result.Summary ?? new MaterialSummary(),
                    Sheets = sheets,
                    UnplacedItems = result.Result.UnplacedItems
                };
            })
            .ToArray();

        var allUnplaced = materialSections
            .SelectMany(section => section.UnplacedItems)
            .ToArray();
        var materialSummaryGroups = BuildMaterialSummaryGroups(project.State.Parts, materialSections);

        return Task.FromResult(
            new ReportData
            {
                Settings = ResolveReportSettings(project),
                ProjectMetadata = project.Metadata ?? new ProjectMetadata(),
                Materials = materialSections,
                MaterialSummaryGroups = materialSummaryGroups,
                UnplacedItems = allUnplaced,
                HasResults = materialSections.Any(HasRenderableLayouts)
            });
    }

    private static BatchNestResponse NormalizeBatchResult(BatchNestResponse? batchResult, Project project)
    {
        if (batchResult is not null && batchResult.MaterialResults.Count > 0)
        {
            return batchResult;
        }

        if (project.State.LastBatchNestingResult is { MaterialResults.Count: > 0 } storedBatch)
        {
            return storedBatch;
        }

        if (project.State.LastNestingResult is null)
        {
            return batchResult ?? new BatchNestResponse();
        }

        return CreateBatchFromSingle(project, project.State.LastNestingResult);
    }

    private static BatchNestResponse CreateBatchFromSingle(Project project, NestResponse singleResult)
    {
        var materialName = singleResult.Sheets.FirstOrDefault()?.MaterialName
            ?? project.MaterialSnapshots
                .FirstOrDefault(material =>
                    string.Equals(material.MaterialId, project.State.SelectedMaterialId, StringComparison.Ordinal))
                ?.Name
            ?? project.State.Parts.FirstOrDefault()?.MaterialName
            ?? string.Empty;

        var materialId = project.MaterialSnapshots
            .FirstOrDefault(material => string.Equals(material.Name, materialName, StringComparison.Ordinal))
            ?.MaterialId ?? project.State.SelectedMaterialId;

        return new BatchNestResponse
        {
            Success = singleResult.Success,
            LegacyResult = singleResult,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = materialName,
                    MaterialId = materialId,
                    Result = singleResult
                }
            ]
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

    private static Dictionary<string, Material> BuildMaterialIdLookup(IEnumerable<Material> materials) =>
        materials
            .Where(material => !string.IsNullOrWhiteSpace(material.MaterialId))
            .GroupBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(material => material.Name, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);

    private static Material? ResolveMaterial(
        MaterialNestResult result,
        IReadOnlyDictionary<string, Material> materialsByName,
        IReadOnlyDictionary<string, Material> materialsById)
    {
        if (!string.IsNullOrWhiteSpace(result.MaterialId) &&
            materialsById.TryGetValue(result.MaterialId, out var byId))
        {
            return byId;
        }

        return materialsByName.GetValueOrDefault(result.MaterialName);
    }

    private static IReadOnlyList<ReportSheetDiagram> BuildSheetDiagrams(NestResponse response)
    {
        var placementsBySheet = response.Placements
            .GroupBy(placement => placement.SheetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NestPlacement>)group
                    .OrderBy(placement => placement.X)
                    .ThenBy(placement => placement.Y)
                    .ThenBy(placement => placement.PartId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return response.Sheets
            .OrderBy(sheet => sheet.SheetNumber)
            .Select(sheet =>
                new ReportSheetDiagram
                {
                    SheetId = sheet.SheetId,
                    SheetNumber = sheet.SheetNumber,
                    SheetLength = sheet.SheetLength,
                    SheetWidth = sheet.SheetWidth,
                    UtilizationPercent = sheet.UtilizationPercent,
                    Placements = placementsBySheet.GetValueOrDefault(sheet.SheetId) ?? Array.Empty<NestPlacement>()
                })
            .ToArray();
    }

    private static bool HasRenderableLayouts(ReportMaterialSection section) =>
        section.Sheets.Any(sheet => sheet.Placements.Count > 0);

    private static IReadOnlyList<ReportMaterialSummaryGroup> BuildMaterialSummaryGroups(
        IReadOnlyList<PartRow> sourceRows,
        IReadOnlyList<ReportMaterialSection> materialSections)
    {
        var groupOrder = BuildSummaryGroupOrder(sourceRows);
        if (groupOrder.Count == 0)
        {
            return Array.Empty<ReportMaterialSummaryGroup>();
        }

        var sectionsByMaterial = materialSections
            .GroupBy(section => section.MaterialName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var summariesByGroup = groupOrder.ToDictionary(
            group => group,
            _ => new Dictionary<string, MaterialGroupSummaryAccumulator>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var row in sourceRows)
        {
            var groupKey = NormalizeGroupKey(row.Group);
            if (!summariesByGroup.TryGetValue(groupKey, out var materials))
            {
                continue;
            }

            var materialName = row.MaterialName ?? string.Empty;
            if (!materials.ContainsKey(materialName))
            {
                materials[materialName] = CreateAccumulator(materialName, sectionsByMaterial);
            }
        }

        var partOrigins = BuildPartOrigins(sourceRows);

        foreach (var section in materialSections)
        {
            foreach (var sheet in section.Sheets)
            {
                foreach (var placement in sheet.Placements)
                {
                    var preferredGroupKey = NormalizeGroupKey(placement.Group);
                    var origin = TakePartOrigin(partOrigins, section.MaterialName, placement.PartId, preferredGroupKey);
                    var groupKey = preferredGroupKey.Length > 0
                        ? preferredGroupKey
                        : origin?.GroupKey ?? string.Empty;

                    if (!summariesByGroup.TryGetValue(groupKey, out var materials))
                    {
                        continue;
                    }

                    if (!materials.TryGetValue(section.MaterialName, out var materialSummary))
                    {
                        materialSummary = CreateAccumulator(section.MaterialName, sectionsByMaterial);
                        materials[section.MaterialName] = materialSummary;
                    }

                    materialSummary.TotalPlaced++;
                    materialSummary.UsedArea += origin?.Area ?? (placement.Width * placement.Height);
                    materialSummary.SheetIds.Add(sheet.SheetId);
                }
            }

            foreach (var item in section.UnplacedItems)
            {
                var origin = TakePartOrigin(partOrigins, section.MaterialName, item.PartId, preferredGroupKey: null);
                var groupKey = origin?.GroupKey ?? string.Empty;

                if (!summariesByGroup.TryGetValue(groupKey, out var materials))
                {
                    continue;
                }

                if (!materials.TryGetValue(section.MaterialName, out var materialSummary))
                {
                    materialSummary = CreateAccumulator(section.MaterialName, sectionsByMaterial);
                    materials[section.MaterialName] = materialSummary;
                }

                materialSummary.TotalUnplaced++;
            }
        }

        return groupOrder
            .Select(groupKey =>
            {
                var materials = summariesByGroup[groupKey]
                    .Values
                    .OrderBy(summary => summary.MaterialName, StringComparer.Ordinal)
                    .Select(summary => summary.ToRow())
                    .ToArray();

                return new ReportMaterialSummaryGroup
                {
                    GroupName = groupKey,
                    Materials = materials
                };
            })
            .Where(group => group.Materials.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSummaryGroupOrder(IReadOnlyList<PartRow> sourceRows)
    {
        var namedGroupsInOrder = new List<string>();
        var seenNamedGroups = new HashSet<string>(StringComparer.Ordinal);
        var hasUngroupedRows = false;

        foreach (var row in sourceRows)
        {
            var groupKey = NormalizeGroupKey(row.Group);
            if (groupKey.Length == 0)
            {
                hasUngroupedRows = true;
                continue;
            }

            if (seenNamedGroups.Add(groupKey))
            {
                namedGroupsInOrder.Add(groupKey);
            }
        }

        if (namedGroupsInOrder.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (hasUngroupedRows)
        {
            namedGroupsInOrder.Add(string.Empty);
        }

        return namedGroupsInOrder;
    }

    private static Dictionary<string, List<PartOrigin>> BuildPartOrigins(IReadOnlyList<PartRow> sourceRows)
    {
        var partOrigins = new Dictionary<string, List<PartOrigin>>(StringComparer.Ordinal);

        foreach (var row in sourceRows)
        {
            var partCount = row.Quantity > 0 ? row.Quantity : 1;
            var basePartId = GetBasePartId(row);
            var groupKey = NormalizeGroupKey(row.Group);
            var area = row.Length * row.Width;

            for (var instanceNumber = 1; instanceNumber <= partCount; instanceNumber++)
            {
                var partId = partCount == 1 ? basePartId : $"{basePartId}#{instanceNumber}";
                var partKey = CreatePartKey(row.MaterialName, partId);
                if (!partOrigins.TryGetValue(partKey, out var origins))
                {
                    origins = [];
                    partOrigins[partKey] = origins;
                }

                origins.Add(new PartOrigin(groupKey, area));
            }
        }

        return partOrigins;
    }

    private static PartOrigin? TakePartOrigin(
        IDictionary<string, List<PartOrigin>> partOrigins,
        string materialName,
        string? partId,
        string? preferredGroupKey)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            return null;
        }

        if (!partOrigins.TryGetValue(CreatePartKey(materialName, partId), out var origins) || origins.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(preferredGroupKey))
        {
            var matchedIndex = origins.FindIndex(origin =>
                string.Equals(origin.GroupKey, preferredGroupKey, StringComparison.Ordinal));

            if (matchedIndex >= 0)
            {
                var matched = origins[matchedIndex];
                origins.RemoveAt(matchedIndex);
                return matched;
            }
        }

        var first = origins[0];
        origins.RemoveAt(0);
        return first;
    }

    private static MaterialGroupSummaryAccumulator CreateAccumulator(
        string materialName,
        IReadOnlyDictionary<string, ReportMaterialSection> sectionsByMaterial)
    {
        sectionsByMaterial.TryGetValue(materialName, out var section);

        return new MaterialGroupSummaryAccumulator(
            materialName,
            section?.MaterialId,
            section?.SheetLength ?? 0m,
            section?.SheetWidth ?? 0m);
    }

    private static string CreatePartKey(string materialName, string partId) =>
        $"{materialName}\u001f{partId}";

    private static string GetBasePartId(PartRow row) =>
        string.IsNullOrWhiteSpace(row.ImportedId) ? row.RowId : row.ImportedId;

    private static string NormalizeGroupKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static decimal ToPercent(decimal numerator, decimal denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return decimal.Round((numerator / denominator) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class MaterialGroupSummaryAccumulator(
        string materialName,
        string? materialId,
        decimal sheetLength,
        decimal sheetWidth)
    {
        public string MaterialName { get; } = materialName;

        public string? MaterialId { get; } = materialId;

        public decimal SheetLength { get; } = sheetLength;

        public decimal SheetWidth { get; } = sheetWidth;

        public int TotalPlaced { get; set; }

        public int TotalUnplaced { get; set; }

        public decimal UsedArea { get; set; }

        public HashSet<string> SheetIds { get; } = new(StringComparer.Ordinal);

        public ReportMaterialSummaryRow ToRow()
        {
            var totalSheets = SheetIds.Count;
            var sheetArea = SheetLength * SheetWidth;

            return new ReportMaterialSummaryRow
            {
                MaterialName = MaterialName,
                MaterialId = MaterialId,
                SheetLength = SheetLength,
                SheetWidth = SheetWidth,
                Summary = new MaterialSummary
                {
                    TotalSheets = totalSheets,
                    TotalPlaced = TotalPlaced,
                    TotalUnplaced = TotalUnplaced,
                    OverallUtilization = totalSheets == 0 ? 0m : ToPercent(UsedArea, sheetArea * totalSheets)
                }
            };
        }
    }

    private sealed record PartOrigin(string GroupKey, decimal Area);

    private static decimal GetSheetLength(NestResponse response) =>
        response.Sheets.FirstOrDefault()?.SheetLength ?? 0m;

    private static decimal GetSheetWidth(NestResponse response) =>
        response.Sheets.FirstOrDefault()?.SheetWidth ?? 0m;

    private static ReportSettings ResolveReportSettings(Project project)
    {
        var metadata = project.Metadata ?? new ProjectMetadata();
        var settings = project.Settings?.ReportSettings ?? new ReportSettings();

        return settings with
        {
            CompanyName = settings.CompanyName ?? metadata.CustomerName,
            ReportTitle = settings.ReportTitle ?? BuildDefaultReportTitle(metadata),
            ProjectJobName = settings.ProjectJobName ?? metadata.ProjectName,
            ProjectJobNumber = settings.ProjectJobNumber ?? metadata.ProjectNumber,
            ReleaseId = settings.ReleaseId,
            Status = settings.Status,
            ReportDate = settings.ReportDate ?? metadata.Date,
            Notes = settings.Notes ?? metadata.Notes
        };
    }

    private static string BuildDefaultReportTitle(ProjectMetadata metadata)
    {
        var projectName = string.IsNullOrWhiteSpace(metadata.ProjectName) ? null : metadata.ProjectName.Trim();
        return string.IsNullOrWhiteSpace(projectName)
            ? "Nesting Report"
            : $"{projectName} Nesting Report";
    }
}
