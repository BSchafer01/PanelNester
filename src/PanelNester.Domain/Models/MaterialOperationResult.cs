namespace PanelNester.Domain.Models;

public sealed record MaterialOperationResult
{
    public bool Success { get; init; }

    public Material? Material { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}

public sealed record MaterialDeleteResult
{
    public bool Success { get; init; }

    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}

public sealed record MaterialValidationResult
{
    public Material Material { get; init; } = new();

    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();

    public bool IsValid => Errors.Count == 0;
}
