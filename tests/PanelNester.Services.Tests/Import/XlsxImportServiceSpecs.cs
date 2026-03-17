using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class XlsxImportServiceSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.XlsxImportServiceSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Xlsx_import_matches_csv_validation_output_for_equivalent_rows()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "parts.xlsx");
        var materials = new[]
        {
            DemoMaterialCatalog.Phase1,
            DemoMaterialCatalog.Phase1 with { MaterialId = "demo-material-2", Name = "Baltic Birch" }
        };
        var csvService = new CsvImportService(materials);
        var xlsxService = new XlsxImportService(materials);

        await File.WriteAllTextAsync(
            csvPath,
            """
            Material,Quantity,Length,Notes,Id,Width
            Demo Material,2,12.5,ok,P-001,48
            Unknown Material,10001,oops,needs review,P-001,24
            """);

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Parts");
            string[] headers = ["Material", "Quantity", "Length", "Notes", "Id", "Width"];

            for (var column = 0; column < headers.Length; column++)
            {
                sheet.Cell(1, column + 1).Value = headers[column];
            }

            sheet.Cell(2, 1).Value = "Demo Material";
            sheet.Cell(2, 2).Value = 2;
            sheet.Cell(2, 3).Value = 12.5m;
            sheet.Cell(2, 4).Value = "ok";
            sheet.Cell(2, 5).Value = "P-001";
            sheet.Cell(2, 6).Value = 48;

            sheet.Cell(3, 1).Value = "Unknown Material";
            sheet.Cell(3, 2).Value = 10001;
            sheet.Cell(3, 3).Value = "oops";
            sheet.Cell(3, 4).Value = "needs review";
            sheet.Cell(3, 5).Value = "P-001";
            sheet.Cell(3, 6).Value = 24;

            workbook.SaveAs(xlsxPath);
        }

        var csvResponse = await csvService.ImportAsync(new ImportRequest { FilePath = csvPath });
        var xlsxResponse = await xlsxService.ImportAsync(new ImportRequest { FilePath = xlsxPath });

        Assert.Equivalent(csvResponse, xlsxResponse, strict: true);
    }

    [Fact]
    public async Task Empty_workbooks_return_an_actionable_error()
    {
        Directory.CreateDirectory(_workspacePath);
        var xlsxPath = Path.Combine(_workspacePath, "empty.xlsx");

        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Empty");
            workbook.SaveAs(xlsxPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = xlsxPath });

        Assert.False(response.Success);
        var error = Assert.Single(response.Errors);
        Assert.Equal("empty-workbook", error.Code);
    }

    [Fact]
    public async Task Xlsx_group_mapping_matches_csv_group_mapping_output()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "grouped-parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "grouped-parts.xlsx");
        var csvService = new CsvImportService();
        var xlsxService = new XlsxImportService();

        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material,Group
            P-001,12.5,48,2,Demo Material,Casework
            P-002,10,24,1,Demo Material,
            """);

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Parts");
            string[] headers = ["Id", "Length", "Width", "Quantity", "Material", "Group"];

            for (var column = 0; column < headers.Length; column++)
            {
                sheet.Cell(1, column + 1).Value = headers[column];
            }

            sheet.Cell(2, 1).Value = "P-001";
            sheet.Cell(2, 2).Value = 12.5m;
            sheet.Cell(2, 3).Value = 48;
            sheet.Cell(2, 4).Value = 2;
            sheet.Cell(2, 5).Value = "Demo Material";
            sheet.Cell(2, 6).Value = "Casework";

            sheet.Cell(3, 1).Value = "P-002";
            sheet.Cell(3, 2).Value = 10m;
            sheet.Cell(3, 3).Value = 24;
            sheet.Cell(3, 4).Value = 1;
            sheet.Cell(3, 5).Value = "Demo Material";
            sheet.Cell(3, 6).Value = string.Empty;

            workbook.SaveAs(xlsxPath);
        }

        var csvResponse = await csvService.ImportAsync(new ImportRequest { FilePath = csvPath });
        var xlsxResponse = await xlsxService.ImportAsync(new ImportRequest { FilePath = xlsxPath });

        Assert.Equivalent(csvResponse, xlsxResponse, strict: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
