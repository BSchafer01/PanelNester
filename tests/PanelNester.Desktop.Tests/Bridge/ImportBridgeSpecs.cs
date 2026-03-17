using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ImportBridgeSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ImportBridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Import_file_message_uses_native_filters_and_routes_csv_and_xlsx_files()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "parts.xlsx");
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "baltic-birch-18");
        var validator = new PartRowValidator();
        var dialogs = new RecordingFileDialogService(openPaths: [csvPath]);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-import-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Baltic Birch 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);
        WriteWorkbook(xlsxPath, material.Name);

        var csvResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest());

        Assert.True(csvResponse.Success);
        Assert.Equal(csvPath, csvResponse.FilePath);
        Assert.Single(csvResponse.Parts);
        Assert.Equal(ImportFieldNames.Required, csvResponse.AvailableColumns);
        Assert.Equal(ImportFieldNames.All.Count, csvResponse.ColumnMappings.Count);
        Assert.All(
            ImportFieldNames.All,
            targetField => Assert.Contains(
                csvResponse.ColumnMappings,
                mapping => mapping.TargetField == targetField));
        Assert.Contains(
            csvResponse.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Equal("P-001", csvResponse.Parts[0].ImportedId);
        Assert.Equal("20", csvResponse.Parts[0].LengthText);
        Assert.Equal("10", csvResponse.Parts[0].WidthText);
        Assert.Equal("1", csvResponse.Parts[0].QuantityText);
        Assert.Contains(csvResponse.MaterialResolutions, resolution =>
            resolution.SourceMaterialName == material.Name &&
            resolution.Status == ImportMaterialResolutionStatuses.Resolved);

        var dialogRequest = Assert.Single(dialogs.OpenRequests);
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("csv", StringComparer.Ordinal));
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("xlsx", StringComparer.Ordinal));

        var xlsxResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = xlsxPath });

        Assert.True(xlsxResponse.Success);
        Assert.Equal(xlsxPath, xlsxResponse.FilePath);
        Assert.Equivalent(csvResponse.Parts, xlsxResponse.Parts, strict: true);
        Assert.Equal(csvResponse.Errors, xlsxResponse.Errors);
        Assert.Equal(csvResponse.Warnings, xlsxResponse.Warnings);
        Assert.Equal(csvResponse.AvailableColumns, xlsxResponse.AvailableColumns);
        Assert.Equal(csvResponse.ColumnMappings, xlsxResponse.ColumnMappings);
        Assert.Equal(csvResponse.MaterialResolutions, xlsxResponse.MaterialResolutions);
    }

    [Fact]
    public async Task Part_row_edit_messages_return_full_revalidated_import_responses()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "editable-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-edit.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "edit-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-edit-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Edit Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        Assert.Single(imported.Parts);

        var afterAdd = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "1",
                    MaterialName = material.Name
                }
            });

        Assert.True(afterAdd.Success);
        Assert.Equal(2, afterAdd.Parts.Count);
        Assert.Equal("row-2", afterAdd.Parts[1].RowId);
        Assert.Equal("18", afterAdd.Parts[1].LengthText);
        var duplicateWarning = Assert.Single(afterAdd.Warnings);
        Assert.Equal("duplicate-id", duplicateWarning.Code);
        Assert.Equal("row-2", duplicateWarning.RowId);

        var afterUpdate = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = afterAdd.Parts,
                Part = new PartRowUpdate
                {
                    RowId = "row-2",
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "oops",
                    MaterialName = material.Name
                }
            });

        Assert.False(afterUpdate.Success);
        Assert.Equal(2, afterUpdate.Parts.Count);
        Assert.Equal("oops", afterUpdate.Parts[1].QuantityText);
        Assert.Contains(afterUpdate.Errors, error => error.Code == "invalid-quantity" && error.RowId == "row-2");
        Assert.Contains(afterUpdate.Warnings, warning => warning.Code == "duplicate-id" && warning.RowId == "row-2");

        var afterDelete = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.DeletePartRow,
            new DeletePartRowRequest
            {
                Parts = afterUpdate.Parts,
                RowId = "row-2"
            });

        Assert.True(afterDelete.Success);
        Assert.Single(afterDelete.Parts);
        Assert.Empty(afterDelete.Errors);
        Assert.Empty(afterDelete.Warnings);
    }

    [Fact]
    public async Task Import_file_request_can_apply_user_defined_column_mappings()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "mapped-columns.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-mapped-columns.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mapped-columns-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-mapped-columns"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Mapped Columns Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Part Id,Len,Width,Qty,Sheet Material
            P-001,20,10,1,{{material.Name}}
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                Options = new ImportOptions
                {
                    ColumnMappings =
                    [
                        new ImportColumnMapping { SourceColumn = "Part Id", TargetField = ImportFieldNames.Id },
                        new ImportColumnMapping { SourceColumn = "Len", TargetField = ImportFieldNames.Length },
                        new ImportColumnMapping { SourceColumn = "Qty", TargetField = ImportFieldNames.Quantity },
                        new ImportColumnMapping { SourceColumn = "Sheet Material", TargetField = ImportFieldNames.Material }
                    ]
                }
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("P-001", row.ImportedId);
        Assert.Equal(material.Name, row.MaterialName);
        Assert.Contains(response.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "Part Id");
    }

    [Fact]
    public async Task Import_file_can_create_a_new_material_and_map_import_rows_to_it()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "create-material-on-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-create-on-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "created-on-import");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-create-material"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,Import MDF
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Import MDF",
                        Material = new Material
                        {
                            Name = "Import MDF 3/4",
                            SheetLength = 96m,
                            SheetWidth = 48m,
                            AllowRotation = true,
                            DefaultSpacing = 0.125m,
                            DefaultEdgeMargin = 0.5m
                        }
                    }
                ]
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("Import MDF 3/4", row.MaterialName);
        var resolution = Assert.Single(response.MaterialResolutions);
        Assert.Equal("Import MDF", resolution.SourceMaterialName);
        Assert.Equal(ImportMaterialResolutionStatuses.Created, resolution.Status);

        var materials = await repository.GetAllAsync();
        Assert.Contains(materials, material => material.MaterialId == "created-on-import" && material.Name == "Import MDF 3/4");
    }

    [Fact]
    public async Task Import_file_and_part_row_edit_messages_round_trip_optional_group_assignments()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "grouped-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-grouped-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "grouped-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-grouped-import"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Grouped Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        var importedRow = Assert.Single(imported.Parts);
        Assert.Equal("Casework", importedRow.Group);
        Assert.Contains(
            imported.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn == ImportFieldNames.Group);

        var updated = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    RowId = importedRow.RowId,
                    ImportedId = importedRow.ImportedId,
                    Length = importedRow.LengthText ?? importedRow.Length.ToString(),
                    Width = importedRow.WidthText ?? importedRow.Width.ToString(),
                    Quantity = importedRow.QuantityText ?? importedRow.Quantity.ToString(),
                    MaterialName = importedRow.MaterialName,
                    Group = "Doors"
                }
            });

        Assert.True(updated.Success);
        Assert.Equal("Doors", Assert.Single(updated.Parts).Group);

        var added = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = updated.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-002",
                    Length = "18",
                    Width = "12",
                    Quantity = "1",
                    MaterialName = material.Name,
                    Group = "   "
                }
            });

        Assert.True(added.Success);
        Assert.Null(added.Parts[1].Group);
    }

    [Fact]
    public async Task Import_file_response_keeps_group_alias_columns_visible_for_manual_mapping_review()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "group-alias-review.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-group-alias-review.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "group-alias-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-group-alias-review"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Alias Review Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Panel Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(response.Success);
        Assert.Contains("Panel Group", response.AvailableColumns);
        Assert.Contains(
            response.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Null(Assert.Single(response.Parts).Group);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static void WriteWorkbook(string filePath, string materialName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Parts");
        string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        sheet.Cell(2, 1).Value = "P-001";
        sheet.Cell(2, 2).Value = 20;
        sheet.Cell(2, 3).Value = 10;
        sheet.Cell(2, 4).Value = 1;
        sheet.Cell(2, 5).Value = materialName;

        workbook.SaveAs(filePath);
    }

    private static async Task<TResponse> DispatchAsync<TResponse>(
        BridgeMessageDispatcher dispatcher,
        string type,
        object payload)
    {
        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                type,
                Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(payload, SerializerOptions)));

        Assert.NotNull(response);
        var typed = response!.Payload.Deserialize<TResponse>(SerializerOptions);
        Assert.NotNull(typed);
        return typed!;
    }

    private sealed class RecordingFileDialogService(IEnumerable<string>? openPaths = null) : IFileDialogService
    {
        private readonly Queue<string> _openPaths = new(openPaths ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return Task.FromResult(
                _openPaths.Count == 0
                    ? OpenFileDialogResponse.Cancelled()
                    : new OpenFileDialogResponse(true, _openPaths.Dequeue(), null, "File selected."));
        }

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveFileDialogResponse.Cancelled());
    }
}
