using System.IO;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Import;

public sealed class CsvImportServiceSpecs
{
    private static readonly HashSet<string> KnownMaterials = ["Demo Material"];

    [Fact]
    public void Required_headers_are_exact_but_column_order_is_not()
    {
        string[] shuffledHeaders = ["Material", "Quantity", "Id", "Width", "Length"];

        Assert.True(CsvImportSpec.HeadersMatch(shuffledHeaders));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Length")]
    [InlineData("Width")]
    [InlineData("Quantity")]
    [InlineData("Material")]
    public void Missing_required_headers_are_file_level_errors(string missingColumn)
    {
        var remainingHeaders = CsvImportSpec.RequiredColumns.Where(column => column != missingColumn);

        Assert.False(CsvImportSpec.HeadersMatch(remainingHeaders));
    }

    [Theory]
    [InlineData("12.5", "48", "2", "Demo Material", null)]
    [InlineData("oops", "48", "2", "Demo Material", "invalid-numeric")]
    [InlineData("0", "48", "2", "Demo Material", "non-positive-dimension")]
    [InlineData("12.5", "48", "0", "Demo Material", "non-positive-quantity")]
    [InlineData("12.5", "48", "2", "", "material-not-found")]
    [InlineData("12.5", "48", "2", "Unknown", "material-not-found")]
    public void Row_validation_rules_map_to_actionable_error_codes(
        string length,
        string width,
        string quantity,
        string material,
        string? expectedErrorCode)
    {
        var actualErrorCode = CsvImportSpec.ClassifyRowError(length, width, quantity, material, KnownMaterials);

        Assert.Equal(expectedErrorCode, actualErrorCode);
    }

    [Fact]
    public void Duplicate_ids_are_warning_only_not_import_blockers()
    {
        var errorCode = CsvImportSpec.ClassifyRowError("12", "48", "1", "Demo Material", KnownMaterials);

        Assert.Null(errorCode);
    }

    [Theory]
    [InlineData(10_000, false)]
    [InlineData(10_001, true)]
    public void Large_quantity_warning_threshold_is_greater_than_ten_thousand(int quantity, bool expectedWarning)
    {
        var actualWarning = quantity > CsvImportSpec.LargeQuantityWarningThreshold;

        Assert.Equal(expectedWarning, actualWarning);
    }

    [Fact]
    public async Task Empty_files_return_actionable_errors_instead_of_throwing()
    {
        var service = new CsvImportService();

        var response = await service.ImportAsync(new StringReader(string.Empty));

        Assert.False(response.Success);
        var error = Assert.Single(response.Errors);
        Assert.Equal("empty-file", error.Code);
    }

    [Fact]
    public async Task Unicode_part_ids_round_trip_without_being_dropped_or_normalized_when_materials_are_loaded_from_the_repository()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.CsvImportServiceSpecs.{Guid.NewGuid():N}");

        try
        {
            var materialFilePath = Path.Combine(workspacePath, "materials.json");
            var repository = new JsonMaterialRepository(materialFilePath);
            var material = await repository.CreateAsync(
                new Material
                {
                    MaterialId = "baltic-birch-18",
                    Name = "Baltic Birch 18mm",
                    SheetLength = 96m,
                    SheetWidth = 48m,
                    AllowRotation = true,
                    DefaultSpacing = 0.125m,
                    DefaultEdgeMargin = 0.5m
                });
            var service = new CsvImportService(repository);
            var csv = $$"""
                Id,Length,Width,Quantity,Material
                棚板-ä,12.5,48,2,{{material.Name}}
                """;

            var response = await service.ImportAsync(new StringReader(csv));

            Assert.True(response.Success);
            var row = Assert.Single(response.Parts);
            Assert.Equal("row-1", row.RowId);
            Assert.Equal("棚板-ä", row.ImportedId);
            Assert.Equal(material.Name, row.MaterialName);
            Assert.Empty(response.Warnings);
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }
        }
    }

    [Fact]
    public async Task User_defined_column_mappings_allow_non_default_headers_without_relaxing_validation()
    {
        var service = new CsvImportService();
        var csv = """
            Part Id,Len,Width,Qty,Sheet Material
            P-001,12.5,48,2,Demo Material
            """;

        var response = await service.ImportAsync(
            new StringReader(csv),
            new ImportOptions
            {
                ColumnMappings =
                [
                    new ImportColumnMapping { SourceColumn = "Part Id", TargetField = ImportFieldNames.Id },
                    new ImportColumnMapping { SourceColumn = "Len", TargetField = ImportFieldNames.Length },
                    new ImportColumnMapping { SourceColumn = "Qty", TargetField = ImportFieldNames.Quantity },
                    new ImportColumnMapping { SourceColumn = "Sheet Material", TargetField = ImportFieldNames.Material }
                ]
            });

        Assert.True(response.Success);
        Assert.Equal(["Part Id", "Len", "Width", "Qty", "Sheet Material"], response.AvailableColumns);
        Assert.Single(response.Parts);
        Assert.Equal("P-001", response.Parts[0].ImportedId);
        Assert.Equal("Demo Material", response.Parts[0].MaterialName);
        Assert.Contains(response.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "Part Id");
        Assert.Contains(response.MaterialResolutions, resolution =>
            resolution.SourceMaterialName == "Demo Material" &&
            resolution.Status == ImportMaterialResolutionStatuses.Resolved);
    }

    [Fact]
    public async Task Missing_column_mappings_are_reported_explicitly_with_the_detected_header_name()
    {
        var service = new CsvImportService();
        var csv = """
            Part Id,Length,Width,Quantity,Material
            P-001,12.5,48,2,Demo Material
            """;

        var response = await service.ImportAsync(new StringReader(csv));

        Assert.False(response.Success);
        Assert.Contains(response.Errors, error => error.Code == "missing-column-mapping");
        var idMapping = Assert.Single(response.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Id);
        Assert.Null(idMapping.SourceColumn);
        Assert.Equal("Part Id", idMapping.SuggestedSourceColumn);
    }

    [Fact]
    public async Task Material_mappings_can_redirect_import_values_to_existing_library_entries()
    {
        var materials = new[]
        {
            DemoMaterialCatalog.Phase1 with { MaterialId = "mat-baltic-birch", Name = "Baltic Birch" }
        };
        var service = new CsvImportService(materials);
        var csv = """
            Id,Length,Width,Quantity,Material
            P-001,12.5,48,2,Raw Birch
            """;

        var response = await service.ImportAsync(
            new StringReader(csv),
            new ImportOptions
            {
                MaterialMappings =
                [
                    new ImportMaterialMapping
                    {
                        SourceMaterialName = "Raw Birch",
                        TargetMaterialId = "mat-baltic-birch"
                    }
                ]
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("Baltic Birch", row.MaterialName);
        var resolution = Assert.Single(response.MaterialResolutions);
        Assert.Equal("Raw Birch", resolution.SourceMaterialName);
        Assert.Equal("mat-baltic-birch", resolution.ResolvedMaterialId);
        Assert.Equal("Baltic Birch", resolution.ResolvedMaterialName);
    }

    [Fact]
    public async Task Optional_group_columns_can_be_mapped_without_blocking_group_free_csv_imports()
    {
        var service = new CsvImportService();

        var groupFreeResponse = await service.ImportAsync(
            new StringReader(
                """
                Id,Length,Width,Quantity,Material
                P-001,12,24,1,Demo Material
                """));

        Assert.True(groupFreeResponse.Success);
        var unmappedGroup = Assert.Single(groupFreeResponse.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Group);
        Assert.Null(unmappedGroup.SourceColumn);

        var groupedResponse = await service.ImportAsync(
            new StringReader(
                """
                Id,Length,Width,Quantity,Material,Panel Group
                P-002,12,24,1,Demo Material,Casework
                """),
            new ImportOptions
            {
                ColumnMappings =
                [
                    new ImportColumnMapping
                    {
                        SourceColumn = "Panel Group",
                        TargetField = ImportFieldNames.Group
                    }
                ]
            });

        Assert.True(groupedResponse.Success);
        var groupedRow = Assert.Single(groupedResponse.Parts);
        Assert.Equal("Casework", groupedRow.Group);
        Assert.Contains(
            groupedResponse.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn == "Panel Group");
    }

    [Fact]
    public async Task Blank_group_values_are_treated_as_ungrouped_during_csv_import()
    {
        var service = new CsvImportService();
        var response = await service.ImportAsync(
            new StringReader(
                """
                Id,Length,Width,Quantity,Material,Group
                P-001,12,24,1,Demo Material,
                P-002,12,24,1,Demo Material,   
                P-003,12,24,1,Demo Material,Casework
                """));

        Assert.True(response.Success);
        Assert.Null(response.Parts[0].Group);
        Assert.Null(response.Parts[1].Group);
        Assert.Equal("Casework", response.Parts[2].Group);
    }
}
