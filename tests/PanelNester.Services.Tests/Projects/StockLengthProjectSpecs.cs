using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Tests.Projects;

public sealed class StockLengthProjectSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.StockLengthProjectSpecs.{Guid.NewGuid():N}");
    [Fact]
    public async Task Manual_required_piece_accepts_mixed_number_inches_in_a_group_with_positive_stock_length()
    {
        var ids = new Queue<string>(["project-1", "group-1", "piece-1"]);
        var service = new ProjectService(new EmptyMaterialService(), idGenerator: ids.Dequeue);
        var created = await service.NewAsync(projectKind: ProjectKind.StockLength);
        var grouped = await service.UpdateOptimizationGroupsAsync(
            created.Project!,
            new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = "Frames",
                StockLength = "240"
            });

        var added = await service.UpdateRequiredPiecesAsync(
            grouped.Project!,
            new RequiredPieceChange
            {
                Type = RequiredPieceChangeType.Create,
                OptimizationGroupId = "group-1",
                Quantity = "3",
                Length = "12 3/8",
                ProfileNumber = "  H-120  ",
                PartName = "  Header  ",
                Finish = "  Clear Anodized  ",
                PartNumber = "  P-17  "
            });

        Assert.True(added.Success);
        var group = Assert.Single(added.Project!.State.OptimizationGroups);
        Assert.Equal(240m, group.StockLength);
        var piece = Assert.Single(group.RequiredPieces);
        Assert.Equal("piece-1", piece.RequiredPieceId);
        Assert.Equal(3, piece.Quantity);
        Assert.Equal(12.375m, piece.Length);
        Assert.Equal("H-120", piece.ProfileNumber);
        Assert.Equal("Header", piece.PartName);
        Assert.Equal("Clear Anodized", piece.Finish);
        Assert.Equal("P-17", piece.PartNumber);
        Assert.Empty(added.Project.MaterialSnapshots);
    }

    [Fact]
    public async Task Stock_groups_compare_trimmed_profile_and_finish_without_case_and_keep_first_spelling()
    {
        var ids = new Queue<string>(["project-1", "group-1", "piece-1", "piece-2", "piece-3"]);
        var service = new ProjectService(new EmptyMaterialService(), idGenerator: ids.Dequeue);
        var project = (await service.NewAsync(projectKind: ProjectKind.StockLength)).Project!;
        project = (await service.UpdateOptimizationGroupsAsync(project, new OptimizationGroupChange
        {
            Type = OptimizationGroupChangeType.Create,
            Name = "Frames",
            StockLength = "20 1/2"
        })).Project!;

        foreach (var change in new[]
                 {
                     Piece("profile a", "Clear"),
                     Piece(" PROFILE A ", " clear "),
                     Piece("profile a", " ")
                 })
        {
            project = (await service.UpdateRequiredPiecesAsync(project, change)).Project!;
        }

        var group = Assert.Single(project.State.OptimizationGroups);
        Assert.Equal(20.5m, group.StockLength);
        Assert.Collection(
            group.StockGroups,
            stockGroup =>
            {
                Assert.Equal("profile a", stockGroup.ProfileNumber);
                Assert.Equal("Clear", stockGroup.Finish);
                Assert.Equal(["piece-1", "piece-2"], stockGroup.RequiredPieceIds);
            },
            stockGroup =>
            {
                Assert.Equal("profile a", stockGroup.ProfileNumber);
                Assert.Null(stockGroup.Finish);
                Assert.Null(stockGroup.Finish);
                Assert.Equal(["piece-3"], stockGroup.RequiredPieceIds);
            });

        static RequiredPieceChange Piece(string profileNumber, string finish) => new()
        {
            Type = RequiredPieceChangeType.Create,
            OptimizationGroupId = "group-1",
            Quantity = "1",
            Length = "10/16",
            ProfileNumber = profileNumber,
            Finish = finish
        };
    }

    [Fact]
    public async Task Manual_required_pieces_group_settings_and_formatting_survive_a_real_save_and_reopen()
    {
        var filePath = Path.Combine(_workspacePath, "manual-stock.pnest");
        var ids = new Queue<string>(["project-1", "group-1", "empty-group", "piece-1"]);
        var service = new ProjectService(new EmptyMaterialService(), idGenerator: ids.Dequeue);
        var project = (await service.NewAsync(
            projectKind: ProjectKind.StockLength,
            settings: new ProjectSettings
            {
                KerfWidth = 0.125m,
                InchDisplayFormat = InchDisplayFormat.Fractional32
            })).Project!;
        project = (await service.UpdateOptimizationGroupsAsync(project, new OptimizationGroupChange
        {
            Type = OptimizationGroupChangeType.Create,
            Name = "Configured",
            StockLength = "240"
        })).Project!;
        project = (await service.UpdateOptimizationGroupsAsync(project, new OptimizationGroupChange
        {
            Type = OptimizationGroupChangeType.Create,
            Name = "Incomplete"
        })).Project!;
        project = (await service.UpdateRequiredPiecesAsync(project, new RequiredPieceChange
        {
            Type = RequiredPieceChangeType.Create,
            OptimizationGroupId = "group-1",
            Quantity = "4",
            Length = "6 17/32",
            ProfileNumber = "H-200",
            PartName = "Header",
            PartNumber = "P-20"
        })).Project!;

        var saved = await service.SaveAsync(project, filePath);
        var reopened = await service.LoadAsync(filePath);

        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        Assert.Equal(InchDisplayFormat.Fractional32, reopened.Project!.Settings.InchDisplayFormat);
        Assert.Equal(0.125m, reopened.Project.Settings.KerfWidth);
        Assert.Collection(
            reopened.Project.State.OptimizationGroups,
            configured =>
            {
                Assert.Equal("group-1", configured.OptimizationGroupId);
                Assert.Equal(240m, configured.StockLength);
                var piece = Assert.Single(configured.RequiredPieces);
                Assert.Equal("piece-1", piece.RequiredPieceId);
                Assert.Equal(6.53125m, piece.Length);
            },
            incomplete =>
            {
                Assert.Equal("empty-group", incomplete.OptimizationGroupId);
                Assert.Null(incomplete.StockLength);
                Assert.Empty(incomplete.RequiredPieces);
            });
        Assert.Empty(reopened.Project.MaterialSnapshots);
    }

    [Fact]
    public async Task Manual_required_piece_can_be_edited_moved_once_and_deleted()
    {
        var ids = new Queue<string>(["project-1", "group-a", "group-b", "piece-1"]);
        var service = new ProjectService(new EmptyMaterialService(), idGenerator: ids.Dequeue);
        var project = (await service.NewAsync(projectKind: ProjectKind.StockLength)).Project!;
        foreach (var (name, stockLength) in new[] { ("A", "240"), ("B", "120") })
        {
            project = (await service.UpdateOptimizationGroupsAsync(project, new OptimizationGroupChange
            {
                Type = OptimizationGroupChangeType.Create,
                Name = name,
                StockLength = stockLength
            })).Project!;
        }

        project = (await service.UpdateRequiredPiecesAsync(project, new RequiredPieceChange
        {
            Type = RequiredPieceChangeType.Create,
            OptimizationGroupId = "group-a",
            Quantity = "1",
            Length = "10",
            ProfileNumber = "A"
        })).Project!;
        var updated = await service.UpdateRequiredPiecesAsync(project, new RequiredPieceChange
        {
            Type = RequiredPieceChangeType.Update,
            OptimizationGroupId = "group-b",
            RequiredPieceId = "piece-1",
            Quantity = "2",
            Length = "11 1/4",
            ProfileNumber = "B"
        });

        Assert.True(updated.Success);
        Assert.Empty(updated.Project!.State.OptimizationGroups[0].RequiredPieces);
        var moved = Assert.Single(updated.Project.State.OptimizationGroups[1].RequiredPieces);
        Assert.Equal("piece-1", moved.RequiredPieceId);
        Assert.Equal(11.25m, moved.Length);

        var deleted = await service.UpdateRequiredPiecesAsync(updated.Project, new RequiredPieceChange
        {
            Type = RequiredPieceChangeType.Delete,
            OptimizationGroupId = "group-b",
            RequiredPieceId = "piece-1"
        });

        Assert.True(deleted.Success);
        Assert.All(deleted.Project!.State.OptimizationGroups, group => Assert.Empty(group.RequiredPieces));
    }

    [Fact]
    public async Task Saving_a_stock_length_project_does_not_read_or_persist_material_library_records()
    {
        var filePath = Path.Combine(_workspacePath, "no-material-library.pnest");
        var service = new ProjectService(new ThrowingMaterialService(), idGenerator: () => "project-1");
        var project = (await service.NewAsync(projectKind: ProjectKind.StockLength)).Project! with
        {
            MaterialSnapshots = [new Material { MaterialId = "synthetic", Name = "Must not persist" }]
        };

        var saved = await service.SaveAsync(project, filePath);
        var reopened = await service.LoadAsync(filePath);

        Assert.True(saved.Success);
        Assert.True(reopened.Success);
        Assert.Empty(saved.Project!.MaterialSnapshots);
        Assert.Empty(reopened.Project!.MaterialSnapshots);
    }

    [Theory]
    [InlineData("", "12", "H-120", "required-piece-quantity-invalid")]
    [InlineData("1", "", "H-120", "required-piece-length-invalid")]
    [InlineData("1", "12", " ", "required-piece-profile-required")]
    public async Task Required_piece_rejects_missing_required_values(
        string quantity,
        string length,
        string profileNumber,
        string expectedCode)
    {
        var ids = new Queue<string>(["project-1", "group-1"]);
        var service = new ProjectService(new EmptyMaterialService(), idGenerator: ids.Dequeue);
        var project = (await service.NewAsync(projectKind: ProjectKind.StockLength)).Project!;
        project = (await service.UpdateOptimizationGroupsAsync(project, new OptimizationGroupChange
        {
            Type = OptimizationGroupChangeType.Create,
            Name = "Frames",
            StockLength = "240"
        })).Project!;

        var result = await service.UpdateRequiredPiecesAsync(project, new RequiredPieceChange
        {
            Type = RequiredPieceChangeType.Create,
            OptimizationGroupId = "group-1",
            Quantity = quantity,
            Length = length,
            ProfileNumber = profileNumber
        });

        Assert.False(result.Success);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private sealed class EmptyMaterialService : IMaterialService
    {
        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(Array.Empty<Material>());

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingMaterialService : IMaterialService
    {
        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Stock-Length Projects must not read the material library.");

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
