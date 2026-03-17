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

        return Task.FromResult(
            new ReportData
            {
                Settings = ResolveReportSettings(project),
                ProjectMetadata = project.Metadata ?? new ProjectMetadata(),
                Materials = materialSections,
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
