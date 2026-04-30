using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class ClosedXmlExcelReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ClosedXmlExcelReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_async_writes_a_single_summary_sheet_when_groups_are_absent()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "summary.xlsx");
        var exporter = new ClosedXmlExcelReportExporter();

        await exporter.ExportAsync(
            new ReportData
            {
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Baltic Birch 18mm",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 2,
                            TotalPlaced = 12,
                            TotalUnplaced = 1,
                            OverallUtilization = 78.5m
                        }
                    }
                ]
            },
            filePath);

        Assert.True(File.Exists(filePath));

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet("Summary");
        Assert.Equal("Material Summary", worksheet.Cell("A1").GetString());
        Assert.Equal("Material", worksheet.Cell("A3").GetString());
        Assert.Equal("Baltic Birch 18mm", worksheet.Cell("A4").GetString());
        Assert.Equal(2d, worksheet.Cell("B4").GetDouble());
        Assert.Equal(0.785d, worksheet.Cell("E4").GetDouble(), 3);
    }

    [Fact]
    public async Task Export_async_writes_one_sheet_per_group_when_group_summaries_exist()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "grouped-summary.xlsx");
        var exporter = new ClosedXmlExcelReportExporter();

        await exporter.ExportAsync(
            new ReportData
            {
                MaterialSummaryGroups =
                [
                    new ReportMaterialSummaryGroup
                    {
                        GroupName = "East",
                        Materials =
                        [
                            new ReportMaterialSummaryRow
                            {
                                MaterialName = "A1",
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Summary = new MaterialSummary
                                {
                                    TotalSheets = 10,
                                    TotalPlaced = 11,
                                    TotalUnplaced = 0,
                                    OverallUtilization = 98.3m
                                }
                            }
                        ]
                    },
                    new ReportMaterialSummaryGroup
                    {
                        GroupName = string.Empty,
                        Materials =
                        [
                            new ReportMaterialSummaryRow
                            {
                                MaterialName = "B1",
                                SheetLength = 59.5m,
                                SheetWidth = 15.5m,
                                Summary = new MaterialSummary
                                {
                                    TotalSheets = 2,
                                    TotalPlaced = 2,
                                    TotalUnplaced = 1,
                                    OverallUtilization = 88.8m
                                }
                            }
                        ]
                    }
                ],
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "A1",
                        SheetLength = 47.5m,
                        SheetWidth = 15.5m,
                        Sheets =
                        [
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-1",
                                SheetNumber = 1,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-1",
                                        SheetId = "sheet-1",
                                        PartId = "A1",
                                        Group = "East",
                                        Width = 47.5m,
                                        Height = 15.5m
                                    }
                                ]
                            },
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-2",
                                SheetNumber = 2,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-2",
                                        SheetId = "sheet-2",
                                        PartId = "A1#2",
                                        Group = "East",
                                        Width = 47.5m,
                                        Height = 15.5m
                                    }
                                ]
                            },
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-3",
                                SheetNumber = 3,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-3",
                                        SheetId = "sheet-3",
                                        PartId = "A1F1",
                                        Group = "East",
                                        X = 0m,
                                        Y = 0m,
                                        Width = 23.75m,
                                        Height = 15.5m
                                    }
                                ]
                            },
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-4",
                                SheetNumber = 4,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-4",
                                        SheetId = "sheet-4",
                                        PartId = "A1F1",
                                        Group = "East",
                                        X = 0m,
                                        Y = 0m,
                                        Width = 23.75m,
                                        Height = 15.5m
                                    },
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-5",
                                        SheetId = "sheet-4",
                                        PartId = "B1",
                                        Group = "West",
                                        Width = 10m,
                                        Height = 10m
                                    }
                                ]
                            },
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-5",
                                SheetNumber = 5,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-6",
                                        SheetId = "sheet-5",
                                        PartId = "A1R3",
                                        Group = "East",
                                        X = 0m,
                                        Y = 0m,
                                        Width = 23.75m,
                                        Height = 15.5m
                                    }
                                ]
                            },
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-6",
                                SheetNumber = 6,
                                SheetLength = 47.5m,
                                SheetWidth = 15.5m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-7",
                                        SheetId = "sheet-6",
                                        PartId = "AR4",
                                        Group = "East",
                                        X = 0m,
                                        Y = 0m,
                                        Width = 20m,
                                        Height = 10m
                                    }
                                ]
                            }
                        ]
                    }
                ]
            },
            filePath);

        Assert.True(File.Exists(filePath));

        using var workbook = new XLWorkbook(filePath);
        var east = workbook.Worksheet("East");
        var ungrouped = workbook.Worksheet("Ungrouped");

        Assert.Equal("East", east.Cell("A1").GetString());
        Assert.Equal("A1", east.Cell("A4").GetString());
        Assert.Equal("A1", east.Cell("A7").GetString());
        Assert.Equal("A1*", east.Cell("A9").GetString());
        Assert.Equal(2d, east.Cell("B9").GetDouble());
        Assert.Equal("A1", east.Cell("C9").GetString());
        Assert.Equal(1d, east.Cell("D9").GetDouble());
        Assert.Equal(0d, east.Cell("E9").GetDouble());
        Assert.Equal(1d, east.Cell("F9").GetDouble());
        Assert.Equal(0d, east.Cell("G9").GetDouble());
        Assert.Equal(0d, east.Cell("H9").GetDouble());
        Assert.Equal("A1#1", east.Cell("A10").GetString());
        Assert.Equal(2d, east.Cell("B10").GetDouble());
        Assert.Equal("A1F1", east.Cell("C10").GetString());
        Assert.Equal(1d, east.Cell("E10").GetDouble());
        Assert.Equal(1d, east.Cell("F10").GetDouble());
        Assert.Equal(2d, east.Cell("G10").GetDouble());
        Assert.Equal(2d, east.Cell("H10").GetDouble());
        Assert.Equal("A1#2", east.Cell("A11").GetString());
        Assert.Equal(1d, east.Cell("B11").GetDouble());
        Assert.Equal("A1R3", east.Cell("C11").GetString());
        Assert.Equal(1d, east.Cell("E11").GetDouble());
        Assert.Equal(1d, east.Cell("F11").GetDouble());
        Assert.Equal(1d, east.Cell("G11").GetDouble());
        Assert.Equal(1d, east.Cell("H11").GetDouble());
        Assert.Equal("A1#3", east.Cell("A12").GetString());
        Assert.Equal("AR4", east.Cell("C12").GetString());
        Assert.Equal(2d, east.Cell("E12").GetDouble());
        Assert.Equal(1d, east.Cell("F12").GetDouble());
        Assert.Equal(2d, east.Cell("G12").GetDouble());
        Assert.Equal(1d, east.Cell("H12").GetDouble());
        Assert.Equal("Ungrouped", ungrouped.Cell("A1").GetString());
        Assert.Equal("B1", ungrouped.Cell("A4").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
