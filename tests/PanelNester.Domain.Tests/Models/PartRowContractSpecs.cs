using PanelNester.Domain.Models;
using PanelNester.Domain.Tests.Specifications;

namespace PanelNester.Domain.Tests.Models;

public sealed class PartRowContractSpecs
{
    [Fact]
    public void Validation_status_contract_is_limited_to_valid_warning_or_error()
    {
        Assert.Equal(["valid", "warning", "error"], Phase01DomainExpectations.ValidationStatuses);
    }

    [Fact]
    public void Part_rows_preserve_identity_quantity_and_validation_messages()
    {
        var row = new PartRow
        {
            RowId = "row-7",
            ImportedId = "P-007",
            LengthText = "18.5",
            Length = 18.5m,
            WidthText = "12.25",
            Width = 12.25m,
            QuantityText = "3",
            Quantity = 3,
            MaterialName = "Demo Material",
            Group = "Casework",
            ValidationStatus = ValidationStatuses.Warning,
            ValidationMessages = ["Duplicate Id 'P-007' found."]
        };

        Assert.Equal("row-7", row.RowId);
        Assert.Equal("P-007", row.ImportedId);
        Assert.Equal("18.5", row.LengthText);
        Assert.Equal("12.25", row.WidthText);
        Assert.Equal("3", row.QuantityText);
        Assert.Equal(3, row.Quantity);
        Assert.Equal("Casework", row.Group);
        Assert.Equal(ValidationStatuses.Warning, row.ValidationStatus);
        Assert.Equal(["Duplicate Id 'P-007' found."], row.ValidationMessages);
    }

    [Fact]
    public void Optional_group_participates_in_part_row_equality_and_hashing()
    {
        var left = new PartRow
        {
            RowId = "row-1",
            ImportedId = "P-001",
            LengthText = "12",
            Length = 12m,
            WidthText = "24",
            Width = 24m,
            QuantityText = "1",
            Quantity = 1,
            MaterialName = "Demo Material",
            Group = "Casework",
            ValidationStatus = ValidationStatuses.Valid
        };
        var same = left with { };
        var different = left with { Group = "Doors" };

        Assert.Equal(left, same);
        Assert.Equal(left.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(left, different);
        Assert.NotEqual(left.GetHashCode(), different.GetHashCode());
    }
}
