namespace PanelNester.Domain.Models;

public sealed record Project
{
    public const int CurrentVersion = 7;

    public int Version { get; init; } = CurrentVersion;

    public ProjectKind ProjectKind { get; init; } = ProjectKind.Sheet;

    public string ProjectId { get; init; } = string.Empty;

    public ProjectMetadata Metadata { get; init; } = new();

    public ProjectSettings Settings { get; init; } = new();

    public IReadOnlyList<Material> MaterialSnapshots { get; init; } = Array.Empty<Material>();

    public ProjectState State { get; init; } = new();
}
