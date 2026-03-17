using Xunit;
using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace TestScenario {
    public class CaseOnlyRenameTest {
        [Fact]
        public void Update_allows_case_only_rename_of_material_name() {
            var service = new MaterialValidationService();
            var existingMaterial = new Material {
                MaterialId = "mat-1",
                Name = "Birch Ply",
                SheetLength = 96m,
                SheetWidth = 48m,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            };
            
            var updatedMaterial = existingMaterial with { Name = "BIRCH PLY" };
            var result = service.ValidateForUpdate(updatedMaterial, new[] { existingMaterial });
            
            Assert.True(result.IsValid, $"Expected valid but got errors: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
