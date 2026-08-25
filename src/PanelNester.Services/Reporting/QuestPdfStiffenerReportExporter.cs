using System.Globalization;
using System.IO;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PanelNester.Services.Reporting;

public sealed class QuestPdfStiffenerReportExporter : IStiffenerPdfReportExporter
{
    private static readonly object LicenseSync = new();
    private static bool _licenseConfigured;

    public Task ExportAsync(
        StiffenerTakeoffReportData report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        EnsureQuestPdfLicense();

        var generatedAt = DateTime.Now;
        var logoBytes = TryLoadLogoBytes(report.CompanyLogoPath);

        Document.Create(container => ComposeDocument(container, report, generatedAt, logoBytes))
            .GeneratePdf(filePath);

        return Task.CompletedTask;
    }

    private static void EnsureQuestPdfLicense()
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

    private static void ComposeDocument(
        IDocumentContainer container,
        StiffenerTakeoffReportData report,
        DateTime generatedAt,
        byte[]? logoBytes)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(34);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Column(column =>
            {
                column.Item().ShowOnce().Element(header =>
                    ComposeFirstPageHeader(header, report, generatedAt, logoBytes));
                column.Item().SkipOnce().Element(header =>
                    ComposeContinuationHeader(header, report, logoBytes));
            });

            page.Content().PaddingTop(12).Column(column =>
            {
                column.Spacing(18);
                column.Item().ShowOnce().Element(content => ComposeOverview(content, report));

                if (report.HasTakeoff)
                {
                    column.Item().Element(content => ComposePieceTable(content, report));
                    if (report.OptimizationGroups.Count > 0)
                    {
                        column.Item().PageBreak();
                    }

                    foreach (var optimizationGroup in report.OptimizationGroups.OrderBy(group => group.Order))
                    {
                        column.Item().Element(content => ComposePieceTable(
                            content,
                            $"Optimization Group — {optimizationGroup.Name}",
                            optimizationGroup.Lengths,
                            optimizationGroup.Summary));
                    }
                }
                else
                {
                    column.Item().Element(PanelCard).Padding(18).Text(
                        "No stiffeners were required for the current ready rows and settings.");
                }
            });

            page.Footer().PaddingTop(10).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).SemiBold());
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void ComposeFirstPageHeader(
        IContainer container,
        StiffenerTakeoffReportData report,
        DateTime generatedAt,
        byte[]? logoBytes)
    {
        container.Column(column =>
        {
            column.Spacing(12);
            column.Item().Row(row =>
            {
                row.RelativeItem(1.4f).MinHeight(72).AlignLeft().Element(logo =>
                    ComposeLogo(logo, report, logoBytes, 190, 72));

                row.RelativeItem().AlignRight().Column(header =>
                {
                    header.Spacing(3);
                    header.Item()
                        .AlignRight()
                        .Text(BuildTitle(report))
                        .FontSize(20)
                        .SemiBold()
                        .FontColor(Colors.Black);
                    header.Item()
                        .AlignRight()
                        .Text($"Generated: {generatedAt:MMM dd, yyyy  |  HH:mm:ss}")
                        .FontSize(11)
                        .FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }

    private static void ComposeContinuationHeader(
        IContainer container,
        StiffenerTakeoffReportData report,
        byte[]? logoBytes)
    {
        container
            .PaddingBottom(8)
            .Row(row =>
            {
                row.RelativeItem(1.15f).MinHeight(42).Element(logo =>
                    ComposeLogo(logo, report, logoBytes, 120, 42));

                row.RelativeItem(2f).AlignRight().Column(header =>
                {
                    header.Spacing(2);
                    header.Item()
                        .AlignRight()
                        .Text(BuildTitle(report))
                        .FontSize(13)
                        .SemiBold()
                        .FontColor(Colors.Black);
                    header.Item().AlignRight().Text(
                        JoinNonEmpty(
                            DisplayOrEmpty(
                                report.ReportSettings.ProjectJobName,
                                report.ProjectMetadata.ProjectName),
                            DisplayOrEmpty(
                                report.ReportSettings.ProjectJobNumber,
                                report.ProjectMetadata.ProjectNumber),
                            DisplayOrEmpty(report.Settings.ReleaseId),
                            DisplayOrEmpty(report.Settings.Status)));
                });
            });
    }

    private static void ComposeOverview(IContainer container, StiffenerTakeoffReportData report)
    {
        container.Column(column =>
        {
            column.Spacing(16);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(card =>
                    ComposeMetaCard(
                        card,
                        ("Job name", DisplayOrEmpty(
                            report.ReportSettings.ProjectJobName,
                            report.ProjectMetadata.ProjectName)),
                        ("Project #", DisplayOrEmpty(
                            report.ReportSettings.ProjectJobNumber,
                            report.ProjectMetadata.ProjectNumber)),
                        ("Required Date", FormatOptionalDate(report.ProjectMetadata.RequiredDate)),
                        ("Report Date", FormatDate(report.ReportSettings.ReportDate, report.ProjectMetadata.Date)),
                        ("Release #", DisplayOrEmpty(report.Settings.ReleaseId)),
                        ("PM/FM", DisplayOrEmpty(report.ProjectMetadata.Pm)),
                        ("P.O. #", DisplayOrEmpty(report.Settings.PoNumber)),
                        ("Manufacturer", DisplayOrEmpty(report.Settings.Manufacturer)),
                        ("Status", DisplayOrEmpty(report.Settings.Status)),
                        ("Detailer", DisplayOrEmpty(report.ProjectMetadata.Drafter))));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(card =>
                    ComposeMetricCard(card, "Eligible Panels", report.OverallSummary.EligiblePanelCount.ToString("N0", CultureInfo.InvariantCulture), null));
                row.RelativeItem().Element(card =>
                    ComposeMetricCard(card, "Total Stiffeners", report.OverallSummary.TotalStiffenerCount.ToString("N0", CultureInfo.InvariantCulture), null));
                row.RelativeItem().Element(card =>
                    ComposeMetricCard(card, "Total Linear Feet", report.OverallSummary.TotalLinearFeet.ToString("N1", CultureInfo.InvariantCulture), "Net cutting length"));
                row.RelativeItem().Element(card =>
                    ComposeStockCard(card, report.OverallSummary));
            });

            column.Item().Row(row =>
            {
                row.RelativeItem(2.2f).Element(card =>
                    ComposeConfigurationCard(card, report));
                row.RelativeItem().Element(card =>
                    ComposeNotesCard(card, report.ReportSettings.Notes));
            });
        });
    }

    private static void ComposeMetaCard(
        IContainer container,
        params (string Label, string Value)[] items)
    {
        container.Element(PanelCard).Padding(18).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            foreach (var (label, value) in items)
            {
                table.Cell().PaddingRight(18).PaddingBottom(12).Column(column =>
                {
                    column.Spacing(2);
                    column.Item().Text(label).FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                    column.Item().Text(value).FontSize(11).SemiBold().FontColor(Colors.Black);
                }); 
            }
        });
    }

    private static void ComposeMetricCard(
        IContainer container,
        string label,
        string value,
        string? note)
    {
        container.Element(PanelCard).Padding(14).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(label).FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
            column.Item().Text(value).FontSize(18).SemiBold().FontColor(Colors.Black);

            if (!string.IsNullOrWhiteSpace(note))
            {
                column.Item().Text(note).FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
            }
        });
    }

    private static void ComposeStockCard(
        IContainer container,
        StiffenerTakeoffSectionSummary summary)
    {
        container.Element(PanelCard).Padding(14).Row(row =>
        {
            row.Spacing(18);
            row.RelativeItem().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("Stock length").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(FormatStockLength(summary.StockLengthFeet))
                    .FontSize(18)
                    .SemiBold()
                    .FontColor(Colors.Black);
            });
            row.RelativeItem(1.2f).Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("Total sticks required").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(summary.RequiredStockCount.ToString("N0", CultureInfo.InvariantCulture))
                    .FontSize(18)
                    .SemiBold()
                    .FontColor(Colors.Black);
            });
        });
    }

    private static void ComposeConfigurationCard(IContainer container, StiffenerTakeoffReportData report)
    {
        container.Element(PanelCard).Padding(18).Column(column =>
        {
            column.Spacing(10);
            column.Item().Text("Takeoff Configuration").FontSize(12).SemiBold().FontColor(Colors.Black);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                AddConfigItem(table, "Stiffener Type", DisplayOrEmpty(report.Settings.Extrusion));
                AddConfigItem(table, "Color", DisplayOrEmpty(report.Settings.Color));
                AddConfigItem(table, "Color #", DisplayOrEmpty(report.Settings.ColorNumber));
            });
        });
    }

    private static void AddConfigItem(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingRight(18).PaddingBottom(10).Column(column =>
        {
            column.Spacing(2);
            column.Item().Text(label).FontSize(9).FontColor(Colors.Grey.Darken1);
            column.Item().Text(value).FontSize(11).SemiBold().FontColor(Colors.Black);
        });
    }

    private static void ComposeNotesCard(IContainer container, string? notes)
    {
        container.Element(PanelCard).Padding(18).Column(column =>
        {
            column.Spacing(10);
            column.Item().Text("Notes").FontSize(12).SemiBold().FontColor(Colors.Black);
            column.Item().Text(DisplayOrEmpty(notes)).FontSize(10);
        });
    }

    private static void ComposePieceTable(IContainer container, StiffenerTakeoffReportData report)
        => ComposePieceTable(
            container,
            "Stiffener Pieces — Project Total",
            report.OverallLengths,
            report.OverallSummary);

    private static void ComposePieceTable(
        IContainer container,
        string title,
        IReadOnlyList<StiffenerTakeoffLengthSummary> lengths,
        StiffenerTakeoffSectionSummary summary)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Text(title).FontSize(12).SemiBold().FontColor(Colors.Black);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Piece mark");
                    header.Cell().Element(HeaderCell).AlignCenter().Text("Qty");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Length");
                });

                foreach (var (length, index) in lengths.Select((item, index) => (item, index)))
                {
                    var rowBackground = index % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Element(cell => BodyCell(cell, rowBackground)).Text(length.Label).SemiBold();
                    table.Cell().Element(cell => BodyCell(cell, rowBackground)).AlignCenter()
                        .Text(length.PieceCount.ToString("N0", CultureInfo.InvariantCulture));
                    table.Cell().Element(cell => BodyCell(cell, rowBackground)).AlignRight()
                        .Text(FormatDimension(length.LengthInches));
                }

                table.Cell().ColumnSpan(3).PaddingTop(10).AlignRight().Text(text =>
                {
                    text.Span("Subtotal pieces: ").SemiBold();
                    text.Span(summary.TotalStiffenerCount.ToString("N0", CultureInfo.InvariantCulture));
                    text.Span("   ");
                    text.Span("Total linear feet: ").SemiBold();
                    text.Span(summary.TotalLinearFeet.ToString("N1", CultureInfo.InvariantCulture));
                });
            });
        });
    }

    private static void ComposeLogo(
        IContainer container,
        StiffenerTakeoffReportData report,
        byte[]? logoBytes,
        float maxWidth,
        float maxHeight)
    {
        if (logoBytes is { Length: > 0 })
        {
            container
                .MaxWidth(maxWidth)
                .MaxHeight(maxHeight)
                .Image(logoBytes)
                .FitArea();
            return;
        }

        container.AlignLeft().Text(DisplayOrEmpty(report.ReportSettings.CompanyName))
            .FontSize(18)
            .SemiBold()
            .FontColor(Colors.Black);
    }

    private static string BuildTitle(StiffenerTakeoffReportData report)
    {
        var title = report.Settings.ReportTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return string.Empty;
    }

    private static string Display(string? primary, string? secondary, string fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : !string.IsNullOrWhiteSpace(secondary)
                ? secondary.Trim()
                : fallback;

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string DisplayOrEmpty(string? primary, string? secondary) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : !string.IsNullOrWhiteSpace(secondary)
                ? secondary.Trim()
                : string.Empty;

    private static string DisplayOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string JoinNonEmpty(params string[] values) =>
        string.Join("  •  ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FormatDate(DateTime? primary, DateTime? fallback) =>
        (primary ?? fallback ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatOptionalDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatDimension(decimal value) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)} in";

    private static string FormatStockLength(decimal value) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)} ft";

    private static IContainer PanelCard(IContainer container) =>
        container
            .Background(Colors.White)
            .Padding(0);

    private static IContainer HeaderCell(IContainer container) =>
        container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Darken1)
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .DefaultTextStyle(style => style.FontSize(10).SemiBold().FontColor(Colors.Black));

    private static IContainer ValueCell(IContainer container) =>
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(6)
            .PaddingHorizontal(8)
            .DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Black));

    private static IContainer BodyCell(IContainer container, string background) =>
        container
            .Background(background)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(8)
            .PaddingHorizontal(10)
            .DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Black));

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
}
