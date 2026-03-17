using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IProjectService
{
    Task<ProjectOperationResult> NewAsync(
        ProjectMetadata? metadata = null,
        ProjectSettings? settings = null,
        CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> SaveAsync(
        Project project,
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> UpdateMetadataAsync(
        Project project,
        ProjectMetadata metadata,
        ProjectSettings settings,
        CancellationToken cancellationToken = default);
}
