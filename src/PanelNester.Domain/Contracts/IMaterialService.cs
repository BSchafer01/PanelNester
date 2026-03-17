using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IMaterialService
{
    Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default);

    Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default);

    Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default);

    Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default);

    Task<MaterialDeleteResult> DeleteAsync(
        string materialId,
        bool isInUse = false,
        CancellationToken cancellationToken = default);
}
