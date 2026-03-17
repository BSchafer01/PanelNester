using PanelNester.Domain.Models;
using PanelNester.Domain.Tests.Specifications;

namespace PanelNester.Domain.Tests.Models;

public sealed class NestResultContractSpecs
{
    [Fact]
    public void Initial_unplaced_reason_codes_cover_phase_one_failure_modes()
    {
        Assert.Contains("outside-usable-sheet", Phase01DomainExpectations.InitialUnplacedReasonCodes);
        Assert.Contains("empty-run", Phase01DomainExpectations.InitialUnplacedReasonCodes);
    }

    [Fact]
    public void Nest_results_expose_sheets_placements_unplaced_items_and_summary_totals()
    {
        var result = new NestResponse
        {
            Success = true,
            Sheets =
            [
                new NestSheet
                {
                    SheetId = "sheet-1",
                    SheetNumber = 1,
                    MaterialName = "Demo Material",
                    SheetLength = 96m,
                    SheetWidth = 48m,
                    UtilizationPercent = 25m
                }
            ],
            Placements =
            [
                new NestPlacement
                {
                    PlacementId = "sheet-1-placement-1",
                    SheetId = "sheet-1",
                    PartId = "P-001",
                    X = 0.5m,
                    Y = 0.5m,
                    Width = 24m,
                    Height = 12m,
                    Rotated90 = false
                }
            ],
            UnplacedItems =
            [
                new UnplacedItem
                {
                    PartId = "P-404",
                    ReasonCode = NestingFailureCodes.NoLayoutSpace,
                    ReasonDescription = "Part could not be placed with the current shelf heuristic."
                }
            ],
            Summary = new MaterialSummary
            {
                TotalSheets = 1,
                TotalPlaced = 1,
                TotalUnplaced = 1,
                OverallUtilization = 25m
            }
        };

        Assert.True(result.Success);
        Assert.Single(result.Sheets);
        var placement = Assert.Single(result.Placements);
        var groupProperty = typeof(NestPlacement).GetProperty("Group");
        Assert.True(groupProperty is not null, "NestPlacement.Group should exist so grouped review can use explicit placement metadata.");
        Assert.Single(result.UnplacedItems);
        Assert.Equal(1, result.Summary.TotalSheets);
        Assert.Equal(25m, result.Summary.OverallUtilization);
    }

    [Fact]
    public void Nest_placements_expose_optional_group_metadata_for_grouped_results_review()
    {
        var groupProperty = typeof(NestPlacement).GetProperty("Group");

        Assert.True(
            groupProperty is not null,
            "NestPlacement.Group should exist so grouped review and mixed-group sheet styling do not infer grouping from part ids.");
        Assert.Equal(typeof(string), groupProperty!.PropertyType);

        var placement = new NestPlacement();
        groupProperty.SetValue(placement, "Casework");

        Assert.Equal("Casework", groupProperty.GetValue(placement));
    }
}
