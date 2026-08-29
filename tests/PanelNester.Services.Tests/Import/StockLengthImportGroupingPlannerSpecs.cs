using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class StockLengthImportGroupingPlannerSpecs
{
    [Fact]
    public void Build_groups_matching_values_across_Worksheets_and_collects_blanks_last()
    {
        var grouping = new StockLengthImportGrouping
        {
            Mode = StockLengthImportGroupingMode.MappedField,
            Field = ImportFieldNames.ProfileNumber,
            Groups =
            [
                Configuration(" p-100 ", "p100", "P-100", 240m),
                Configuration("P-200", "p200", "P-200", 300m),
                Configuration("", "unspecified", "Unspecified Profile Number", 360m)
            ]
        };
        var pieces = new[]
        {
            Piece("a", " P-100 ", "First", 0),
            Piece("b", "p-200", "First", 1),
            Piece("c", "p-100", "Second", 2),
            Piece("d", "", "Second", 3)
        };

        var groups = new StockLengthImportGroupingPlanner().Build(grouping, pieces);

        Assert.Collection(
            groups,
            group => Assert.Equal(("p100", 2, "p-100"), (group.Configuration.OptimizationGroupId, group.RequiredPieces.Count, group.Key.NormalizedValue)),
            group => Assert.Equal(("p200", 1, "p-200"), (group.Configuration.OptimizationGroupId, group.RequiredPieces.Count, group.Key.NormalizedValue)),
            group => Assert.Equal(("unspecified", 1, ""), (group.Configuration.OptimizationGroupId, group.RequiredPieces.Count, group.Key.NormalizedValue)));
    }

    private static StockLengthImportGroupConfiguration Configuration(
        string value,
        string id,
        string name,
        decimal stockLength) => new()
        {
            GroupingValue = value,
            OptimizationGroupId = id,
            Name = name,
            StockLength = stockLength
        };

    private static RequiredPiece Piece(string id, string profile, string partName, int worksheetPosition) => new()
    {
        RequiredPieceId = id,
        Quantity = 1,
        Length = 12m,
        ProfileNumber = profile,
        PartName = partName,
        SourceReferences =
        [
            new SourceReference
            {
                WorksheetName = worksheetPosition == 0 ? "First" : "Second",
                WorksheetPosition = worksheetPosition,
                PhysicalRow = 2
            }
        ]
    };
}
