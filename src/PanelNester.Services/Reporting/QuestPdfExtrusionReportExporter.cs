using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.IO;

namespace PanelNester.Services.Reporting;

public sealed class QuestPdfExtrusionReportExporter : IExtrusionPdfReportExporter
{
    private static readonly object LicenseSync = new();
    private static bool _licenseConfigured;

    public Task ExportAsync(
        ExtrusionReportData report,
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
        Document.Create(container => Compose(container, report, generatedAt)).GeneratePdf(filePath);
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

    private static void Compose(IDocumentContainer container, ExtrusionReportData report, DateTime generatedAt)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(34);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken4));
            page.Header().Column(column =>
            {
                column.Item().Text(BuildTitle(report)).FontSize(20).SemiBold().FontColor(Colors.Black);
                column.Item().Text($"Generated: {generatedAt:yyyy-MM-dd HH:mm:ss}").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
            page.Content().PaddingTop(16).Column(column =>
            {
                column.Spacing(16);
                column.Item().Element(card => ComposeMetadata(card, report));
                column.Item().Element(card => ComposeLengths(card, "Overall Summary", report.OverallLengths));

                foreach (var group in report.Groups)
                {
                    column.Item().Element(card => ComposeLengths(card, $"Group: {group.GroupName}", group.Lengths));
                }

                if (!report.HasTakeoff)
                {
                    column.Item().Text("No extrusion segments have been assigned for the current layout.");
                }
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void ComposeMetadata(IContainer container, ExtrusionReportData report)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Project").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(Display(report.ReportSettings.ProjectJobName, report.ProjectMetadata.ProjectName)).SemiBold();
            });
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Project #").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(Display(report.ReportSettings.ProjectJobNumber, report.ProjectMetadata.ProjectNumber));
            });
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Groups").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(report.Groups.Count.ToString("N0", CultureInfo.InvariantCulture));
            });
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Segments").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken1);
                column.Item().Text(report.Segments.Count.ToString("N0", CultureInfo.InvariantCulture));
            });
        });
    }

    private static void ComposeLengths(
        IContainer container,
        string title,
        IReadOnlyList<ExtrusionLengthSummary> rows)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Text(title).FontSize(12).SemiBold().FontColor(Colors.Black);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    Header(header.Cell()).Text("Category");
                    Header(header.Cell()).Text("Extrusion");
                    Header(header.Cell()).AlignRight().Text("Running ft");
                    Header(header.Cell()).AlignRight().Text("Segments");
                    Header(header.Cell()).AlignRight().Text("Stick ft");
                    Header(header.Cell()).AlignRight().Text("Sticks");
                });

                foreach (var (row, index) in rows.Select((value, index) => (value, index)))
                {
                    var background = index % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    Body(table.Cell(), background).Text(row.Category);
                    Body(table.Cell(), background).Text(row.ExtrusionName);
                    Body(table.Cell(), background).AlignRight().Text(row.TotalLinearFeet.ToString("0.###", CultureInfo.InvariantCulture));
                    Body(table.Cell(), background).AlignRight().Text(row.SegmentCount.ToString("N0", CultureInfo.InvariantCulture));
                    Body(table.Cell(), background).AlignRight().Text(row.StickLengthFeet.ToString("0.###", CultureInfo.InvariantCulture));
                    Body(table.Cell(), background).AlignRight().Text(row.RequiredStickCount.ToString("N0", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static IContainer Header(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(5).DefaultTextStyle(style => style.SemiBold());

    private static IContainer Body(IContainer container, string background) =>
        container.Background(background).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6);

    private static string BuildTitle(ExtrusionReportData report) =>
        string.IsNullOrWhiteSpace(report.ReportSettings.ReportTitle)
            ? "Extrusion Takeoff"
            : $"{report.ReportSettings.ReportTitle} - Extrusion Takeoff";

    private static string Display(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback.Trim()
                : string.Empty;

}
