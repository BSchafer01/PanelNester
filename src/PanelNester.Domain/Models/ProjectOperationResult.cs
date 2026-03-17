namespace PanelNester.Domain.Models;

public sealed record ProjectOperationResult
{
    public bool Success { get; init; }

    public Project? Project { get; init; }

    public string? FilePath { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}
