using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class XlsxImportServiceSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.XlsxImportServiceSpecs.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public async Task Excel_import_reads_the_requested_worksheet(string extension)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, $"selected-worksheet{extension}");

        using (var workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook.AddWorksheet("First"), "FIRST");
            WriteWorksheet(workbook.AddWorksheet("Second"), "SECOND");
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            WorksheetName = "Second"
        });

        Assert.True(response.Success);
        Assert.Equal("SECOND", Assert.Single(response.Parts).ImportedId);
        Assert.Equal("Second", response.Worksheet?.WorksheetName);
        Assert.Equal(2, response.Worksheet?.OriginalPosition);
    }

    [Fact]
    public async Task Excel_import_uses_the_confirmed_shifted_heading_range_for_its_table_region()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "shifted-heading-range.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            worksheet.Cell("A1").Value = "This title is outside the table";
            worksheet.Cell("B3").Value = "Id";
            worksheet.Cell("C3").Value = "Length";
            worksheet.Cell("D3").Value = "Width";
            worksheet.Cell("E3").Value = "Quantity";
            worksheet.Cell("F3").Value = "Material";
            worksheet.Cell("G3").Value = "Notes outside the confirmed range";
            worksheet.Cell("B4").Value = "P-200";
            worksheet.Cell("C4").Value = 30;
            worksheet.Cell("D4").Value = 12;
            worksheet.Cell("E4").Value = 3;
            worksheet.Cell("F4").Value = "Demo Material";
            worksheet.Cell("G4").Value = "ignored";
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            WorksheetName = "Parts",
            HeadingRange = "B3:F3"
        });

        Assert.True(response.Success);
        Assert.Equal("P-200", Assert.Single(response.Parts).ImportedId);
        Assert.Equal("B3:F3", response.Worksheet?.HeadingRange);
        Assert.Equal(["B", "C", "D", "E", "F"], response.AvailableColumns);
        Assert.DoesNotContain(response.SourceColumns, column => column.Address == "G");
    }

    [Theory]
    [InlineData("A1:E2")]
    [InlineData("A1:C1,E1:F1")]
    public async Task Excel_import_rejects_multi_row_and_noncontiguous_heading_ranges(string headingRange)
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "invalid-heading-range.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook.AddWorksheet("Parts"), "P-001");
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = headingRange
        });

        Assert.False(response.Success);
        Assert.Contains(response.Errors, error => error.Code == "invalid-heading-range");
    }

    [Fact]
    public async Task Excel_import_rejects_merged_cells_inside_the_heading_range()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "merged-heading-range.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteWorksheet(worksheet, "P-001");
            worksheet.Range("A1:B1").Merge();
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "A1:E1"
        });

        Assert.False(response.Success);
        Assert.Contains(response.Errors, error => error.Code == "merged-heading-range");
    }

    [Fact]
    public async Task Excel_import_uses_column_addresses_to_distinguish_duplicate_headings()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "duplicate-headings.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            string[] headings = ["Id", "Length", "Width", "Quantity", "Material", "Width"];
            for (var index = 0; index < headings.Length; index++)
            {
                worksheet.Cell(1, index + 1).Value = headings[index];
            }

            worksheet.Cell(2, 1).Value = "P-001";
            worksheet.Cell(2, 2).Value = 20;
            worksheet.Cell(2, 3).Value = 10;
            worksheet.Cell(2, 4).Value = 1;
            worksheet.Cell(2, 5).Value = "Demo Material";
            worksheet.Cell(2, 6).Value = 42;
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            Options = new ImportOptions
            {
                ColumnMappings =
                [
                    new ImportColumnMapping
                    {
                        SourceColumn = "F",
                        TargetField = ImportFieldNames.Width
                    }
                ]
            }
        });

        Assert.True(response.Success);
        Assert.Equal(42m, Assert.Single(response.Parts).Width);
        Assert.Contains(
            response.SourceColumns,
            column => column.Address == "C" && column.Heading == "Width");
        Assert.Contains(
            response.SourceColumns,
            column => column.Address == "F" && column.Heading == "Width");
        Assert.Contains(
            response.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Width && mapping.SourceColumn == "F");
    }

    [Fact]
    public async Task Excel_import_leaves_duplicate_normalized_headings_unresolved_without_an_explicit_mapping()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "ambiguous-duplicate-headings.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            string[] headings = ["Id", "Length", "Width", "Quantity", "Material", "W-i-d-t-h"];
            for (var index = 0; index < headings.Length; index++)
            {
                worksheet.Cell(1, index + 1).Value = headings[index];
            }

            worksheet.Cell(2, 1).Value = "P-001";
            worksheet.Cell(2, 2).Value = 20;
            worksheet.Cell(2, 3).Value = 10;
            worksheet.Cell(2, 4).Value = 1;
            worksheet.Cell(2, 5).Value = "Demo Material";
            worksheet.Cell(2, 6).Value = 42;
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath
        });

        var widthMapping = Assert.Single(
            response.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Width);
        Assert.False(response.Success);
        Assert.Null(widthMapping.SourceColumn);
        Assert.Null(widthMapping.SuggestedSourceColumn);
        Assert.Contains(
            response.Errors,
            error => error.Code == "missing-column" && error.Message.Contains("Width", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Excel_import_ignores_blank_heading_columns_and_warns_when_they_contain_data()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "blank-heading-data.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            string[] headings = ["Id", "Length", "Width", "Quantity", "Material"];
            for (var index = 0; index < headings.Length; index++)
            {
                worksheet.Cell(1, index + 1).Value = headings[index];
            }

            worksheet.Cell("A2").Value = "P-001";
            worksheet.Cell("B2").Value = 20;
            worksheet.Cell("C2").Value = 10;
            worksheet.Cell("D2").Value = 1;
            worksheet.Cell("E2").Value = "Demo Material";
            worksheet.Cell("F2").Value = "must be ignored";
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "A1:F1"
        });

        Assert.True(response.Success);
        Assert.DoesNotContain(response.SourceColumns, column => column.Address == "F");
        Assert.Contains(
            response.Warnings,
            warning => warning.Code == "ignored-data-without-heading" &&
                       warning.Message.Contains("F", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Excel_import_prefills_unique_aliases_for_a_shifted_and_reordered_schema()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "shifted-reordered-aliases.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            string[] headings = ["Stock", "Qty", "Part No", "Wid", "Len"];
            for (var index = 0; index < headings.Length; index++)
            {
                worksheet.Cell(3, index + 3).Value = headings[index];
            }

            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "C3:G3"
        });

        Assert.False(response.Success);
        Assert.Collection(
            response.ColumnMappings.Take(5),
            mapping => Assert.Equal("E", mapping.SuggestedSourceColumn),
            mapping => Assert.Equal("G", mapping.SuggestedSourceColumn),
            mapping => Assert.Equal("F", mapping.SuggestedSourceColumn),
            mapping => Assert.Equal("D", mapping.SuggestedSourceColumn),
            mapping => Assert.Equal("C", mapping.SuggestedSourceColumn));
    }

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

        Assert.Equivalent(WithoutSourceIdentity(csvResponse), WithoutSourceIdentity(xlsxResponse), strict: true);
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
    public async Task Locked_xlsx_files_return_a_file_in_use_error_instead_of_throwing()
    {
        Directory.CreateDirectory(_workspacePath);
        var xlsxPath = Path.Combine(_workspacePath, "locked.xlsx");

        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Parts");
            string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];

            for (var column = 0; column < headers.Length; column++)
            {
                sheet.Cell(1, column + 1).Value = headers[column];
            }

            sheet.Cell(2, 1).Value = "P-001";
            sheet.Cell(2, 2).Value = 12.5m;
            sheet.Cell(2, 3).Value = 48m;
            sheet.Cell(2, 4).Value = 2;
            sheet.Cell(2, 5).Value = "Demo Material";
            workbook.SaveAs(xlsxPath);
        }

        using var lockStream = new FileStream(xlsxPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = xlsxPath });

        Assert.False(response.Success);
        var error = Assert.Single(response.Errors);
        Assert.Equal("file-in-use", error.Code);
        Assert.Contains("Close the file and try importing again.", error.Message);
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

        Assert.Equivalent(WithoutSourceIdentity(csvResponse), WithoutSourceIdentity(xlsxResponse), strict: true);
    }

    [Fact]
    public async Task Xlsx_import_merges_like_rows_using_group_when_available()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "merged-parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "merged-parts.xlsx");
        var csvService = new CsvImportService();
        var xlsxService = new XlsxImportService();

        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material,Group
            P-001,24,96,2,Demo Material,A
            P-001,24.00,96.00,3,Demo Material,A
            P-001,24,96,4,Demo Material,B
            P-002,12,24,1,Demo Material,
            P-002,12,24,2,Demo Material,
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
            sheet.Cell(2, 2).Value = 24m;
            sheet.Cell(2, 3).Value = 96m;
            sheet.Cell(2, 4).Value = 2;
            sheet.Cell(2, 5).Value = "Demo Material";
            sheet.Cell(2, 6).Value = "A";

            sheet.Cell(3, 1).Value = "P-001";
            sheet.Cell(3, 2).Value = 24m;
            sheet.Cell(3, 3).Value = 96m;
            sheet.Cell(3, 4).Value = 3;
            sheet.Cell(3, 5).Value = "Demo Material";
            sheet.Cell(3, 6).Value = "A";

            sheet.Cell(4, 1).Value = "P-001";
            sheet.Cell(4, 2).Value = 24m;
            sheet.Cell(4, 3).Value = 96m;
            sheet.Cell(4, 4).Value = 4;
            sheet.Cell(4, 5).Value = "Demo Material";
            sheet.Cell(4, 6).Value = "B";

            sheet.Cell(5, 1).Value = "P-002";
            sheet.Cell(5, 2).Value = 12m;
            sheet.Cell(5, 3).Value = 24m;
            sheet.Cell(5, 4).Value = 1;
            sheet.Cell(5, 5).Value = "Demo Material";
            sheet.Cell(5, 6).Value = string.Empty;

            sheet.Cell(6, 1).Value = "P-002";
            sheet.Cell(6, 2).Value = 12m;
            sheet.Cell(6, 3).Value = 24m;
            sheet.Cell(6, 4).Value = 2;
            sheet.Cell(6, 5).Value = "Demo Material";
            sheet.Cell(6, 6).Value = string.Empty;

            workbook.SaveAs(xlsxPath);
        }

        var csvResponse = await csvService.ImportAsync(new ImportRequest { FilePath = csvPath });
        var xlsxResponse = await xlsxService.ImportAsync(new ImportRequest { FilePath = xlsxPath });

        Assert.Equivalent(WithoutSourceIdentity(csvResponse), WithoutSourceIdentity(xlsxResponse), strict: true);
        Assert.Equal(3, xlsxResponse.Parts.Count);
        Assert.Equal(5, xlsxResponse.Parts[0].Quantity);
        Assert.Equal(4, xlsxResponse.Parts[1].Quantity);
        Assert.Equal(3, xlsxResponse.Parts[2].Quantity);
    }

    private static ImportResponse WithoutSourceIdentity(ImportResponse response) =>
        response with
        {
            Worksheet = null,
            AvailableColumns = Array.Empty<string>(),
            SourceColumns = Array.Empty<ImportSourceColumn>(),
            ColumnMappings = Array.Empty<ImportFieldMappingStatus>(),
            Parts = response.Parts.Select(part => part with
            {
                SourceReferences = Array.Empty<SourceReference>()
            }).ToArray()
        };

    private static void WriteWorksheet(IXLWorksheet worksheet, string partId)
    {
        string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];
        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        worksheet.Cell(2, 1).Value = partId;
        worksheet.Cell(2, 2).Value = 20;
        worksheet.Cell(2, 3).Value = 10;
        worksheet.Cell(2, 4).Value = 1;
        worksheet.Cell(2, 5).Value = "Demo Material";
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
