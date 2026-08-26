using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
        AddChartOnlyTab(workbookPath);

        var result = await new WorkbookDiscoveryService().DiscoverAsync(workbookPath);

        Assert.Equal(macrosPresent, result.MacrosPresent);
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

    private static void AddChartOnlyTab(string workbookPath)
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

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }
}
