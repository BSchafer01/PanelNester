using System.IO;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;
using QuestPDF.Infrastructure;

namespace PanelNester.Services.Tests.Reporting;

public sealed class QuestPdfReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.QuestPdfReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_async_writes_a_pdf_file_for_report_data()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "nesting-report.pdf");
        var exporter = new QuestPdfReportExporter();

        await exporter.ExportAsync(
            new ReportData
            {
                Settings = new ReportSettings
                {
                    CompanyName = "Northwind Fixtures",
                    ReportTitle = "Workshop Cabinets Nesting Report",
                    ProjectJobName = "Workshop Cabinets",
                    ProjectJobNumber = "PN-500",
                    ReportDate = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc),
                    Notes = "Customer-facing report."
                },
                ProjectMetadata = new ProjectMetadata
                {
                    ProjectName = "Workshop Cabinets",
                    CustomerName = "Northwind Fixtures",
                    Pm = "Bishop",
                    RequiredDate = new DateTime(2026, 03, 28, 0, 0, 0, DateTimeKind.Utc)
                },
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Baltic Birch 18mm",
                        MaterialId = "mat-birch",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        CostPerSheet = 120m,
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 1,
                            TotalPlaced = 2,
                            TotalUnplaced = 0,
                            OverallUtilization = 60m
                        },
                        Sheets =
                        [
                            new ReportSheetDiagram
                            {
                                SheetId = "sheet-1",
                                SheetNumber = 1,
                                SheetLength = 96m,
                                SheetWidth = 48m,
                                UtilizationPercent = 60m,
                                Placements =
                                [
                                    new NestPlacement
                                    {
                                        PlacementId = "placement-1",
                                        SheetId = "sheet-1",
                                        PartId = "B-001",
                                        X = 0m,
                                        Y = 0m,
                                        Width = 24m,
                                        Height = 12m
                                    }
                                ]
                            }
                        ]
                    }
                ],
                HasResults = true
            },
            filePath);

        Assert.True(File.Exists(filePath));
        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5)));
    }

    [Fact]
    public async Task Export_async_throws_for_invalid_file_path()
    {
        Directory.CreateDirectory(_workspacePath);

        var invalidChar = Path.GetInvalidFileNameChars().First();
        var invalidPath = Path.Combine(_workspacePath, $"report{invalidChar}bad.pdf");
        var exporter = new QuestPdfReportExporter();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            exporter.ExportAsync(CreateMinimalReport(), invalidPath));
    }

    [Fact]
    public async Task Export_async_honors_cancellation()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "cancelled-report.pdf");
        var exporter = new QuestPdfReportExporter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            exporter.ExportAsync(CreateMinimalReport(), filePath, cts.Token));

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task Export_async_writes_a_pdf_file_for_empty_report_data()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "empty-report.pdf");
        var exporter = new QuestPdfReportExporter();

        await exporter.ExportAsync(CreateMinimalReport(), filePath);

        Assert.True(File.Exists(filePath));
        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5)));
    }

    [Fact]
    public async Task Export_async_supports_grouped_material_summaries_that_span_multiple_pages()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "grouped-multipage-report.pdf");
        var exporter = new QuestPdfReportExporter();

        await exporter.ExportAsync(
            new ReportData
            {
                Settings = new ReportSettings
                {
                    ReportTitle = "Large Grouped Summary Report",
                    ProjectJobName = "Large Grouped Summary"
                },
                ProjectMetadata = new ProjectMetadata
                {
                    ProjectName = "Large Grouped Summary"
                },
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Baltic Birch 18mm",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 24,
                            TotalPlaced = 240,
                            TotalUnplaced = 0,
                            OverallUtilization = 72m
                        }
                    }
                ],
                MaterialSummaryGroups = Enumerable.Range(1, 14)
                    .Select(groupIndex =>
                        new ReportMaterialSummaryGroup
                        {
                            GroupName = $"Group {groupIndex:00}",
                            Materials = Enumerable.Range(1, 5)
                                .Select(materialIndex =>
                                    new ReportMaterialSummaryRow
                                    {
                                        MaterialName = $"Material {groupIndex:00}-{materialIndex:00}",
                                        SheetLength = 96m,
                                        SheetWidth = 48m,
                                        Summary = new MaterialSummary
                                        {
                                            TotalSheets = materialIndex,
                                            TotalPlaced = materialIndex * 10,
                                            TotalUnplaced = 0,
                                            OverallUtilization = 60m + materialIndex
                                        }
                                    })
                                .ToArray()
                        })
                    .ToArray()
            },
            filePath);

        Assert.True(File.Exists(filePath));
        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5)));
    }

    [Fact]
    public void Build_sheet_svg_includes_panel_labels_for_each_placement()
    {
        var svg = InvokeBuildSheetSvg(
            new ReportSheetDiagram
            {
                SheetId = "sheet-1",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "placement-2",
                        SheetId = "sheet-1",
                        PartId = "Panel-B",
                        X = 32m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    },
                    new NestPlacement
                    {
                        PlacementId = "placement-1",
                        SheetId = "sheet-1",
                        PartId = "Panel-A",
                        X = 0m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    }
                ]
            },
            new Size(320, 160));

        Assert.Contains(">Panel-A</text>", svg);
        Assert.Contains(">Panel-B</text>", svg);
        Assert.True(svg.IndexOf("Panel-A", StringComparison.Ordinal) < svg.IndexOf("Panel-B", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_sheet_svg_assigns_the_same_color_to_matching_panel_sizes_even_when_rotated()
    {
        var svg = InvokeBuildSheetSvg(
            new ReportSheetDiagram
            {
                SheetId = "sheet-sizes",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "placement-1",
                        SheetId = "sheet-sizes",
                        PartId = "Panel-A",
                        X = 0m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    },
                    new NestPlacement
                    {
                        PlacementId = "placement-2",
                        SheetId = "sheet-sizes",
                        PartId = "Panel-B",
                        X = 24m,
                        Y = 0m,
                        Width = 12m,
                        Height = 24m
                    },
                    new NestPlacement
                    {
                        PlacementId = "placement-3",
                        SheetId = "sheet-sizes",
                        PartId = "Panel-C",
                        X = 48m,
                        Y = 0m,
                        Width = 18m,
                        Height = 12m
                    }
                ]
            },
            new Size(320, 160));

        var panelColors = ExtractPanelColors(svg);
        Assert.Equal(3, panelColors.Length);
        Assert.Equal(panelColors[0], panelColors[1]);
        Assert.NotEqual(panelColors[0], panelColors[2]);
    }

    [Fact]
    public void Build_sheet_svg_shows_a_no_placements_empty_state()
    {
        var svg = InvokeBuildSheetSvg(
            new ReportSheetDiagram
            {
                SheetId = "sheet-empty",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m
            },
            new Size(320, 160));

        Assert.Contains("class=\"placement-empty-state\"", svg);
        Assert.Contains(">No placements</text>", svg);
    }

    [Fact]
    public void Build_sheet_svg_uses_minimum_strokes_and_callouts_for_dense_layouts()
    {
        var svg = InvokeBuildSheetSvg(
            new ReportSheetDiagram
            {
                SheetId = "sheet-dense",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements = Enumerable.Range(0, 24)
                    .Select(index => new NestPlacement
                    {
                        PlacementId = $"placement-{index + 1}",
                        SheetId = "sheet-dense",
                        PartId = $"Panel-{index + 1:00}",
                        X = (index % 8) * 4m,
                        Y = (index / 8) * 3m,
                        Width = 4m,
                        Height = 3m
                    })
                    .ToArray()
            },
            new Size(320, 160));

        Assert.Contains("class=\"placement-callout-badge\"", svg);
        Assert.Contains("class=\"placement-callout-label\"", svg);
        Assert.DoesNotContain("stroke-width=\"0.8\"", svg);

        var fontSizes = ExtractFontSizes(svg);
        Assert.NotEmpty(fontSizes);
        Assert.All(fontSizes, size => Assert.True(size >= 6f, $"Expected font-size >= 6 but found {size}."));
    }

    [Fact]
    public void Build_placement_summary_prefixes_group_when_present()
    {
        var summary = InvokeBuildPlacementSummary(
            new ReportSheetDiagram
            {
                SheetId = "sheet-grouped",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "placement-1",
                        SheetId = "sheet-grouped",
                        PartId = "Panel-A",
                        Group = "Casework",
                        X = 0m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    }
                ]
            });

        Assert.Contains("1. [Casework] Panel-A: 24\" × 12\" at (0\", 0\")", summary);
    }

    [Fact]
    public void Build_placement_summary_omits_group_markup_for_ungrouped_placements()
    {
        var summary = InvokeBuildPlacementSummary(
            new ReportSheetDiagram
            {
                SheetId = "sheet-ungrouped",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "placement-1",
                        SheetId = "sheet-ungrouped",
                        PartId = "Panel-A",
                        Group = "   ",
                        X = 0m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    }
                ]
            });

        Assert.Contains("1. Panel-A: 24\" × 12\" at (0\", 0\")", summary);
        Assert.DoesNotContain("[", summary);
    }

    [Fact]
    public void Build_placement_summary_distinguishes_grouped_from_ungrouped_panels_on_the_same_sheet()
    {
        var summary = InvokeBuildPlacementSummary(
            new ReportSheetDiagram
            {
                SheetId = "sheet-mixed",
                SheetNumber = 1,
                SheetLength = 96m,
                SheetWidth = 48m,
                Placements =
                [
                    new NestPlacement
                    {
                        PlacementId = "placement-2",
                        SheetId = "sheet-mixed",
                        PartId = "Ungrouped-B",
                        Group = null,
                        X = 24m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    },
                    new NestPlacement
                    {
                        PlacementId = "placement-1",
                        SheetId = "sheet-mixed",
                        PartId = "Grouped-A",
                        Group = "Casework",
                        X = 0m,
                        Y = 0m,
                        Width = 24m,
                        Height = 12m
                    }
                ]
            });

        var lines = summary.Split(Environment.NewLine);
        Assert.Equal("1. [Casework] Grouped-A: 24\" × 12\" at (0\", 0\")", lines[0]);
        Assert.Equal("2. Ungrouped-B: 24\" × 12\" at (24\", 0\")", lines[1]);
        Assert.DoesNotContain("[", lines[1]);
    }

    [Fact]
    public void Format_percent_treats_utilization_as_a_percent_value()
    {
        var formatted = InvokeFormatPercent(60m);

        Assert.Equal("60.0%", formatted);
    }

    [Fact]
    public void Format_percent_handles_zero_utilization_cleanly()
    {
        var formatted = InvokeFormatPercent(0m);

        Assert.Equal("0.0%", formatted);
    }

    [Fact]
    public void Build_project_summary_omits_overall_status_from_the_summary_copy()
    {
        var summary = InvokeBuildProjectSummary(
            new ReportData
            {
                ProjectMetadata = new ProjectMetadata
                {
                    ProjectName = "Empty Export",
                    CustomerName = "Northwind Fixtures"
                },
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Baltic Birch 18mm",
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 0,
                            TotalPlaced = 0,
                            OverallUtilization = 0m
                        }
                    }
                ],
                HasResults = true
            },
            hasRenderableLayouts: false);

        Assert.DoesNotContain("Overall Status", summary);
    }

    [Fact]
    public void Build_project_summary_includes_report_date_when_present()
    {
        var summary = InvokeBuildProjectSummary(
            new ReportData
            {
                Settings = new ReportSettings
                {
                    ReportDate = new DateTime(2026, 04, 02, 0, 0, 0, DateTimeKind.Utc)
                },
                ProjectMetadata = new ProjectMetadata
                {
                    CustomerName = "Northwind Fixtures",
                    RequiredDate = new DateTime(2026, 04, 16, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            hasRenderableLayouts: true);

        Assert.Contains("Required Date: 2026-04-16", summary);
        Assert.Contains("Report Date: 2026-04-02", summary);
    }

    [Fact]
    public void Build_material_summary_consolidates_each_material_into_a_single_line()
    {
        var summary = InvokeBuildMaterialSummary(
            new ReportData
            {
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Baltic Birch 18mm",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        CostPerSheet = 120m,
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 2,
                            TotalPlaced = 12,
                            TotalUnplaced = 1,
                            OverallUtilization = 78.5m
                        }
                    },
                    new ReportMaterialSection
                    {
                        MaterialName = "Maple Ply 18mm",
                        SheetLength = 120m,
                        SheetWidth = 60m,
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 1,
                            TotalPlaced = 4,
                            TotalUnplaced = 0,
                            OverallUtilization = 61m
                        }
                    }
                ]
            });

        Assert.Contains("Baltic Birch 18mm  •  Sheets: 2  •  Placed: 12  •  Unplaced: 1  •  Utilization: 78.5%  •  Sheet Size: 96\" × 48\"", summary);
        Assert.DoesNotContain("Cost/Sheet", summary);
        Assert.Contains("Maple Ply 18mm  •  Sheets: 1  •  Placed: 4  •  Unplaced: 0  •  Utilization: 61.0%  •  Sheet Size: 120\" × 60\"", summary);
    }

    [Fact]
    public void Build_material_summary_renders_group_headers_when_grouped_summaries_are_available()
    {
        var summary = InvokeBuildMaterialSummary(
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
                            TotalSheets = 3,
                            TotalPlaced = 12,
                            TotalUnplaced = 1,
                            OverallUtilization = 70m
                        }
                    }
                ],
                MaterialSummaryGroups =
                [
                    new ReportMaterialSummaryGroup
                    {
                        GroupName = "Casework",
                        Materials =
                        [
                            new ReportMaterialSummaryRow
                            {
                                MaterialName = "Baltic Birch 18mm",
                                SheetLength = 96m,
                                SheetWidth = 48m,
                                Summary = new MaterialSummary
                                {
                                    TotalSheets = 2,
                                    TotalPlaced = 8,
                                    TotalUnplaced = 0,
                                    OverallUtilization = 75m
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
                                MaterialName = "Baltic Birch 18mm",
                                SheetLength = 96m,
                                SheetWidth = 48m,
                                Summary = new MaterialSummary
                                {
                                    TotalSheets = 0,
                                    TotalPlaced = 0,
                                    TotalUnplaced = 1,
                                    OverallUtilization = 0m
                                }
                            }
                        ]
                    }
                ]
            });

        Assert.Contains($"Casework{Environment.NewLine}Baltic Birch 18mm  •  Sheets: 2  •  Placed: 8  •  Unplaced: 0  •  Utilization: 75.0%  •  Sheet Size: 96\" × 48\"", summary);
        Assert.Contains($"Ungrouped{Environment.NewLine}Baltic Birch 18mm  •  Sheets: 0  •  Placed: 0  •  Unplaced: 1  •  Utilization: 0.0%  •  Sheet Size: 96\" × 48\"", summary);
    }

    [Fact]
    public void Material_summary_pdf_sections_use_total_then_location_contract()
    {
        Assert.Equal("Total Material Summary", GetPrivateStaticStringField("TotalMaterialSummaryTitle"));
        Assert.Equal("Material Summary by Location", GetPrivateStaticStringField("MaterialSummaryByLocationTitle"));

        var totalColumns = GetPrivateStaticField<string[]>("TotalMaterialSummaryColumnLabels");

        Assert.Equal(["Material", "Sheets", "Placed", "Utilization", "Sheet Size"], totalColumns);
        Assert.DoesNotContain("Unplaced", totalColumns);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static ReportData CreateMinimalReport() =>
        new()
        {
            ProjectMetadata = new ProjectMetadata
            {
                ProjectName = "Baseline Report"
            }
        };

    private static string InvokeBuildSheetSvg(ReportSheetDiagram sheet, Size size)
    {
        var placementColors = InvokeBuildPlacementColorLookup(
            new ReportData
            {
                Materials =
                [
                    new ReportMaterialSection
                    {
                        MaterialName = "Test Material",
                        Sheets = [sheet]
                    }
                ]
            });
        var method = GetPrivateStaticMethod("BuildSheetSvg");
        return Assert.IsType<string>(method.Invoke(null, [sheet, size, placementColors]));
    }

    private static string InvokeBuildPlacementSummary(ReportSheetDiagram sheet)
    {
        var method = GetPrivateStaticMethod("BuildPlacementSummary");
        return Assert.IsType<string>(method.Invoke(null, [sheet]));
    }

    private static string InvokeFormatPercent(decimal value)
    {
        var method = GetPrivateStaticMethod("FormatPercent");
        return Assert.IsType<string>(method.Invoke(null, [value]));
    }

    private static string InvokeBuildProjectSummary(ReportData report, bool hasRenderableLayouts)
    {
        var method = GetPrivateStaticMethod("BuildProjectSummary");
        return Assert.IsType<string>(method.Invoke(null, [report, hasRenderableLayouts]));
    }

    private static string InvokeBuildMaterialSummary(ReportData report)
    {
        var method = GetPrivateStaticMethod("BuildMaterialSummary");
        return Assert.IsType<string>(method.Invoke(null, [report]));
    }

    private static IReadOnlyDictionary<string, string> InvokeBuildPlacementColorLookup(ReportData report)
    {
        var method = GetPrivateStaticMethod("BuildPlacementColorLookup");
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(method.Invoke(null, [report]));
    }

    private static float[] ExtractFontSizes(string svg) =>
        Regex.Matches(svg, "font-size=\"(?<size>[0-9]+(?:\\.[0-9]+)?)\"", RegexOptions.CultureInvariant)
            .Select(match => float.Parse(match.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    private static string[] ExtractPanelColors(string svg) =>
        Regex.Matches(svg, "class=\"placement-panel\"[^>]*fill=\"(?<color>#[0-9A-Fa-f]{6})\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["color"].Value)
            .ToArray();

    private static MethodInfo GetPrivateStaticMethod(string name) =>
        typeof(QuestPdfReportExporter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find method '{name}'.");

    private static string GetPrivateStaticStringField(string name)
    {
        var field = GetPrivateStaticFieldInfo(name);
        var value = field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null);
        return Assert.IsType<string>(value);
    }

    private static T GetPrivateStaticField<T>(string name) =>
        Assert.IsType<T>(GetPrivateStaticFieldInfo(name).GetValue(null));

    private static FieldInfo GetPrivateStaticFieldInfo(string name) =>
        typeof(QuestPdfReportExporter).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find field '{name}'.");
}
