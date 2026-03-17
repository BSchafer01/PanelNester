using PanelNester.Domain;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace PanelNester.Services.Tests.Materials;

public sealed class MaterialServiceSpecs
{
    [Fact]
    public async Task CreateAsync_assigns_a_material_id_and_persists_the_material()
    {
        var repository = new InMemoryMaterialRepository();
        var service = new MaterialService(repository, idGenerator: () => "material-002");

        var result = await service.CreateAsync(new Material
        {
            Name = "Birch Ply",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Material);
        Assert.Equal("material-002", result.Material!.MaterialId);
        Assert.Collection(
            repository.StoredMaterials,
            material =>
            {
                Assert.Equal("material-002", material.MaterialId);
                Assert.Equal("Birch Ply", material.Name);
            });
    }

    [Fact]
    public async Task CreateAsync_returns_material_name_exists_when_name_collides_case_insensitively()
    {
        var repository = new InMemoryMaterialRepository(BuildMaterial("mat-1", "Birch Ply"));
        var service = new MaterialService(repository, idGenerator: () => "material-002");

        var result = await service.CreateAsync(BuildMaterial(string.Empty, "birch ply"));

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal("material-name-exists", error.Code);
    }

    [Fact]
    public async Task DeleteAsync_returns_material_in_use_without_touching_persistence()
    {
        var repository = new InMemoryMaterialRepository(BuildMaterial("mat-1", "Demo Material"));
        var service = new MaterialService(repository);

        var result = await service.DeleteAsync("mat-1", isInUse: true);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal("material-in-use", error.Code);
        Assert.Single(repository.StoredMaterials);
        Assert.Equal(0, repository.SaveCallCount);
    }

    [Fact]
    public async Task UpdateAsync_returns_material_not_found_when_the_record_does_not_exist()
    {
        var repository = new InMemoryMaterialRepository();
        var service = new MaterialService(repository);

        var result = await service.UpdateAsync(BuildMaterial("missing", "Birch Ply"));

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal("material-not-found", error.Code);
    }

    private static Material BuildMaterial(string materialId, string name) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        };

    private sealed class InMemoryMaterialRepository(params Material[] materials) : IMaterialRepository
    {
        private List<Material> _storedMaterials = materials.ToList();
        private readonly MaterialValidationService _validationService = new();

        internal int SaveCallCount { get; private set; }

        internal IReadOnlyList<Material> StoredMaterials => _storedMaterials;

        public Task<IReadOnlyList<Material>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(_storedMaterials.ToArray());

        public Task<Material?> GetByIdAsync(string materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_storedMaterials.FirstOrDefault(material => string.Equals(material.MaterialId, materialId, StringComparison.Ordinal)));

        public Task<Material> CreateAsync(Material material, CancellationToken cancellationToken = default)
        {
            var prepared = _validationService.PrepareForCreate(material, _storedMaterials);
            if (_storedMaterials.Any(existing => string.Equals(existing.MaterialId, prepared.MaterialId, StringComparison.Ordinal)))
            {
                throw new MaterialValidationException("material-id-exists", $"Material '{prepared.MaterialId}' already exists.");
            }

            _storedMaterials.Add(prepared);
            SaveCallCount++;
            return Task.FromResult(prepared);
        }

        public Task<Material> UpdateAsync(Material material, CancellationToken cancellationToken = default)
        {
            var index = _storedMaterials.FindIndex(existing => string.Equals(existing.MaterialId, material.MaterialId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            var prepared = _validationService.PrepareForUpdate(material, _storedMaterials);
            _storedMaterials[index] = prepared;
            SaveCallCount++;
            return Task.FromResult(prepared);
        }

        public Task DeleteAsync(string materialId, CancellationToken cancellationToken = default)
        {
            var remainingMaterials = _storedMaterials
                .Where(material => !string.Equals(material.MaterialId, materialId, StringComparison.Ordinal))
                .ToList();

            if (remainingMaterials.Count == _storedMaterials.Count)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            _storedMaterials = remainingMaterials;
            SaveCallCount++;
            return Task.CompletedTask;
        }
    }
}
