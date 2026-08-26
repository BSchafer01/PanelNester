using ClosedXML.Excel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class XlsxImportService : IImportService, IWorkbookImportProgressService
{
    private readonly IReadOnlyList<Material> _fallbackMaterials;
    private readonly ImportMappingResolver _mappingResolver;
    private readonly IMaterialRepository? _materialRepository;
    private readonly IProgress<WorkbookImportProgress>? _progress;
    private readonly WorkbookSafetyLimits _safetyLimits;
    private readonly PartRowValidator _validator;

    public XlsxImportService(
        IEnumerable<Material>? knownMaterials = null,
        PartRowValidator? validator = null,
        WorkbookSafetyLimits? safetyLimits = null,
        IProgress<WorkbookImportProgress>? progress = null)
    {
        _fallbackMaterials = (knownMaterials ?? DemoMaterialCatalog.All).ToArray();
        _mappingResolver = new ImportMappingResolver();
        _validator = validator ?? new PartRowValidator();
        _safetyLimits = safetyLimits ?? WorkbookSafetyLimits.DesktopDefault;
        _progress = progress;
    }

    public XlsxImportService(
        IMaterialRepository materialRepository,
        PartRowValidator? validator = null,
        WorkbookSafetyLimits? safetyLimits = null,
        IProgress<WorkbookImportProgress>? progress = null)
    {
        _materialRepository = materialRepository ?? throw new ArgumentNullException(nameof(materialRepository));
        _fallbackMaterials = Array.Empty<Material>();
        _mappingResolver = new ImportMappingResolver();
        _validator = validator ?? new PartRowValidator();
        _safetyLimits = safetyLimits ?? WorkbookSafetyLimits.DesktopDefault;
        _progress = progress;
    }

    public Task<ImportResponse> ImportAsync(
        ImportRequest request,
        CancellationToken cancellationToken = default) =>
        ImportWithProgressAsync(request, _progress, cancellationToken);

    public Task<ImportResponse> ImportAsync(
        ImportRequest request,
        IProgress<WorkbookImportProgress> progress,
        CancellationToken cancellationToken = default) =>
        ImportWithProgressAsync(request, progress, cancellationToken);

    private async Task<ImportResponse> ImportWithProgressAsync(
        ImportRequest request,
        IProgress<WorkbookImportProgress>? progress,
        CancellationToken cancellationToken)
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
        var requiredPieces = new List<RequiredPiece>();
        var availableColumns = Array.Empty<string>();
        var sourceColumns = Array.Empty<ImportSourceColumn>();
        IReadOnlyList<ImportFieldMappingStatus> columnMappings = Array.Empty<ImportFieldMappingStatus>();
        IReadOnlyList<ImportMaterialResolution> materialResolutions = Array.Empty<ImportMaterialResolution>();
        ImportWorksheetDescriptor? worksheetDescriptor = null;

        WorkbookPreflightAssessment preflight;
        try
        {
            preflight = WorkbookPackagePreflight.Inspect(request.FilePath, _safetyLimits, cancellationToken);
        }
        catch (WorkbookSafetyException exception)
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("workbook-safety-ceiling-exceeded", exception.Message)],
                []);
        }
        catch (EncryptedWorkbookException exception)
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("encrypted-workbook", exception.Message)],
                []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PartRowValidator.CreateResponse(
                [],
                [ImportFileAccessGuard.CreateXlsxReadError(request.FilePath, exception)],
                []);
        }

        progress?.Report(new WorkbookImportProgress
        {
            Phase = WorkbookImportPhase.Preflight,
            Label = "Checking Workbook safety",
            Preflight = preflight
        });
        warnings.AddRange(preflight.Warnings.Select(message =>
            new ValidationWarning("workbook-safety-warning", message)));
        var knownMaterials = await LoadKnownMaterialsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(new WorkbookImportProgress
            {
                Phase = WorkbookImportPhase.OpeningWorkbook,
                Label = "Opening workbook",
                Preflight = preflight
            });
            await using var stream = ImportFileAccessGuard.OpenReadShared(request.FilePath);
            ImportFileAccessGuard.RejectEncryptedOpenXmlPackage(stream);
            var vbaProject = WorkbookVbaProjectInspector.Inspect(stream);
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

            var visibleWorksheets = workbook.Worksheets.Count(candidateWorksheet =>
                candidateWorksheet.Visibility == XLWorksheetVisibility.Visible &&
                candidateWorksheet.RangeUsed() is not null);
            var visibleWorksheetNumber = workbook.Worksheets
                .Where(candidateWorksheet =>
                    candidateWorksheet.Visibility == XLWorksheetVisibility.Visible &&
                    candidateWorksheet.RangeUsed() is not null)
                .OrderBy(candidateWorksheet => candidateWorksheet.Position)
                .TakeWhile(candidateWorksheet => candidateWorksheet.Position != worksheet.Position)
                .Count() + 1;
            progress?.Report(new WorkbookImportProgress
            {
                Phase = WorkbookImportPhase.ReadingWorksheet,
                Label = $"Reading Worksheet {visibleWorksheetNumber} of {visibleWorksheets}",
                Current = visibleWorksheetNumber,
                Total = visibleWorksheets,
                WorksheetName = worksheet.Name,
                Preflight = preflight
            });

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

            var headingRowNumber = headingAddress.FirstAddress.RowNumber;
            var firstHeadingColumn = headingAddress.FirstAddress.ColumnNumber;
            var lastHeadingColumn = headingAddress.LastAddress.ColumnNumber;
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

            var headingColumns = Enumerable.Range(
                    firstHeadingColumn,
                    lastHeadingColumn - firstHeadingColumn + 1)
                .Select(columnNumber => new ImportSourceColumn
                {
                    Address = XLHelper.GetColumnLetterFromNumber(columnNumber),
                    Heading = GetCellText(worksheet.Cell(headingRowNumber, columnNumber))
                })
                .ToArray();
            sourceColumns = headingColumns
                .Where(column => !string.IsNullOrWhiteSpace(column.Heading))
                .ToArray();
            availableColumns = sourceColumns.Select(column => column.Address).ToArray();

            var lastTableRegionRow = worksheet.LastRowUsed()?.RowNumber() ?? headingRowNumber;
            foreach (var blankHeading in headingColumns.Where(column => string.IsNullOrWhiteSpace(column.Heading)))
            {
                var columnNumber = XLHelper.GetColumnNumberFromLetter(blankHeading.Address);
                var containsData = Enumerable
                    .Range(headingRowNumber + 1, Math.Max(0, lastTableRegionRow - headingRowNumber))
                    .Any(rowNumber => !string.IsNullOrWhiteSpace(GetCellText(worksheet.Cell(rowNumber, columnNumber))));
                if (containsData)
                {
                    warnings.Add(new ValidationWarning(
                        "ignored-data-without-heading",
                        $"Worksheet column {blankHeading.Address} contains data but its Heading Range cell is blank, so that data was ignored."));
                }
            }

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

            var rowsVisited = 0;
            foreach (var row in Enumerable.Range(headingRowNumber + 1, Math.Max(0, lastTableRegionRow - headingRowNumber))
                         .Select(worksheet.Row))
            {
                rowsVisited++;
                if (rowsVisited % 256 == 0)
                {
                    progress?.Report(new WorkbookImportProgress
                    {
                        Phase = WorkbookImportPhase.ReadingWorksheet,
                        Label = $"Reading Worksheet {visibleWorksheetNumber} of {visibleWorksheets}",
                        Current = visibleWorksheetNumber,
                        Total = visibleWorksheets,
                        WorksheetName = worksheet.Name,
                        Preflight = preflight
                    });
                }
                cancellationToken.ThrowIfCancellationRequested();

                var cellValues = Enumerable
                    .Range(firstHeadingColumn, lastHeadingColumn - firstHeadingColumn + 1)
                    .ToDictionary(
                        columnNumber => columnNumber,
                        columnNumber => WorkbookCellValueReader.Read(row.Cell(columnNumber), vbaProject));
                if (cellValues.Values.All(value =>
                        !value.IsFormula && string.IsNullOrWhiteSpace(value.Value)))
                {
                    continue;
                }

                rowIndex++;
                var rowId = $"row-{rowIndex}";
                foreach (var error in cellValues.Values
                             .Select(value => value.Error)
                             .OfType<WorkbookCellReadError>())
                {
                    errors.Add(new ValidationError(
                        error.Code,
                        error.Message,
                        rowId,
                        new WorksheetRowLocation
                        {
                            WorksheetName = worksheet.Name,
                            WorksheetPosition = worksheet.Position,
                            PhysicalRow = row.RowNumber()
                        }));
                }

                string ReadMappedCell(string sourceColumn)
                {
                    return cellValues[headerMap[sourceColumn]].Value;
                }

                var sourceReference = new SourceReference
                {
                    WorksheetName = worksheet.Name,
                    WorksheetPosition = worksheet.Position,
                    PhysicalRow = row.RowNumber(),
                    SourceFingerprint = Fingerprint(
                        Enumerable.Range(firstHeadingColumn, lastHeadingColumn - firstHeadingColumn + 1)
                            .Select(columnNumber => cellValues[columnNumber].Value))
                };

                if ((request.Options?.ProjectKind ?? ProjectKind.Sheet) == ProjectKind.StockLength)
                {
                    var pieceId = CreateRequiredPieceId(sourceReference);
                    var rowErrors = new List<string>();
                    var quantityText = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Quantity]).Trim();
                    var lengthText = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Length]).Trim();
                    var profileNumber = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.ProfileNumber]).Trim();

                    if (!int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
                    {
                        AddStockError("invalid-quantity", "Quantity must be an integer value.", pieceId, sourceReference, rowErrors, errors);
                    }
                    else if (quantity <= 0)
                    {
                        AddStockError("quantity-out-of-range", "Quantity must be greater than zero.", pieceId, sourceReference, rowErrors, errors);
                    }

                    if (!InchMeasurementParser.TryParse(lengthText, out var length))
                    {
                        AddStockError("invalid-length", "Length must be a decimal, fraction, or mixed-number inch value.", pieceId, sourceReference, rowErrors, errors);
                    }
                    else if (length <= 0)
                    {
                        AddStockError("length-out-of-range", "Length must be greater than zero.", pieceId, sourceReference, rowErrors, errors);
                    }

                    if (string.IsNullOrWhiteSpace(profileNumber))
                    {
                        AddStockError("missing-profile-number", "Profile Number is required.", pieceId, sourceReference, rowErrors, errors);
                    }

                    requiredPieces.Add(new RequiredPiece
                    {
                        RequiredPieceId = pieceId,
                        Quantity = quantity,
                        QuantityText = quantityText,
                        Length = length,
                        LengthText = lengthText,
                        ProfileNumber = profileNumber,
                        PartName = ReadOptionalMappedCell(columnPlan, ImportFieldNames.PartName, ReadMappedCell),
                        Finish = ReadOptionalMappedCell(columnPlan, ImportFieldNames.Finish, ReadMappedCell),
                        PartNumber = ReadOptionalMappedCell(columnPlan, ImportFieldNames.PartNumber, ReadMappedCell),
                        IsManual = false,
                        ValidationStatus = rowErrors.Count == 0 ? ValidationStatuses.Valid : ValidationStatuses.Error,
                        ValidationMessages = rowErrors,
                        SourceReferences = [sourceReference]
                    });
                    continue;
                }

                rowUpdates.Add(new PartRowUpdate
                {
                    RowId = rowId,
                    ImportedId = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Id]),
                    Length = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Length]),
                    Width = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Width]),
                    Quantity = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Quantity]),
                    MaterialName = ReadMappedCell(columnPlan.FieldToSource[ImportFieldNames.Material]),
                    Group = hasGroupColumn ? ReadMappedCell(groupSourceColumn!) : null,
                    SheetNumber = hasSheetNumberColumn ? ReadMappedCell(sheetNumberSourceColumn!) : null,
                    RowNumber = hasRowNumberColumn ? ReadMappedCell(rowNumberSourceColumn!) : null,
                    ColumnNumber = hasColumnNumberColumn ? ReadMappedCell(columnNumberSourceColumn!) : null,
                    SourceReferences = [sourceReference]
                });
            }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "Workbook header was present, but no data rows were found."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new WorkbookImportProgress
            {
                Phase = WorkbookImportPhase.Validating,
                Label = "Validating",
                WorksheetName = worksheet.Name,
                Preflight = preflight
            });
            if ((request.Options?.ProjectKind ?? ProjectKind.Sheet) != ProjectKind.StockLength)
            {
                var materialPlan = _mappingResolver.ResolveMaterials(rowUpdates, knownMaterials, request.Options, errors);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new WorkbookImportProgress
                {
                    Phase = WorkbookImportPhase.CombiningParts,
                    Label = "Combining parts",
                    WorksheetName = worksheet.Name,
                    Preflight = preflight
                });
                rowUpdates = ImportedPartRowMerger
                    .MergeCompatibleRows(
                        materialPlan.Updates,
                        hasGroupColumn,
                        hasSheetNumberColumn,
                        hasRowNumberColumn,
                        hasColumnNumberColumn,
                        cancellationToken)
                    .ToList();
                materialResolutions = materialPlan.Resolutions;
            }
        }
        catch (EncryptedWorkbookException exception)
        {
            errors.Add(new ValidationError("encrypted-workbook", exception.Message));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(ImportFileAccessGuard.CreateXlsxReadError(request.FilePath, exception));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if ((request.Options?.ProjectKind ?? ProjectKind.Sheet) == ProjectKind.StockLength)
        {
            return new ImportResponse
            {
                Success = errors.Count == 0,
                RequiredPieces = requiredPieces,
                Errors = errors,
                Warnings = warnings,
                AvailableColumns = availableColumns,
                SourceColumns = sourceColumns,
                ColumnMappings = columnMappings,
                Worksheet = worksheetDescriptor
            };
        }

        return _validator.ValidateRows(rowUpdates, knownMaterials, errors, warnings, cancellationToken) with
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
        WorkbookCellValueReader.ReadText(cell);

    private static string? ReadOptionalMappedCell(
        ColumnMappingPlan plan,
        string field,
        Func<string, string> readMappedCell)
    {
        if (!plan.FieldToSource.TryGetValue(field, out var sourceColumn))
        {
            return null;
        }

        var value = readMappedCell(sourceColumn).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void AddStockError(
        string code,
        string message,
        string rowId,
        SourceReference sourceReference,
        ICollection<string> rowErrors,
        ICollection<ValidationError> errors)
    {
        rowErrors.Add(message);
        errors.Add(new ValidationError(code, message, rowId, sourceReference));
    }

    private static string CreateRequiredPieceId(SourceReference sourceReference)
    {
        var identity = $"{sourceReference.WorksheetPosition}\u001f{sourceReference.PhysicalRow}\u001f{sourceReference.SourceFingerprint}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"required-{hash[..24].ToLowerInvariant()}";
    }

    private static string Fingerprint(IEnumerable<string?> values)
    {
        var canonicalRow = string.Join('\u001f', values.Select(value => value ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRow)));
    }

}
