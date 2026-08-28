using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class WorkbookDiscoveryServiceSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester-WorkbookDiscovery-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(".xlsx", false)]
    [InlineData(".xlsm", true)]
    public async Task Discovery_returns_visible_nonempty_worksheets_in_workbook_order(
        string extension,
        bool macrosPresent)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, $"discovery{extension}");

        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Empty");
            workbook.AddWorksheet("First").Cell("B3").Value = "Part";
            workbook.AddWorksheet("Hidden").Cell("A1").Value = "Hidden part";
            workbook.Worksheet("Hidden").Visibility = XLWorksheetVisibility.Hidden;
            workbook.AddWorksheet("Second").Cell("D7").Value = 42;
            workbook.AddWorksheet("Very Hidden").Cell("A1").Value = "Secret";
            workbook.Worksheet("Very Hidden").Visibility = XLWorksheetVisibility.VeryHidden;
            workbook.SaveAs(workbookPath);
        }
        AddChartSheet(workbookPath);

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        Assert.Equal(macrosPresent, result.MacrosPresent);
        Assert.Equal("First", result.InitialWorksheetName);
        Assert.Collection(
            result.Worksheets,
            worksheet =>
            {
                Assert.Equal("First", worksheet.WorksheetName);
                Assert.Equal(2, worksheet.OriginalPosition);
            },
            worksheet =>
            {
                Assert.Equal("Second", worksheet.WorksheetName);
                Assert.Equal(4, worksheet.OriginalPosition);
            });
    }

    [Fact]
    public async Task Discovery_detects_a_unique_heading_range_below_title_rows_and_returns_a_bounded_preview()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "headings-below-title.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            worksheet.Cell("A1").Value = "North elevation panels";
            worksheet.Cell("A3").Value = "Part No";
            worksheet.Cell("B3").Value = "Length";
            worksheet.Cell("C3").Value = "Width";
            worksheet.Cell("D3").Value = "Qty";
            worksheet.Cell("E3").Value = "Material";
            worksheet.Cell("F3").Value = "Part Group";
            worksheet.Cell("A4").Value = "P-100";
            worksheet.Cell("B4").Value = 48;
            worksheet.Cell("C4").Value = 24;
            worksheet.Cell("D4").Value = 2;
            worksheet.Cell("E4").Value = "ACM";
            worksheet.Cell("F4").Value = "North";
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal("A3:F3", worksheetResult.HeadingRange);
        Assert.Equal("unique-high-confidence", worksheetResult.HeadingRangeDetectionStatus);
        var candidate = Assert.Single(worksheetResult.HeadingRangeCandidates);
        Assert.Equal("A3:F3", candidate.Address);
        Assert.True(candidate.IsHighConfidence);
        Assert.Contains(
            worksheetResult.PreviewRows,
            row => row.RowNumber == 3 && row.Cells.Any(cell => cell.Address == "D3" && cell.Value == "Qty"));
        Assert.True(worksheetResult.PreviewRows.Count <= 25);
        Assert.All(worksheetResult.PreviewRows, row => Assert.True(row.Cells.Count <= 16));
    }

    [Fact]
    public async Task Stock_length_discovery_scores_required_and_optional_headings_for_a_unique_Heading_Range()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "stock-headings.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Required Pieces");
            worksheet.Cell("A1").Value = "Cut list";
            worksheet.Cell("B4").Value = "Qty";
            worksheet.Cell("C4").Value = "Length";
            worksheet.Cell("D4").Value = "Die";
            worksheet.Cell("E4").Value = "Description";
            worksheet.Cell("F4").Value = "Finish";
            worksheet.Cell("G4").Value = "Part No";
            worksheet.Cell("B5").Value = 2;
            worksheet.Cell("C5").Value = 48;
            worksheet.Cell("D5").Value = "P-100";
            worksheet.Cell("E5").Value = "Jamb";
            worksheet.Cell("F5").Value = "Clear";
            worksheet.Cell("G5").Value = "A-1";
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService()
            .DiscoverAsync(workbookPath, ProjectKind.StockLength);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal(5, worksheetResult.UsedRowCount);
        Assert.Equal("B4:G4", worksheetResult.HeadingRange);
        Assert.Equal(
            HeadingRangeDetectionStatuses.UniqueHighConfidence,
            worksheetResult.HeadingRangeDetectionStatus);
    }

    [Fact]
    public async Task Discovery_identifies_hidden_rows_and_columns_in_the_Worksheet_preview()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "hidden-preview-content.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteCandidate(worksheet, 1, "VISIBLE");
            worksheet.Cell("F1").Value = "Part Group";
            worksheet.Cell("F2").Value = "Hidden column value";
            worksheet.Column(6).Hide();
            worksheet.Row(2).Hide();
            worksheet.Cell("Z1").Value = "Hidden beyond preview cap";
            worksheet.Column(26).Hide();
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var preview = Assert.Single(result.Worksheets).PreviewRows;
        Assert.True(preview.Single(row => row.RowNumber == 1).Cells.Single(cell => cell.Address == "F1").IsHidden);
        Assert.True(preview.Single(row => row.RowNumber == 1).Cells.Single(cell => cell.Address == "Z1").IsHidden);
        Assert.All(preview.Single(row => row.RowNumber == 2).Cells, cell => Assert.True(cell.IsHidden));
    }

    [Fact]
    public async Task Discovery_identifies_formula_derived_cells_in_the_Worksheet_preview()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "formula-preview.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteCandidate(worksheet, 1, "P-100");
            worksheet.Cell("B2").FormulaA1 = "20+28";
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var formulaCell = Assert.Single(result.Worksheets).PreviewRows
            .Single(row => row.RowNumber == 2).Cells.Single(cell => cell.Address == "B2");
        Assert.True(formulaCell.IsFormula);
    }

    [Fact]
    public async Task Discovery_leaves_tied_heading_candidates_unset_for_manual_choice()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "tied-headings.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteCandidate(worksheet, 2, "P-100");
            WriteCandidate(worksheet, 7, "P-200");
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal("tied", worksheetResult.HeadingRangeDetectionStatus);
        Assert.Equal(string.Empty, worksheetResult.HeadingRange);
        Assert.Equal(
            ["A2:E2", "A7:E7"],
            worksheetResult.HeadingRangeCandidates
                .Where(candidate => candidate.IsHighConfidence)
                .Select(candidate => candidate.Address)
                .Order());
    }

    [Fact]
    public async Task Discovery_does_not_choose_between_unequal_high_confidence_Heading_Ranges()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "multiple-high-confidence-headings.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteCandidate(worksheet, 2, "P-100");
            WriteCandidate(worksheet, 7, "P-200");
            worksheet.Cell("F2").Value = "Part Group";
            worksheet.Cell("F3").Value = "North";
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal("tied", worksheetResult.HeadingRangeDetectionStatus);
        Assert.Equal(string.Empty, worksheetResult.HeadingRange);
        Assert.Equal(2, worksheetResult.HeadingRangeCandidates.Count(candidate => candidate.IsHighConfidence));
        Assert.Equal(2, worksheetResult.HeadingRangeCandidates.Count(candidate => candidate.IsTied));
    }

    [Fact]
    public async Task Discovery_leaves_low_confidence_candidates_unset()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "low-confidence.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            worksheet.Cell("C4").Value = "Part Number";
            worksheet.Cell("D4").Value = "Qty";
            worksheet.Cell("C5").Value = "P-100";
            worksheet.Cell("D5").Value = 2;
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal("low-confidence", worksheetResult.HeadingRangeDetectionStatus);
        Assert.Equal(string.Empty, worksheetResult.HeadingRange);
        Assert.False(Assert.Single(worksheetResult.HeadingRangeCandidates).IsHighConfidence);
    }

    [Fact]
    public async Task Discovery_marks_equal_low_confidence_candidates_as_tied()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "low-confidence-tie.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteLowConfidenceCandidate(worksheet, 2, "P-100");
            WriteLowConfidenceCandidate(worksheet, 7, "P-200");
            workbook.SaveAs(workbookPath);
        }

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        var worksheetResult = Assert.Single(result.Worksheets);
        Assert.Equal("tied", worksheetResult.HeadingRangeDetectionStatus);
        Assert.Equal(string.Empty, worksheetResult.HeadingRange);
        Assert.Equal(2, worksheetResult.HeadingRangeCandidates.Count(candidate => candidate.IsTied));
    }

    private static void AddChartSheet(string workbookPath)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, isEditable: true);
        var workbookPart = document.WorkbookPart!;
        var chartsheetPart = workbookPart.AddNewPart<ChartsheetPart>();
        chartsheetPart.Chartsheet = new Chartsheet();
        var sheets = workbookPart.Workbook.Sheets!;
        var nextSheetId = sheets.Elements<Sheet>().Max(sheet => sheet.SheetId!.Value) + 1;
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(chartsheetPart),
            Name = "Chart Only",
            SheetId = nextSheetId
        });
        workbookPart.Workbook.Save();
    }

    private static void WriteCandidate(IXLWorksheet worksheet, int rowNumber, string partId)
    {
        string[] headings = ["Id", "Length", "Width", "Quantity", "Material"];
        for (var index = 0; index < headings.Length; index++)
        {
            worksheet.Cell(rowNumber, index + 1).Value = headings[index];
        }

        worksheet.Cell(rowNumber + 1, 1).Value = partId;
        worksheet.Cell(rowNumber + 1, 2).Value = 48;
        worksheet.Cell(rowNumber + 1, 3).Value = 24;
        worksheet.Cell(rowNumber + 1, 4).Value = 1;
        worksheet.Cell(rowNumber + 1, 5).Value = "ACM";
    }

    private static void WriteLowConfidenceCandidate(
        IXLWorksheet worksheet,
        int rowNumber,
        string partId)
    {
        worksheet.Cell(rowNumber, 1).Value = "Part Number";
        worksheet.Cell(rowNumber, 2).Value = "Qty";
        worksheet.Cell(rowNumber + 1, 1).Value = partId;
        worksheet.Cell(rowNumber + 1, 2).Value = 2;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }
}
