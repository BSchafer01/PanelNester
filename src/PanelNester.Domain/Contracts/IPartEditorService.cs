using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IPartEditorService
{
    Task<ImportResponse> AddRowAsync(
        IReadOnlyList<PartRow> parts,
        PartRowUpdate update,
        CancellationToken cancellationToken = default);

    Task<ImportResponse> UpdateRowAsync(
        IReadOnlyList<PartRow> parts,
        PartRowUpdate update,
        CancellationToken cancellationToken = default);

    Task<ImportResponse> DeleteRowAsync(
        IReadOnlyList<PartRow> parts,
        string rowId,
        CancellationToken cancellationToken = default);
}
