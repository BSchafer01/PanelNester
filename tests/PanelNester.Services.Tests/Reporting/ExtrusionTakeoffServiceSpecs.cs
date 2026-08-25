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

    [Fact]
    public async Task Sheet_numbers_default_layout_grouping_and_row_column_seed_cells()
    {
        var service = new ExtrusionTakeoffService();
        var project = new Project
        {
            State = new ProjectState
            {
                Parts =
                [
                    CreatePart("row-1", "A-1", "A", 120m, 24m) with
                    {
                        SheetNumber = "A2",
                        RowNumber = 2,
                        ColumnNumber = 3
                    },
                    CreatePart("row-2", "A-2", "A", 60m, 36m) with
                    {
                        SheetNumber = "A2",
                        RowNumber = 1,
                        ColumnNumber = 1
                    }
                ]
            }
        };

        var layout = await service.BuildLayoutAsync(new ExtrusionLayoutRequest { Project = project });

        Assert.Equal(ExtrusionGroupingModes.SheetNumber, layout.GroupingMode);
        var sheet = Assert.Single(layout.Groups);
        Assert.Equal("Sheet A2", sheet.GroupName);
        Assert.Contains(sheet.Cells, cell => cell.InstanceId == "row-1#1" && cell.Row == 0 && cell.Column == 2);
        Assert.Contains(sheet.Cells, cell => cell.InstanceId == "row-2#1" && cell.Row == 1 && cell.Column == 0);
    }

    [Fact]
    public async Task Edge_assignments_can_ignore_or_create_boundary_panel_joints()
    {
        var service = new ExtrusionTakeoffService();
        var project = new Project
        {
            State = new ProjectState
            {
                Parts =
                [
                    CreatePart("row-1", "A-1", "A", 120m, 24m),
                    CreatePart("row-2", "A-2", "A", 60m, 36m)
                ],
                ExtrusionLayout = new ExtrusionLayoutState
                {
                    GroupingMode = ExtrusionGroupingModes.Group,
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
                            ],
                            EdgeAssignments =
                            [
                                new ExtrusionEdgeAssignment
                                {
                                    InstanceId = "row-1#1",
                                    Edge = ExtrusionEdgeNames.Right,
                                    IsIgnored = true
                                }
                            ],
                            JointAssignments =
                            [
                                new ExtrusionJointAssignment
                                {
                                    JointId = "row-1#1|top|boundary",
                                    FirstInstanceId = "row-1#1",
                                    SecondInstanceId = string.Empty,
                                    Edge = ExtrusionEdgeNames.Top,
                                    ExtrusionName = "Boundary Joint",
                                    IsEnabled = true
                                }
                            ]
                        }
                    ]
                }
            }
        };

        var report = await service.BuildReportAsync(new ExtrusionReportRequest { Project = project });

        Assert.DoesNotContain(report.Segments, segment => segment.Location == "A-1#1 / A-2#1");
        Assert.Contains(report.Segments, segment =>
            segment.Category == ExtrusionCategories.PanelToPanel &&
            segment.ExtrusionName == "Boundary Joint" &&
            segment.Location == "A-1#1 top" &&
            segment.LengthInches == 120m);
    }

    [Fact]
    public async Task Same_part_group_in_different_optimization_groups_remains_separate_and_filterable()
    {
        var first = CreatePart("row-first", "FIRST", "Elevation", 120m, 24m);
        var second = CreatePart("row-second", "SECOND", "Elevation", 60m, 36m);
        var project = new Project
        {
            State = new ProjectState
            {
                Parts = [second, first],
                OptimizationGroups =
                [
                    new OptimizationGroup { OptimizationGroupId = "second", Name = "Alpha", Order = 2, Parts = [second] },
                    new OptimizationGroup { OptimizationGroupId = "first", Name = "Zebra", Order = 1, Parts = [first] }
                ],
                ExtrusionLayout = new ExtrusionLayoutState
                {
                    GroupingMode = ExtrusionGroupingModes.Group,
                    Groups =
                    [
                        new ExtrusionGroupLayout
                        {
                            GroupName = "Elevation",
                            Rows = 1,
                            Columns = 2,
                            Cells =
                            [
                                new ExtrusionGridCell { InstanceId = "row-first#1", Row = 0, Column = 0 },
                                new ExtrusionGridCell { InstanceId = "row-second#1", Row = 0, Column = 1 }
                            ]
                        }
                    ]
                }
            }
        };
        var service = new ExtrusionTakeoffService();

        var layout = await service.BuildLayoutAsync(new ExtrusionLayoutRequest { Project = project });
        var report = await service.BuildReportAsync(new ExtrusionReportRequest { Project = project });

        Assert.Collection(
            layout.Groups,
            group =>
            {
                Assert.Equal("first", group.OptimizationGroupId);
                Assert.Equal("Elevation", group.GroupName);
                Assert.Equal("row-first#1", Assert.Single(group.Cells).InstanceId);
            },
            group =>
            {
                Assert.Equal("second", group.OptimizationGroupId);
                Assert.Equal("Elevation", group.GroupName);
                Assert.Equal("row-second#1", Assert.Single(group.Cells).InstanceId);
            });
        Assert.Collection(
            report.OptimizationGroups,
            group => Assert.Equal("first", group.OptimizationGroupId),
            group => Assert.Equal("second", group.OptimizationGroupId));
        Assert.Collection(
            report.Groups,
            group => Assert.Equal("first", group.OptimizationGroupId),
            group => Assert.Equal("second", group.OptimizationGroupId));
        Assert.All(report.Panels, panel => Assert.False(string.IsNullOrWhiteSpace(panel.OptimizationGroupId)));
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
