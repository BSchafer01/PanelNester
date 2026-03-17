using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Nesting;

namespace PanelNester.Services.Tests.Nesting;

public sealed class BatchNestingServiceSpecs
{
    [Fact]
    public async Task Batch_nesting_groups_parts_by_material_and_sets_legacy_selection()
    {
        var birch = BuildMaterial("mat-birch", "Baltic Birch");
        var maple = BuildMaterial("mat-maple", "Maple Ply");
        PartRow[] parts =
        [
            new PartRow
            {
                RowId = "row-1",
                ImportedId = "B-001",
                Length = 20m,
                Width = 10m,
                Quantity = 1,
                MaterialName = birch.Name,
                ValidationStatus = ValidationStatuses.Valid
            },
            new PartRow
            {
                RowId = "row-2",
                ImportedId = "M-001",
                Length = 18m,
                Width = 12m,
                Quantity = 1,
                MaterialName = maple.Name,
                ValidationStatus = ValidationStatuses.Valid
            }
        ];

        var service = new BatchNestingService(new ShelfNestingService());

        var result = await service.NestBatchAsync(
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [maple, birch],
                KerfWidth = 0.0625m,
                SelectedMaterialId = maple.MaterialId
            });

        Assert.True(result.Success);
        Assert.Equal(["Baltic Birch", "Maple Ply"], result.MaterialResults.Select(r => r.MaterialName).ToArray());
        Assert.Equal(result.MaterialResults[1].Result, result.LegacyResult);
    }

    [Fact]
    public async Task Missing_materials_return_actionable_unplaced_items()
    {
        var birch = BuildMaterial("mat-birch", "Baltic Birch");
        PartRow[] parts =
        [
            new PartRow
            {
                RowId = "row-1",
                ImportedId = "X-001",
                Length = 20m,
                Width = 10m,
                Quantity = 2,
                MaterialName = "Unknown Material",
                ValidationStatus = ValidationStatuses.Valid
            }
        ];

        var service = new BatchNestingService(new ShelfNestingService());

        var result = await service.NestBatchAsync(
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [birch],
                KerfWidth = 0.0625m
            });

        var materialResult = Assert.Single(result.MaterialResults);
        Assert.False(materialResult.Result.Success);
        Assert.Equal(2, materialResult.Result.UnplacedItems.Count);
        Assert.All(
            materialResult.Result.UnplacedItems,
            item => Assert.Equal(NestingFailureCodes.InvalidInput, item.ReasonCode));
    }

    [Fact]
    public async Task Rows_without_group_assignments_keep_the_existing_material_only_batch_boundaries()
    {
        var birch = BuildMaterial("mat-birch", "Baltic Birch");
        var maple = BuildMaterial("mat-maple", "Maple Ply");
        PartRow[] parts =
        [
            new()
            {
                RowId = "row-1",
                ImportedId = "M-001",
                Length = 18m,
                Width = 12m,
                Quantity = 1,
                MaterialName = maple.Name,
                ValidationStatus = ValidationStatuses.Valid
            },
            new()
            {
                RowId = "row-2",
                ImportedId = "B-001",
                Length = 20m,
                Width = 10m,
                Quantity = 1,
                MaterialName = birch.Name,
                ValidationStatus = ValidationStatuses.Valid
            },
            new()
            {
                RowId = "row-3",
                ImportedId = "B-002",
                Length = 22m,
                Width = 11m,
                Quantity = 2,
                MaterialName = birch.Name,
                ValidationStatus = ValidationStatuses.Valid
            }
        ];

        var nestingService = new RecordingNestingService();
        var service = new BatchNestingService(nestingService);

        var result = await service.NestBatchAsync(
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [maple, birch],
                KerfWidth = 0.0625m
            });

        Assert.True(result.Success);
        Assert.Equal(["Baltic Birch", "Maple Ply"], nestingService.Requests.Select(request => request.Material.Name).ToArray());
        Assert.Equal(["B-001", "B-002"], nestingService.Requests[0].Parts.Select(part => part.ImportedId).ToArray());
        Assert.Equal(["M-001"], nestingService.Requests[1].Parts.Select(part => part.ImportedId).ToArray());
    }

    [Fact]
    public async Task Grouped_parts_are_nested_in_first_seen_order_with_ungrouped_rows_last()
    {
        var material = BuildMaterial("mat-birch", "Baltic Birch", defaultSpacing: 0m, defaultEdgeMargin: 0m);
        PartRow[] parts =
        [
            CreatePartRow("row-1", "B-001", 96m, 24m, material.Name, "B"),
            CreatePartRow("row-2", "U-001", 96m, 24m, material.Name, null),
            CreatePartRow("row-3", "A-001", 96m, 24m, material.Name, "A"),
            CreatePartRow("row-4", "B-002", 96m, 24m, material.Name, "B")
        ];

        var service = new BatchNestingService(new ShelfNestingService());
        var result = await service.NestBatchAsync(
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [material],
                KerfWidth = 0m
            });

        var materialResult = Assert.Single(result.MaterialResults);
        Assert.True(materialResult.Result.Success);
        Assert.Equal(
            ["B-001", "B-002", "A-001", "U-001"],
            materialResult.Result.Placements.Select(placement => placement.PartId).ToArray());
        Assert.Equal(
            new string?[] { "B", "B", "A", null },
            materialResult.Result.Placements.Select(ReadPlacementGroup).ToArray());
    }

    [Fact]
    public async Task Only_the_final_partially_used_sheet_can_accept_parts_from_the_next_group()
    {
        var material = BuildMaterial("mat-birch", "Baltic Birch", defaultSpacing: 0m, defaultEdgeMargin: 0m);
        PartRow[] parts =
        [
            CreatePartRow("row-1", "A-001", 60m, 24m, material.Name, "A"),
            CreatePartRow("row-2", "A-002", 60m, 24m, material.Name, "A"),
            CreatePartRow("row-3", "A-003", 60m, 24m, material.Name, "A"),
            CreatePartRow("row-4", "B-001", 36m, 24m, material.Name, "B", quantity: 5)
        ];

        var service = new BatchNestingService(new ShelfNestingService());
        var result = await service.NestBatchAsync(
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [material],
                KerfWidth = 0m
            });

        var materialResult = Assert.Single(result.MaterialResults);
        Assert.True(materialResult.Result.Success);
        Assert.Equal(3, materialResult.Result.Sheets.Count);

        var nextGroupPlacements = materialResult.Result.Placements
            .Where(placement => placement.PartId.StartsWith("B-001#", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["sheet-2", "sheet-2", "sheet-2", "sheet-3", "sheet-3"], nextGroupPlacements.Select(placement => placement.SheetId).ToArray());
        Assert.DoesNotContain(nextGroupPlacements, placement => string.Equals(placement.SheetId, "sheet-1", StringComparison.Ordinal));

        var mixedSheetPlacements = materialResult.Result.Placements
            .Where(placement => string.Equals(placement.SheetId, "sheet-2", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(mixedSheetPlacements, placement => string.Equals(ReadPlacementGroup(placement), "A", StringComparison.Ordinal));
        Assert.Contains(mixedSheetPlacements, placement => string.Equals(ReadPlacementGroup(placement), "B", StringComparison.Ordinal));
    }

    private static Material BuildMaterial(
        string materialId,
        string name,
        decimal defaultSpacing = 0.125m,
        decimal defaultEdgeMargin = 0.5m) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = defaultSpacing,
            DefaultEdgeMargin = defaultEdgeMargin
        };

    private static PartRow CreatePartRow(
        string rowId,
        string importedId,
        decimal length,
        decimal width,
        string materialName,
        string? group,
        int quantity = 1) =>
        new()
        {
            RowId = rowId,
            ImportedId = importedId,
            Length = length,
            Width = width,
            Quantity = quantity,
            MaterialName = materialName,
            Group = group,
            ValidationStatus = ValidationStatuses.Valid
        };

    private static string? ReadPlacementGroup(NestPlacement placement)
    {
        var groupProperty = typeof(NestPlacement).GetProperty("Group");
        Assert.True(groupProperty is not null, "NestPlacement.Group should exist so nested placements keep their originating group.");
        return groupProperty!.GetValue(placement) as string;
    }

    private sealed class RecordingNestingService : INestingService
    {
        public List<NestRequest> Requests { get; } = [];

        public Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(
                new NestResponse
                {
                    Success = true,
                    Summary = new MaterialSummary
                    {
                        TotalSheets = 1,
                        TotalPlaced = request.Parts.Sum(part => part.Quantity),
                        TotalUnplaced = 0,
                        OverallUtilization = 50m
                    }
                });
        }
    }
}
