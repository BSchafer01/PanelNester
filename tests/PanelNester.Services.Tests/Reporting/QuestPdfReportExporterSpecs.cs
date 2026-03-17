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
                    Pm = "Bishop"
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
    public void Build_project_summary_reports_no_results_when_materials_have_no_renderable_layouts()
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

        Assert.Contains("Overall Status: No results", summary);
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
        var method = GetPrivateStaticMethod("BuildSheetSvg");
        return Assert.IsType<string>(method.Invoke(null, [sheet, size]));
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

    private static float[] ExtractFontSizes(string svg) =>
        Regex.Matches(svg, "font-size=\"(?<size>[0-9]+(?:\\.[0-9]+)?)\"", RegexOptions.CultureInvariant)
            .Select(match => float.Parse(match.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    private static MethodInfo GetPrivateStaticMethod(string name) =>
        typeof(QuestPdfReportExporter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find method '{name}'.");
}
