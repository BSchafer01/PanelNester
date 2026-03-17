using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Persistence;

internal sealed class ProjectJsonSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal async Task<Project> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (stream.Length == 0)
            {
                throw new ProjectPersistenceException("project-corrupt", "Project file is empty.");
            }

            var project = await JsonSerializer.DeserializeAsync<Project>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (project is null)
            {
                throw new ProjectPersistenceException("project-corrupt", "Project file did not contain a valid project.");
            }

            if (project.Version != Project.CurrentVersion)
            {
                throw new ProjectPersistenceException(
                    "project-unsupported-version",
                    $"Project version '{project.Version}' is not supported.");
            }

            return project;
        }
        catch (FileNotFoundException exception)
        {
            throw new ProjectPersistenceException("project-not-found", "Project file was not found.", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new ProjectPersistenceException("project-not-found", "Project file was not found.", exception);
        }
        catch (JsonException exception)
        {
            throw new ProjectPersistenceException("project-corrupt", "Project file is not valid JSON.", exception);
        }
    }

    internal static JsonSerializerOptions CreateOptions() => new(SerializerOptions);
}
