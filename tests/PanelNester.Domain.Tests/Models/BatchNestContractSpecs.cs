using PanelNester.Domain.Models;

namespace PanelNester.Domain.Tests.Models;

public sealed class BatchNestContractSpecs
{
    [Fact]
    public void Batch_nesting_results_expose_legacy_and_per_material_payloads()
    {
        var legacy = new NestResponse
        {
            Success = true,
            Summary = new MaterialSummary
            {
                TotalSheets = 1,
                TotalPlaced = 2,
                TotalUnplaced = 0,
                OverallUtilization = 50m
            }
        };

        var batch = new BatchNestResponse
        {
            Success = true,
            LegacyResult = legacy,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = "Baltic Birch",
                    MaterialId = "mat-birch",
                    Result = legacy
                }
            ]
        };

        Assert.True(batch.Success);
        Assert.NotNull(batch.LegacyResult);
        var materialResult = Assert.Single(batch.MaterialResults);
        Assert.Equal("Baltic Birch", materialResult.MaterialName);
        Assert.Equal("mat-birch", materialResult.MaterialId);
        Assert.True(materialResult.Result.Success);
    }
}
