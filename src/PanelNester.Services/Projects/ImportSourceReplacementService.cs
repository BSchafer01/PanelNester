using System.IO;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

public static class ImportSourceReplacementService
{
    public static ImportSourceReplacementPreparation Prepare(
        Project project,
        bool replacementConfirmed)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!HasExistingImportSource(project))
        {
            return new ImportSourceReplacementPreparation(false, project);
        }

        if (!replacementConfirmed)
        {
            return new ImportSourceReplacementPreparation(true, project);
        }

        var retainedGroups = project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group =>
            {
                var manualParts = group.Parts.Where(part => part.IsManual).ToArray();
                var manualRequiredPieces = group.RequiredPieces.Where(piece => piece.IsManual).ToArray();
                return manualParts.Length == group.Parts.Count &&
                       manualRequiredPieces.Length == group.RequiredPieces.Count
                    ? group
                    : ClearResults(group with
                    {
                        Parts = manualParts,
                        RequiredPieces = manualRequiredPieces,
                        StockGroups = Array.Empty<StockGroup>()
                    });
            })
            .Where(group =>
                group.Parts.Count > 0 ||
                group.RequiredPieces.Count > 0 ||
                group.Origin != OptimizationGroupOrigin.ImportSource)
            .Select((group, order) => group with { Order = order })
            .ToArray();
        var preparedProject = project with
        {
            State = project.State with
            {
                SourceFilePath = null,
                ImportSource = null,
                ImportConfiguration = null,
                Parts = Array.Empty<PartRow>(),
                OptimizationGroups = retainedGroups,
                LastNestingResult = null,
                LastBatchNestingResult = null
            }
        };

        return new ImportSourceReplacementPreparation(false, preparedProject);
    }

    public static OptimizationGroup CreateSourceOptimizationGroup(
        Project project,
        ImportSourceMetadata importSource,
        IReadOnlyList<PartRow> parts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentNullException.ThrowIfNull(parts);

        var groupId = !string.IsNullOrWhiteSpace(project.ProjectId)
            ? $"{project.ProjectId}-import"
            : "import";
        var groupName = Path.GetFileNameWithoutExtension(importSource.ImportSourcePath);
        return new OptimizationGroup
        {
            OptimizationGroupId = groupId,
            Name = string.IsNullOrWhiteSpace(groupName) ? "Parts" : groupName,
            Order = 0,
            Origin = OptimizationGroupOrigin.ImportSource,
            Parts = parts,
            ResultStatus = OptimizationResultStatus.None
        };
    }

    private static bool HasExistingImportSource(Project project) =>
        project.State.ImportSource is not null ||
        project.State.ImportConfiguration is not null ||
        !string.IsNullOrWhiteSpace(project.State.SourceFilePath);

    private static OptimizationGroup ClearResults(OptimizationGroup group) =>
        group with
        {
            LastStockLengthOptimizationResult = null,
            LastNestingResult = null,
            LastBatchNestingResult = null,
            ResultStatus = OptimizationResultStatus.None
        };
}

public sealed record ImportSourceReplacementPreparation(
    bool ConfirmationRequired,
    Project Project);
