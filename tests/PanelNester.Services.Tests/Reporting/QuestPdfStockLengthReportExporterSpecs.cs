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
        var logoPath = Path.Combine(_workspacePath, "company-logo.png");
        await File.WriteAllBytesAsync(
            logoPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));
        var stockItems = Enumerable.Range(1, 45)
            .Select(number => new StockLengthReportStockItem
            {
                StockItemNumber = number,
                Kind = number == 45 ? StockItemKind.Oversized : StockItemKind.Regular,
                StockLength = number == 45 ? 144m : 120m,
                PieceLength = number == 45 ? 24.125m : 36.125m,
                Remainder = number == 45 ? 119.875m : 83.875m,
                UtilizationPercent = number == 45 ? 16.753m : 30.104m,
                CutSequence =
                [
                    new StockLengthReportPieceInstance
                    {
                        PieceInstanceId = $"piece-{number}",
                        RequiredPieceId = "required-1",
                        QuantityInstance = number,
                        Sequence = 1,
                        PartNumber = "PN-MULLION",
                        PartName = "Mullion",
                        ProfileNumber = "P-100",
                        Finish = "Clear",
                        Length = 24.125m,
                        SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = number + 10 }]
                    },
                    .. number == 45
                        ? Array.Empty<StockLengthReportPieceInstance>()
                        :
                        [
                            new StockLengthReportPieceInstance
                            {
                                PieceInstanceId = $"short-piece-{number}",
                                RequiredPieceId = "required-short",
                                QuantityInstance = number,
                                Sequence = 2,
                                PartNumber = "PN-SHORT",
                                PartName = "Short Mullion",
                                ProfileNumber = "P-100",
                                Finish = "Clear",
                                Length = 12m,
                                SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = number + 100 }]
                            }
                        ]
                ]
            })
            .ToArray();
        var summary = new StockLengthReportSummary
        {
            AcceptedPieceInstanceCount = stockItems.Sum(item => item.CutSequence.Count) + 1,
            PlacedPieceInstanceCount = stockItems.Sum(item => item.CutSequence.Count),
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

        var report = new StockLengthReportData
        {
            CompanyLogoPath = logoPath,
            Settings = new ReportSettings { CompanyName = "Northwind", ReportTitle = "Legacy Nesting Report" },
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
                    Order = 1,
                    State = StockLengthReportState.Empty
                },
                new StockLengthReportOptimizationGroup
                {
                    OptimizationGroupId = "error",
                    Name = "Errored Group",
                    Order = 2,
                    State = StockLengthReportState.ApplicationError,
                    FailureMessage = "Cut Plan generation failed unexpectedly."
                }
            ],
            UnplacedPieceInstances = [unplaced],
        };

        var requirementRows = QuestPdfStockLengthReportExporter.BuildStockRequirementRows(report);
        Assert.Collection(
            requirementRows,
            row => Assert.Equal(("P-100", "Clear", StockItemKind.Regular, 120m, 44),
                (row.ProfileNumber, row.Finish, row.Kind, row.StockLength, row.Count)),
            row => Assert.Equal(("P-100", "Clear", StockItemKind.Oversized, 144m, 1),
                (row.ProfileNumber, row.Finish, row.Kind, row.StockLength, row.Count)));

        var cutRows = QuestPdfStockLengthReportExporter.BuildCutRequirementRows(stockItems);
        Assert.Collection(
            cutRows,
            row => Assert.Equal(("PN-MULLION", "Mullion", 24.125m, 45),
                (row.PartNumber, row.PartName, row.Length, row.Quantity)),
            row => Assert.Equal(("PN-SHORT", "Short Mullion", 12m, 44),
                (row.PartNumber, row.PartName, row.Length, row.Quantity)));

        await new QuestPdfStockLengthReportExporter().ExportAsync(report, filePath);

        using var document = PdfDocument.Open(filePath);
        Assert.True(document.NumberOfPages > 1);
        var pages = document.GetPages().ToArray();
        var pageTexts = pages.Select(ExtractPageText).ToArray();
        var text = string.Join("\n", pageTexts);
        Assert.DoesNotContain("Legacy Nesting Report", text);
        Assert.All(pageTexts, pageText => Assert.Contains("Storefront Frames Stock Length Report", pageText));
        Assert.All(pages, page => Assert.NotEmpty(page.GetImages()));
        Assert.Contains("Overall Stock Requirements", text);
        Assert.Contains("Piece exceeds the Stock Length", text);
        Assert.Contains("Empty Group", text);
        Assert.Contains("Empty", text);
        Assert.Contains("Errored Group", text);
        Assert.Contains("Cut Requirements", text);
        Assert.Contains("Required Stock: 44 Regular Stock Items", text);
        Assert.Contains("Required Stock: 1 Oversized Stock Item", text);
        Assert.Contains("Cut Maps", text);
        Assert.Contains("Stock Item 45", text);
        Assert.Contains("Oversized Stock Item 45", text);
        Assert.Contains("Cut Sequence", text);
        Assert.Contains("PN-MULLION", text);
        Assert.Contains("Lengths!55", text);
        Assert.Contains("24 1/8", text);
        Assert.True(
            text.IndexOf("Piece exceeds the Stock Length", StringComparison.Ordinal) <
            text.IndexOf("Cut Requirements", StringComparison.Ordinal));

        var cutRequirementsStart = text.IndexOf("Cut Requirements", StringComparison.Ordinal);
        var cutMapsStart = text.IndexOf("Cut Maps", StringComparison.Ordinal);
        Assert.DoesNotContain("Source References", text[cutRequirementsStart..cutMapsStart]);

        var cutMapsPage = Array.FindIndex(pageTexts, pageText => pageText.Contains("Cut Maps", StringComparison.Ordinal));
        Assert.True(cutMapsPage > 0);
        Assert.DoesNotContain("Cut Requirements", pageTexts[cutMapsPage]);

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
    public void Presentation_rows_keep_stock_groups_and_lengths_separate_and_sort_cut_parts_naturally()
    {
        var regular = StockItem(1, StockItemKind.Regular, 120m, "PN-10", "Rail", 20m, "Lengths", 10);
        var oversized = StockItem(2, StockItemKind.Oversized, 144m, "PN-2", "Rail", 30m, "Lengths", 11);
        var matchingCut = StockItem(3, StockItemKind.Regular, 120m, " pn-2 ", " rail ", 30m, "Lengths", 12);
        var differentLength = StockItem(4, StockItemKind.Regular, 120m, "PN-2", "Rail", 32m, "Lengths", 13);
        var report = new StockLengthReportData
        {
            OptimizationGroups =
            [
                new StockLengthReportOptimizationGroup
                {
                    Name = "Frames",
                    StockGroups =
                    [
                        new StockLengthReportStockGroup
                        {
                            ProfileNumber = "P-100",
                            Finish = "Clear",
                            StockItems = [regular, oversized]
                        },
                        new StockLengthReportStockGroup
                        {
                            ProfileNumber = "P-100",
                            Finish = "Bronze",
                            StockItems = [matchingCut]
                        }
                    ]
                }
            ]
        };

        var requirementRows = QuestPdfStockLengthReportExporter.BuildStockRequirementRows(report);
        Assert.Collection(
            requirementRows,
            row => Assert.Equal(("P-100", "Clear", StockItemKind.Regular, 120m, 1),
                (row.ProfileNumber, row.Finish, row.Kind, row.StockLength, row.Count)),
            row => Assert.Equal(("P-100", "Clear", StockItemKind.Oversized, 144m, 1),
                (row.ProfileNumber, row.Finish, row.Kind, row.StockLength, row.Count)),
            row => Assert.Equal(("P-100", "Bronze", StockItemKind.Regular, 120m, 1),
                (row.ProfileNumber, row.Finish, row.Kind, row.StockLength, row.Count)));

        var cuts = QuestPdfStockLengthReportExporter.BuildCutRequirementRows(
            [regular, oversized, matchingCut, differentLength]);
        Assert.Collection(
            cuts,
            row => Assert.Equal(("PN-2", "Rail", 30m, 2),
                (row.PartNumber, row.PartName, row.Length, row.Quantity)),
            row => Assert.Equal(("PN-2", "Rail", 32m, 1),
                (row.PartNumber, row.PartName, row.Length, row.Quantity)),
            row => Assert.Equal(("PN-10", "Rail", 20m, 1),
                (row.PartNumber, row.PartName, row.Length, row.Quantity)));
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
        var text = string.Join("\n", document.GetPages().Select(ExtractPageText));
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

    private static string ExtractPageText(UglyToad.PdfPig.Content.Page page) =>
        string.Join(" ", page.GetWords().Select(word => word.Text));

    private static StockLengthReportStockItem StockItem(
        int number,
        StockItemKind kind,
        decimal stockLength,
        string partNumber,
        string partName,
        decimal pieceLength,
        string worksheet,
        int row) => new()
    {
        StockItemNumber = number,
        Kind = kind,
        StockLength = stockLength,
        PieceLength = pieceLength,
        Remainder = stockLength - pieceLength,
        UtilizationPercent = pieceLength / stockLength * 100m,
        CutSequence =
        [
            new StockLengthReportPieceInstance
            {
                PieceInstanceId = $"piece-{number}",
                RequiredPieceId = $"required-{number}",
                QuantityInstance = 1,
                Sequence = 1,
                PartNumber = partNumber,
                PartName = partName,
                ProfileNumber = "P-100",
                Finish = "Clear",
                Length = pieceLength,
                SourceReferences = [new SourceReference { WorksheetName = worksheet, PhysicalRow = row }]
            }
        ]
    };
}
