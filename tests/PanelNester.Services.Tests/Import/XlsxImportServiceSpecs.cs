using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
            worksheet.Range("A1:G1").Merge();
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
    public async Task Excel_import_reads_hidden_and_filtered_rows_and_confirmed_hidden_columns()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "hidden-table-region-content.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteWorksheet(worksheet, "VISIBLE");
            worksheet.Cell("F1").Value = "Group";
            worksheet.Cell("F2").Value = "Visible Group";
            worksheet.Column(6).Hide();
            worksheet.Cell("G1").Value = "Outside";
            worksheet.Cell("G2").Value = "ignored";

            worksheet.Cell("A3").Value = "HIDDEN";
            worksheet.Cell("B3").Value = 30;
            worksheet.Cell("C3").Value = 15;
            worksheet.Cell("D3").Value = 2;
            worksheet.Cell("E3").Value = "Demo Material";
            worksheet.Cell("F3").Value = "Filtered Group";
            var filter = worksheet.Range("A1:F3").SetAutoFilter();
            filter.Column(1).AddFilter("VISIBLE");
            filter.Reapply();
            Assert.True(worksheet.Row(3).IsHidden);
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "A1:F1"
        });

        Assert.True(response.Success);
        Assert.Equal(["VISIBLE", "HIDDEN"], response.Parts.Select(part => part.ImportedId));
        Assert.Equal(["Visible Group", "Filtered Group"], response.Parts.Select(part => part.Group));
        Assert.Contains(response.SourceColumns, column => column.Address == "F");
        Assert.DoesNotContain(response.SourceColumns, column => column.Address == "G");
    }

    [Fact]
    public async Task Excel_import_reads_only_the_top_left_value_of_merged_data_cells()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "merged-data-cells.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Parts");
            WriteWorksheet(worksheet, "MERGED");
            worksheet.Range("A2:B2").Merge();
            workbook.SaveAs(workbookPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "A1:E1"
        });

        Assert.False(response.Success);
        var part = Assert.Single(response.Parts);
        Assert.Equal("MERGED", part.ImportedId);
        Assert.Contains(response.Errors, error => error.Code == "invalid-length" && error.RowId == part.RowId);
    }

    [Fact]
    public async Task Excel_import_uses_stored_formula_values_without_recalculation()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "cached-formulas.xlsx");
        using (var workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook.AddWorksheet("Parts"), "placeholder");
            workbook.SaveAs(workbookPath);
        }

        SetFormulaCell(workbookPath, "A2", "_xlfn.UNSUPPORTED_FUNCTION()", "CACHED-ID", CellValues.String);
        SetFormulaCell(workbookPath, "B2", "999+999", "24", CellValues.Number);

        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.True(response.Success);
        var part = Assert.Single(response.Parts);
        Assert.Equal("CACHED-ID", part.ImportedId);
        Assert.Equal(24m, part.Length);
    }

    [Fact]
    public async Task Excel_import_rejects_unusable_formula_results_with_Source_References()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "formula-failures.xlsm");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Formula Parts");
            WriteWorksheet(worksheet, "placeholder");
            worksheet.Cell("F1").Value = "Group";
            worksheet.Cell("F2").Value = "placeholder";
            workbook.SaveAs(workbookPath);
        }

        SetFormulaCell(workbookPath, "B2", "1+1", null, CellValues.Number);
        SetFormulaCell(workbookPath, "C2", "1/0", "#DIV/0!", CellValues.Error);
        SetFormulaCell(workbookPath, "D2", "TRUE()", "1", CellValues.Boolean);
        SetFormulaCell(workbookPath, "E2", "MaterialFromVba()", "Demo Material", CellValues.String);
        AddVbaProject(workbookPath);

        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.False(response.Success);
        AssertFormulaError(response, "missing-formula-value", "B2");
        AssertFormulaError(response, "formula-error", "C2");
        AssertFormulaError(response, "unsupported-formula-result", "D2");
        AssertFormulaError(response, "vba-formula-not-supported", "E2");
    }

    [Fact]
    public async Task Excel_import_does_not_confuse_VBA_subroutines_with_Worksheet_functions()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "built-in-formula.xlsm");
        using (var workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook.AddWorksheet("Parts"), "placeholder");
            workbook.SaveAs(workbookPath);
        }

        SetFormulaCell(workbookPath, "A2", "SUM(1,1)", "BUILTIN-ID", CellValues.String);
        AddVbaProject(workbookPath);

        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.True(response.Success);
        Assert.Equal("BUILTIN-ID", Assert.Single(response.Parts).ImportedId);
    }

    [Fact]
    public async Task Excel_import_reports_formula_failures_in_unmapped_columns_and_formula_only_rows()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "formula-only-rows.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Formula Rows");
            WriteWorksheet(worksheet, "VALID");
            worksheet.Cell("F1").Value = "Notes";
            worksheet.Cell("F2").Value = "placeholder";
            for (var column = 1; column <= 5; column++)
            {
                worksheet.Cell(3, column).Value = "placeholder";
            }
            workbook.SaveAs(workbookPath);
        }

        SetFormulaCell(workbookPath, "F2", "1+1", null, CellValues.Number);
        for (var column = 1; column <= 5; column++)
        {
            SetFormulaCell(workbookPath, $"{XLHelper.GetColumnLetterFromNumber(column)}3", "1+1", null, CellValues.Number);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            HeadingRange = "A1:F1"
        });

        Assert.Equal(2, response.Parts.Count);
        Assert.Contains(response.Errors, error =>
            error.Code == "missing-formula-value" &&
            error.Message.Contains("F2", StringComparison.Ordinal) &&
            error.Location?.PhysicalRow == 2);
        Assert.Equal(
            5,
            response.Errors.Count(error =>
                error.Code == "missing-formula-value" && error.Location?.PhysicalRow == 3));
        Assert.Equal(3, Assert.Single(response.Parts[1].SourceReferences).PhysicalRow);
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

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xlsm")]
    public async Task Encrypted_Workbooks_are_rejected_with_unencrypted_copy_guidance(string extension)
    {
        var workbookPath = FixturePath($"encrypted-parts{extension}");

        var response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.False(response.Success);
        var error = Assert.Single(response.Errors);
        Assert.Equal("encrypted-workbook", error.Code);
        Assert.Contains("save an unencrypted copy", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Macro_enabled_Workbooks_are_read_without_formula_or_source_modification()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "read-only-macros.xlsm");
        File.Copy(FixturePath("macro-enabled-parts.xlsm"), workbookPath);
        AddVbaProject(workbookPath);
        var originalContents = await File.ReadAllBytesAsync(workbookPath);
        File.SetAttributes(workbookPath, FileAttributes.ReadOnly);

        ImportResponse response;
        try
        {
            response = await new XlsxImportService().ImportAsync(new ImportRequest { FilePath = workbookPath });
        }
        finally
        {
            File.SetAttributes(workbookPath, FileAttributes.Normal);
        }

        Assert.True(response.Success);
        var part = Assert.Single(response.Parts);
        Assert.Equal("MACRO-PACKAGE", part.ImportedId);
        Assert.Equal(20m, part.Length);
        Assert.Equal(originalContents, await File.ReadAllBytesAsync(workbookPath));
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

    [Fact]
    public async Task Excel_import_reads_the_Table_Region_and_reports_physical_Worksheet_locations()
    {
        Directory.CreateDirectory(_workspacePath);
        var xlsxPath = Path.Combine(_workspacePath, "table-region.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Parts East");
            string[] headings =
                ["Id", "Length", "Width", "Quantity", "Material", "Group", "Sheet Number"];
            for (var column = 0; column < headings.Length; column++)
            {
                sheet.Cell(2, column + 2).Value = headings[column];
            }

            WritePart(sheet, 3, 2, "P-001", 24, 12, 2, "Demo Material", "A", "S1");
            sheet.Cell(4, 1).Value = "outside the Heading Range";
            sheet.Cell(5, 7).Value = "A";
            WritePart(sheet, 6, 2, "P-001", 24, 12, 3, "Demo Material", "A", "S1");
            WritePart(sheet, 7, 2, "P-001", 24, 12, 4, "Demo Material", "B", "S1");
            WritePart(sheet, 8, 2, "P-001", 24, 12, 5, "Demo Material", "A", "S2");
            workbook.SaveAs(xlsxPath);
        }

        var response = await new XlsxImportService().ImportAsync(new ImportRequest
        {
            FilePath = xlsxPath,
            WorksheetName = "Parts East",
            HeadingRange = "B2:H2"
        });

        Assert.False(response.Success);
        Assert.Equal(4, response.Parts.Count);
        Assert.Equal(5, response.Parts[0].Quantity);
        Assert.Equal([3, 6], response.Parts[0].SourceReferences.Select(reference => reference.PhysicalRow));
        Assert.Equal("B", response.Parts[2].Group);
        Assert.Equal("S2", response.Parts[3].SheetNumber);

        var invalidSourceRow = response.Parts[1];
        Assert.Equal(5, Assert.Single(invalidSourceRow.SourceReferences).PhysicalRow);
        Assert.All(response.Errors.Where(error => error.RowId == invalidSourceRow.RowId), error =>
        {
            Assert.Equal("Parts East", error.Location?.WorksheetName);
            Assert.Equal(1, error.Location?.WorksheetPosition);
            Assert.Equal(5, error.Location?.PhysicalRow);
        });
    }

    private static ImportResponse WithoutSourceIdentity(ImportResponse response) =>
        response with
        {
            Worksheet = null,
            AvailableColumns = Array.Empty<string>(),
            SourceColumns = Array.Empty<ImportSourceColumn>(),
            ColumnMappings = Array.Empty<ImportFieldMappingStatus>(),
            Errors = response.Errors.Select(error => error with { Location = null }).ToArray(),
            Warnings = response.Warnings.Select(warning => warning with { Location = null }).ToArray(),
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

    private static void AssertFormulaError(ImportResponse response, string code, string cellAddress)
    {
        var error = Assert.Single(response.Errors, error => error.Code == code);
        var part = Assert.Single(response.Parts, part => part.RowId == error.RowId);
        var sourceReference = Assert.Single(part.SourceReferences);
        Assert.Contains(cellAddress, error.Message, StringComparison.Ordinal);
        Assert.Equal("Formula Parts", error.Location?.WorksheetName);
        Assert.Equal(2, error.Location?.PhysicalRow);
        Assert.Equal("Formula Parts", sourceReference.WorksheetName);
        Assert.Equal(2, sourceReference.PhysicalRow);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Import", fileName);

    private static void SetFormulaCell(
        string workbookPath,
        string cellAddress,
        string formula,
        string? cachedValue,
        CellValues dataType)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, isEditable: true);
        var worksheetPart = document.WorkbookPart!.WorksheetParts.Single();
        var cell = worksheetPart.Worksheet.Descendants<Cell>()
            .Single(cell => string.Equals(cell.CellReference?.Value, cellAddress, StringComparison.Ordinal));
        cell.CellFormula = new CellFormula(formula);
        cell.CellValue = cachedValue is null ? null : new CellValue(cachedValue);
        cell.DataType = dataType;
        worksheetPart.Worksheet.Save();
    }

    private static void AddVbaProject(string workbookPath)
    {
        using var fixture = SpreadsheetDocument.Open(FixturePath("vba-udf-project.xlsm"), isEditable: false);
        using var source = fixture.WorkbookPart!.VbaProjectPart!.GetStream(FileMode.Open, FileAccess.Read);
        using var document = SpreadsheetDocument.Open(workbookPath, isEditable: true);
        document.ChangeDocumentType(SpreadsheetDocumentType.MacroEnabledWorkbook);
        var vbaProjectPart = document.WorkbookPart!.AddNewPart<VbaProjectPart>();
        vbaProjectPart.FeedData(source);
    }

    private static void WritePart(
        IXLWorksheet worksheet,
        int row,
        int firstColumn,
        string id,
        decimal length,
        decimal width,
        int quantity,
        string material,
        string group,
        string sheetNumber)
    {
        worksheet.Cell(row, firstColumn).Value = id;
        worksheet.Cell(row, firstColumn + 1).Value = length;
        worksheet.Cell(row, firstColumn + 2).Value = width;
        worksheet.Cell(row, firstColumn + 3).Value = quantity;
        worksheet.Cell(row, firstColumn + 4).Value = material;
        worksheet.Cell(row, firstColumn + 5).Value = group;
        worksheet.Cell(row, firstColumn + 6).Value = sheetNumber;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
