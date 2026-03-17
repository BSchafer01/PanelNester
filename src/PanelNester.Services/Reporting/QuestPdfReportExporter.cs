using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using System.Globalization;
using System.Text;
using QuestPDF.Infrastructure;

namespace PanelNester.Services.Reporting;

public sealed class QuestPdfReportExporter : IPdfReportExporter
{
    private const float MinimumPlacementStrokeWidth = 1f;
    private const float MinimumLabelFontSize = 6f;
    private const float MaximumLabelFontSize = 14f;
    private const float CalloutFontSize = 6f;
    private static readonly object LicenseSync = new();
    private static bool _licenseConfigured;

    public Task ExportAsync(
        ReportData report,
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

        Document.Create(container => ComposeDocument(container, report))
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

    private static void ComposeDocument(IDocumentContainer container, ReportData report)
    {
        var hasRenderableLayouts = HasRenderableLayouts(report);

        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(32);
            page.DefaultTextStyle(style => style.FontSize(10));

            page.Header().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(Display(report.Settings.ReportTitle, "Nesting Report")).FontSize(20).SemiBold();
                column.Item().Text(Display(report.Settings.CompanyName, "PanelNester"));
                column.Item().Text(text =>
                {
                    text.Span("Project: ").SemiBold();
                    text.Span(Display(report.Settings.ProjectJobName, report.ProjectMetadata.ProjectName, "Untitled Project"));
                });
                column.Item().Text(text =>
                {
                    text.Span("Job #: ").SemiBold();
                    text.Span(Display(report.Settings.ProjectJobNumber, report.ProjectMetadata.ProjectNumber, "Not specified"));
                });
                column.Item().Text(text =>
                {
                    text.Span("Report Date: ").SemiBold();
                    text.Span(FormatDate(report.Settings.ReportDate));
                });
            });

            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(SectionCard).Column(section =>
                {
                    section.Spacing(6);
                    section.Item().Text("Project Summary").FontSize(14).SemiBold();
                    section.Item().Text(BuildProjectSummary(report, hasRenderableLayouts));
                    if (!string.IsNullOrWhiteSpace(report.Settings.Notes))
                    {
                        section.Item().Text(text =>
                        {
                            text.Span("Notes: ").SemiBold();
                            text.Span(report.Settings.Notes!.Trim());
                        });
                    }
                });

                if (!hasRenderableLayouts)
                {
                    column.Item().Element(SectionCard).Text("No nesting results are available for this report.");
                }

                foreach (var material in report.Materials)
                {
                    column.Item().Element(SectionCard).Column(section =>
                    {
                        section.Spacing(8);
                        section.Item().Text(material.MaterialName).FontSize(14).SemiBold();
                        section.Item().Text(
                            $"Sheets: {material.Summary.TotalSheets}  •  Placed: {material.Summary.TotalPlaced}  •  Unplaced: {material.Summary.TotalUnplaced}  •  Utilization: {FormatPercent(material.Summary.OverallUtilization)}");
                        section.Item().Text(
                            $"Sheet Size: {FormatDimension(material.SheetLength)} × {FormatDimension(material.SheetWidth)}{FormatCost(material.CostPerSheet)}");

                        if (material.Sheets.Count > 0)
                        {
                            section.Item().Text("Sheet Layouts").SemiBold();

                            foreach (var sheet in material.Sheets.OrderBy(item => item.SheetNumber))
                            {
                                section.Item().PaddingBottom(8).Row(row =>
                                {
                                    row.RelativeItem(3).Element(container => SheetDiagram(container, sheet));
                                    row.RelativeItem(2).Column(details =>
                                    {
                                        details.Spacing(4);
                                        details.Item().Text($"Sheet #{sheet.SheetNumber}").SemiBold();
                                        details.Item().Text($"Utilization: {FormatPercent(sheet.UtilizationPercent)}");
                                        details.Item().Text(
                                            $"Sheet Size: {FormatDimension(sheet.SheetLength)} × {FormatDimension(sheet.SheetWidth)}");
                                        details.Item().Text("Placements:").SemiBold();
                                        details.Item().Text(BuildPlacementSummary(sheet)).FontSize(9);
                                    });
                                });
                            }
                        }
                        else
                        {
                            section.Item().Text("No sheet layouts were produced for this material.");
                        }

                        if (material.UnplacedItems.Count > 0)
                        {
                            section.Item().Text("Unplaced Items").SemiBold();
                            section.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(5);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(TableHeaderCell).Text("Part");
                                    header.Cell().Element(TableHeaderCell).Text("Reason");
                                });

                                foreach (var item in material.UnplacedItems)
                                {
                                    table.Cell().Element(TableBodyCell).Text(Display(item.PartId, "(unnamed part)"));
                                    table.Cell().Element(TableBodyCell).Text(item.ReasonDescription);
                                }
                            });
                        }
                    });
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("PanelNester report");
                text.Span(" • ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static string BuildProjectSummary(ReportData report, bool hasRenderableLayouts)
    {
        var customerName = Display(report.ProjectMetadata.CustomerName, "Not specified");
        var estimator = Display(report.ProjectMetadata.Estimator, "Not specified");
        var drafter = Display(report.ProjectMetadata.Drafter, "Not specified");
        var projectManager = Display(report.ProjectMetadata.Pm, "Not specified");
        var revision = Display(report.ProjectMetadata.Revision, "Not specified");
        var materialCount = report.Materials.Count;
        var totalSheets = report.Materials.Sum(material => material.Summary.TotalSheets);

        return
            $"Customer: {customerName}  •  Estimator: {estimator}  •  Drafter: {drafter}  •  PM: {projectManager}  •  Revision: {revision}{Environment.NewLine}" +
            $"Materials: {materialCount}  •  Total Sheets: {totalSheets}  •  Overall Status: {(hasRenderableLayouts ? "Results available" : "No results")}";
    }

    private static string BuildPlacementSummary(ReportSheetDiagram sheet)
    {
        var orderedPlacements = sheet.Placements
            .OrderBy(placement => placement.X)
            .ThenBy(placement => placement.Y)
            .ThenBy(placement => placement.PartId, StringComparer.Ordinal)
            .ToArray();

        if (orderedPlacements.Length == 0)
        {
            return "No placements";
        }

        return string.Join(
            Environment.NewLine,
            orderedPlacements.Select((placement, index) =>
                $"{index + 1}. {Display(placement.PartId, "(unnamed part)")}: {FormatDimension(placement.Width)} × {FormatDimension(placement.Height)} at ({FormatDimension(placement.X)}, {FormatDimension(placement.Y)}){(placement.Rotated90 ? " rotated" : string.Empty)}"));
    }

    private static readonly Color[] PlacementPalette =
    [
        Colors.Blue.Lighten3,
        Colors.Green.Lighten3,
        Colors.Orange.Lighten3,
        Colors.Red.Lighten3,
        Colors.Purple.Lighten3
    ];

    private static IContainer SheetDiagram(IContainer container, ReportSheetDiagram sheet) =>
        BuildSheetDiagram(container, sheet);

    private static IContainer BuildSheetDiagram(IContainer container, ReportSheetDiagram sheet)
    {
        container
            .Height(160)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Background(Colors.Grey.Lighten4)
            .Svg(size => BuildSheetSvg(sheet, size));

        return container;
    }

    private static string BuildSheetSvg(ReportSheetDiagram sheet, Size size)
    {
        var width = size.Width;
        var height = size.Height;
        var svg = new StringBuilder();
        var orderedPlacements = sheet.Placements
            .OrderBy(item => item.X)
            .ThenBy(item => item.Y)
            .ThenBy(item => item.PartId, StringComparer.Ordinal)
            .ToArray();

        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{FormatSvgNumber(width)}\" height=\"{FormatSvgNumber(height)}\" viewBox=\"0 0 {FormatSvgNumber(width)} {FormatSvgNumber(height)}\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");

        const float padding = 6f;
        var availableWidth = Math.Max(0, width - padding * 2);
        var availableHeight = Math.Max(0, height - padding * 2);

        if (sheet.SheetLength <= 0 || sheet.SheetWidth <= 0 || availableWidth <= 0 || availableHeight <= 0)
        {
            svg.Append("</svg>");
            return svg.ToString();
        }

        var scaleX = availableWidth / (float)sheet.SheetLength;
        var scaleY = availableHeight / (float)sheet.SheetWidth;
        var scale = Math.Min(scaleX, scaleY);

        var sheetWidth = (float)sheet.SheetLength * scale;
        var sheetHeight = (float)sheet.SheetWidth * scale;
        var offsetX = padding + (availableWidth - sheetWidth) / 2f;
        var offsetY = padding + (availableHeight - sheetHeight) / 2f;

        svg.Append(
            $"<rect x=\"{FormatSvgNumber(offsetX)}\" y=\"{FormatSvgNumber(offsetY)}\" width=\"{FormatSvgNumber(sheetWidth)}\" height=\"{FormatSvgNumber(sheetHeight)}\" fill=\"#ffffff\" stroke=\"#262626\" stroke-width=\"1\"/>");

        if (orderedPlacements.Length == 0)
        {
            AppendEmptySheetState(svg, offsetX, offsetY, sheetWidth, sheetHeight);
            svg.Append("</svg>");
            return svg.ToString();
        }

        foreach (var (placement, index) in orderedPlacements.Select((item, index) => (item, index)))
        {
            var placementWidth = (float)placement.Width * scale;
            var placementHeight = (float)placement.Height * scale;
            if (placementWidth <= 0 || placementHeight <= 0)
            {
                continue;
            }

            var placementX = offsetX + (float)placement.X * scale;
            var placementY = offsetY + (float)placement.Y * scale;
            var placementColor = ResolvePlacementColor(placement.PartId);
            var colorHex = ToSvgColor(placementColor);

            svg.Append(
                $"<rect class=\"placement-panel\" x=\"{FormatSvgNumber(placementX)}\" y=\"{FormatSvgNumber(placementY)}\" width=\"{FormatSvgNumber(placementWidth)}\" height=\"{FormatSvgNumber(placementHeight)}\" fill=\"{colorHex}\" fill-opacity=\"0.6\" stroke=\"{colorHex}\" stroke-width=\"{FormatSvgNumber(MinimumPlacementStrokeWidth)}\"/>");

            if (!TryAppendPlacementLabel(svg, placement, placementX, placementY, placementWidth, placementHeight))
            {
                AppendPlacementCallout(svg, index + 1, placementX, placementY, placementWidth, placementHeight);
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void AppendEmptySheetState(
        StringBuilder svg,
        float offsetX,
        float offsetY,
        float sheetWidth,
        float sheetHeight)
    {
        var centerX = offsetX + (sheetWidth / 2f);
        var centerY = offsetY + (sheetHeight / 2f);
        svg.Append(
            $"<text class=\"placement-empty-state\" x=\"{FormatSvgNumber(centerX)}\" y=\"{FormatSvgNumber(centerY)}\" fill=\"#4b5563\" font-family=\"Arial, sans-serif\" font-size=\"10\" font-weight=\"600\" text-anchor=\"middle\" dominant-baseline=\"middle\">No placements</text>");
    }

    private static bool TryAppendPlacementLabel(
        StringBuilder svg,
        NestPlacement placement,
        float placementX,
        float placementY,
        float placementWidth,
        float placementHeight)
    {
        var label = Display(placement.PartId, "(unnamed part)");
        var labelWidth = Math.Max(1f, placementWidth - 6f);
        var labelHeight = Math.Max(1f, placementHeight - 4f);
        var fontSize = Math.Clamp(labelHeight * 0.6f, MinimumLabelFontSize, MaximumLabelFontSize);
        if (!CanRenderInlineLabel(label, labelWidth, labelHeight, fontSize))
        {
            return false;
        }

        var centerX = placementX + (placementWidth / 2f);
        var centerY = placementY + (placementHeight / 2f);
        svg.Append(
            $"<text class=\"placement-label\" x=\"{FormatSvgNumber(centerX)}\" y=\"{FormatSvgNumber(centerY)}\" fill=\"#111111\" font-family=\"Arial, sans-serif\" font-size=\"{FormatSvgNumber(fontSize)}\" font-weight=\"600\" text-anchor=\"middle\" dominant-baseline=\"middle\" textLength=\"{FormatSvgNumber(labelWidth)}\" lengthAdjust=\"spacingAndGlyphs\" paint-order=\"stroke\" stroke=\"#ffffff\" stroke-width=\"1\">{EscapeSvgText(label)}</text>");

        return true;
    }

    private static bool CanRenderInlineLabel(string label, float labelWidth, float labelHeight, float fontSize) =>
        labelWidth >= 18f &&
        labelHeight >= 10f &&
        EstimateTextWidth(label, fontSize) <= labelWidth;

    private static float EstimateTextWidth(string label, float fontSize) =>
        Math.Max(fontSize, label.Length * fontSize * 0.58f);

    private static void AppendPlacementCallout(
        StringBuilder svg,
        int calloutNumber,
        float placementX,
        float placementY,
        float placementWidth,
        float placementHeight)
    {
        var calloutText = calloutNumber.ToString(CultureInfo.InvariantCulture);
        var badgeWidth = Math.Max(10f, (calloutText.Length * 4.5f) + 4f);
        var badgeHeight = 10f;
        var badgeX = placementX + 1f;
        var badgeY = placementY + 1f;

        if (badgeWidth > placementWidth)
        {
            badgeX = placementX + Math.Max(0f, (placementWidth - badgeWidth) / 2f);
        }
        else if (badgeX + badgeWidth > placementX + placementWidth)
        {
            badgeX = placementX + placementWidth - badgeWidth - 1f;
        }

        if (badgeHeight > placementHeight)
        {
            badgeY = placementY + Math.Max(0f, (placementHeight - badgeHeight) / 2f);
        }
        else if (badgeY + badgeHeight > placementY + placementHeight)
        {
            badgeY = placementY + placementHeight - badgeHeight - 1f;
        }

        var textX = badgeX + (badgeWidth / 2f);
        var textY = badgeY + (badgeHeight / 2f) + 0.2f;

        svg.Append(
            $"<rect class=\"placement-callout-badge\" x=\"{FormatSvgNumber(badgeX)}\" y=\"{FormatSvgNumber(badgeY)}\" width=\"{FormatSvgNumber(badgeWidth)}\" height=\"{FormatSvgNumber(badgeHeight)}\" rx=\"2\" ry=\"2\" fill=\"#ffffff\" stroke=\"#111111\" stroke-width=\"{FormatSvgNumber(MinimumPlacementStrokeWidth)}\"/>");
        svg.Append(
            $"<text class=\"placement-callout-label\" x=\"{FormatSvgNumber(textX)}\" y=\"{FormatSvgNumber(textY)}\" fill=\"#111111\" font-family=\"Arial, sans-serif\" font-size=\"{FormatSvgNumber(CalloutFontSize)}\" font-weight=\"700\" text-anchor=\"middle\" dominant-baseline=\"middle\">{EscapeSvgText(calloutText)}</text>");
    }

    private static Color ResolvePlacementColor(string? partId)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            return PlacementPalette[0];
        }

        var hash = StringComparer.Ordinal.GetHashCode(partId);
        var index = (hash & 0x7FFFFFFF) % PlacementPalette.Length;
        return PlacementPalette[index];
    }

    private static string ToSvgColor(Color color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static string EscapeSvgText(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string FormatSvgNumber(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static IContainer SectionCard(IContainer container) =>
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(12);

    private static IContainer TableHeaderCell(IContainer container) =>
        container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingBottom(4)
            .PaddingRight(6);

    private static IContainer TableBodyCell(IContainer container) =>
        container
            .PaddingTop(4)
            .PaddingBottom(4)
            .PaddingRight(6);

    private static string Display(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Display(string? primary, string? secondary, string fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : !string.IsNullOrWhiteSpace(secondary)
                ? secondary.Trim()
                : fallback;

    private static string FormatDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");

    private static string FormatDimension(decimal value) =>
        $"{value:0.###}\"";

    private static string FormatPercent(decimal value) =>
        $"{value.ToString("0.0", CultureInfo.InvariantCulture)}%";

    private static string FormatCost(decimal? value) =>
        value.HasValue ? $"  •  Cost/Sheet: {value.Value:C}" : string.Empty;

    private static bool HasRenderableLayouts(ReportData report) =>
        report.Materials.Any(material => material.Sheets.Any(sheet => sheet.Placements.Count > 0));
}
