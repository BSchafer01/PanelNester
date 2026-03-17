using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IMaterialRepository
{
    Task<IReadOnlyList<Material>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Material?> GetByIdAsync(string materialId, CancellationToken cancellationToken = default);

    Task<Material> CreateAsync(Material material, CancellationToken cancellationToken = default);

    Task<Material> UpdateAsync(Material material, CancellationToken cancellationToken = default);

    Task DeleteAsync(string materialId, CancellationToken cancellationToken = default);
}
