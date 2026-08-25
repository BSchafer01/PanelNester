namespace PanelNester.Domain.Models;

public sealed record BatchNestResponse
{
    public string ExecutionId { get; init; } = string.Empty;

    public bool Success { get; init; }

    public bool PartialSuccess { get; init; }

    public NestResponse? LegacyResult { get; init; }

    public IReadOnlyList<MaterialNestResult> MaterialResults { get; init; } = Array.Empty<MaterialNestResult>();

    public IReadOnlyList<OptimizationGroupNestResult> OptimizationGroupResults { get; init; } =
        Array.Empty<OptimizationGroupNestResult>();
}
