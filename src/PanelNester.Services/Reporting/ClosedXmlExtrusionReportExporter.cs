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
        WriteLengthHeader(worksheet, 3, includeGroup: false);

        var rowIndex = 4;
        foreach (var row in rows)
        {
            WriteLengthRow(worksheet, rowIndex++, null, row);
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 6);
    }

    private static void WriteByGroup(IXLWorksheet worksheet, IReadOnlyList<ExtrusionGroupSummary> groups)
    {
        worksheet.Cell(1, 1).Value = "Extrusion Summary By Group";
        worksheet.Range(1, 1, 1, 7).Merge().Style.Font.SetBold().Font.FontSize = 14;
        WriteLengthHeader(worksheet, 3, includeGroup: true);

        var rowIndex = 4;
        foreach (var group in groups)
        {
            foreach (var row in group.Lengths)
            {
                WriteLengthRow(worksheet, rowIndex++, group.GroupName, row);
            }
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 7);
    }

    private static void WriteSegments(IXLWorksheet worksheet, IReadOnlyList<ExtrusionSegmentDetail> segments)
    {
        worksheet.Cell(1, 1).Value = "Extrusion Segments";
        worksheet.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.FontSize = 14;
        worksheet.Cell(3, 1).Value = "Group";
        worksheet.Cell(3, 2).Value = "Category";
        worksheet.Cell(3, 3).Value = "Extrusion";
        worksheet.Cell(3, 4).Value = "Location";
        worksheet.Cell(3, 5).Value = "Length";

        var rowIndex = 4;
        foreach (var segment in segments)
        {
            worksheet.Cell(rowIndex, 1).Value = segment.GroupName;
            worksheet.Cell(rowIndex, 2).Value = segment.Category;
            worksheet.Cell(rowIndex, 3).Value = segment.ExtrusionName;
            worksheet.Cell(rowIndex, 4).Value = segment.Location;
            worksheet.Cell(rowIndex, 5).Value = FormatDimension(segment.LengthInches);
            rowIndex++;
        }

        FinalizeTable(worksheet, 3, Math.Max(3, rowIndex - 1), 5);
    }

    private static void WriteLengthHeader(IXLWorksheet worksheet, int row, bool includeGroup)
    {
        var offset = includeGroup ? 1 : 0;
        if (includeGroup)
        {
            worksheet.Cell(row, 1).Value = "Group";
        }

        worksheet.Cell(row, 1 + offset).Value = "Category";
        worksheet.Cell(row, 2 + offset).Value = "Extrusion";
        worksheet.Cell(row, 3 + offset).Value = "Running Feet";
        worksheet.Cell(row, 4 + offset).Value = "Segments";
        worksheet.Cell(row, 5 + offset).Value = "Stick Feet";
        worksheet.Cell(row, 6 + offset).Value = "Required Sticks";
    }

    private static void WriteLengthRow(
        IXLWorksheet worksheet,
        int rowIndex,
        string? groupName,
        ExtrusionLengthSummary row)
    {
        var offset = groupName is null ? 0 : 1;
        if (groupName is not null)
        {
            worksheet.Cell(rowIndex, 1).Value = groupName;
        }

        worksheet.Cell(rowIndex, 1 + offset).Value = row.Category;
        worksheet.Cell(rowIndex, 2 + offset).Value = row.ExtrusionName;
        worksheet.Cell(rowIndex, 3 + offset).Value = row.TotalLinearFeet;
        worksheet.Cell(rowIndex, 3 + offset).Style.NumberFormat.Format = "0.###";
        worksheet.Cell(rowIndex, 4 + offset).Value = row.SegmentCount;
        worksheet.Cell(rowIndex, 5 + offset).Value = row.StickLengthFeet;
        worksheet.Cell(rowIndex, 5 + offset).Style.NumberFormat.Format = "0.###";
        worksheet.Cell(rowIndex, 6 + offset).Value = row.RequiredStickCount;
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
