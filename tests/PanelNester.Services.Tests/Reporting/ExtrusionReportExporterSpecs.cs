using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;
using UglyToad.PdfPig;

namespace PanelNester.Services.Tests.Reporting;

public sealed class ExtrusionReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.ExtrusionReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Excel_export_preserves_optimization_group_above_part_group()
    {
        Directory.CreateDirectory(_workspacePath);
        var filePath = Path.Combine(_workspacePath, "extrusions.xlsx");
        var length = new ExtrusionLengthSummary
        {
            Category = ExtrusionCategories.Edge,
            ExtrusionName = "Edge",
            TotalLinearFeet = 10m,
            SegmentCount = 2,
            StickLengthFeet = 20m,
            RequiredStickCount = 1
        };
        var report = new ExtrusionReportData
        {
            OverallLengths = [length],
            Groups =
            [
                new ExtrusionGroupSummary
                {
                    OptimizationGroupId = "first",
                    OptimizationGroupName = "First",
                    GroupName = "Elevation",
                    Lengths = [length]
                },
                new ExtrusionGroupSummary
                {
                    OptimizationGroupId = "second",
                    OptimizationGroupName = "Second",
                    GroupName = "Elevation",
                    Lengths = [length]
                }
            ]
        };

        await new ClosedXmlExtrusionReportExporter().ExportAsync(report, filePath);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet("By Group");
        Assert.Equal("Optimization Group", worksheet.Cell("A3").GetString());
        Assert.Equal("Part Group", worksheet.Cell("B3").GetString());
        Assert.Equal("First", worksheet.Cell("A4").GetString());
        Assert.Equal("Elevation", worksheet.Cell("B4").GetString());
        Assert.Equal("Second", worksheet.Cell("A5").GetString());
    }

    [Fact]
    public async Task Pdf_export_preserves_ordered_optimization_group_and_part_group_headings()
    {
        Directory.CreateDirectory(_workspacePath);
        var filePath = Path.Combine(_workspacePath, "extrusions.pdf");
        var length = new ExtrusionLengthSummary
        {
            Category = ExtrusionCategories.Edge,
            ExtrusionName = "Edge",
            TotalLinearFeet = 10m,
            SegmentCount = 2,
            StickLengthFeet = 20m,
            RequiredStickCount = 1
        };
        var firstPartGroup = new ExtrusionGroupSummary
        {
            OptimizationGroupId = "first",
            OptimizationGroupName = "Zebra",
            GroupName = "Faces",
            Lengths = [length]
        };
        var secondPartGroup = new ExtrusionGroupSummary
        {
            OptimizationGroupId = "second",
            OptimizationGroupName = "Alpha",
            GroupName = "Frames",
            Lengths = [length]
        };

        await new QuestPdfExtrusionReportExporter().ExportAsync(
            new ExtrusionReportData
            {
                OverallLengths = [length],
                Groups = [firstPartGroup, secondPartGroup],
                OptimizationGroups =
                [
                    new ExtrusionOptimizationGroupSummary
                    {
                        OptimizationGroupId = "first",
                        Name = "Zebra",
                        Order = 1,
                        OverallLengths = [length],
                        PartGroups = [firstPartGroup]
                    },
                    new ExtrusionOptimizationGroupSummary
                    {
                        OptimizationGroupId = "second",
                        Name = "Alpha",
                        Order = 2,
                        OverallLengths = [length],
                        PartGroups = [secondPartGroup]
                    }
                ],
                HasTakeoff = true
            },
            filePath);

        using var document = PdfDocument.Open(filePath);
        var words = document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text).ToArray();
        Assert.True(Array.IndexOf(words, "Zebra") < Array.IndexOf(words, "Alpha"));
        Assert.Contains("Faces", words);
        Assert.Contains("Frames", words);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
