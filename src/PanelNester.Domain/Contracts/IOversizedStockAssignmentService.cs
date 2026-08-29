using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IOversizedStockAssignmentService
{
    Task<ProjectOperationResult> SetAsync(
        Project project,
        string optimizationGroupId,
        string? oversizedStockLength,
        CancellationToken cancellationToken = default);
}
