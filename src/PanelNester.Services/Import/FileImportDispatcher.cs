using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class FileImportDispatcher : IImportService
{
    private readonly IImportService _csvImportService;
    private readonly IImportService _xlsxImportService;

    public FileImportDispatcher(IImportService csvImportService, IImportService xlsxImportService)
    {
        _csvImportService = csvImportService ?? throw new ArgumentNullException(nameof(csvImportService));
        _xlsxImportService = xlsxImportService ?? throw new ArgumentNullException(nameof(xlsxImportService));
    }

    public Task<ImportResponse> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Task.FromResult(
                PartRowValidator.CreateResponse(
                    [],
                    [new ValidationError("file-path-required", "A file path is required.")],
                    []));
        }

        var importService = ResolveByExtension(request.FilePath);
        if (importService is null)
        {
            return Task.FromResult(
                PartRowValidator.CreateResponse(
                    [],
                    [new ValidationError("unsupported-file-type", "Only .csv and .xlsx files are supported.")],
                    []));
        }

        return importService.ImportAsync(request, cancellationToken);
    }

    public Task<ImportResponse> ImportAsync(
        TextReader reader,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _csvImportService.ImportAsync(reader, options, cancellationToken);

    private IImportService? ResolveByExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return extension.ToLowerInvariant() switch
        {
            ".csv" => _csvImportService,
            ".xlsx" => _xlsxImportService,
            _ => null
        };
    }
}
