using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class ExtrusionTakeoffServiceSpecs
{
    [Fact]
    public async Task Additional_line_items_are_added_to_overall_and_group_summaries()
    {
        var service = new ExtrusionTakeoffService();
        var project = new Project
        {
            State = new ProjectState
            {
                Parts =
                [
                    CreatePart("row-1", "A-1", "A", 120m, 24m),
                    CreatePart("row-2", "A-2", "A", 60m, 36m),
                    CreatePart("row-3", "B-1", "B", 48m, 24m)
                ],
                ExtrusionLayout = new ExtrusionLayoutState
                {
                    EdgeStickLengthFeet = 10m,
                    PanelToPanelStickLengthFeet = 10m,
                    AdditionalLineItems =
                    [
                        new ExtrusionAdditionalLineItem
                        {
                            Id = "trim",
                            Name = "Trim Cap",
                            QuantityBasis = ExtrusionLineItemQuantityBases.Both,
                            StickLengthFeet = 12m
                        },
                        new ExtrusionAdditionalLineItem
                        {
                            Id = "edge-only",
                            Name = "Sealant",
                            QuantityBasis = ExtrusionLineItemQuantityBases.Edge,
                            StickLengthFeet = 5m
                        }
                    ],
                    Groups =
                    [
                        new ExtrusionGroupLayout
                        {
                            GroupName = "A",
                            Rows = 1,
                            Columns = 2,
                            Cells =
                            [
                                new ExtrusionGridCell { InstanceId = "row-1#1", Row = 0, Column = 0 },
                                new ExtrusionGridCell { InstanceId = "row-2#1", Row = 0, Column = 1 }
                            ]
                        },
                        new ExtrusionGroupLayout
                        {
                            GroupName = "B",
                            Rows = 1,
                            Columns = 1,
                            Cells =
                            [
                                new ExtrusionGridCell { InstanceId = "row-3#1", Row = 0, Column = 0 }
                            ]
                        }
                    ]
                }
            }
        };

        var report = await service.BuildReportAsync(new ExtrusionReportRequest { Project = project });

        var overallTrim = Assert.Single(report.OverallLengths, row => row.ExtrusionName == "Trim Cap");
        Assert.Equal(49m, overallTrim.TotalLinearFeet);
        Assert.Equal(5, overallTrim.RequiredStickCount);

        var overallSealant = Assert.Single(report.OverallLengths, row => row.ExtrusionName == "Sealant");
        Assert.Equal(47m, overallSealant.TotalLinearFeet);
        Assert.Equal(10, overallSealant.RequiredStickCount);

        var groupA = Assert.Single(report.Groups, group => group.GroupName == "A");
        var groupATrim = Assert.Single(groupA.Lengths, row => row.ExtrusionName == "Trim Cap");
        Assert.Equal(37m, groupATrim.TotalLinearFeet);
        Assert.Equal(4, groupATrim.RequiredStickCount);
    }

    private static PartRow CreatePart(
        string rowId,
        string importedId,
        string group,
        decimal length,
        decimal width) =>
        new()
        {
            RowId = rowId,
            ImportedId = importedId,
            MaterialName = "ACM",
            Group = group,
            Length = length,
            Width = width,
            Quantity = 1,
            ValidationStatus = ValidationStatuses.Valid
        };
}
