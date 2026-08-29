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
        document.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(28);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Element(container => ComposeHeader(container, report));
            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(container => ComposeSummary(container, "Project Summary", report.Summary));

                if (report.UnplacedPieceInstances.Count > 0)
                {
                    column.Item().Element(container => ComposeUnplaced(container, report));
                }

                foreach (var group in report.OptimizationGroups)
                {
                    column.Item().Element(container => ComposeGroup(container, report, group));
                }
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Stock-Length Cut Report · Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void ComposeHeader(IContainer container, StockLengthReportData report)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(string.IsNullOrWhiteSpace(report.Settings.ReportTitle)
                    ? "Stock-Length Cut Report"
                    : report.Settings.ReportTitle.Trim()).FontSize(18).SemiBold();
                column.Item().Text(Display(report.ProjectMetadata.ProjectName, "Untitled Project")).FontSize(11);
            });
            row.ConstantItem(190).AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text(Display(report.Settings.CompanyName, "OptiFab"));
                column.Item().AlignRight().Text($"Project: {Display(report.ProjectMetadata.ProjectNumber, "—")}");
                column.Item().AlignRight().Text($"Report date: {(report.Settings.ReportDate ?? DateTime.Today):yyyy-MM-dd}");
            });
        });
    }

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

    private static void ComposeUnplaced(IContainer container, StockLengthReportData report)
    {
        container.Border(2).BorderColor(Colors.Red.Darken2).Padding(8).Column(column =>
        {
            column.Item().Text($"UNPLACED PIECE INSTANCES ({report.UnplacedPieceInstances.Count:N0})")
                .FontSize(12).Bold();
            column.Item().PaddingTop(4).Table(table =>
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
                    BodyCell(table.Cell(), Display(item.PieceInstance.PartNumber, "—"));
                    BodyCell(table.Cell(), FormatLength(item.PieceInstance.Length, report.InchDisplayFormat));
                    BodyCell(table.Cell(), FormatSourceReferences(item.PieceInstance.SourceReferences));
                    BodyCell(table.Cell(), $"{item.ReasonDescription} [{item.ReasonCode}]");
                }
            });
        });
    }

    private static void ComposeGroup(
        IContainer container,
        StockLengthReportData report,
        StockLengthReportOptimizationGroup group)
    {
        container.Column(column =>
        {
            column.Item().Background(StateBackground(group.State)).Padding(7).Row(row =>
            {
                row.RelativeItem().Text($"{group.Order + 1}. {Display(group.Name, "Unnamed Optimization Group")}")
                    .FontSize(13).SemiBold();
                row.AutoItem().Text(StateLabel(group.State)).Bold();
            });
            column.Item().Element(summary => ComposeSummary(summary, "Optimization Group Summary", group.Summary));

            if (!string.IsNullOrWhiteSpace(group.FailureMessage))
            {
                column.Item().Padding(7).BorderLeft(3).BorderColor(Colors.Red.Darken2).Text(group.FailureMessage);
            }

            if (group.StockGroups.Count == 0)
            {
                column.Item().Padding(8).Text(StateExplanation(group.State)).Italic();
            }

            foreach (var stockGroup in group.StockGroups)
            {
                column.Item().PaddingTop(8).Element(section => ComposeStockGroup(section, report, stockGroup));
            }
        });
    }

    private static void ComposeStockGroup(
        IContainer container,
        StockLengthReportData report,
        StockLengthReportStockGroup stockGroup)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(7).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Stock Group: {StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish)}")
                    .FontSize(11).SemiBold();
                row.AutoItem().Text(StateLabel(stockGroup.State)).Bold();
            });
            column.Item().Element(summary => ComposeSummary(summary, "Stock Group Summary", stockGroup.Summary));
            foreach (var stockItem in stockGroup.StockItems)
            {
                column.Item().PaddingTop(8).Element(item => ComposeStockItem(item, report, stockItem));
            }
        });
    }

    private static void ComposeStockItem(
        IContainer container,
        StockLengthReportData report,
        StockLengthReportStockItem stockItem)
    {
        container.EnsureSpace(120).BorderTop(1).BorderColor(Colors.Grey.Darken1).PaddingTop(5).Column(column =>
        {
            column.Item().Text(stockItem.Kind == StockItemKind.Oversized
                ? $"Oversized Stock Item {stockItem.StockItemNumber}"
                : $"Regular Stock Item {stockItem.StockItemNumber}").FontSize(10).SemiBold();
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
                    BodyCell(table.Cell(), Display(piece.PartNumber, "—"));
                    BodyCell(table.Cell(), Display(piece.PartName, "—"));
                    BodyCell(table.Cell(), Display(piece.ProfileNumber, "—"));
                    BodyCell(table.Cell(), Display(piece.Finish, "—"));
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

    private static string StateBackground(StockLengthReportState state) => state switch
    {
        StockLengthReportState.Complete => Colors.Grey.Lighten3,
        StockLengthReportState.Partial => Colors.Orange.Lighten4,
        StockLengthReportState.Failed => Colors.Red.Lighten4,
        StockLengthReportState.ApplicationError => Colors.Red.Lighten4,
        StockLengthReportState.Empty => Colors.Grey.Lighten3,
        _ => Colors.Yellow.Lighten4
    };

    private static string FormatSourceReferences(IEnumerable<SourceReference> sourceReferences)
    {
        var value = string.Join(", ", sourceReferences.Select(reference =>
            $"{reference.WorksheetName}!{reference.PhysicalRow}"));
        return Display(value, "—");
    }

    private static string StockGroupLabel(string profileNumber, string? finish) =>
        $"{Display(profileNumber, "Unnamed Profile")} — {Display(finish, "No finish specified")}";

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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
