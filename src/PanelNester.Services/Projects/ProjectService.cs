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
        ProjectKind projectKind = ProjectKind.Sheet,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var project = NormalizeProject(
            new Project
            {
                ProjectId = CreateProjectId(),
                ProjectKind = projectKind,
                Metadata = metadata ?? new ProjectMetadata(),
                Settings = settings ?? CreateDefaultSettings(projectKind)
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

    public Task<ProjectOperationResult> ChangeKindAsync(
        Project project,
        ProjectKind projectKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(projectKind))
        {
            return Task.FromResult(Failure(
                "project-kind-invalid",
                "Choose either Sheet Project or Stock-Length Project."));
        }

        var normalized = NormalizeProject(project);
        if (normalized.ProjectKind == projectKind)
        {
            return Task.FromResult(Success(normalized));
        }

        if (normalized.State.Parts.Count > 0 ||
            normalized.State.OptimizationGroups.Any(group => group.Parts.Count > 0))
        {
            return Task.FromResult(Failure(
                "project-kind-change-not-empty",
                "Project Kind can change only when the project has no sheet parts or Required Pieces."));
        }

        var changed = normalized with
        {
            ProjectKind = projectKind,
            Settings = CreateDefaultSettings(projectKind) with
            {
                ReportSettings = normalized.Settings.ReportSettings
            },
            MaterialSnapshots = Array.Empty<Material>(),
            State = new ProjectState()
        };

        return Task.FromResult(Success(NormalizeProject(changed)));
    }

    public Task<ProjectOperationResult> UpdateOptimizationGroupsAsync(
        Project project,
        OptimizationGroupChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(change);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProject = NormalizeProject(project);
        var groups = normalizedProject.State.OptimizationGroups.ToList();

        ProjectOperationResult result = change.Type switch
        {
            OptimizationGroupChangeType.Create => CreateOptimizationGroup(normalizedProject, groups, change.Name),
            OptimizationGroupChangeType.Rename => RenameOptimizationGroup(
                normalizedProject,
                groups,
                change.OptimizationGroupId,
                change.Name),
            OptimizationGroupChangeType.Reorder => ReorderOptimizationGroups(
                normalizedProject,
                groups,
                change.OrderedOptimizationGroupIds),
            OptimizationGroupChangeType.MovePart => MovePartToOptimizationGroup(
                normalizedProject,
                groups,
                change.PartRowId,
                change.TargetOptimizationGroupId),
            OptimizationGroupChangeType.Delete => DeleteOptimizationGroup(
                normalizedProject,
                groups,
                change.OptimizationGroupId,
                change.RemoveOwnedContent),
            _ => Failure("optimization-group-change-invalid", "Choose a valid Optimization Group change.")
        };

        return Task.FromResult(result);
    }

    private ProjectOperationResult CreateOptimizationGroup(
        Project project,
        List<OptimizationGroup> groups,
        string? requestedName)
    {
        var nameValidation = ValidateOptimizationGroupName(groups, requestedName);
        if (nameValidation.Error is not null)
        {
            return nameValidation.Error;
        }

        var generatedId = NormalizeOptional(_idGenerator()) ?? Guid.NewGuid().ToString("N");
        var uniqueId = generatedId;
        for (var suffix = 2; groups.Any(group => group.OptimizationGroupId == uniqueId); suffix++)
        {
            uniqueId = $"{generatedId}-{suffix}";
        }

        groups.Add(new OptimizationGroup
        {
            OptimizationGroupId = uniqueId,
            Name = nameValidation.Name!,
            Order = groups.Count
        });

        return Success(ApplyOptimizationGroups(project, groups));
    }

    private static ProjectOperationResult RenameOptimizationGroup(
        Project project,
        List<OptimizationGroup> groups,
        string? optimizationGroupId,
        string? requestedName)
    {
        var index = FindOptimizationGroupIndex(groups, optimizationGroupId);
        if (index < 0)
        {
            return Failure("optimization-group-not-found", "The Optimization Group was not found.");
        }

        var nameValidation = ValidateOptimizationGroupName(groups, requestedName, optimizationGroupId);
        if (nameValidation.Error is not null)
        {
            return nameValidation.Error;
        }

        groups[index] = groups[index] with { Name = nameValidation.Name! };
        return Success(ApplyOptimizationGroups(project, groups));
    }

    private static ProjectOperationResult ReorderOptimizationGroups(
        Project project,
        List<OptimizationGroup> groups,
        IReadOnlyList<string>? orderedIds)
    {
        orderedIds ??= Array.Empty<string>();
        var currentIds = groups.Select(group => group.OptimizationGroupId).ToHashSet(StringComparer.Ordinal);
        if (orderedIds.Count != groups.Count ||
            orderedIds.Distinct(StringComparer.Ordinal).Count() != groups.Count ||
            orderedIds.Any(id => !currentIds.Contains(id)))
        {
            return Failure(
                "optimization-group-order-invalid",
                "The Optimization Group order must contain every group exactly once.");
        }

        var groupsById = groups.ToDictionary(group => group.OptimizationGroupId, StringComparer.Ordinal);
        var reordered = orderedIds.Select(id => groupsById[id]).ToList();
        return Success(ApplyOptimizationGroups(project, reordered));
    }

    private static ProjectOperationResult MovePartToOptimizationGroup(
        Project project,
        List<OptimizationGroup> groups,
        string? partRowId,
        string? targetOptimizationGroupId)
    {
        if (string.IsNullOrWhiteSpace(partRowId))
        {
            return Failure("optimization-group-part-required", "Choose a manual part to move.");
        }

        var targetIndex = FindOptimizationGroupIndex(groups, targetOptimizationGroupId);
        if (targetIndex < 0)
        {
            return Failure("optimization-group-not-found", "The target Optimization Group was not found.");
        }

        var sourceIndex = groups.FindIndex(group => group.Parts.Any(part => part.RowId == partRowId));
        if (sourceIndex < 0)
        {
            return Failure("optimization-group-part-not-found", "The manual part was not found in an Optimization Group.");
        }

        if (sourceIndex == targetIndex)
        {
            return Success(project);
        }

        var part = groups[sourceIndex].Parts.First(item => item.RowId == partRowId);
        if (!part.IsManual)
        {
            return Failure(
                "optimization-group-part-not-manual",
                "Imported parts move with their Worksheet. Only manual parts can be moved individually.");
        }

        groups[sourceIndex] = InvalidateOptimizationGroup(groups[sourceIndex] with
        {
            Parts = groups[sourceIndex].Parts.Where(item => item.RowId != partRowId).ToArray()
        });
        groups[targetIndex] = InvalidateOptimizationGroup(groups[targetIndex] with
        {
            Parts = [.. groups[targetIndex].Parts, part]
        });

        return Success(ApplyOptimizationGroups(project, groups));
    }

    private static ProjectOperationResult DeleteOptimizationGroup(
        Project project,
        List<OptimizationGroup> groups,
        string? optimizationGroupId,
        bool removeOwnedContent)
    {
        var index = FindOptimizationGroupIndex(groups, optimizationGroupId);
        if (index < 0)
        {
            return Failure("optimization-group-not-found", "The Optimization Group was not found.");
        }

        if (groups.Count == 1)
        {
            return Failure("optimization-group-last-group", "A project must keep at least one Optimization Group.");
        }

        var group = groups[index];
        var hasOwnedContent =
            group.Parts.Count > 0 ||
            group.LastNestingResult is not null ||
            group.LastBatchNestingResult is not null;
        if (hasOwnedContent && !removeOwnedContent)
        {
            return Failure(
                "optimization-group-not-empty",
                $"Optimization Group '{group.Name}' owns content. Reassign it or explicitly remove it first.");
        }

        groups.RemoveAt(index);
        return Success(ApplyOptimizationGroups(project, groups));
    }

    private static OptimizationGroup InvalidateOptimizationGroup(OptimizationGroup group) =>
        group with
        {
            ResultStatus = group.LastNestingResult is null && group.LastBatchNestingResult is null
                ? OptimizationResultStatus.None
                : OptimizationResultStatus.Stale
        };

    private static Project ApplyOptimizationGroups(Project project, IReadOnlyList<OptimizationGroup> groups)
    {
        var orderedGroups = groups
            .Select((group, order) => group with { Order = order })
            .ToArray();
        var compatibilityGroup = orderedGroups.Length == 1 ? orderedGroups[0] : null;

        return project with
        {
            State = project.State with
            {
                OptimizationGroups = orderedGroups,
                Parts = orderedGroups.SelectMany(group => group.Parts).ToArray(),
                LastNestingResult = compatibilityGroup?.LastNestingResult,
                LastBatchNestingResult = compatibilityGroup?.LastBatchNestingResult
            }
        };
    }

    private static int FindOptimizationGroupIndex(
        IReadOnlyList<OptimizationGroup> groups,
        string? optimizationGroupId) =>
        string.IsNullOrWhiteSpace(optimizationGroupId)
            ? -1
            : groups.ToList().FindIndex(group => group.OptimizationGroupId == optimizationGroupId);

    private static (string? Name, ProjectOperationResult? Error) ValidateOptimizationGroupName(
        IEnumerable<OptimizationGroup> groups,
        string? requestedName,
        string? excludedOptimizationGroupId = null)
    {
        var name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, Failure("optimization-group-name-required", "Enter an Optimization Group name."));
        }

        if (groups.Any(group =>
                group.OptimizationGroupId != excludedOptimizationGroupId &&
                string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, Failure(
                "optimization-group-name-duplicate",
                $"An Optimization Group named '{name}' already exists in this project."));
        }

        return (name, null);
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

    private Project NormalizeProject(Project project)
    {
        var projectId = NormalizeId(project.ProjectId);

        return project with
        {
            Version = Project.CurrentVersion,
            ProjectId = projectId,
            Metadata = NormalizeMetadata(project.Metadata),
            Settings = NormalizeSettings(project.Settings, project.Metadata, project.ProjectKind),
            MaterialSnapshots = NormalizeSnapshots(project.MaterialSnapshots),
            State = NormalizeState(project.State, projectId)
        };
    }

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

    private static ProjectSettings NormalizeSettings(
        ProjectSettings? settings,
        ProjectMetadata? metadata,
        ProjectKind projectKind)
    {
        settings ??= CreateDefaultSettings(projectKind);
        var reportSettings = NormalizeReportSettings(metadata, settings.ReportSettings);
        var stiffenerTakeoff = NormalizeStiffenerTakeoffSettings(settings.StiffenerTakeoff);

        return settings with
        {
            KerfWidth = settings.KerfWidth < 0
                ? CreateDefaultSettings(projectKind).KerfWidth
                : settings.KerfWidth,
            ReportSettings = reportSettings,
            StiffenerTakeoff = stiffenerTakeoff
        };
    }

    private static ProjectSettings CreateDefaultSettings(ProjectKind projectKind) =>
        new()
        {
            KerfWidth = projectKind == ProjectKind.StockLength ? 0m : DefaultKerfWidth
        };

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
            ReleaseId = NormalizeOptional(settings.ReleaseId),
            Status = NormalizeOptional(settings.Status),
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

    private static StiffenerTakeoffSettings NormalizeStiffenerTakeoffSettings(StiffenerTakeoffSettings? settings)
    {
        settings ??= new StiffenerTakeoffSettings();

        return settings with
        {
            MinimumLengthInches = settings.MinimumLengthInches < 0 ? 32m : settings.MinimumLengthInches,
            MinimumWidthInches = settings.MinimumWidthInches < 0 ? 32m : settings.MinimumWidthInches,
            WidthDeductionInches = settings.WidthDeductionInches < 0 ? 4m : settings.WidthDeductionInches,
            StockLengthFeet = settings.StockLengthFeet <= 0 ? 20m : settings.StockLengthFeet,
            ReportTitle = NormalizeOptional(settings.ReportTitle),
            Extrusion = NormalizeOptional(settings.Extrusion),
            ReleaseId = NormalizeOptional(settings.ReleaseId),
            PoNumber = NormalizeOptional(settings.PoNumber),
            Color = NormalizeOptional(settings.Color),
            ColorNumber = NormalizeOptional(settings.ColorNumber),
            Manufacturer = NormalizeOptional(settings.Manufacturer),
            Status = NormalizeOptional(settings.Status)
        };
    }

    private static ProjectState NormalizeState(ProjectState? state, string projectId)
    {
        state ??= new ProjectState();
        var groupedState = ProjectSchemaMigrator.NormalizeOptimizationGroups(state, projectId);

        return groupedState with
        {
            SourceFilePath = NormalizeOptional(groupedState.SourceFilePath),
            SelectedMaterialId = NormalizeOptional(groupedState.SelectedMaterialId),
            ExtrusionLayout = NormalizeExtrusionLayout(state.ExtrusionLayout)
        };
    }

    private static ExtrusionLayoutState NormalizeExtrusionLayout(ExtrusionLayoutState? layout)
    {
        layout ??= new ExtrusionLayoutState();

        return layout with
        {
            PanelToPanelExtrusionName = NormalizeOptional(layout.PanelToPanelExtrusionName) ?? "Panel Joint",
            EdgeExtrusionName = NormalizeOptional(layout.EdgeExtrusionName) ?? "Perimeter Edge",
            Groups = (layout.Groups ?? Array.Empty<ExtrusionGroupLayout>())
                .Where(group => group is not null)
                .Select(group => group with
                {
                    GroupName = NormalizeOptional(group.GroupName) ?? "Ungrouped",
                    Rows = Math.Max(1, group.Rows),
                    Columns = Math.Max(1, group.Columns),
                    Cells = (group.Cells ?? Array.Empty<ExtrusionGridCell>()).ToArray(),
                    EdgeAssignments = (group.EdgeAssignments ?? Array.Empty<ExtrusionEdgeAssignment>()).ToArray(),
                    JointAssignments = (group.JointAssignments ?? Array.Empty<ExtrusionJointAssignment>()).ToArray()
                })
                .ToArray()
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
