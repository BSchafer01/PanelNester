using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Bridge;

internal static class ProjectImportFinalizer
{
    public static Project Finalize(
        Project project,
        ImportSourceMetadata importSource,
        ImportOptions importOptions,
        ImportResponse importResponse,
        string? targetOptimizationGroupId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(importSource.ImportSourcePath);
        ArgumentNullException.ThrowIfNull(importOptions);
        ArgumentNullException.ThrowIfNull(importResponse);
        var parts = importResponse.Parts;

        var nextPartsById = parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var assignedIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => SynchronizeExistingParts(group, nextPartsById, assignedIds))
            .ToArray();
        var unassignedParts = parts.Where(part => !assignedIds.Contains(part.RowId)).ToArray();

        if (groups.Length > 0 && unassignedParts.Length > 0)
        {
            var targetIndex = Array.FindIndex(
                groups,
                group => string.Equals(
                    group.OptimizationGroupId,
                    targetOptimizationGroupId,
                    StringComparison.Ordinal));
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            groups[targetIndex] = ClearResults(groups[targetIndex] with
            {
                Parts = groups[targetIndex].Parts.Concat(unassignedParts).ToArray()
            });
        }

        var compatibilityGroup = groups.FirstOrDefault();
        return project with
        {
            State = project.State with
            {
                SourceFilePath = importSource.ImportSourcePath,
                ImportSource = importSource,
                ImportConfiguration = BuildImportConfiguration(
                    importOptions,
                    importResponse,
                    groups.FirstOrDefault(group => string.Equals(
                        group.OptimizationGroupId,
                        targetOptimizationGroupId,
                        StringComparison.Ordinal))?.OptimizationGroupId ??
                    groups.FirstOrDefault()?.OptimizationGroupId),
                Parts = parts.ToArray(),
                OptimizationGroups = groups,
                LastNestingResult = compatibilityGroup?.LastNestingResult,
                LastBatchNestingResult = compatibilityGroup?.LastBatchNestingResult
            }
        };
    }

    private static ImportConfiguration BuildImportConfiguration(
        ImportOptions importOptions,
        ImportResponse importResponse,
        string? optimizationGroupId)
    {
        var resolvedColumnMappings = importResponse.ColumnMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
            .Select(mapping => new ImportColumnMapping
            {
                SourceColumn = mapping.SourceColumn!,
                TargetField = mapping.TargetField
            })
            .ToArray();
        var exactOptions = importOptions with { ColumnMappings = resolvedColumnMappings };
        var worksheet = importResponse.Worksheet;

        return new ImportConfiguration
        {
            Options = exactOptions,
            Worksheets = worksheet is null
                ? Array.Empty<ImportWorksheetConfiguration>()
                :
                [
                    new ImportWorksheetConfiguration
                    {
                        WorksheetName = worksheet.WorksheetName,
                        OriginalPosition = worksheet.OriginalPosition,
                        HeadingRange = worksheet.HeadingRange,
                        ColumnMappings = resolvedColumnMappings,
                        OptimizationGroupId = optimizationGroupId,
                        ExcludedSourceRows = Array.Empty<int>()
                    }
                ]
        };
    }

    private static OptimizationGroup SynchronizeExistingParts(
        OptimizationGroup group,
        IReadOnlyDictionary<string, PartRow> nextPartsById,
        ISet<string> assignedIds)
    {
        var parts = group.Parts
            .Select(part => nextPartsById.GetValueOrDefault(part.RowId))
            .Where(part => part is not null)
            .Cast<PartRow>()
            .ToArray();
        foreach (var part in parts)
        {
            assignedIds.Add(part.RowId);
        }

        var changed = parts.Length != group.Parts.Count ||
                      parts.Where((part, index) => !part.Equals(group.Parts[index])).Any();
        return changed ? ClearResults(group with { Parts = parts }) : group;
    }

    private static OptimizationGroup ClearResults(OptimizationGroup group) =>
        group with
        {
            LastNestingResult = null,
            LastBatchNestingResult = null,
            ResultStatus = OptimizationResultStatus.None
        };
}
