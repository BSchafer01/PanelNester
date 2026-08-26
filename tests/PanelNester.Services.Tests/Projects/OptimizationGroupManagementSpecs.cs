using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Projects;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Projects;

public sealed class OptimizationGroupManagementSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.OptimizationGroupManagementSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Users_can_manage_assign_delete_save_and_reopen_ordered_optimization_groups()
    {
        var generatedIds = new Queue<string>(["project-001", "group-002", "group-003"]);
        var service = new ProjectService(
            new FakeMaterialService(),
            idGenerator: () => generatedIds.Dequeue());
        var created = await service.NewAsync();
        Assert.Empty(created.Project!.State.OptimizationGroups);
        var firstGroupCreated = await service.UpdateOptimizationGroupsAsync(
            created.Project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Parts"
            });
        Assert.True(firstGroupCreated.Success);
        var originalGroup = Assert.Single(firstGroupCreated.Project!.State.OptimizationGroups);
        var firstPart = CreatePart("manual-001", "Door", "Casework");
        var secondPart = CreatePart("manual-002", "Drawer", "Casework");
        var project = firstGroupCreated.Project with
        {
            State = created.Project.State with
            {
                OptimizationGroups =
                [
                    originalGroup with { Parts = [firstPart, secondPart] }
                ],
                Parts = [firstPart, secondPart]
            }
        };

        var added = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Secondary"
            });

        Assert.True(added.Success);
        project = added.Project!;
        Assert.Equal(["Parts", "Secondary"], project.State.OptimizationGroups.Select(group => group.Name));
        var secondaryGroup = project.State.OptimizationGroups[1];

        var renamed = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Rename,
                OptimizationGroupId = originalGroup.OptimizationGroupId,
                Name = "Primary"
            });

        Assert.True(renamed.Success);
        project = renamed.Project!;
        Assert.Equal(originalGroup.OptimizationGroupId, project.State.OptimizationGroups[0].OptimizationGroupId);

        var duplicateName = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Rename,
                OptimizationGroupId = secondaryGroup.OptimizationGroupId,
                Name = " primary "
            });

        Assert.False(duplicateName.Success);
        Assert.Equal("optimization-group-name-duplicate", Assert.Single(duplicateName.Errors).Code);

        var assigned = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.MovePart,
                PartRowId = secondPart.RowId,
                TargetOptimizationGroupId = secondaryGroup.OptimizationGroupId
            });

        Assert.True(assigned.Success);
        project = assigned.Project!;
        Assert.Equal(secondPart.RowId, Assert.Single(project.State.OptimizationGroups[1].Parts).RowId);
        Assert.Equal("Casework", Assert.Single(project.State.OptimizationGroups[1].Parts).Group);

        var guardedDelete = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = secondaryGroup.OptimizationGroupId
            });

        Assert.False(guardedDelete.Success);
        Assert.Equal("optimization-group-not-empty", Assert.Single(guardedDelete.Errors).Code);
        Assert.Equal(2, project.State.Parts.Count);

        var reordered = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Reorder,
                OrderedOptimizationGroupIds =
                [
                    secondaryGroup.OptimizationGroupId,
                    originalGroup.OptimizationGroupId
                ]
            });

        Assert.True(reordered.Success);
        project = reordered.Project!;
        Assert.Equal([secondaryGroup.OptimizationGroupId, originalGroup.OptimizationGroupId],
            project.State.OptimizationGroups.Select(group => group.OptimizationGroupId));
        Assert.Equal([0, 1], project.State.OptimizationGroups.Select(group => group.Order));

        var orderedFilePath = Path.Combine(_workspacePath, "ordered-groups.pnest");
        Assert.True((await service.SaveAsync(project, orderedFilePath)).Success);
        var reopenedOrderedProject = await service.LoadAsync(orderedFilePath);
        Assert.True(reopenedOrderedProject.Success);
        project = reopenedOrderedProject.Project!;
        Assert.Equal(
            [secondaryGroup.OptimizationGroupId, originalGroup.OptimizationGroupId],
            project.State.OptimizationGroups.Select(group => group.OptimizationGroupId));
        Assert.Equal([0, 1], project.State.OptimizationGroups.Select(group => group.Order));

        var removedWithContent = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = secondaryGroup.OptimizationGroupId,
                RemoveOwnedContent = true
            });

        Assert.True(removedWithContent.Success);
        project = removedWithContent.Project!;
        var survivingGroup = Assert.Single(project.State.OptimizationGroups);
        Assert.Equal(originalGroup.OptimizationGroupId, survivingGroup.OptimizationGroupId);
        Assert.Equal("Primary", survivingGroup.Name);
        Assert.Equal(0, survivingGroup.Order);
        Assert.Equal(firstPart.RowId, Assert.Single(project.State.Parts).RowId);

        var filePath = Path.Combine(_workspacePath, "managed-groups.pnest");
        var saved = await service.SaveAsync(project, filePath);
        var reopened = await service.LoadAsync(filePath);

        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        var reopenedGroup = Assert.Single(reopened.Project!.State.OptimizationGroups);
        Assert.Equal(originalGroup.OptimizationGroupId, reopenedGroup.OptimizationGroupId);
        Assert.Equal("Primary", reopenedGroup.Name);
        Assert.Equal(0, reopenedGroup.Order);
        Assert.Equal(firstPart.RowId, Assert.Single(reopenedGroup.Parts).RowId);
        Assert.True(Assert.Single(reopenedGroup.Parts).IsManual);
    }

    [Fact]
    public async Task Removing_an_empty_group_leaves_unrelated_parts_order_and_results_unchanged()
    {
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var ownedGroup = new OptimizationGroup
        {
            OptimizationGroupId = "owned-group",
            Name = "Owned",
            Order = 0,
            Parts = sample.State.Parts,
            LastNestingResult = sample.State.LastNestingResult,
            LastBatchNestingResult = sample.State.LastBatchNestingResult,
            ResultStatus = OptimizationResultStatus.Valid
        };
        var emptyGroup = new OptimizationGroup
        {
            OptimizationGroupId = "empty-group",
            Name = "Empty",
            Order = 1
        };
        var project = sample with
        {
            State = sample.State with
            {
                OptimizationGroups = [ownedGroup, emptyGroup]
            }
        };
        var service = new ProjectService(new FakeMaterialService());

        var result = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = emptyGroup.OptimizationGroupId
            });

        Assert.True(result.Success);
        var survivingGroup = Assert.Single(result.Project!.State.OptimizationGroups);
        Assert.Equal(ownedGroup.OptimizationGroupId, survivingGroup.OptimizationGroupId);
        Assert.Equal(ownedGroup.Name, survivingGroup.Name);
        Assert.Equal(0, survivingGroup.Order);
        Assert.Equivalent(ownedGroup.Parts, survivingGroup.Parts, strict: true);
        Assert.Equivalent(ownedGroup.LastNestingResult, survivingGroup.LastNestingResult, strict: true);
        Assert.Equivalent(ownedGroup.LastBatchNestingResult, survivingGroup.LastBatchNestingResult, strict: true);
        Assert.Equal(OptimizationResultStatus.Valid, survivingGroup.ResultStatus);
    }

    [Fact]
    public async Task Deleting_a_result_owning_group_requires_explicit_content_removal()
    {
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var project = sample with
        {
            State = sample.State with
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "parts-group",
                        Name = "Parts",
                        Order = 0,
                        Parts = sample.State.Parts
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "results-group",
                        Name = "Saved Results",
                        Order = 1,
                        LastNestingResult = sample.State.LastNestingResult
                    }
                ]
            }
        };
        var service = new ProjectService(new FakeMaterialService());

        var guarded = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Delete,
                OptimizationGroupId = "results-group"
            });

        Assert.False(guarded.Success);
        Assert.Equal("optimization-group-not-empty", Assert.Single(guarded.Errors).Code);
    }

    [Fact]
    public async Task Moving_owned_content_invalidates_only_the_old_and_new_optimization_groups()
    {
        var sample = Phase03ProjectPersistenceSpec.CreateSampleProject();
        var movedPart = CreatePart("manual-move", "MOVE-1", "Casework");
        var targetPart = CreatePart("manual-target", "TARGET-1", "Casework");
        var untouchedPart = CreatePart("manual-untouched", "UNTOUCHED-1", "Casework");
        var sourceResults = CreateSavedResults(movedPart, "source");
        var targetResults = CreateSavedResults(targetPart, "target");
        var untouchedResults = CreateSavedResults(untouchedPart, "untouched");
        var project = sample with
        {
            State = sample.State with
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "source",
                        Name = "Source",
                        Order = 0,
                        Parts = [movedPart],
                        LastNestingResult = sourceResults.Nest,
                        LastBatchNestingResult = sourceResults.Batch,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "target",
                        Name = "Target",
                        Order = 1,
                        Parts = [targetPart],
                        LastNestingResult = targetResults.Nest,
                        LastBatchNestingResult = targetResults.Batch,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "untouched",
                        Name = "Untouched",
                        Order = 2,
                        Parts = [untouchedPart],
                        LastNestingResult = untouchedResults.Nest,
                        LastBatchNestingResult = untouchedResults.Batch,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ],
                Parts = [movedPart, targetPart, untouchedPart]
            }
        };
        var service = new ProjectService(new FakeMaterialService());

        var result = await service.UpdateOptimizationGroupsAsync(
            project,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.MovePart,
                PartRowId = movedPart.RowId,
                TargetOptimizationGroupId = "target"
            });

        Assert.True(result.Success);
        Assert.Equal(
            [OptimizationResultStatus.Stale, OptimizationResultStatus.Stale, OptimizationResultStatus.Valid],
            result.Project!.State.OptimizationGroups.Select(group => group.ResultStatus));
        Assert.Empty(result.Project.State.OptimizationGroups[0].Parts);
        Assert.Equal([targetPart, movedPart], result.Project.State.OptimizationGroups[1].Parts);
        Assert.Same(untouchedResults.Batch, result.Project.State.OptimizationGroups[2].LastBatchNestingResult);
    }

    [Fact]
    public async Task Failed_group_diagnostics_remain_current_and_inspectable_after_save_and_reopen()
    {
        var failedPart = CreatePart("manual-failed", "FAIL-1", "Casework");
        var excludedPart = CreatePart("manual-excluded", "EXCLUDED-1", "Casework") with
        {
            ValidationStatus = ValidationStatuses.Error,
            ValidationMessages = ["Length is required."]
        };
        var failedGroupResult = new OptimizationGroupNestResult
        {
            OptimizationResultId = "run-001:failed",
            OptimizationGroupId = "failed",
            Name = "Failed",
            Order = 0,
            Success = false,
            FailureMessage = "The nesting engine rejected this Optimization Group.",
            InputPartRowIds = [failedPart.RowId],
            OwnedPartRowIds = [failedPart.RowId, excludedPart.RowId]
        };
        var failedBatch = new BatchNestResponse
        {
            ExecutionId = "run-001",
            Success = false,
            OptimizationGroupResults = [failedGroupResult]
        };
        var project = new Project
        {
            ProjectId = "project-failure",
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "failed",
                        Name = "Failed",
                        Order = 0,
                        Parts = [failedPart, excludedPart],
                        LastBatchNestingResult = failedBatch,
                        ResultStatus = OptimizationResultStatus.Valid
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "untouched",
                        Name = "Untouched",
                        Order = 1
                    }
                ],
                Parts = [failedPart, excludedPart]
            }
        };
        var service = new ProjectService(new FakeMaterialService());
        var filePath = Path.Combine(_workspacePath, "failed-group.pnest");

        Assert.True((await service.SaveAsync(project, filePath)).Success);
        var reopened = await service.LoadAsync(filePath);

        Assert.True(reopened.Success);
        var failedGroup = reopened.Project!.State.OptimizationGroups[0];
        Assert.Equal(OptimizationResultStatus.Valid, failedGroup.ResultStatus);
        var reopenedFailure = Assert.Single(
            failedGroup.LastBatchNestingResult!.OptimizationGroupResults);
        Assert.False(reopenedFailure.Success);
        Assert.Equal(failedGroupResult.FailureMessage, reopenedFailure.FailureMessage);
        Assert.Equal(OptimizationResultStatus.None,
            reopened.Project.State.OptimizationGroups[1].ResultStatus);
    }

    private static (NestResponse Nest, BatchNestResponse Batch) CreateSavedResults(
        PartRow part,
        string identity)
    {
        var nest = new NestResponse
        {
            Success = true,
            Sheets =
            [
                new NestSheet
                {
                    SheetId = $"{identity}-sheet",
                    SheetNumber = 1,
                    MaterialName = part.MaterialName,
                    SheetLength = 96m,
                    SheetWidth = 48m
                }
            ],
            Placements =
            [
                new NestPlacement
                {
                    PlacementId = $"{identity}-placement",
                    SheetId = $"{identity}-sheet",
                    PartId = part.ImportedId,
                    Width = part.Width,
                    Height = part.Length
                }
            ],
            Summary = new MaterialSummary
            {
                TotalSheets = 1,
                TotalPlaced = 1,
                TotalUnplaced = 0
            }
        };

        return (
            nest,
            new BatchNestResponse
            {
                Success = true,
                LegacyResult = nest,
                MaterialResults =
                [
                    new MaterialNestResult
                    {
                        MaterialName = part.MaterialName,
                        Result = nest
                    }
                ]
            });
    }

    private static PartRow CreatePart(string rowId, string importedId, string partGroup) =>
        new()
        {
            RowId = rowId,
            ImportedId = importedId,
            Length = 24m,
            Width = 12m,
            Quantity = 1,
            MaterialName = "Maple",
            Group = partGroup,
            IsManual = true,
            ValidationStatus = ValidationStatuses.Valid
        };

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private sealed class FakeMaterialService : IMaterialService
    {
        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>([]);

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
