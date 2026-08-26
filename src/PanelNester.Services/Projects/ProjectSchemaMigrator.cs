using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

internal static class ProjectSchemaMigrator
{
    internal const int FirstSupportedVersion = 1;
    private const string LegacyImportSessionGroupIdPrefix = "import-";

    internal static Project MigrateToCurrent(Project project)
    {
        if (project.Version == Project.CurrentVersion && project.State.OptimizationGroups.Count > 0)
        {
            return project;
        }

        var state = project.State ?? new ProjectState();
        var normalizedState = NormalizeOptimizationGroups(state, project.ProjectId);
        if (project.Version < 4)
        {
            normalizedState = MigrateOptimizationGroupOrigins(normalizedState);
        }

        return project with
        {
            Version = Project.CurrentVersion,
            State = normalizedState
        };
    }

    private static ProjectState MigrateOptimizationGroupOrigins(ProjectState state)
    {
        var configuredGroupIds = (state.ImportConfiguration?.Worksheets ?? [])
            .Select(worksheet => worksheet.OptimizationGroupId)
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        return state with
        {
            OptimizationGroups = state.OptimizationGroups
                .Select(group =>
                    IsLegacyImportSessionGroup(group, configuredGroupIds)
                        ? group with { Origin = OptimizationGroupOrigin.ImportSource }
                        : group)
                .ToArray()
        };
    }

    private static bool IsLegacyImportSessionGroup(
        OptimizationGroup group,
        IReadOnlySet<string> configuredGroupIds) =>
        configuredGroupIds.Contains(group.OptimizationGroupId) &&
        group.OptimizationGroupId.StartsWith(LegacyImportSessionGroupIdPrefix, StringComparison.Ordinal);

    internal static ProjectState NormalizeOptimizationGroups(ProjectState? state, string projectId)
    {
        state ??= new ProjectState();
        var sourceGroups = state.OptimizationGroups?.Where(group => group is not null).ToArray() ?? [];
        if (sourceGroups.Length == 0 && HasLegacyOptimizationGroupContent(state))
        {
            sourceGroups = [CreateLegacyGroup(projectId, state)];
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = sourceGroups
            .Select((group, index) => (Group: group, Index: index))
            .OrderBy(item => item.Group.Order)
            .ThenBy(item => item.Index)
            .Select((item, index) => NormalizeGroup(item.Group, projectId, index, usedIds, usedNames))
            .ToArray();

        if (string.IsNullOrWhiteSpace(state.SourceFilePath))
        {
            groups = groups
                .Select(group => group with
                {
                    Parts = group.Parts
                        .Select(part => part with { IsManual = true })
                        .ToArray()
                })
                .ToArray();
        }

        var compatibilityGroup = groups.Length == 1 ? groups[0] : null;
        return state with
        {
            OptimizationGroups = groups,
            Parts = groups.SelectMany(group => group.Parts).ToArray(),
            LastNestingResult = compatibilityGroup?.LastNestingResult ?? state.LastNestingResult,
            LastBatchNestingResult = compatibilityGroup?.LastBatchNestingResult ?? state.LastBatchNestingResult
        };
    }

    private static OptimizationGroup CreateLegacyGroup(string? projectId, ProjectState state) =>
        new()
        {
            OptimizationGroupId = string.IsNullOrWhiteSpace(projectId)
                ? "optimization-group-1"
                : projectId.Trim(),
            Name = CreateDefaultGroupName(state.SourceFilePath),
            Order = 0,
            Parts = state.Parts ?? Array.Empty<PartRow>(),
            LastNestingResult = state.LastNestingResult,
            LastBatchNestingResult = state.LastBatchNestingResult
        };

    private static bool HasLegacyOptimizationGroupContent(ProjectState state) =>
        (state.Parts?.Count ?? 0) > 0 ||
        state.LastNestingResult is not null ||
        state.LastBatchNestingResult is not null;

    private static OptimizationGroup NormalizeGroup(
        OptimizationGroup group,
        string projectId,
        int index,
        ISet<string> usedIds,
        ISet<string> usedNames)
    {
        var parts = (group.Parts ?? Array.Empty<PartRow>()).ToArray();
        var id = MakeUnique(
            NormalizeOptional(group.OptimizationGroupId) ?? $"{projectId}-group-{index + 1}",
            usedIds,
            separator: "-");
        var name = MakeUnique(
            NormalizeOptional(group.Name) ?? "Parts",
            usedNames,
            separator: " ");

        return group with
        {
            OptimizationGroupId = id,
            Name = name,
            Parts = parts,
            ResultStatus = GetResultStatus(parts, group.LastNestingResult, group.LastBatchNestingResult)
        };
    }

    private static string MakeUnique(string candidate, ISet<string> usedValues, string separator)
    {
        if (usedValues.Add(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var unique = separator == " "
                ? $"{candidate} ({suffix})"
                : $"{candidate}{separator}{suffix}";
            if (usedValues.Add(unique))
            {
                return unique;
            }
        }
    }

    private static OptimizationResultStatus GetResultStatus(
        IReadOnlyList<PartRow> parts,
        NestResponse? nestingResult,
        BatchNestResponse? batchResult)
    {
        if (nestingResult is null && batchResult is null)
        {
            return OptimizationResultStatus.None;
        }

        return AreResultsConsistent(parts, nestingResult, batchResult)
            ? OptimizationResultStatus.Valid
            : OptimizationResultStatus.Stale;
    }

    private static bool AreResultsConsistent(
        IReadOnlyList<PartRow> parts,
        NestResponse? nestingResult,
        BatchNestResponse? batchResult)
    {
        var optimizationGroupResults = batchResult?.OptimizationGroupResults ??
            Array.Empty<OptimizationGroupNestResult>();
        var resultParts = parts;
        if (optimizationGroupResults.Count > 0)
        {
            if (optimizationGroupResults.Count != 1)
            {
                return false;
            }

            var groupResult = optimizationGroupResults[0];
            var ownedPartRowIds = (groupResult.OwnedPartRowIds ?? Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            var currentPartRowIds = parts.Select(part => part.RowId).ToHashSet(StringComparer.Ordinal);
            if (!currentPartRowIds.SetEquals(ownedPartRowIds))
            {
                return false;
            }

            var inputPartRowIds = (groupResult.InputPartRowIds ?? Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            resultParts = parts.Where(part => inputPartRowIds.Contains(part.RowId)).ToArray();
            if (resultParts.Count != inputPartRowIds.Count)
            {
                return false;
            }

            if (!groupResult.Success &&
                groupResult.MaterialResults.Count == 0 &&
                batchResult!.MaterialResults.Count == 0 &&
                batchResult.LegacyResult is null)
            {
                return !string.IsNullOrWhiteSpace(groupResult.FailureMessage);
            }
        }

        if (nestingResult is not null && !IsNestResultConsistent(resultParts, nestingResult))
        {
            return false;
        }

        if (batchResult is null)
        {
            return true;
        }

        if (batchResult.LegacyResult is not null &&
            !IsNestResultConsistent(resultParts, batchResult.LegacyResult))
        {
            return false;
        }

        var materialNames = resultParts.Select(part => part.MaterialName).ToHashSet(StringComparer.Ordinal);
        if (batchResult.MaterialResults.Any(result =>
                !materialNames.Contains(result.MaterialName) ||
                !IsNestResultConsistent(resultParts, result.Result)))
        {
            return false;
        }

        var expectedPartIds = ExpandResultPartIds(resultParts).Keys.ToHashSet(StringComparer.Ordinal);
        var batchPartIds = batchResult.MaterialResults
            .SelectMany(result => GetReferencedPartIds(result.Result))
            .ToArray();
        if (batchPartIds.Distinct(StringComparer.Ordinal).Count() != batchPartIds.Length ||
            !expectedPartIds.SetEquals(batchPartIds))
        {
            return false;
        }

        return batchResult.LegacyResult is null ||
               batchResult.MaterialResults.Count == 0 ||
               batchResult.MaterialResults.Any(result => result.Result == batchResult.LegacyResult);
    }

    private static bool IsNestResultConsistent(IReadOnlyList<PartRow> parts, NestResponse result)
    {
        var sheets = result.Sheets ?? Array.Empty<NestSheet>();
        var placements = result.Placements ?? Array.Empty<NestPlacement>();
        var unplacedItems = result.UnplacedItems ?? Array.Empty<UnplacedItem>();
        var sheetIds = sheets.Select(sheet => sheet.SheetId).ToArray();
        if (sheetIds.Any(string.IsNullOrWhiteSpace) ||
            sheetIds.Distinct(StringComparer.Ordinal).Count() != sheetIds.Length ||
            result.Summary.TotalSheets != sheets.Count ||
            result.Summary.TotalPlaced != placements.Count ||
            result.Summary.TotalUnplaced != unplacedItems.Count)
        {
            return false;
        }

        var partsByResultId = ExpandResultPartIds(parts);
        var knownSheetIds = sheetIds.ToHashSet(StringComparer.Ordinal);
        var knownMaterials = parts.Select(part => part.MaterialName).ToHashSet(StringComparer.Ordinal);
        var referencedPartIds = GetReferencedPartIds(result).ToArray();
        if (referencedPartIds.Distinct(StringComparer.Ordinal).Count() != referencedPartIds.Length ||
            (parts.Count > 0 && referencedPartIds.Length == 0) ||
            referencedPartIds.Any(partId => !partsByResultId.ContainsKey(partId)))
        {
            return false;
        }

        var representedMaterials = sheets.Select(sheet => sheet.MaterialName)
            .Concat(referencedPartIds.Select(partId => partsByResultId[partId].MaterialName))
            .ToHashSet(StringComparer.Ordinal);
        var expectedRepresentedPartIds = partsByResultId
            .Where(item => representedMaterials.Contains(item.Value.MaterialName))
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedRepresentedPartIds.SetEquals(referencedPartIds))
        {
            return false;
        }

        return sheets.All(sheet => knownMaterials.Contains(sheet.MaterialName)) &&
               placements.All(placement =>
                   knownSheetIds.Contains(placement.SheetId) &&
                   partsByResultId.ContainsKey(placement.PartId)) &&
               unplacedItems.All(item =>
                   string.IsNullOrWhiteSpace(item.PartId) || partsByResultId.ContainsKey(item.PartId));
    }

    private static IEnumerable<string> GetReferencedPartIds(NestResponse result) =>
        result.Placements.Select(placement => placement.PartId)
            .Concat(result.UnplacedItems
                .Select(item => item.PartId)
                .Where(partId => !string.IsNullOrWhiteSpace(partId)));

    private static IReadOnlyDictionary<string, PartRow> ExpandResultPartIds(IEnumerable<PartRow> parts)
    {
        var result = new Dictionary<string, PartRow>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var quantity = part.Quantity > 0 ? part.Quantity : 1;
            var baseId = string.IsNullOrWhiteSpace(part.ImportedId) ? part.RowId : part.ImportedId;
            for (var instance = 1; instance <= quantity; instance++)
            {
                var resultId = quantity == 1 ? baseId : $"{baseId}#{instance}";
                result[resultId] = part;
            }
        }

        return result;
    }

    private static string CreateDefaultGroupName(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return "Parts";
        }

        var name = Path.GetFileNameWithoutExtension(sourceFilePath.Trim());
        return string.IsNullOrWhiteSpace(name) ? "Parts" : name;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
