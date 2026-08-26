using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IImportService
{
    Task<ImportResponse> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default);

    Task<ImportResponse> ImportAsync(
        TextReader reader,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IWorkbookImportProgressService
{
    Task<ImportResponse> ImportAsync(
        ImportRequest request,
        IProgress<WorkbookImportProgress> progress,
        CancellationToken cancellationToken = default);
}
