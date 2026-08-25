using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Nesting;

namespace PanelNester.Services.Tests.Nesting;

public sealed class OptimizationGroupNestingSpecs
{
    [Fact]
    public async Task Run_all_keeps_groups_isolated_in_explicit_order_with_unique_identities()
    {
        var material = CreateMaterial();
        var generatedIds = new Queue<string>(["run-001", "run-002"]);
        var service = new BatchNestingService(
            new ShelfNestingService(),
            () => generatedIds.Dequeue());
        var request = new BatchNestRequest
        {
            OptimizationGroups =
            [
                CreateGroup("group-second", "Second", 1, "SECOND-1", material.Name),
                CreateGroup("group-first", "First", 0, "FIRST-1", material.Name)
            ],
            Materials = [material],
            KerfWidth = 0m
        };

        var firstRun = await service.NestBatchAsync(request);
        var secondRun = await service.NestBatchAsync(request);

        Assert.Equal(["group-first", "group-second"],
            firstRun.OptimizationGroupResults.Select(result => result.OptimizationGroupId));
        Assert.All(firstRun.OptimizationGroupResults, result => Assert.True(result.Success));
        Assert.Equal(2, firstRun.OptimizationGroupResults
            .SelectMany(group => group.MaterialResults)
            .SelectMany(materialResult => materialResult.Result.Sheets)
            .Select(sheet => sheet.SheetId)
            .Distinct(StringComparer.Ordinal)
            .Count());

        foreach (var groupResult in firstRun.OptimizationGroupResults)
        {
            var materialResult = Assert.Single(groupResult.MaterialResults);
            Assert.All(materialResult.Result.Sheets,
                sheet => Assert.StartsWith(groupResult.OptimizationResultId, sheet.SheetId, StringComparison.Ordinal));
            Assert.All(materialResult.Result.Placements,
                placement =>
                {
                    Assert.StartsWith(groupResult.OptimizationResultId, placement.PlacementId, StringComparison.Ordinal);
                    Assert.Contains(materialResult.Result.Sheets,
                        sheet => sheet.SheetId == placement.SheetId);
                });
        }

        Assert.NotEqual(firstRun.ExecutionId, secondRun.ExecutionId);
        Assert.Empty(firstRun.OptimizationGroupResults
            .SelectMany(group => group.MaterialResults)
            .SelectMany(materialResult => materialResult.Result.Sheets)
            .Select(sheet => sheet.SheetId)
            .Intersect(
                secondRun.OptimizationGroupResults
                    .SelectMany(group => group.MaterialResults)
                    .SelectMany(materialResult => materialResult.Result.Sheets)
                    .Select(sheet => sheet.SheetId),
                StringComparer.Ordinal));
    }

    [Fact]
    public async Task Run_all_reports_a_failed_group_without_discarding_other_group_results()
    {
        var material = CreateMaterial();
        var service = new BatchNestingService(
            new SelectivelyFailingNestingService("FAIL-1"),
            () => "run-partial");

        var response = await service.NestBatchAsync(
            new BatchNestRequest
            {
                OptimizationGroups =
                [
                    CreateGroup("group-good-1", "Good one", 0, "GOOD-1", material.Name),
                    CreateGroup("group-bad", "Bad", 1, "FAIL-1", material.Name),
                    CreateGroup("group-good-2", "Good two", 2, "GOOD-2", material.Name)
                ],
                Materials = [material],
                KerfWidth = 0m
            });

        Assert.False(response.Success);
        Assert.True(response.PartialSuccess);
        Assert.Equal([true, false, true], response.OptimizationGroupResults.Select(result => result.Success));
        Assert.Contains("simulated group failure", response.OptimizationGroupResults[1].FailureMessage);
        Assert.Single(response.OptimizationGroupResults[0].MaterialResults);
        Assert.Single(response.OptimizationGroupResults[2].MaterialResults);
    }

    private static OptimizationGroupNestRequest CreateGroup(
        string id,
        string name,
        int order,
        string partId,
        string materialName) =>
        new()
        {
            OptimizationGroupId = id,
            Name = name,
            Order = order,
            Parts =
            [
                new PartRow
                {
                    RowId = partId,
                    ImportedId = partId,
                    Length = 24m,
                    Width = 12m,
                    Quantity = 1,
                    MaterialName = materialName,
                    Group = "Part Group A",
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };

    private static Material CreateMaterial() =>
        new()
        {
            MaterialId = "mat-birch",
            Name = "Baltic Birch",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0m,
            DefaultEdgeMargin = 0m
        };

    private sealed class SelectivelyFailingNestingService(string failingPartId) : INestingService
    {
        private readonly ShelfNestingService _inner = new();

        public Task<NestResponse> NestAsync(
            NestRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Parts.Any(part => part.ImportedId == failingPartId))
            {
                throw new InvalidOperationException("simulated group failure");
            }

            return _inner.NestAsync(request, cancellationToken);
        }
    }
}
