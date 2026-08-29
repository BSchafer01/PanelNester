using System.Globalization;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PanelNester.Services.Reporting;

public sealed class QuestPdfStockLengthReportExporter
{
    private static readonly object LicenseSync = new();
    private static bool _licenseConfigured;

    public Task ExportAsync(
        StockLengthReportData report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        CreateDocument(report).GeneratePdf(filePath);
        return Task.CompletedTask;
    }

    internal static IDocument CreateDocument(StockLengthReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        EnsureLicense();
        return Document.Create(document => ComposeDocument(document, report));
    }

    private static void ComposeDocument(IDocumentContainer document, StockLengthReportData report)
    {
        var logoBytes = TryLoadLogoBytes(report.CompanyLogoPath);
        var hasStockItems = report.OptimizationGroups.Any(group =>
            group.StockGroups.Any(stockGroup => stockGroup.StockItems.Count > 0));

        document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(28);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Element(container => ComposeHeader(container, report, logoBytes));
            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(container => ComposeSummary(container, "Project Summary", report.Summary));
                column.Item().Element(container => ComposeStockRequirements(container, report));

                if (HasExceptions(report))
                {
                    column.Item().Element(container => ComposeExceptions(container, report));
                }

                if (hasStockItems)
                {
                    column.Item().Element(container => ComposeCutRequirements(container, report));
                }
            });
            page.Footer().Element(ComposeFooter);
        });

        if (!hasStockItems)
        {
            return;
        }

        document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(28);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Element(container => ComposeHeader(container, report, logoBytes));
            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(container => ComposeHeader(container, report, logoBytes));
                column.Item().Text("Cut Maps").FontSize(15).SemiBold();

                foreach (var group in report.OptimizationGroups)
                {
                    foreach (var stockGroup in group.StockGroups)
                    {
                        foreach (var stockItem in stockGroup.StockItems)
                        {
                            column.Item().Element(container =>
                                ComposeStockItem(container, report, group, stockGroup, stockItem));
                        }
                    }
                }
            });
            page.Footer().Element(ComposeFooter);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("Stock-Length Cut Report - Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }

    private static void ComposeHeader(
        IContainer container,
        StockLengthReportData report,
        byte[]? logoBytes)
    {
        container.MinHeight(54).BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(8).Row(row =>
        {
            row.ConstantItem(105).AlignLeft().AlignMiddle().Element(logoContainer =>
            {
                if (logoBytes is { Length: > 0 })
                {
                    logoContainer.MaxWidth(95).MaxHeight(44).Image(logoBytes).FitArea();
                }
            });
            row.RelativeItem().AlignMiddle().Text(BuildReportTitle(report)).FontSize(16).SemiBold();
            row.ConstantItem(175).AlignRight().AlignMiddle().Column(column =>
            {
                column.Item().AlignRight().Text(Display(report.Settings.CompanyName, "OptiFab"));
                column.Item().AlignRight().Text($"Project: {Display(report.ProjectMetadata.ProjectNumber, "-")}");
                column.Item().AlignRight().Text($"Report date: {(report.Settings.ReportDate ?? DateTime.Today):yyyy-MM-dd}");
            });
        });
    }

    private static string BuildReportTitle(StockLengthReportData report) =>
        $"{Display(report.ProjectMetadata.ProjectName, "Untitled Project")} Stock Length Report";

    private static void ComposeSummary(IContainer container, string title, StockLengthReportSummary summary)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(7).Column(column =>
        {
            column.Item().Text(title).FontSize(11).SemiBold();
            column.Item().PaddingTop(3).Text(
                $"Accepted Piece Instances: {summary.AcceptedPieceInstanceCount:N0}  |  " +
                $"Placed: {summary.PlacedPieceInstanceCount:N0}  |  Unplaced: {summary.UnplacedPieceInstanceCount:N0}  |  " +
                $"Stock: {summary.StockLength:0.###} in  |  Pieces: {summary.PieceLength:0.###} in  |  " +
                $"Saw Loss: {summary.SawLoss:0.###} in  |  Remainder: {summary.Remainder:0.###} in  |  " +
                $"Utilization: {summary.UtilizationPercent:0.0}%");
        });
    }

    private static void ComposeStockRequirements(IContainer container, StockLengthReportData report)
    {
        container.Column(column =>
        {
            column.Item().Text("Overall Stock Requirements").FontSize(15).SemiBold();
            var rows = BuildStockRequirementRows(report);

            if (rows.Count == 0)
            {
                column.Item().PaddingTop(4).Text("No Stock Items are present in the selected scope.").Italic();
                return;
            }

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(0.7f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(0.45f);
                });
                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Optimization Group");
                    HeaderCell(header.Cell(), "Profile Number");
                    HeaderCell(header.Cell(), "Finish");
                    HeaderCell(header.Cell(), "Stock Type");
                    HeaderCell(header.Cell(), "Stock Length");
                    HeaderCell(header.Cell(), "Count");
                });
                foreach (var row in rows)
                {
                    BodyCell(table.Cell(), Display(row.OptimizationGroupName, "Unnamed Optimization Group"));
                    BodyCell(table.Cell(), Display(row.ProfileNumber, "Unnamed Profile"));
                    BodyCell(table.Cell(), Display(row.Finish, "No finish specified"));
                    BodyCell(table.Cell(), DisplayStockItemKind(row.Kind));
                    BodyCell(table.Cell(), FormatLength(row.StockLength, report.InchDisplayFormat));
                    BodyCell(table.Cell(), row.Count.ToString("N0", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static bool HasExceptions(StockLengthReportData report) =>
        report.UnplacedPieceInstances.Count > 0 ||
        report.OptimizationGroups.Any(group =>
            group.State is StockLengthReportState.Empty or
                StockLengthReportState.NeedsGeneration or
                StockLengthReportState.Failed or
                StockLengthReportState.ApplicationError ||
            !string.IsNullOrWhiteSpace(group.FailureMessage));

    private static void ComposeExceptions(IContainer container, StockLengthReportData report)
    {
        container.Border(2).BorderColor(Colors.Red.Darken2).Padding(8).Column(column =>
        {
            column.Item().Text("Exceptions").FontSize(15).Bold();

            var stateExceptions = report.OptimizationGroups
                .Where(group => group.State is StockLengthReportState.Empty or
                    StockLengthReportState.NeedsGeneration or
                    StockLengthReportState.Failed or
                    StockLengthReportState.ApplicationError ||
                    !string.IsNullOrWhiteSpace(group.FailureMessage))
                .ToArray();
            if (stateExceptions.Length > 0)
            {
                column.Item().PaddingTop(4).Text("Optimization Group Status").FontSize(10).SemiBold();
                column.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.3f);
                        columns.RelativeColumn(0.7f);
                        columns.RelativeColumn(2.5f);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Optimization Group");
                        HeaderCell(header.Cell(), "Status");
                        HeaderCell(header.Cell(), "Details");
                    });
                    foreach (var group in stateExceptions)
                    {
                        BodyCell(table.Cell(), Display(group.Name, "Unnamed Optimization Group"));
                        BodyCell(table.Cell(), StateLabel(group.State));
                        BodyCell(table.Cell(), string.IsNullOrWhiteSpace(group.FailureMessage)
                            ? StateExplanation(group.State)
                            : group.FailureMessage.Trim());
                    }
                });
            }

            if (report.UnplacedPieceInstances.Count > 0)
            {
                column.Item().PaddingTop(6)
                    .Text($"Unplaced Piece Instances ({report.UnplacedPieceInstances.Count:N0})")
                    .FontSize(10).SemiBold();
                column.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.2f);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(0.8f);
                        columns.RelativeColumn(0.7f);
                        columns.RelativeColumn(1.2f);
                        columns.RelativeColumn(2f);
                    });
                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Optimization Group");
                        HeaderCell(header.Cell(), "Stock Group");
                        HeaderCell(header.Cell(), "Part Number");
                        HeaderCell(header.Cell(), "Length");
                        HeaderCell(header.Cell(), "Source References");
                        HeaderCell(header.Cell(), "Reason");
                    });
                    foreach (var item in report.UnplacedPieceInstances)
                    {
                        BodyCell(table.Cell(), item.OptimizationGroupName);
                        BodyCell(table.Cell(), StockGroupLabel(item.ProfileNumber, item.Finish));
                        BodyCell(table.Cell(), Display(item.PieceInstance.PartNumber, "-"));
                        BodyCell(table.Cell(), FormatLength(item.PieceInstance.Length, report.InchDisplayFormat));
                        BodyCell(table.Cell(), FormatSourceReferences(item.PieceInstance.SourceReferences));
                        BodyCell(table.Cell(), $"{item.ReasonDescription} [{item.ReasonCode}]");
                    }
                });
            }
        });
    }

    private static void ComposeCutRequirements(IContainer container, StockLengthReportData report)
    {
        container.Column(column =>
        {
            column.Item().Text("Cut Requirements").FontSize(15).SemiBold();

            foreach (var group in report.OptimizationGroups)
            {
                foreach (var stockGroup in group.StockGroups)
                {
                    foreach (var items in stockGroup.StockItems
                                 .GroupBy(item => new { item.Kind, item.StockLength })
                                 .OrderBy(itemGroup => itemGroup.Key.Kind == StockItemKind.Oversized ? 1 : 0)
                                 .ThenBy(itemGroup => itemGroup.Key.StockLength))
                    {
                        column.Item().PaddingTop(6).Element(section => ComposeCutRequirementSection(
                            section, report, group, stockGroup, items.Key.Kind, items.Key.StockLength, items.ToArray()));
                    }
                }
            }
        });
    }

    private static void ComposeCutRequirementSection(
        IContainer container,
        StockLengthReportData report,
        StockLengthReportOptimizationGroup group,
        StockLengthReportStockGroup stockGroup,
        StockItemKind kind,
        decimal stockLength,
        IReadOnlyList<StockLengthReportStockItem> stockItems)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(7).Column(column =>
        {
            column.Item().Text(Display(group.Name, "Unnamed Optimization Group")).FontSize(9).SemiBold();
            column.Item().Text(
                $"{StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish)} | " +
                $"{FormatLength(stockLength, report.InchDisplayFormat)} | " +
                $"Required Stock: {StockItemCountLabel(stockItems.Count, kind)}")
                .FontSize(11).SemiBold();

            var cuts = BuildCutRequirementRows(stockItems);

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(0.65f);
                    columns.RelativeColumn(0.5f);
                });
                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Part Number");
                    HeaderCell(header.Cell(), "Part Name");
                    HeaderCell(header.Cell(), "Length");
                    HeaderCell(header.Cell(), "Total Qty");
                });
                foreach (var cut in cuts)
                {
                    BodyCell(table.Cell(), cut.PartNumber);
                    BodyCell(table.Cell(), cut.PartName);
                    BodyCell(table.Cell(), FormatLength(cut.Length, report.InchDisplayFormat));
                    BodyCell(table.Cell(), cut.Quantity.ToString("N0", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    internal static IReadOnlyList<StockRequirementRow> BuildStockRequirementRows(StockLengthReportData report) =>
        report.OptimizationGroups
            .SelectMany(group => group.StockGroups.SelectMany(stockGroup => stockGroup.StockItems
                .GroupBy(item => new { item.Kind, item.StockLength })
                .OrderBy(items => items.Key.Kind == StockItemKind.Oversized ? 1 : 0)
                .ThenBy(items => items.Key.StockLength)
                .Select(items => new StockRequirementRow(
                    group.Name,
                    stockGroup.ProfileNumber,
                    stockGroup.Finish,
                    items.Key.Kind,
                    items.Key.StockLength,
                    items.Count()))))
            .ToArray();

    internal static IReadOnlyList<CutRequirementRow> BuildCutRequirementRows(
        IReadOnlyList<StockLengthReportStockItem> stockItems) =>
        stockItems
                .SelectMany(item => item.CutSequence)
                .GroupBy(piece => new
                {
                    PartNumber = Normalize(piece.PartNumber),
                    PartName = Normalize(piece.PartName),
                    piece.Length
                })
                .Select(pieces => new CutRequirementRow(
                    Display(pieces.First().PartNumber, "-"),
                    Display(pieces.First().PartName, "-"),
                    pieces.Key.Length,
                    pieces.Count()))
                .OrderBy(row => row.PartNumber, NaturalLabelComparer.Instance)
                .ThenBy(row => row.PartName, NaturalLabelComparer.Instance)
                .ThenBy(row => row.Length)
                .ToArray();

    private static void ComposeStockItem(
        IContainer container,
        StockLengthReportData report,
        StockLengthReportOptimizationGroup group,
        StockLengthReportStockGroup stockGroup,
        StockLengthReportStockItem stockItem)
    {
        var block = stockItem.CutSequence.Count <= 12
            ? container.ShowEntire()
            : container.EnsureSpace(150);
        block.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(7).Column(column =>
        {
            column.Item().Text(
                $"{Display(group.Name, "Unnamed Optimization Group")} | " +
                $"{StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish)}")
                .FontSize(9).SemiBold();
            column.Item().Text($"{DisplayStockItemKind(stockItem.Kind)} Stock Item {stockItem.StockItemNumber}")
                .FontSize(11).SemiBold();
            column.Item().Text(
                $"Stock Length {FormatLength(stockItem.StockLength, report.InchDisplayFormat)}  |  " +
                $"Piece Length {FormatLength(stockItem.PieceLength, report.InchDisplayFormat)}  |  " +
                $"Saw Loss {FormatLength(stockItem.SawLoss, report.InchDisplayFormat)}  |  " +
                $"Remainder {FormatLength(stockItem.Remainder, report.InchDisplayFormat)}  |  " +
                $"Utilization {stockItem.UtilizationPercent:0.0}%");
            column.Item().PaddingVertical(4).Element(diagram => ComposeDiagram(diagram, stockItem));
            column.Item().Text("Cut Sequence").SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(0.9f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(1.3f);
                });
                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Seq");
                    HeaderCell(header.Cell(), "Part Number");
                    HeaderCell(header.Cell(), "Part Name");
                    HeaderCell(header.Cell(), "Profile Number");
                    HeaderCell(header.Cell(), "Finish");
                    HeaderCell(header.Cell(), "Length");
                    HeaderCell(header.Cell(), "Source References");
                });
                foreach (var piece in stockItem.CutSequence.OrderBy(piece => piece.Sequence))
                {
                    BodyCell(table.Cell(), piece.Sequence.ToString(CultureInfo.InvariantCulture));
                    BodyCell(table.Cell(), Display(piece.PartNumber, "-"));
                    BodyCell(table.Cell(), Display(piece.PartName, "-"));
                    BodyCell(table.Cell(), Display(piece.ProfileNumber, "-"));
                    BodyCell(table.Cell(), Display(piece.Finish, "-"));
                    BodyCell(table.Cell(), FormatLength(piece.Length, report.InchDisplayFormat));
                    BodyCell(table.Cell(), FormatSourceReferences(piece.SourceReferences));
                }
            });
        });
    }

    private static void ComposeDiagram(IContainer container, StockLengthReportStockItem item)
    {
        container.Height(30).Row(row =>
        {
            var pieces = item.CutSequence.OrderBy(piece => piece.Sequence).ToArray();
            var kerf = pieces.Length > 1 ? item.SawLoss / (pieces.Length - 1) : 0m;
            for (var index = 0; index < pieces.Length; index++)
            {
                var piece = pieces[index];
                row.RelativeItem((float)Math.Max(piece.Length, 0.001m))
                    .Border(1)
                    .BorderColor(Colors.Black)
                    .Background(piece.Sequence % 2 == 0 ? Colors.Grey.Lighten2 : Colors.White)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text($"{piece.Sequence}").Bold();

                if (index < pieces.Length - 1 && kerf > 0m)
                {
                    row.RelativeItem((float)kerf)
                        .BorderVertical(1)
                        .BorderColor(Colors.Black)
                        .Background(Colors.Black)
                        .AlignCenter()
                        .AlignMiddle()
                        .Text("K").FontSize(5).FontColor(Colors.White);
                }
            }

            if (item.Remainder > 0m)
            {
                row.RelativeItem((float)item.Remainder)
                    .Border(1)
                    .BorderColor(Colors.Black)
                    .Background(Colors.Grey.Lighten3)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text("R").Bold();
            }
        });
    }

    private static void HeaderCell(IContainer container, string value) =>
        container.Background(Colors.Grey.Lighten2).BorderBottom(1).Padding(3).Text(value).FontSize(7).SemiBold();

    private static void BodyCell(IContainer container, string value) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(value).FontSize(7);

    private static string FormatLength(decimal value, InchDisplayFormat format)
    {
        if (format == InchDisplayFormat.Decimal)
        {
            return $"{value.ToString("0.###", CultureInfo.InvariantCulture)}\"";
        }

        var denominator = format switch
        {
            InchDisplayFormat.Fractional16 => 16,
            InchDisplayFormat.Fractional32 => 32,
            _ => 64
        };
        var whole = decimal.ToInt32(decimal.Floor(value));
        var numerator = decimal.ToInt32(decimal.Round((value - whole) * denominator, 0, MidpointRounding.AwayFromZero));
        if (numerator == denominator)
        {
            whole++;
            numerator = 0;
        }
        if (numerator == 0)
        {
            return $"{whole}\"";
        }
        var divisor = GreatestCommonDivisor(numerator, denominator);
        return whole == 0
            ? $"{numerator / divisor}/{denominator / divisor}\""
            : $"{whole} {numerator / divisor}/{denominator / divisor}\"";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return Math.Abs(left);
    }

    private static string StateLabel(StockLengthReportState state) => state == StockLengthReportState.NeedsGeneration
        ? "Needs Generation"
        : state.ToString();

    private static string StateExplanation(StockLengthReportState state) => state switch
    {
        StockLengthReportState.Empty => "This Optimization Group has no Required Pieces.",
        StockLengthReportState.NeedsGeneration => "This Optimization Group needs a current Cut Plan.",
        StockLengthReportState.Failed => "No Piece Instances were placed for this Optimization Group.",
        StockLengthReportState.ApplicationError => "Cut Plan generation encountered an application error.",
        _ => "No Stock Items are present in the selected scope."
    };

    private static string FormatSourceReferences(IEnumerable<SourceReference> sourceReferences)
    {
        var value = string.Join(", ", sourceReferences.Select(reference =>
            $"{reference.WorksheetName}!{reference.PhysicalRow}"));
        return Display(value, "-");
    }

    private static string StockGroupLabel(string profileNumber, string? finish) =>
        $"{Display(profileNumber, "Unnamed Profile")} - {Display(finish, "No finish specified")}";

    private static string DisplayStockItemKind(StockItemKind kind) =>
        kind == StockItemKind.Oversized ? "Oversized" : "Regular";

    private static string StockItemCountLabel(int count, StockItemKind kind) =>
        $"{count:N0} {DisplayStockItemKind(kind)} Stock Item{(count == 1 ? string.Empty : "s")}";

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static byte[]? TryLoadLogoBytes(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(filePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    internal sealed record StockRequirementRow(
        string OptimizationGroupName,
        string ProfileNumber,
        string? Finish,
        StockItemKind Kind,
        decimal StockLength,
        int Count);

    internal sealed record CutRequirementRow(
        string PartNumber,
        string PartName,
        decimal Length,
        int Quantity);

    private sealed class NaturalLabelComparer : IComparer<string>
    {
        public static NaturalLabelComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftEnd = DigitRunEnd(left, leftIndex);
                    var rightEnd = DigitRunEnd(right, rightIndex);
                    var leftSignificant = SkipLeadingZeroes(left, leftIndex, leftEnd);
                    var rightSignificant = SkipLeadingZeroes(right, rightIndex, rightEnd);
                    var lengthComparison = (leftEnd - leftSignificant).CompareTo(rightEnd - rightSignificant);
                    if (lengthComparison != 0)
                    {
                        return lengthComparison;
                    }

                    var numberComparison = string.Compare(
                        left,
                        leftSignificant,
                        right,
                        rightSignificant,
                        leftEnd - leftSignificant,
                        StringComparison.Ordinal);
                    if (numberComparison != 0)
                    {
                        return numberComparison;
                    }

                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0)
                {
                    return characterComparison;
                }

                leftIndex++;
                rightIndex++;
            }

            var remainingComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
            return remainingComparison != 0
                ? remainingComparison
                : string.Compare(left, right, StringComparison.Ordinal);
        }

        private static int DigitRunEnd(string value, int start)
        {
            var index = start;
            while (index < value.Length && char.IsDigit(value[index]))
            {
                index++;
            }
            return index;
        }

        private static int SkipLeadingZeroes(string value, int start, int end)
        {
            var index = start;
            while (index < end - 1 && value[index] == '0')
            {
                index++;
            }
            return index;
        }
    }

    private static void EnsureLicense()
    {
        if (_licenseConfigured)
        {
            return;
        }
        lock (LicenseSync)
        {
            if (_licenseConfigured)
            {
                return;
            }
            QuestPDF.Settings.License = LicenseType.Community;
            _licenseConfigured = true;
        }
    }
}
