using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IMaterialLibraryLocationService
{
    Task<MaterialLibraryLocation> GetLocationAsync(CancellationToken cancellationToken = default);

    Task<MaterialLibraryLocation> RepointAsync(string filePath, CancellationToken cancellationToken = default);

    Task<MaterialLibraryLocation> RestoreDefaultAsync(CancellationToken cancellationToken = default);
}
