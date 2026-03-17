using ClosedXML.Excel;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class XlsxImportService : IImportService
{
    private readonly IReadOnlyList<Material> _fallbackMaterials;
    private readonly ImportMappingResolver _mappingResolver;
    private readonly IMaterialRepository? _materialRepository;
    private readonly PartRowValidator _validator;

    public XlsxImportService(IEnumerable<Material>? knownMaterials = null, PartRowValidator? validator = null)
    {
        _fallbackMaterials = (knownMaterials ?? DemoMaterialCatalog.All).ToArray();
        _mappingResolver = new ImportMappingResolver();
        _validator = validator ?? new PartRowValidator();
    }

    public XlsxImportService(IMaterialRepository materialRepository, PartRowValidator? validator = null)
    {
        _materialRepository = materialRepository ?? throw new ArgumentNullException(nameof(materialRepository));
        _fallbackMaterials = Array.Empty<Material>();
        _mappingResolver = new ImportMappingResolver();
        _validator = validator ?? new PartRowValidator();
    }

    public async Task<ImportResponse> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("file-path-required", "An XLSX file path is required.")],
                []);
        }

        if (!File.Exists(request.FilePath))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("file-not-found", $"XLSX file was not found: {request.FilePath}")],
                []);
        }

        if (!string.Equals(Path.GetExtension(request.FilePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("unsupported-file-type", "XlsxImportService only supports .xlsx files.")],
                []);
        }

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var rowUpdates = new List<PartRowUpdate>();
        var availableColumns = Array.Empty<string>();
        IReadOnlyList<ImportFieldMappingStatus> columnMappings = Array.Empty<ImportFieldMappingStatus>();
        IReadOnlyList<ImportMaterialResolution> materialResolutions = Array.Empty<ImportMaterialResolution>();
        var knownMaterials = await LoadKnownMaterialsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var workbook = new XLWorkbook(request.FilePath);
            var worksheet = workbook.Worksheets.FirstOrDefault(sheet => sheet.RangeUsed() is not null);

            if (worksheet is null)
            {
                errors.Add(new ValidationError("empty-workbook", "Workbook does not contain any populated worksheets."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            var usedRows = worksheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
            {
                errors.Add(new ValidationError("empty-workbook", "Workbook does not contain any populated worksheets."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            var headerRow = usedRows[0];
            var lastHeaderColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
            if (lastHeaderColumn == 0)
            {
                errors.Add(new ValidationError("missing-column", "Worksheet header row is empty."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            availableColumns = Enumerable.Range(1, lastHeaderColumn)
                .Select(columnNumber => GetCellText(headerRow.Cell(columnNumber)))
                .ToArray();
            var columnPlan = _mappingResolver.ResolveColumns(availableColumns, request.Options, errors);
            columnMappings = columnPlan.FieldMappings;

            if (!columnPlan.HasAllRequiredFields)
            {
                return PartRowValidator.CreateResponse([], errors, warnings) with
                {
                    AvailableColumns = availableColumns,
                    ColumnMappings = columnMappings
                };
            }

            var headerMap = availableColumns
                .Select((header, index) => new { header, columnNumber = index + 1 })
                .Where(item => !string.IsNullOrWhiteSpace(item.header))
                .GroupBy(item => item.header, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().columnNumber, StringComparer.Ordinal);

            var rowIndex = 0;
            var hasGroupColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.Group, out var groupSourceColumn);

            foreach (var row in usedRows.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsBlankRequiredRow(row, headerMap, columnPlan.FieldToSource))
                {
                    continue;
                }

                rowIndex++;
                rowUpdates.Add(new PartRowUpdate
                {
                    RowId = $"row-{rowIndex}",
                    ImportedId = GetCellText(row.Cell(headerMap[columnPlan.FieldToSource[ImportFieldNames.Id]])),
                    Length = GetCellText(row.Cell(headerMap[columnPlan.FieldToSource[ImportFieldNames.Length]])),
                    Width = GetCellText(row.Cell(headerMap[columnPlan.FieldToSource[ImportFieldNames.Width]])),
                    Quantity = GetCellText(row.Cell(headerMap[columnPlan.FieldToSource[ImportFieldNames.Quantity]])),
                    MaterialName = GetCellText(row.Cell(headerMap[columnPlan.FieldToSource[ImportFieldNames.Material]])),
                    Group = hasGroupColumn ? GetCellText(row.Cell(headerMap[groupSourceColumn!])) : null
                });
            }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "Workbook header was present, but no data rows were found."));
            }

            var materialPlan = _mappingResolver.ResolveMaterials(rowUpdates, knownMaterials, request.Options, errors);
            rowUpdates = materialPlan.Updates.ToList();
            materialResolutions = materialPlan.Resolutions;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(new ValidationError("xlsx-read-failed", exception.Message));
        }

        return _validator.ValidateRows(rowUpdates, knownMaterials, errors, warnings) with
        {
            AvailableColumns = availableColumns,
            ColumnMappings = columnMappings,
            MaterialResolutions = materialResolutions
        };
    }

    public Task<ImportResponse> ImportAsync(
        TextReader reader,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            PartRowValidator.CreateResponse(
                [],
                [new ValidationError("unsupported-import-source", "XlsxImportService requires a file path.")],
                []));
    }

    private async Task<IReadOnlyDictionary<string, Material>> LoadKnownMaterialsAsync(CancellationToken cancellationToken)
    {
        var materials = _materialRepository is not null
            ? await _materialRepository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : _fallbackMaterials;

        return materials
            .GroupBy(material => material.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static string GetCellText(IXLCell cell) =>
        cell.GetString().Trim();

    private static bool IsBlankRequiredRow(
        IXLRow row,
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyDictionary<string, string> fieldToSource) =>
        ImportFieldNames.Required.All(field =>
            string.IsNullOrWhiteSpace(GetCellText(row.Cell(headerMap[fieldToSource[field]]))));
}
