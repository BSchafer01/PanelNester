using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

internal static class ProjectSchemaMigrator
{
    internal const int FirstSupportedVersion = 1;

    internal static Project MigrateToCurrent(Project project)
    {
        if (project.Version == Project.CurrentVersion && project.State.OptimizationGroups.Count > 0)
        {
            return project;
        }

        var state = project.State ?? new ProjectState();
        var groups = state.OptimizationGroups.Count > 0
            ? state.OptimizationGroups
            : [CreateLegacyGroup(project.ProjectId, state)];

        return project with
        {
            Version = Project.CurrentVersion,
            State = state with { OptimizationGroups = groups }
        };
    }

    private static OptimizationGroup CreateLegacyGroup(string? projectId, ProjectState state) =>
        new()
        {
            OptimizationGroupId = string.IsNullOrWhiteSpace(projectId)
                ? "optimization-group-1"
                : projectId,
            Name = CreateDefaultGroupName(state.SourceFilePath),
            Order = 0,
            Parts = state.Parts ?? Array.Empty<PartRow>(),
            LastNestingResult = state.LastNestingResult,
            LastBatchNestingResult = state.LastBatchNestingResult,
            ResultStatus = state.LastNestingResult is null && state.LastBatchNestingResult is null
                ? OptimizationResultStatuses.None
                : AreResultsConsistent(state.LastNestingResult, state.LastBatchNestingResult)
                    ? OptimizationResultStatuses.Valid
                    : OptimizationResultStatuses.Stale
        };

    private static string CreateDefaultGroupName(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return "Parts";
        }

        var name = Path.GetFileNameWithoutExtension(sourceFilePath.Trim());
        return string.IsNullOrWhiteSpace(name) ? "Parts" : name;
    }

    private static bool AreResultsConsistent(NestResponse? nestingResult, BatchNestResponse? batchResult)
    {
        if (nestingResult is not null && !IsNestResultConsistent(nestingResult))
        {
            return false;
        }

        if (batchResult?.LegacyResult is not null && !IsNestResultConsistent(batchResult.LegacyResult))
        {
            return false;
        }

        return batchResult?.MaterialResults.All(result => IsNestResultConsistent(result.Result)) ?? true;
    }

    private static bool IsNestResultConsistent(NestResponse result)
    {
        var sheetIds = result.Sheets
            .Select(sheet => sheet.SheetId)
            .Where(sheetId => !string.IsNullOrWhiteSpace(sheetId))
            .ToHashSet(StringComparer.Ordinal);

        return result.Placements.All(placement => sheetIds.Contains(placement.SheetId));
    }
}
