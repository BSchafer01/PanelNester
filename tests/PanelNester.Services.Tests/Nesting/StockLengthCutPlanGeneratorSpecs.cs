using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Nesting;

namespace PanelNester.Services.Tests.Nesting;

public sealed class StockLengthCutPlanGeneratorSpecs
{
    [Fact]
    public async Task Generate_accepts_domain_inputs_and_hides_the_constrained_sheet_representation()
    {
        NestRequest? engineRequest = null;
        var engine = new DelegatingNestingService(request =>
        {
            engineRequest = request;
            return new NestResponse
            {
                Success = true,
                Sheets =
                [
                    new NestSheet
                    {
                        SheetId = "engine-sheet-guid",
                        SheetNumber = 1,
                        MaterialName = request.Material.Name,
                        SheetLength = 120,
                        SheetWidth = 1
                    }
                ],
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "engine-placement-guid",
                        SheetId = "engine-sheet-guid",
                        PartId = request.Parts[0].ImportedId,
                        Width = 48,
                        Height = 1,
                        Rotated90 = false
                    }
                ]
            };
        });
        IStockLengthCutPlanGenerator generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

        var result = await generator.GenerateAsync(new StockLengthCutPlanRequest
        {
            OptimizationGroupId = "frames",
            StockLength = 120,
            SawKerf = 0.125m,
            RequiredPieces =
            [
                new RequiredPiece
                {
                    RequiredPieceId = "piece-1",
                    Quantity = 1,
                    Length = 48,
                    ProfileNumber = " P-100 ",
                    Finish = " Clear ",
                    PartNumber = "DUPLICATE"
                }
            ]
        });

        Assert.NotNull(engineRequest);
        Assert.Equal(1, engineRequest.Material.SheetWidth);
        Assert.Equal(0, engineRequest.Material.DefaultEdgeMargin);
        Assert.Equal(0, engineRequest.Material.DefaultSpacing);
        Assert.False(engineRequest.Material.AllowRotation);
        Assert.Equal(0.125m, engineRequest.KerfWidth);
        Assert.All(engineRequest.Parts, part =>
        {
            Assert.Equal(1, part.Width);
            Assert.Null(part.Group);
        });
        Assert.DoesNotContain(engineRequest.Material.Name, result.ToString(), StringComparison.Ordinal);

        Assert.Equal(CutPlanStatus.Complete, result.Status);
        var cutPlan = Assert.Single(result.CutPlans);
        Assert.Equal("P-100", cutPlan.StockGroup.ProfileNumber);
        Assert.Equal("Clear", cutPlan.StockGroup.Finish);
        var stockItem = Assert.Single(cutPlan.StockItems);
        Assert.Equal("frames:stock-group-1:stock-item-1", stockItem.StockItemId);
        var instance = Assert.Single(stockItem.CutSequence);
        Assert.Equal("piece-1:instance-1", instance.PieceInstanceId);
        Assert.Equal("piece-1", instance.RequiredPieceId);
        Assert.NotEqual("engine-placement-guid", instance.PieceInstanceId);
    }

    [Fact]
    public async Task Generate_derives_kerf_remainder_utilization_and_ordered_Cut_Sequence()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        var result = await generator.GenerateAsync(Request(
            stockLength: 10,
            sawKerf: 0.05m,
            Piece("piece-1", 3, 3.3m, "P-100", "Clear")));

        var stockItem = Assert.Single(Assert.Single(result.CutPlans).StockItems);
        Assert.Equal(9.9m, stockItem.PieceLength);
        Assert.Equal(0.1m, stockItem.SawLoss);
        Assert.Equal(0m, stockItem.Remainder);
        Assert.Equal(99m, stockItem.UtilizationPercent);
        Assert.Equal(
            ["piece-1:instance-1", "piece-1:instance-2", "piece-1:instance-3"],
            stockItem.CutSequence.Select(instance => instance.PieceInstanceId));
    }

    [Fact]
    public async Task Generate_clamps_a_fit_tolerance_remainder_to_zero()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        var result = await generator.GenerateAsync(Request(
            10,
            0,
            Piece("near-fit", 1, 10.00005m, "P-100", null)));

        Assert.Equal(0m, Assert.Single(Assert.Single(result.CutPlans).StockItems).Remainder);
    }

    [Fact]
    public async Task Synthetic_material_keys_are_collision_safe_for_ambiguous_Profile_and_Finish_pairs()
    {
        var syntheticKeys = new List<string>();
        var engine = new DelegatingNestingService(request =>
        {
            syntheticKeys.Add(request.Material.Name);
            var part = request.Parts[0];
            return new NestResponse
            {
                Success = true,
                Sheets = [new NestSheet { SheetId = "sheet", SheetNumber = 1, MaterialName = request.Material.Name, SheetLength = request.Material.SheetLength, SheetWidth = 1 }],
                Placements = [new NestPlacement { PlacementId = "placement", SheetId = "sheet", PartId = part.ImportedId, Width = part.Length, Height = 1 }]
            };
        });
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

        var result = await generator.GenerateAsync(Request(
            100,
            0,
            Piece("first", 1, 20, "AB", "C"),
            Piece("second", 1, 20, "A", "BC")));

        Assert.Equal(2, syntheticKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("__stock__", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_keeps_normalized_Stock_Groups_on_separate_Stock_Items()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        var result = await generator.GenerateAsync(Request(
            120,
            0,
            Piece("clear-a", 1, 60, " P-100 ", " Clear "),
            Piece("clear-b", 1, 40, "p-100", "clear"),
            Piece("bronze", 1, 50, "P-100", "Bronze"),
            Piece("other-profile", 1, 30, "P-200", null)));

        Assert.Equal(3, result.CutPlans.Count);
        var clear = Assert.Single(result.CutPlans, plan => plan.StockGroup.Finish == "Clear");
        Assert.Equal(["clear-a", "clear-b"], clear.StockGroup.RequiredPieceIds);
        Assert.Equal(2, Assert.Single(clear.StockItems).CutSequence.Count);
        Assert.Equal(
            result.CutPlans.Sum(plan => plan.StockItems.Count),
            result.CutPlans.SelectMany(plan => plan.StockItems).Select(item => item.StockItemId).Distinct().Count());
    }

    [Fact]
    public async Task Generate_uses_stable_identities_when_Part_Numbers_are_duplicate()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());
        var request = Request(
            100,
            0,
            Piece("required-a", 2, 20, "P-100", null, "DUPLICATE"),
            Piece("required-b", 1, 20, "P-100", null, "DUPLICATE"));

        var first = await generator.GenerateAsync(request);
        var second = await generator.GenerateAsync(request);

        Assert.Equal(
            first.CutPlans.SelectMany(plan => plan.StockItems).Select(item => item.StockItemId),
            second.CutPlans.SelectMany(plan => plan.StockItems).Select(item => item.StockItemId));
        Assert.Equal(
            ["required-a:instance-1", "required-a:instance-2", "required-b:instance-1"],
            first.CutPlans.SelectMany(plan => plan.StockItems).SelectMany(item => item.CutSequence)
                .Select(instance => instance.PieceInstanceId));
    }

    [Fact]
    public async Task Generate_classifies_partial_and_failed_from_piece_counts_and_explains_overlength_pieces()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        var partial = await generator.GenerateAsync(Request(
            100,
            0,
            Piece("fits", 1, 40, "P-100", null),
            Piece("too-long", 1, 101, "P-100", null)));
        var failed = await generator.GenerateAsync(Request(
            100,
            0,
            Piece("all-too-long", 2, 101, "P-100", null)));

        Assert.Equal(CutPlanStatus.Partial, partial.Status);
        var partialUnplaced = Assert.Single(Assert.Single(partial.CutPlans).UnplacedPieceInstances);
        Assert.Equal("too-long:instance-1", partialUnplaced.PieceInstance.PieceInstanceId);
        Assert.Equal("exceeds-stock-length", partialUnplaced.ReasonCode);
        Assert.Contains("exceeds Stock Length", partialUnplaced.ReasonDescription, StringComparison.Ordinal);
        Assert.Equal(CutPlanStatus.Failed, failed.Status);
        Assert.Equal(2, Assert.Single(failed.CutPlans).UnplacedPieceInstances.Count);
    }

    [Fact]
    public async Task Generate_classifies_valid_placements_independently_of_the_engine_success_flag()
    {
        var engine = new DelegatingNestingService(request => new NestResponse
        {
            Success = false,
            Sheets =
            [
                new NestSheet
                {
                    SheetId = "sheet-1", SheetNumber = 1, MaterialName = request.Material.Name,
                    SheetLength = request.Material.SheetLength, SheetWidth = 1
                }
            ],
            Placements =
            [
                new NestPlacement
                {
                    PlacementId = "placement-1", SheetId = "sheet-1",
                    PartId = request.Parts[0].ImportedId, Width = 40, Height = 1
                }
            ]
        });
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

        var result = await generator.GenerateAsync(Request(100, 0, Piece("piece", 1, 40, "P-100", null)));

        Assert.Equal(CutPlanStatus.Complete, result.Status);
    }

    [Fact]
    public async Task Generate_reports_adapter_invariant_violations_as_application_errors()
    {
        var engine = new DelegatingNestingService(request => new NestResponse
        {
            Success = true,
            Sheets =
            [
                new NestSheet
                {
                    SheetId = "sheet-1", SheetNumber = 1, MaterialName = request.Material.Name,
                    SheetLength = request.Material.SheetLength, SheetWidth = 1
                }
            ],
            Placements =
            [
                new NestPlacement
                {
                    PlacementId = "placement-1", SheetId = "sheet-1", PartId = request.Parts[0].ImportedId,
                    Width = 1, Height = 40, Rotated90 = true
                }
            ]
        });
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

        var error = await Assert.ThrowsAsync<CutPlanGenerationException>(() =>
            generator.GenerateAsync(Request(100, 0, Piece("piece", 1, 40, "P-100", null))));

        Assert.Equal("cut-plan-adapter-invariant", error.Code);
    }

    [Fact]
    public async Task Generate_rejects_invalid_one_dimensional_engine_geometry()
    {
        var invalidPlacements = new[]
        {
            new NestPlacement { PlacementId = "wrong-length", SheetId = "sheet-1", PartId = "__piece__7:piece-1", X = 0, Y = 0, Width = 1, Height = 1 },
            new NestPlacement { PlacementId = "wrong-strip", SheetId = "sheet-1", PartId = "__piece__7:piece-1", X = 0, Y = 0.1m, Width = 40, Height = 1 },
            new NestPlacement { PlacementId = "out-of-bounds", SheetId = "sheet-1", PartId = "__piece__7:piece-1", X = 70, Y = 0, Width = 40, Height = 1 }
        };

        foreach (var invalidPlacement in invalidPlacements)
        {
            var engine = new DelegatingNestingService(request => new NestResponse
            {
                Success = true,
                Sheets = [new NestSheet { SheetId = "sheet-1", SheetNumber = 1, MaterialName = request.Material.Name, SheetLength = 100, SheetWidth = 1 }],
                Placements = [invalidPlacement]
            });
            var generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

            var error = await Assert.ThrowsAsync<CutPlanGenerationException>(() =>
                generator.GenerateAsync(Request(100, 0, Piece("piece-1", 1, 40, "P-100", null))));

            Assert.Equal("cut-plan-adapter-invariant", error.Code);
        }
    }

    [Fact]
    public async Task Generate_rejects_overlapping_or_under_spaced_engine_placements()
    {
        var engine = new DelegatingNestingService(request => new NestResponse
        {
            Success = true,
            Sheets = [new NestSheet { SheetId = "sheet-1", SheetNumber = 1, MaterialName = request.Material.Name, SheetLength = 100, SheetWidth = 1 }],
            Placements =
            [
                new NestPlacement { PlacementId = "first", SheetId = "sheet-1", PartId = request.Parts[0].ImportedId, X = 0, Width = 40, Height = 1 },
                new NestPlacement { PlacementId = "second", SheetId = "sheet-1", PartId = request.Parts[1].ImportedId, X = 40.1m, Width = 20, Height = 1 }
            ]
        });
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(engine);

        var error = await Assert.ThrowsAsync<CutPlanGenerationException>(() => generator.GenerateAsync(Request(
            100,
            0.125m,
            Piece("first", 1, 40, "P-100", null),
            Piece("second", 1, 20, "P-100", null))));

        Assert.Equal("cut-plan-adapter-invariant", error.Code);
    }

    [Fact]
    public async Task Generate_documents_the_known_nonoptimal_first_fit_result()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        var result = await generator.GenerateAsync(Request(
            10,
            0,
            Piece("six", 1, 6, "P-100", null),
            Piece("five", 1, 5, "P-100", null),
            Piece("three", 1, 3, "P-100", null),
            Piece("two", 3, 2, "P-100", null)));

        Assert.Equal(3, Assert.Single(result.CutPlans).StockItems.Count);
        Assert.Contains("heuristic", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("optimal", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_reports_domain_Stock_Group_progress_without_adapter_details()
    {
        var reports = new List<StockLengthGenerationProgress>();
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());

        await generator.GenerateAsync(
            Request(
                100,
                0,
                Piece("first", 1, 20, "P-100", "Clear"),
                Piece("second", 1, 20, "P-200", null)),
            new InlineProgress(reports.Add));

        var stockGroupReports = reports
            .Where(report => report.Phase == StockLengthGenerationProgressPhase.StockGroups)
            .ToArray();
        Assert.Equal([0, 1, 1, 2], stockGroupReports.Select(report => report.CompletedStockGroups));
        Assert.All(stockGroupReports, report => Assert.Equal(2, report.TotalStockGroups));
        Assert.All(reports, report =>
        {
            Assert.Contains("Stock Group", report.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("__stock__", report.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("sheet", report.Label, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Generate_cancels_during_large_quantity_preparation_without_calling_the_engine()
    {
        var engineCalls = 0;
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(
            new DelegatingNestingService(request =>
            {
                engineCalls++;
                return new NestResponse();
            }));
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(
            Request(100, 0, Piece("large", 20_001, 1, "P-100", null)),
            progress,
            cancellation.Token));

        Assert.Equal(0, engineCalls);
    }

    [Fact]
    public async Task Generate_accepts_a_representative_quantity_above_the_warning_threshold()
    {
        var generator = new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService());
        var reports = new List<StockLengthGenerationProgress>();

        var result = await generator.GenerateAsync(
            Request(1_000, 0, Piece("large", 10_001, 1, "P-100", null)),
            new InlineProgress(reports.Add));

        Assert.Equal(CutPlanStatus.Complete, result.Status);
        Assert.Equal(
            10_001,
            Assert.Single(result.CutPlans).StockItems.Sum(item => item.CutSequence.Count));
        var pieceProgress = reports
            .Where(report => report.Phase == StockLengthGenerationProgressPhase.PieceInstances)
            .ToArray();
        Assert.Contains(pieceProgress, report =>
            report.CompletedPieceInstanceSteps > 0 &&
            report.CompletedPieceInstanceSteps < report.TotalPieceInstanceSteps);
        Assert.All(pieceProgress, report => Assert.Equal(20_002, report.TotalPieceInstanceSteps));
        Assert.Equal(
            pieceProgress.Select(report => report.CompletedPieceInstanceSteps).Order(),
            pieceProgress.Select(report => report.CompletedPieceInstanceSteps));
        Assert.InRange(pieceProgress.Length, 4, 200);
    }

    private static StockLengthCutPlanRequest Request(
        decimal stockLength,
        decimal sawKerf,
        params RequiredPiece[] pieces) =>
        new()
        {
            OptimizationGroupId = "frames",
            StockLength = stockLength,
            SawKerf = sawKerf,
            RequiredPieces = pieces
        };

    private static RequiredPiece Piece(
        string id,
        int quantity,
        decimal length,
        string profileNumber,
        string? finish,
        string? partNumber = null) =>
        new()
        {
            RequiredPieceId = id,
            Quantity = quantity,
            Length = length,
            ProfileNumber = profileNumber,
            Finish = finish,
            PartNumber = partNumber
        };

    private sealed class DelegatingNestingService(Func<NestRequest, NestResponse> handler) : INestingService
    {
        public Task<NestResponse> NestAsync(
            NestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(handler(request));
    }

    private sealed class InlineProgress(Action<StockLengthGenerationProgress> report)
        : IProgress<StockLengthGenerationProgress>
    {
        public void Report(StockLengthGenerationProgress value) => report(value);
    }
}
