using PanelNester.Domain.Models;
using PanelNester.Services.Nesting;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Nesting;

public sealed class NestingBoundarySpecs
{
    [Fact]
    public void Empty_runs_are_rejected_before_nesting_starts()
    {
        Assert.Equal("empty-run", NestingSpec.GuardAgainstEmptyRun(partCount: 0));
    }

    [Fact]
    public void Oversized_parts_are_flagged_against_usable_sheet_area()
    {
        var material = new MaterialBounds(
            SheetLength: 96d,
            SheetWidth: 48d,
            AllowRotation: true,
            PartSpacing: 0.125d,
            EdgeMargin: 0.5d);

        var decision = NestingSpec.EvaluateSinglePart(partLength: 120d, partWidth: 49d, material);

        Assert.False(decision.Fits);
        Assert.Equal("outside-usable-sheet", decision.ReasonCode);
    }

    [Fact]
    public void Sheet_sized_parts_still_fail_when_edge_margins_reduce_usable_area()
    {
        var material = new MaterialBounds(
            SheetLength: 96d,
            SheetWidth: 48d,
            AllowRotation: true,
            PartSpacing: 0.125d,
            EdgeMargin: 0.5d);

        var decision = NestingSpec.EvaluateSinglePart(partLength: 96d, partWidth: 48d, material);

        Assert.False(decision.Fits);
        Assert.Equal("outside-usable-sheet", decision.ReasonCode);
    }

    [Fact]
    public void Rotation_can_flip_a_part_from_unfit_to_fit()
    {
        var material = new MaterialBounds(
            SheetLength: 96d,
            SheetWidth: 48d,
            AllowRotation: true,
            PartSpacing: 0.125d,
            EdgeMargin: 0d);

        var decision = NestingSpec.EvaluateSinglePart(partLength: 47.5d, partWidth: 95.5d, material);

        Assert.True(decision.Fits);
        Assert.True(decision.RequiresRotation);
    }

    [Fact]
    public async Task New_shelf_placement_prefers_the_current_width_when_it_already_matches_the_sheet_width()
    {
        var material = DemoMaterialCatalog.Phase1 with { DefaultEdgeMargin = 0m };
        var request = new NestRequest
        {
            Material = material,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-1",
                    ImportedId = "P-048",
                    Length = 24m,
                    Width = 48m,
                    Quantity = 1,
                    MaterialName = material.Name,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };

        var service = new ShelfNestingService();

        var result = await service.NestAsync(request);

        Assert.True(result.Success);
        var placement = Assert.Single(result.Placements);
        Assert.False(placement.Rotated90);
        Assert.Equal(24m, placement.Width);
        Assert.Equal(48m, placement.Height);
    }

    [Fact]
    public async Task New_shelf_placement_keeps_the_existing_height_first_preference_when_sheet_width_is_not_matched()
    {
        var material = DemoMaterialCatalog.Phase1 with { DefaultEdgeMargin = 0m };
        var request = new NestRequest
        {
            Material = material,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-1",
                    ImportedId = "P-030",
                    Length = 30m,
                    Width = 40m,
                    Quantity = 1,
                    MaterialName = material.Name,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };

        var service = new ShelfNestingService();

        var result = await service.NestAsync(request);

        Assert.True(result.Success);
        var placement = Assert.Single(result.Placements);
        Assert.True(placement.Rotated90);
        Assert.Equal(40m, placement.Width);
        Assert.Equal(30m, placement.Height);
    }

    [Fact]
    public void Floating_point_tolerance_prevents_false_negatives_near_the_fit_boundary()
    {
        var material = new MaterialBounds(
            SheetLength: 96d,
            SheetWidth: 48d,
            AllowRotation: false,
            PartSpacing: 0.125d,
            EdgeMargin: 0d);

        var decision = NestingSpec.EvaluateSinglePart(partLength: 96.00005d, partWidth: 48d, material);

        Assert.True(decision.Fits);
    }

    [Fact]
    public void Effective_clearance_is_material_spacing_plus_kerf_width()
    {
        Assert.Equal(0.1875d, NestingSpec.Clearance(spacing: 0.125d, kerfWidth: 0.0625d), precision: 6);
    }

    [Fact]
    public async Task Actual_service_rejects_empty_runs_with_an_actionable_reason_code()
    {
        var service = new ShelfNestingService();

        var result = await service.NestAsync(new NestRequest { Parts = [] });

        Assert.False(result.Success);
        var reason = Assert.Single(result.UnplacedItems);
        Assert.Equal(NestingFailureCodes.EmptyRun, reason.ReasonCode);
    }

    [Fact]
    public async Task Actual_service_keeps_the_same_reason_code_for_parts_that_cannot_fit_in_any_orientation()
    {
        var material = DemoMaterialCatalog.Phase1 with { DefaultEdgeMargin = 0m };
        var request = new NestRequest
        {
            Material = material,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-1",
                    ImportedId = "P-oversized",
                    Length = 97m,
                    Width = 49m,
                    Quantity = 1,
                    MaterialName = material.Name,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };

        var service = new ShelfNestingService();

        var result = await service.NestAsync(request);

        Assert.False(result.Success);
        var reason = Assert.Single(result.UnplacedItems);
        Assert.Equal(NestingFailureCodes.OutsideUsableSheet, reason.ReasonCode);
    }

    [Fact]
    public async Task Identical_parts_do_not_overlap_and_keep_utilization_deterministic()
    {
        var request = new NestRequest
        {
            Material = DemoMaterialCatalog.Phase1,
            KerfWidth = 0.0625m,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-1",
                    ImportedId = "P-100",
                    Length = 24m,
                    Width = 24m,
                    Quantity = 2,
                    MaterialName = DemoMaterialCatalog.Phase1.Name,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };
        var service = new ShelfNestingService();

        var first = await service.NestAsync(request);
        var second = await service.NestAsync(request);

        Assert.True(first.Success);
        Assert.Equal(2, first.Placements.Count);
        Assert.Equal(
            first.Placements.Select(placement => (placement.X, placement.Y, placement.Width, placement.Height, placement.Rotated90)),
            second.Placements.Select(placement => (placement.X, placement.Y, placement.Width, placement.Height, placement.Rotated90)));

        var ordered = first.Placements.OrderBy(placement => placement.X).ToArray();
        Assert.True(ordered[0].X + ordered[0].Width <= ordered[1].X);
        Assert.Equal(25m, first.Summary.OverallUtilization);
    }

    [Fact]
    public async Task Successful_runs_return_sheets_placements_unplaced_items_and_summary_totals()
    {
        var request = new NestRequest
        {
            Material = DemoMaterialCatalog.Phase1,
            KerfWidth = 0.0625m,
            Parts =
            [
                new PartRow
                {
                    RowId = "row-1",
                    ImportedId = "P-001",
                    Length = 20m,
                    Width = 10m,
                    Quantity = 1,
                    MaterialName = DemoMaterialCatalog.Phase1.Name,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };
        var service = new ShelfNestingService();

        var result = await service.NestAsync(request);

        Assert.True(result.Success);
        Assert.Single(result.Sheets);
        Assert.Single(result.Placements);
        Assert.Empty(result.UnplacedItems);
        Assert.Equal(1, result.Summary.TotalSheets);
        Assert.Equal(1, result.Summary.TotalPlaced);
        Assert.Equal(0, result.Summary.TotalUnplaced);
    }
}
