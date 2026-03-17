using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class PartEditorServiceSpecs
{
    private static readonly IReadOnlyList<Material> Materials =
    [
        DemoMaterialCatalog.Phase1,
        DemoMaterialCatalog.Phase1 with { MaterialId = "mat-birch", Name = "Baltic Birch" }
    ];

    [Fact]
    public async Task Updating_one_row_revalidates_the_full_parts_list_using_preserved_raw_values()
    {
        var service = new PartEditorService(Materials);
        var parts = new[]
        {
            CreatePartRow("row-1", "P-001", "12", "24", "1", "Demo Material", ValidationStatuses.Valid),
            CreatePartRow("row-2", "P-002", "oops", "24", "1", "Demo Material", ValidationStatuses.Error, "Length must be a decimal value.")
        };

        var response = await service.UpdateRowAsync(
            parts,
            new PartRowUpdate
            {
                RowId = "row-1",
                ImportedId = "P-001",
                Length = "12",
                Width = "24",
                Quantity = "2",
                MaterialName = "Demo Material"
            });

        Assert.False(response.Success);
        Assert.Equal(2, response.Parts.Count);
        Assert.Equal("2", response.Parts[0].QuantityText);
        Assert.Equal(2, response.Parts[0].Quantity);
        var preservedError = Assert.Single(response.Errors);
        Assert.Equal("row-2", preservedError.RowId);
        Assert.Equal("invalid-length", preservedError.Code);
        Assert.Equal("oops", response.Parts[1].LengthText);
        Assert.Equal(ValidationStatuses.Error, response.Parts[1].ValidationStatus);
    }

    [Fact]
    public async Task Adding_and_deleting_rows_return_full_revalidated_import_responses()
    {
        var service = new PartEditorService(Materials);
        var initialParts = new[]
        {
            CreatePartRow("row-1", "P-001", "12", "24", "1", "Demo Material", ValidationStatuses.Valid)
        };

        var afterAdd = await service.AddRowAsync(
            initialParts,
            new PartRowUpdate
            {
                ImportedId = "P-001",
                Length = "18",
                Width = "30",
                Quantity = "1",
                MaterialName = "Demo Material"
            });

        Assert.True(afterAdd.Success);
        Assert.Equal(2, afterAdd.Parts.Count);
        Assert.Equal("row-2", afterAdd.Parts[1].RowId);
        var duplicateWarning = Assert.Single(afterAdd.Warnings);
        Assert.Equal("duplicate-id", duplicateWarning.Code);
        Assert.Equal("row-2", duplicateWarning.RowId);

        var afterDelete = await service.DeleteRowAsync(afterAdd.Parts, "row-2");

        Assert.True(afterDelete.Success);
        Assert.Single(afterDelete.Parts);
        Assert.Empty(afterDelete.Warnings);
        Assert.Empty(afterDelete.Errors);
    }

    [Fact]
    public async Task Add_update_and_delete_round_trips_preserve_optional_group_assignments()
    {
        var service = new PartEditorService(Materials);
        var initialParts = new[]
        {
            CreatePartRow("row-1", "P-001", "12", "24", "1", "Demo Material", ValidationStatuses.Valid, group: "Casework")
        };

        var afterAdd = await service.AddRowAsync(
            initialParts,
            new PartRowUpdate
            {
                ImportedId = "P-002",
                Length = "18",
                Width = "30",
                Quantity = "1",
                MaterialName = "Demo Material",
                Group = " Doors "
            });

        Assert.True(afterAdd.Success);
        Assert.Equal("Doors", afterAdd.Parts[1].Group);

        var afterUpdate = await service.UpdateRowAsync(
            afterAdd.Parts,
            new PartRowUpdate
            {
                RowId = "row-1",
                ImportedId = "P-001",
                Length = "12",
                Width = "24",
                Quantity = "1",
                MaterialName = "Demo Material",
                Group = "Trim"
            });

        Assert.True(afterUpdate.Success);
        Assert.Equal("Trim", afterUpdate.Parts[0].Group);

        var afterDelete = await service.DeleteRowAsync(afterUpdate.Parts, "row-1");

        var remainingRow = Assert.Single(afterDelete.Parts);
        Assert.Equal("Doors", remainingRow.Group);
    }

    private static PartRow CreatePartRow(
        string rowId,
        string importedId,
        string lengthText,
        string widthText,
        string quantityText,
        string materialName,
        string validationStatus,
        params string[] validationMessages) =>
        CreatePartRow(rowId, importedId, lengthText, widthText, quantityText, materialName, validationStatus, null, validationMessages);

    private static PartRow CreatePartRow(
        string rowId,
        string importedId,
        string lengthText,
        string widthText,
        string quantityText,
        string materialName,
        string validationStatus,
        string? group,
        params string[] validationMessages) =>
        new()
        {
            RowId = rowId,
            ImportedId = importedId,
            LengthText = lengthText,
            Length = decimal.TryParse(lengthText, out var parsedLength) ? parsedLength : 0m,
            WidthText = widthText,
            Width = decimal.TryParse(widthText, out var parsedWidth) ? parsedWidth : 0m,
            QuantityText = quantityText,
            Quantity = int.TryParse(quantityText, out var parsedQuantity) ? parsedQuantity : 0,
            MaterialName = materialName,
            Group = group,
            ValidationStatus = validationStatus,
            ValidationMessages = validationMessages
        };
}
