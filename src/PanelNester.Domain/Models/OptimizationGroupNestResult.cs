namespace PanelNester.Domain.Models;

public sealed record OptimizationGroupNestResult
{
    public string OptimizationResultId { get; init; } = string.Empty;

    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public bool Success { get; init; }

    public string? FailureMessage { get; init; }

    public IReadOnlyList<string> InputPartRowIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OwnedPartRowIds { get; init; } = Array.Empty<string>();

    public NestResponse? LegacyResult { get; init; }

    public IReadOnlyList<MaterialNestResult> MaterialResults { get; init; } =
        Array.Empty<MaterialNestResult>();
}
