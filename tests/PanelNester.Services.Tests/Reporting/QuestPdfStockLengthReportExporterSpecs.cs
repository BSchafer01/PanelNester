using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace PanelNester.Services.Tests.Reporting;

public sealed class QuestPdfStockLengthReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.QuestPdfStockLengthReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_async_renders_scoped_stock_items_cut_sequences_states_and_unplaced_work()
    {
        Directory.CreateDirectory(_workspacePath);
        var filePath = Path.Combine(_workspacePath, "stock-length-report.pdf");
        var stockItems = Enumerable.Range(1, 45)
            .Select(number => new StockLengthReportStockItem
            {
                StockItemNumber = number,
                StockLength = 120m,
                PieceLength = 24.125m,
                Remainder = 95.875m,
                UtilizationPercent = 20.104m,
                CutSequence =
                [
                    new StockLengthReportPieceInstance
                    {
                        PieceInstanceId = $"piece-{number}",
                        RequiredPieceId = "required-1",
                        QuantityInstance = number,
                        Sequence = 1,
                        PartNumber = $"PN-{number:00}",
                        PartName = "Mullion",
                        ProfileNumber = "P-100",
                        Finish = "Clear",
                        Length = 24.125m,
                        SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = number + 10 }]
                    }
                ]
            })
            .ToArray();
        var summary = new StockLengthReportSummary
        {
            AcceptedPieceInstanceCount = stockItems.Length + 1,
            PlacedPieceInstanceCount = stockItems.Length,
            UnplacedPieceInstanceCount = 1,
            StockLength = stockItems.Sum(item => item.StockLength),
            PieceLength = stockItems.Sum(item => item.PieceLength),
            Remainder = stockItems.Sum(item => item.Remainder),
            UtilizationPercent = 20.104m
        };
        var unplaced = new StockLengthReportUnplacedPieceInstance
        {
            OptimizationGroupId = "north",
            OptimizationGroupName = "North Frames",
            ProfileNumber = "P-100",
            Finish = "Clear",
            State = StockLengthReportState.Partial,
            PieceInstance = new StockLengthReportPieceInstance
            {
                PieceInstanceId = "piece-unplaced",
                RequiredPieceId = "required-2",
                QuantityInstance = 1,
                PartNumber = "PN-LONG",
                PartName = "Header",
                ProfileNumber = "P-100",
                Finish = "Clear",
                Length = 130m,
                SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = 99 }]
            },
            ReasonCode = "too-long",
            ReasonDescription = "Piece exceeds the Stock Length."
        };

        await new QuestPdfStockLengthReportExporter().ExportAsync(
            new StockLengthReportData
            {
                Settings = new ReportSettings { CompanyName = "Northwind", ReportTitle = "Shop Cut Report" },
                ProjectMetadata = new ProjectMetadata { ProjectName = "Storefront Frames", ProjectNumber = "PN-500" },
                InchDisplayFormat = InchDisplayFormat.Fractional16,
                Summary = summary,
                OptimizationGroups =
                [
                    new StockLengthReportOptimizationGroup
                    {
                        OptimizationGroupId = "north",
                        Name = "North Frames",
                        State = StockLengthReportState.Partial,
                        Summary = summary,
                        StockGroups =
                        [
                            new StockLengthReportStockGroup
                            {
                                ProfileNumber = "P-100",
                                Finish = "Clear",
                                State = StockLengthReportState.Partial,
                                Summary = summary,
                                StockItems = stockItems
                            }
                        ]
                    },
                    new StockLengthReportOptimizationGroup
                    {
                        OptimizationGroupId = "empty",
                        Name = "Empty Group",
                        State = StockLengthReportState.Empty
                    }
                ],
                UnplacedPieceInstances = [unplaced],
            },
            filePath);

        using var document = PdfDocument.Open(filePath);
        Assert.True(document.NumberOfPages > 1);
        var text = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.Contains("Shop Cut Report", text);
        Assert.Contains("UNPLACED PIECE INSTANCES", text);
        Assert.Contains("Piece exceeds the Stock Length", text);
        Assert.Contains("Empty Group", text);
        Assert.Contains("Empty", text);
        Assert.Contains("Stock Item 45", text);
        Assert.Contains("Cut Sequence", text);
        Assert.Contains("PN-45", text);
        Assert.Contains("Lengths!55", text);
        Assert.Contains("24 1/8", text);

        var renderedPages = QuestPdfStockLengthReportExporter.CreateDocument(
            new StockLengthReportData
            {
                ProjectMetadata = new ProjectMetadata { ProjectName = "Rendered multi-group report" },
                Summary = summary,
                OptimizationGroups =
                [
                    new StockLengthReportOptimizationGroup
                    {
                        Name = "First Group",
                        State = StockLengthReportState.Partial,
                        Summary = summary,
                        StockGroups =
                        [
                            new StockLengthReportStockGroup
                            {
                                ProfileNumber = "P-100",
                                State = StockLengthReportState.Partial,
                                Summary = summary,
                                StockItems = stockItems
                            }
                        ]
                    },
                    new StockLengthReportOptimizationGroup
                    {
                        Name = "Second Group",
                        Order = 1,
                        State = StockLengthReportState.NeedsGeneration,
                        Summary = new StockLengthReportSummary { AcceptedPieceInstanceCount = 3 }
                    }
                ],
                UnplacedPieceInstances = [unplaced]
            })
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 96 })
            .ToArray();
        Assert.True(renderedPages.Length > 1);
        Assert.All(renderedPages, AssertPngPageHasReadableDimensions);
    }

    [Fact]
    public async Task Export_async_paginates_one_long_cut_sequence_without_clipping_the_last_row()
    {
        Directory.CreateDirectory(_workspacePath);
        var filePath = Path.Combine(_workspacePath, "long-cut-sequence.pdf");
        var pieces = Enumerable.Range(1, 80).Select(sequence => new StockLengthReportPieceInstance
        {
            PieceInstanceId = $"piece-{sequence}",
            RequiredPieceId = $"required-{sequence}",
            QuantityInstance = 1,
            Sequence = sequence,
            PartNumber = $"PN-{sequence:00}",
            PartName = "Repeated rail",
            ProfileNumber = "P-100",
            Finish = "Clear",
            Length = 1m,
            SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = sequence + 1 }]
        }).ToArray();
        var summary = new StockLengthReportSummary
        {
            AcceptedPieceInstanceCount = pieces.Length,
            PlacedPieceInstanceCount = pieces.Length,
            StockLength = 120m,
            PieceLength = 80m,
            SawLoss = 7.9m,
            Remainder = 32.1m,
            UtilizationPercent = 66.67m
        };
        await new QuestPdfStockLengthReportExporter().ExportAsync(
            new StockLengthReportData
            {
                ProjectMetadata = new ProjectMetadata { ProjectName = "Long Sequence" },
                Summary = summary,
                OptimizationGroups =
                [
                    new StockLengthReportOptimizationGroup
                    {
                        Name = "Frames",
                        State = StockLengthReportState.Complete,
                        Summary = summary,
                        StockGroups =
                        [
                            new StockLengthReportStockGroup
                            {
                                ProfileNumber = "P-100",
                                Finish = "Clear",
                                State = StockLengthReportState.Complete,
                                Summary = summary,
                                StockItems =
                                [
                                    new StockLengthReportStockItem
                                    {
                                        StockItemNumber = 1,
                                        StockLength = 120m,
                                        PieceLength = 80m,
                                        SawLoss = 7.9m,
                                        Remainder = 32.1m,
                                        UtilizationPercent = 66.67m,
                                        CutSequence = pieces
                                    }
                                ]
                            }
                        ]
                    }
                ],
            },
            filePath);

        using var document = PdfDocument.Open(filePath);
        Assert.True(document.NumberOfPages > 1);
        var text = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.Contains("PN-80", text);
        Assert.Contains("Lengths!81", text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    private static void AssertPngPageHasReadableDimensions(byte[] page)
    {
        Assert.True(page.Length > 24);
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], page[..8]);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(page.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(page.AsSpan(20, 4));
        Assert.True(width >= 700, $"Rendered page width was only {width}px.");
        Assert.True(height >= 900, $"Rendered page height was only {height}px.");
    }
}
