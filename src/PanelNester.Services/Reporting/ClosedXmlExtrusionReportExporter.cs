using ClosedXML.Excel;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using System.Globalization;
using System.IO;

namespace PanelNester.Services.Reporting;

public sealed class ClosedXmlExtrusionReportExporter : IExtrusionExcelReportExporter
{
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

        using var workbook = new XLWorkbook();
        WriteSummary(workbook.Worksheets.Add("Overall Summary"), report.OverallLengths);
        WriteByGroup(workbook.Worksheets.Add("By Group"), report.Groups);
        WriteSegments(workbook.Worksheets.Add("Segments"), report.Segments);
        workbook.SaveAs(filePath);

        return Task.CompletedTask;
    }

    private static void WriteSummary(IXLWorksheet worksheet, IReadOnlyList<ExtrusionLengthSummary> rows)
    {
        worksheet.Cell(1, 1).Value = "Extrusion Summary";
        worksheet.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.FontSize = 14;
        WriteLengthHeader(worksheet, 3);

        var rowIndex = 4;
        foreach (var row in rows)
        {
            WriteLengthRow(worksheet, rowIndex++, row);
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 6);
    }

    private static void WriteByGroup(IXLWorksheet worksheet, IReadOnlyList<ExtrusionGroupSummary> groups)
    {
        worksheet.Cell(1, 1).Value = "Extrusion Summary by Optimization Group and Part Group";
        worksheet.Range(1, 1, 1, 8).Merge().Style.Font.SetBold().Font.FontSize = 14;
        worksheet.Cell(3, 1).Value = "Optimization Group";
        worksheet.Cell(3, 2).Value = "Part Group";
        WriteLengthHeader(worksheet, 3, startColumn: 3);

        var rowIndex = 4;
        foreach (var group in groups)
        {
            foreach (var row in group.Lengths)
            {
                worksheet.Cell(rowIndex, 1).Value = group.OptimizationGroupName;
                worksheet.Cell(rowIndex, 2).Value = group.GroupName;
                WriteLengthRow(worksheet, rowIndex++, row, startColumn: 3);
            }
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 8);
    }

    private static void WriteSegments(IXLWorksheet worksheet, IReadOnlyList<ExtrusionSegmentDetail> segments)
    {
        worksheet.Cell(1, 1).Value = "Extrusion Segments";
        worksheet.Range(1, 1, 1, 6).Merge().Style.Font.SetBold().Font.FontSize = 14;
        worksheet.Cell(3, 1).Value = "Optimization Group";
        worksheet.Cell(3, 2).Value = "Part Group";
        worksheet.Cell(3, 3).Value = "Category";
        worksheet.Cell(3, 4).Value = "Extrusion";
        worksheet.Cell(3, 5).Value = "Location";
        worksheet.Cell(3, 6).Value = "Length";

        var rowIndex = 4;
        foreach (var segment in segments)
        {
            worksheet.Cell(rowIndex, 1).Value = segment.OptimizationGroupName;
            worksheet.Cell(rowIndex, 2).Value = segment.GroupName;
            worksheet.Cell(rowIndex, 3).Value = segment.Category;
            worksheet.Cell(rowIndex, 4).Value = segment.ExtrusionName;
            worksheet.Cell(rowIndex, 5).Value = segment.Location;
            worksheet.Cell(rowIndex, 6).Value = FormatDimension(segment.LengthInches);
            rowIndex++;
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 6);
    }

    private static void WriteLengthHeader(IXLWorksheet worksheet, int row, int startColumn = 1)
    {
        worksheet.Cell(row, startColumn).Value = "Category";
        worksheet.Cell(row, startColumn + 1).Value = "Extrusion";
        worksheet.Cell(row, startColumn + 2).Value = "Running Feet";
        worksheet.Cell(row, startColumn + 3).Value = "Segments";
        worksheet.Cell(row, startColumn + 4).Value = "Stick Feet";
        worksheet.Cell(row, startColumn + 5).Value = "Required Sticks";
    }

    private static void WriteLengthRow(
        IXLWorksheet worksheet,
        int rowIndex,
        ExtrusionLengthSummary row,
        int startColumn = 1)
    {
        worksheet.Cell(rowIndex, startColumn).Value = row.Category;
        worksheet.Cell(rowIndex, startColumn + 1).Value = row.ExtrusionName;
        worksheet.Cell(rowIndex, startColumn + 2).Value = row.TotalLinearFeet;
        worksheet.Cell(rowIndex, startColumn + 2).Style.NumberFormat.Format = "0.###";
        worksheet.Cell(rowIndex, startColumn + 3).Value = row.SegmentCount;
        worksheet.Cell(rowIndex, startColumn + 4).Value = row.StickLengthFeet;
        worksheet.Cell(rowIndex, startColumn + 4).Style.NumberFormat.Format = "0.###";
        worksheet.Cell(rowIndex, startColumn + 5).Value = row.RequiredStickCount;
    }

    private static void FinalizeTable(IXLWorksheet worksheet, int startRow, int endRow, int columns)
    {
        var header = worksheet.Range(startRow, 1, startRow, columns);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        if (endRow > startRow)
        {
            var table = worksheet.Range(startRow, 1, endRow, columns).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        worksheet.SheetView.FreezeRows(startRow);
        worksheet.Columns().AdjustToContents();
    }

    private static string FormatDimension(decimal value) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)}\"";
}
