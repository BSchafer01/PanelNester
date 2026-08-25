using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class StiffenerTakeoffServiceSpecs
{
    [Fact]
    public async Task Build_async_returns_an_empty_report_when_stiffener_takeoff_is_disabled()
    {
        var service = new StiffenerTakeoffService();

        var report = await service.BuildAsync(
            new StiffenerTakeoffRequest
            {
                Project = new Project
                {
                    Metadata = new ProjectMetadata
                    {
                        ProjectName = "Disabled Takeoff"
                    },
                    Settings = new ProjectSettings
                    {
                        StiffenerTakeoff = new StiffenerTakeoffSettings
                        {
                            Enabled = false
                        }
                    },
                    State = new ProjectState
                    {
                        Parts =
                        [
                            new PartRow
                            {
                                RowId = "row-1",
                                ImportedId = "A-100",
                                Length = 120m,
                                Width = 48m,
                                Quantity = 1,
                                MaterialName = "Baltic Birch 18mm",
                                ValidationStatus = ValidationStatuses.Valid
                            }
                        ]
                    }
                }
            });

        Assert.False(report.HasTakeoff);
        Assert.Empty(report.OverallLengths);
        Assert.Empty(report.Materials);
        Assert.Equal("Disabled Takeoff", report.ProjectMetadata.ProjectName);
        Assert.False(report.Settings.Enabled);
    }

    [Fact]
    public async Task Build_async_applies_configurable_thresholds_rounding_grouping_and_stock_optimization()
    {
        var service = new StiffenerTakeoffService();

        var report = await service.BuildAsync(
            new StiffenerTakeoffRequest
            {
                Project = new Project
                {
                    Metadata = new ProjectMetadata
                    {
                        ProjectName = "Configurable Takeoff"
                    },
                    Settings = new ProjectSettings
                    {
                        StiffenerTakeoff = new StiffenerTakeoffSettings
                        {
                            Enabled = true,
                            MinimumLengthInches = 40m,
                            MinimumWidthInches = 36m,
                            WidthDeductionInches = 3.5m,
                            StockLengthFeet = 10m
                        }
                    },
                    State = new ProjectState
                    {
                        Parts =
                        [
                            new PartRow
                            {
                                RowId = "row-1",
                                ImportedId = "A-100",
                                Length = 40m,
                                Width = 40m,
                                Quantity = 2,
                                MaterialName = " Baltic Birch 18mm ",
                                ValidationStatus = ValidationStatuses.Valid
                            },
                            new PartRow
                            {
                                RowId = "row-2",
                                ImportedId = "A-101",
                                Length = 64m,
                                Width = 60m,
                                Quantity = 1,
                                MaterialName = "Baltic Birch 18mm",
                                ValidationStatus = ValidationStatuses.Valid
                            },
                            new PartRow
                            {
                                RowId = "row-3",
                                ImportedId = "A-102",
                                Length = 88m,
                                Width = 50m,
                                Quantity = 1,
                                MaterialName = "   ",
                                ValidationStatus = ValidationStatuses.Valid
                            },
                            new PartRow
                            {
                                RowId = "row-4",
                                ImportedId = "A-103",
                                Length = 39m,
                                Width = 100m,
                                Quantity = 5,
                                MaterialName = "Maple Ply 18mm",
                                ValidationStatus = ValidationStatuses.Valid
                            },
                            new PartRow
                            {
                                RowId = "row-5",
                                ImportedId = "A-104",
                                Length = 100m,
                                Width = 35.9m,
                                Quantity = 1,
                                MaterialName = "Maple Ply 18mm",
                                ValidationStatus = ValidationStatuses.Valid
                            },
                            new PartRow
                            {
                                RowId = "row-6",
                                ImportedId = "A-105",
                                Length = 100m,
                                Width = 42m,
                                Quantity = 1,
                                MaterialName = "Maple Ply 18mm",
                                ValidationStatus = ValidationStatuses.Error
                            }
                        ]
                    }
                }
            });

        Assert.True(report.HasTakeoff);
        Assert.Equal(3, report.OverallLengths.Count);
        Assert.Equal(["S37", "S47", "S57"], report.OverallLengths.Select(length => length.Label).ToArray());
        Assert.Equal([2, 3, 2], report.OverallLengths.Select(length => length.PieceCount).ToArray());

        Assert.Equal(4, report.OverallSummary.EligiblePanelCount);
        Assert.Equal(7, report.OverallSummary.TotalStiffenerCount);
        Assert.Equal(329m / 12m, report.OverallSummary.TotalLinearFeet);
        Assert.Equal(10m, report.OverallSummary.StockLengthFeet);
        Assert.Equal(4, report.OverallSummary.RequiredStockCount);
        Assert.Empty(report.Materials);
    }

    [Fact]
    public async Task Build_async_uses_project_kerf_width_when_estimating_required_stock_count()
    {
        var service = new StiffenerTakeoffService();

        var report = await service.BuildAsync(
            new StiffenerTakeoffRequest
            {
                Project = new Project
                {
                    Settings = new ProjectSettings
                    {
                        KerfWidth = 0.0625m,
                        StiffenerTakeoff = new StiffenerTakeoffSettings
                        {
                            Enabled = true,
                            MinimumLengthInches = 1m,
                            MinimumWidthInches = 1m,
                            WidthDeductionInches = 0m,
                            StockLengthFeet = 20m
                        }
                    },
                    State = new ProjectState
                    {
                        Parts =
                        [
                            new PartRow
                            {
                                RowId = "row-1",
                                ImportedId = "S-100",
                                Length = 10m,
                                Width = 120m,
                                Quantity = 2,
                                MaterialName = "Test Material",
                                ValidationStatus = ValidationStatuses.Valid
                            }
                        ]
                    }
                }
            });

        Assert.True(report.HasTakeoff);
        Assert.Equal(2, report.OverallSummary.TotalStiffenerCount);
        Assert.Equal(2, report.OverallSummary.RequiredStockCount);
    }

    [Fact]
    public async Task Build_async_keeps_project_totals_and_adds_ordered_optimization_group_breakdowns()
    {
        var first = CreateEligiblePart("row-first", 1);
        var second = CreateEligiblePart("row-second", 2);
        var project = new Project
        {
            Settings = new ProjectSettings
            {
                StiffenerTakeoff = new StiffenerTakeoffSettings
                {
                    Enabled = true,
                    MinimumLengthInches = 40m,
                    MinimumWidthInches = 36m,
                    WidthDeductionInches = 0m,
                    StockLengthFeet = 20m
                }
            },
            State = new ProjectState
            {
                Parts = [second, first],
                OptimizationGroups =
                [
                    new OptimizationGroup { OptimizationGroupId = "second", Name = "Second", Order = 2, Parts = [second] },
                    new OptimizationGroup { OptimizationGroupId = "first", Name = "First", Order = 1, Parts = [first] }
                ]
            }
        };

        var report = await new StiffenerTakeoffService().BuildAsync(new StiffenerTakeoffRequest { Project = project });

        Assert.Equal(3, report.OverallSummary.EligiblePanelCount);
        Assert.Collection(
            report.OptimizationGroups,
            group =>
            {
                Assert.Equal("first", group.OptimizationGroupId);
                Assert.Equal(1, group.Summary.EligiblePanelCount);
            },
            group =>
            {
                Assert.Equal("second", group.OptimizationGroupId);
                Assert.Equal(2, group.Summary.EligiblePanelCount);
            });
    }

    private static PartRow CreateEligiblePart(string rowId, int quantity) =>
        new()
        {
            RowId = rowId,
            ImportedId = rowId,
            Length = 40m,
            Width = 48m,
            Quantity = quantity,
            MaterialName = "ACM",
            ValidationStatus = ValidationStatuses.Valid
        };
}
