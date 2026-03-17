using PanelNester.Domain.Models;

namespace PanelNester.Domain.Tests.Specifications;

internal static class Phase01DomainExpectations
{
    internal static IReadOnlyList<string> ValidationStatuses { get; } =
        [
            PanelNester.Domain.Models.ValidationStatuses.Valid,
            PanelNester.Domain.Models.ValidationStatuses.Warning,
            PanelNester.Domain.Models.ValidationStatuses.Error
        ];

    internal static IReadOnlyList<string> InitialUnplacedReasonCodes { get; } = NestingFailureCodes.All;
}
