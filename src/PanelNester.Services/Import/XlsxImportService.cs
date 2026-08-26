using ClosedXML.Excel;
using System.Security.Cryptography;
using System.Text;
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
                [new ValidationError("file-path-required", "An Excel Workbook path is required.")],
                []);
        }

        if (!File.Exists(request.FilePath))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("file-not-found", $"Excel Workbook was not found: {request.FilePath}")],
                []);
        }

        var extension = Path.GetExtension(request.FilePath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("unsupported-file-type", "Excel import only supports .xlsx and .xlsm Workbooks.")],
                []);
        }

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var rowUpdates = new List<PartRowUpdate>();
        var availableColumns = Array.Empty<string>();
        var sourceColumns = Array.Empty<ImportSourceColumn>();
        IReadOnlyList<ImportFieldMappingStatus> columnMappings = Array.Empty<ImportFieldMappingStatus>();
        IReadOnlyList<ImportMaterialResolution> materialResolutions = Array.Empty<ImportMaterialResolution>();
        ImportWorksheetDescriptor? worksheetDescriptor = null;
        var knownMaterials = await LoadKnownMaterialsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = ImportFileAccessGuard.OpenReadShared(request.FilePath);
            using var workbook = new XLWorkbook(stream);
            var worksheet = string.IsNullOrWhiteSpace(request.WorksheetName)
                ? workbook.Worksheets.FirstOrDefault(sheet =>
                    sheet.Visibility == XLWorksheetVisibility.Visible && sheet.RangeUsed() is not null)
                : workbook.Worksheets.FirstOrDefault(sheet =>
                    sheet.Visibility == XLWorksheetVisibility.Visible &&
                    sheet.RangeUsed() is not null &&
                    string.Equals(sheet.Name, request.WorksheetName, StringComparison.Ordinal));

            if (worksheet is null)
            {
                errors.Add(string.IsNullOrWhiteSpace(request.WorksheetName)
                    ? new ValidationError("empty-workbook", "Workbook does not contain any visible, populated Worksheets.")
                    : new ValidationError(
                        "worksheet-not-found",
                        $"Worksheet '{request.WorksheetName}' was not found, visible, and populated."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            var usedRows = worksheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
            {
                errors.Add(new ValidationError("empty-workbook", "Workbook does not contain any populated worksheets."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            IXLRange headingRange;
            try
            {
                headingRange = string.IsNullOrWhiteSpace(request.HeadingRange)
                    ? worksheet.Range(
                        usedRows[0].RowNumber(),
                        usedRows[0].FirstCellUsed()!.Address.ColumnNumber,
                        usedRows[0].RowNumber(),
                        usedRows[0].LastCellUsed()!.Address.ColumnNumber)
                    : worksheet.Range(request.HeadingRange.Trim());
            }
            catch (ArgumentException)
            {
                errors.Add(new ValidationError(
                    "invalid-heading-range",
                    $"Heading Range '{request.HeadingRange}' is not a valid A1-style range."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            var headingAddress = headingRange.RangeAddress;
            if (headingAddress.FirstAddress.RowNumber != headingAddress.LastAddress.RowNumber)
            {
                errors.Add(new ValidationError(
                    "invalid-heading-range",
                    "Heading Range must be one contiguous row."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            if (worksheet.MergedRanges.Any(mergedRange => mergedRange.Intersects(headingRange)))
            {
                errors.Add(new ValidationError(
                    "merged-heading-range",
                    "Heading Range cannot contain merged cells."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            var headerRowNumber = headingAddress.FirstAddress.RowNumber;
            var firstHeaderColumn = headingAddress.FirstAddress.ColumnNumber;
            var lastHeaderColumn = headingAddress.LastAddress.ColumnNumber;
            if (headingRange.Cells().All(cell => string.IsNullOrWhiteSpace(GetCellText(cell))))
            {
                errors.Add(new ValidationError("missing-column", "Worksheet header row is empty."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            worksheetDescriptor = new ImportWorksheetDescriptor
            {
                WorksheetName = worksheet.Name,
                OriginalPosition = worksheet.Position,
                HeadingRange = headingRange.RangeAddress.ToStringRelative()
            };

            sourceColumns = Enumerable.Range(firstHeaderColumn, lastHeaderColumn - firstHeaderColumn + 1)
                .Select(columnNumber => new ImportSourceColumn
                {
                    Address = XLHelper.GetColumnLetterFromNumber(columnNumber),
                    Heading = GetCellText(worksheet.Cell(headerRowNumber, columnNumber))
                })
                .ToArray();
            availableColumns = sourceColumns.Select(column => column.Address).ToArray();
            var columnPlan = _mappingResolver.ResolveWorksheetColumns(sourceColumns, request.Options, errors);
            columnMappings = columnPlan.FieldMappings;

            if (!columnPlan.HasAllRequiredFields)
            {
                return PartRowValidator.CreateResponse([], errors, warnings) with
                {
                    AvailableColumns = availableColumns,
                    SourceColumns = sourceColumns,
                    ColumnMappings = columnMappings
                };
            }

            var headerMap = sourceColumns
                .Select(column => new
                {
                    column.Address,
                    columnNumber = XLHelper.GetColumnNumberFromLetter(column.Address)
                })
                .ToDictionary(item => item.Address, item => item.columnNumber, StringComparer.Ordinal);

            var rowIndex = 0;
            var hasGroupColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.Group, out var groupSourceColumn);
            var hasSheetNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.SheetNumber, out var sheetNumberSourceColumn);
            var hasRowNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.RowNumber, out var rowNumberSourceColumn);
            var hasColumnNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.ColumnNumber, out var columnNumberSourceColumn);

            var lastTableRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
            foreach (var row in Enumerable.Range(headerRowNumber + 1, Math.Max(0, lastTableRow - headerRowNumber))
                         .Select(worksheet.Row))
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
                    Group = hasGroupColumn ? GetCellText(row.Cell(headerMap[groupSourceColumn!])) : null,
                    SheetNumber = hasSheetNumberColumn ? GetCellText(row.Cell(headerMap[sheetNumberSourceColumn!])) : null,
                    RowNumber = hasRowNumberColumn ? GetCellText(row.Cell(headerMap[rowNumberSourceColumn!])) : null,
                    ColumnNumber = hasColumnNumberColumn ? GetCellText(row.Cell(headerMap[columnNumberSourceColumn!])) : null,
                    SourceReferences =
                    [
                        new SourceReference
                        {
                            WorksheetName = worksheet.Name,
                            WorksheetPosition = worksheet.Position,
                            PhysicalRow = row.RowNumber(),
                            SourceFingerprint = Fingerprint(
                                Enumerable.Range(firstHeaderColumn, lastHeaderColumn - firstHeaderColumn + 1)
                                    .Select(columnNumber => GetCellText(row.Cell(columnNumber))))
                        }
                    ]
                });
            }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "Workbook header was present, but no data rows were found."));
            }

            var materialPlan = _mappingResolver.ResolveMaterials(rowUpdates, knownMaterials, request.Options, errors);
            rowUpdates = ImportedPartRowMerger
                .MergeCompatibleRows(materialPlan.Updates, hasGroupColumn, hasSheetNumberColumn, hasRowNumberColumn, hasColumnNumberColumn)
                .ToList();
            materialResolutions = materialPlan.Resolutions;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(ImportFileAccessGuard.CreateXlsxReadError(request.FilePath, exception));
        }

        return _validator.ValidateRows(rowUpdates, knownMaterials, errors, warnings) with
        {
            AvailableColumns = availableColumns,
            SourceColumns = sourceColumns,
            ColumnMappings = columnMappings,
            MaterialResolutions = materialResolutions,
            Worksheet = worksheetDescriptor
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

    private static string Fingerprint(IEnumerable<string?> values)
    {
        var canonicalRow = string.Join('\u001f', values.Select(value => value ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRow)));
    }

    private static bool IsBlankRequiredRow(
        IXLRow row,
        IReadOnlyDictionary<string, int> headerMap,
        IReadOnlyDictionary<string, string> fieldToSource) =>
        ImportFieldNames.Required.All(field =>
            string.IsNullOrWhiteSpace(GetCellText(row.Cell(headerMap[fieldToSource[field]]))));
}
