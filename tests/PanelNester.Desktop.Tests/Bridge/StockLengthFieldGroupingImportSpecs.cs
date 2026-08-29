using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class StockLengthFieldGroupingImportSpecs
{
    [Fact]
    public void Finalize_creates_field_derived_Optimization_Groups_across_Worksheets()
    {
        var grouping = new StockLengthImportGrouping
        {
            Mode = StockLengthImportGroupingMode.MappedField,
            Field = ImportFieldNames.ProfileNumber,
            Groups =
            [
                Definition("P-100", "p100", 240m),
                Definition("P-200", "p200", 300m),
                Definition("", "blank", 360m)
            ]
        };

        var finalized = ProjectImportFinalizer.FinalizeWorkbook(
            new Project { ProjectId = "project", ProjectKind = ProjectKind.StockLength },
            new ImportSourceMetadata { ImportSourcePath = "stock.xlsx" },
            [
                Worksheet("First", 0, Piece("a", " P-100 "), Piece("b", "P-200")),
                Worksheet("Second", 1, Piece("c", "p-100"), Piece("d", ""))
            ],
            stockLengthGrouping: grouping);

        Assert.Collection(
            finalized.State.OptimizationGroups,
            group => Assert.Equal(("p100", 2, 240m, "p-100"), (group.OptimizationGroupId, Assert.Single(group.RequiredPieces).Quantity, group.StockLength, group.ImportGroupingKey?.NormalizedValue)),
            group => Assert.Equal(("p200", 1, 300m, "p-200"), (group.OptimizationGroupId, group.RequiredPieces.Count, group.StockLength, group.ImportGroupingKey?.NormalizedValue)),
            group => Assert.Equal(("blank", 1, 360m, ""), (group.OptimizationGroupId, group.RequiredPieces.Count, group.StockLength, group.ImportGroupingKey?.NormalizedValue)));
        Assert.Equal(StockLengthImportGroupingMode.MappedField, finalized.State.ImportConfiguration?.StockLengthGrouping.Mode);
        Assert.All(finalized.State.ImportConfiguration!.Worksheets, worksheet => Assert.Null(worksheet.OptimizationGroupId));
    }

    private static StockLengthImportGroupConfiguration Definition(string value, string id, decimal stockLength) => new()
    {
        GroupingValue = value,
        OptimizationGroupId = id,
        Name = string.IsNullOrWhiteSpace(value) ? "Unspecified Profile Number" : value,
        StockLength = stockLength
    };

    private static FinalizedWorksheetImport Worksheet(
        string name,
        int position,
        params RequiredPiece[] pieces) => new(
            new ImportWorksheetSelection
            {
                WorksheetName = name,
                OriginalPosition = position,
                HeadingRange = "A1:C1"
            },
            new ImportOptions
            {
                ProjectKind = ProjectKind.StockLength,
                ColumnMappings =
                [
                    new ImportColumnMapping { SourceColumn = "A", TargetField = ImportFieldNames.Quantity },
                    new ImportColumnMapping { SourceColumn = "B", TargetField = ImportFieldNames.Length },
                    new ImportColumnMapping { SourceColumn = "C", TargetField = ImportFieldNames.ProfileNumber }
                ]
            },
            new ImportResponse
            {
                Success = true,
                RequiredPieces = pieces,
                Worksheet = new ImportWorksheetDescriptor
                {
                    WorksheetName = name,
                    OriginalPosition = position,
                    HeadingRange = "A1:C1"
                },
                ColumnMappings =
                [
                    new ImportFieldMappingStatus { SourceColumn = "A", TargetField = ImportFieldNames.Quantity },
                    new ImportFieldMappingStatus { SourceColumn = "B", TargetField = ImportFieldNames.Length },
                    new ImportFieldMappingStatus { SourceColumn = "C", TargetField = ImportFieldNames.ProfileNumber }
                ]
            });

    private static RequiredPiece Piece(string id, string profile) => new()
    {
        RequiredPieceId = id,
        Quantity = 1,
        Length = 12m,
        ProfileNumber = profile,
        IsManual = false
    };
}
