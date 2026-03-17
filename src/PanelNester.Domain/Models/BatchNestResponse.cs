namespace PanelNester.Domain.Models;

public sealed record BatchNestResponse
{
    public bool Success { get; init; }

    public NestResponse? LegacyResult { get; init; }

    public IReadOnlyList<MaterialNestResult> MaterialResults { get; init; } = Array.Empty<MaterialNestResult>();
}
