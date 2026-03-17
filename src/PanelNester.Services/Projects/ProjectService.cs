using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

public sealed class ProjectService : IProjectService
{
    private const decimal DefaultKerfWidth = 0.0625m;

    private readonly Func<string> _idGenerator;
    private readonly IMaterialService _materialService;
    private readonly ProjectSerializer _serializer;

    public ProjectService(
        IMaterialService materialService,
        ProjectSerializer? serializer = null,
        Func<string>? idGenerator = null)
    {
        _materialService = materialService ?? throw new ArgumentNullException(nameof(materialService));
        _serializer = serializer ?? new ProjectSerializer();
        _idGenerator = idGenerator ?? (() => Guid.NewGuid().ToString("N"));
    }

    public Task<ProjectOperationResult> NewAsync(
        ProjectMetadata? metadata = null,
        ProjectSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var project = NormalizeProject(
            new Project
            {
                ProjectId = CreateProjectId(),
                Metadata = metadata ?? new ProjectMetadata(),
                Settings = settings ?? new ProjectSettings { KerfWidth = DefaultKerfWidth }
            });

        return Task.FromResult(Success(project));
    }

    public async Task<ProjectOperationResult> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Failure("project-not-found", "A project file path is required.", filePath);
        }

        try
        {
            var project = await _serializer.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
            return Success(NormalizeProject(project), filePath);
        }
        catch (ProjectPersistenceException exception)
        {
            return Failure(exception.Code, exception.Message, filePath);
        }
    }

    public async Task<ProjectOperationResult> SaveAsync(
        Project project,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Failure("project-save-failed", "A project file path is required.", filePath);
        }

        try
        {
            var normalized = NormalizeProject(project);
            var savedProject = normalized with
            {
                MaterialSnapshots = await CaptureMaterialSnapshotsAsync(normalized, cancellationToken).ConfigureAwait(false)
            };

            await _serializer.SaveAsync(savedProject, filePath, cancellationToken).ConfigureAwait(false);
            return Success(savedProject, filePath);
        }
        catch (ProjectPersistenceException exception)
        {
            return Failure(exception.Code, exception.Message, filePath);
        }
    }

    public Task<ProjectOperationResult> UpdateMetadataAsync(
        Project project,
        ProjectMetadata metadata,
        ProjectSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var updatedProject = NormalizeProject(
            project with
            {
                Metadata = metadata,
                Settings = settings
            });

        return Task.FromResult(Success(updatedProject));
    }

    private async Task<IReadOnlyList<Material>> CaptureMaterialSnapshotsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var liveMaterials = await _materialService.ListAsync(cancellationToken).ConfigureAwait(false);
        var liveById = CreateMaterialLookupById(liveMaterials);
        var liveByName = CreateMaterialLookupByName(liveMaterials);
        var existingById = project.MaterialSnapshots
            .Where(material => !string.IsNullOrWhiteSpace(material.MaterialId))
            .GroupBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var existingByName = project.MaterialSnapshots
            .Where(material => !string.IsNullOrWhiteSpace(material.Name))
            .GroupBy(material => material.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var snapshots = new Dictionary<string, Material>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(project.State.SelectedMaterialId))
        {
            AddSnapshot(
                snapshots,
                project.State.SelectedMaterialId,
                liveById.TryGetValue(project.State.SelectedMaterialId, out var liveMaterial)
                    ? liveMaterial
                    : existingById.GetValueOrDefault(project.State.SelectedMaterialId));
        }

        foreach (var materialName in project.State.Parts
                     .Select(part => part.MaterialName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.Ordinal))
        {
            AddSnapshot(
                snapshots,
                materialName,
                liveByName.TryGetValue(materialName, out var liveMaterial)
                    ? liveMaterial
                    : existingByName.GetValueOrDefault(materialName));
        }

        return snapshots.Values
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, Material> CreateMaterialLookupById(IEnumerable<Material> materials) =>
        materials
            .Where(material => !string.IsNullOrWhiteSpace(material.MaterialId))
            .GroupBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(material => material.Name, StringComparer.Ordinal)
                    .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
                    .Last(),
                StringComparer.Ordinal);

    private static Dictionary<string, Material> CreateMaterialLookupByName(IEnumerable<Material> materials) =>
        materials
            .Where(material => !string.IsNullOrWhiteSpace(material.Name))
            .GroupBy(material => material.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(material => material.MaterialId, StringComparer.Ordinal)
                    .ThenBy(material => material.Name, StringComparer.Ordinal)
                    .Last(),
                StringComparer.Ordinal);

    private static void AddSnapshot(
        IDictionary<string, Material> snapshots,
        string key,
        Material? material)
    {
        if (material is null)
        {
            return;
        }

        var snapshotKey = string.IsNullOrWhiteSpace(material.MaterialId)
            ? key
            : material.MaterialId;

        snapshots[snapshotKey] = material;
    }

    private Project NormalizeProject(Project project) =>
        project with
        {
            Version = Project.CurrentVersion,
            ProjectId = NormalizeId(project.ProjectId),
            Metadata = NormalizeMetadata(project.Metadata),
            Settings = NormalizeSettings(project.Settings, project.Metadata),
            MaterialSnapshots = NormalizeSnapshots(project.MaterialSnapshots),
            State = NormalizeState(project.State)
        };

    private static IReadOnlyList<Material> NormalizeSnapshots(IReadOnlyList<Material>? snapshots) =>
        (snapshots ?? Array.Empty<Material>())
        .Where(material => material is not null)
        .GroupBy(material => material.MaterialId, StringComparer.Ordinal)
        .Select(group => group.Last())
        .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
        .ToArray();

    private static ProjectMetadata NormalizeMetadata(ProjectMetadata? metadata)
    {
        metadata ??= new ProjectMetadata();

        return metadata with
        {
            ProjectName = NormalizeProjectName(metadata.ProjectName),
            ProjectNumber = NormalizeOptional(metadata.ProjectNumber),
            CustomerName = NormalizeOptional(metadata.CustomerName),
            Estimator = NormalizeOptional(metadata.Estimator),
            Drafter = NormalizeOptional(metadata.Drafter),
            Pm = NormalizeOptional(metadata.Pm),
            Revision = NormalizeOptional(metadata.Revision),
            Notes = NormalizeOptional(metadata.Notes)
        };
    }

    private static ProjectSettings NormalizeSettings(ProjectSettings? settings, ProjectMetadata? metadata)
    {
        settings ??= new ProjectSettings { KerfWidth = DefaultKerfWidth };
        var reportSettings = NormalizeReportSettings(metadata, settings.ReportSettings);

        return settings with
        {
            KerfWidth = settings.KerfWidth < 0 ? DefaultKerfWidth : settings.KerfWidth,
            ReportSettings = reportSettings
        };
    }

    private static ReportSettings NormalizeReportSettings(ProjectMetadata? metadata, ReportSettings? settings)
    {
        metadata = NormalizeMetadata(metadata);
        settings ??= new ReportSettings();

        return settings with
        {
            CompanyName = settings.CompanyName ?? metadata.CustomerName,
            ReportTitle = settings.ReportTitle ?? BuildDefaultReportTitle(metadata),
            ProjectJobName = settings.ProjectJobName ?? metadata.ProjectName,
            ProjectJobNumber = settings.ProjectJobNumber ?? metadata.ProjectNumber,
            ReportDate = settings.ReportDate ?? metadata.Date,
            Notes = settings.Notes ?? metadata.Notes
        };
    }

    private static string BuildDefaultReportTitle(ProjectMetadata metadata)
    {
        var projectName = NormalizeProjectName(metadata.ProjectName);
        return string.IsNullOrWhiteSpace(projectName)
            ? "Nesting Report"
            : $"{projectName} Nesting Report";
    }

    private static ProjectState NormalizeState(ProjectState? state)
    {
        state ??= new ProjectState();

        return state with
        {
            SourceFilePath = NormalizeOptional(state.SourceFilePath),
            SelectedMaterialId = NormalizeOptional(state.SelectedMaterialId),
            Parts = (state.Parts ?? Array.Empty<PartRow>()).ToArray()
        };
    }

    private string NormalizeId(string? projectId)
    {
        var trimmed = projectId?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? CreateProjectId() : trimmed;
    }

    private static string NormalizeProjectName(string? projectName)
    {
        var trimmed = projectName?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Untitled Project" : trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private string CreateProjectId()
    {
        var projectId = _idGenerator().Trim();
        return string.IsNullOrWhiteSpace(projectId) ? Guid.NewGuid().ToString("N") : projectId;
    }

    private static ProjectOperationResult Success(Project project, string? filePath = null) =>
        new()
        {
            Success = true,
            Project = project,
            FilePath = filePath
        };

    private static ProjectOperationResult Failure(string code, string message, string? filePath = null) =>
        new()
        {
            Success = false,
            FilePath = filePath,
            Errors = [new ValidationError(code, message)]
        };
}
