using PanelNester.Domain.Models;
using PanelNester.Services.Persistence;

namespace PanelNester.Services.Projects;

public sealed class ProjectSerializer
{
    private readonly ProjectJsonSerializer _jsonSerializer = new();
    private readonly ProjectFlatBufferSerializer _flatBufferSerializer = new();

    public async Task<Project> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (ProjectFileHeader.TryRead(filePath, out var header))
        {
            var project = await _flatBufferSerializer.LoadAsync(filePath, header, cancellationToken).ConfigureAwait(false);
            return ProjectSchemaMigrator.MigrateToCurrent(project);
        }

        var legacyProject = await _jsonSerializer.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        return ProjectSchemaMigrator.MigrateToCurrent(legacyProject);
    }

    public async Task SaveAsync(Project project, string filePath, CancellationToken cancellationToken = default)
    {
        await _flatBufferSerializer.SaveAsync(project, filePath, cancellationToken).ConfigureAwait(false);
    }
}
