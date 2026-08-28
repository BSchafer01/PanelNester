using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using System.Security.Cryptography;
using System.Text;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class CsvImportService : IImportService
{
    private readonly IReadOnlyList<Material> _fallbackMaterials;
    private readonly ImportMappingResolver _mappingResolver;
    private readonly IMaterialRepository? _materialRepository;
    private readonly PartRowValidator _validator;

    public CsvImportService(IEnumerable<Material>? knownMaterials = null, PartRowValidator? validator = null)
    {
        _fallbackMaterials = (knownMaterials ?? DemoMaterialCatalog.All).ToArray();
        _mappingResolver = new ImportMappingResolver();
        _validator = validator ?? new PartRowValidator();
    }

    public CsvImportService(IMaterialRepository materialRepository, PartRowValidator? validator = null)
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
                [new ValidationError("file-path-required", "A CSV file path is required.")],
                []);
        }

        if (!File.Exists(request.FilePath))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("file-not-found", $"CSV file was not found: {request.FilePath}")],
                []);
        }

        if (!string.Equals(Path.GetExtension(request.FilePath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return PartRowValidator.CreateResponse(
                [],
                [new ValidationError("unsupported-file-type", "CsvImportService only supports .csv files.")],
                []);
        }

        try
        {
            await using var stream = ImportFileAccessGuard.OpenReadShared(request.FilePath);
            using var reader = new StreamReader(stream);
            var response = await ImportAsync(reader, request.Options, cancellationToken).ConfigureAwait(false);
            var worksheetName = Path.GetFileName(request.FilePath);
            return response with
            {
                Worksheet = new ImportWorksheetDescriptor
                {
                    WorksheetName = worksheetName,
                    OriginalPosition = 0,
                    UsedRowCount = Math.Max(response.Parts.Count, response.RequiredPieces.Count) + 1,
                    HeadingRange = $"R1C1:R1C{response.AvailableColumns.Count}"
                },
                Parts = response.Parts.Select(part => part with
                {
                    SourceReferences = part.SourceReferences.Select(reference => reference with
                    {
                        WorksheetName = worksheetName
                    }).ToArray()
                }).ToArray(),
                RequiredPieces = response.RequiredPieces.Select(piece => piece with
                {
                    SourceReferences = piece.SourceReferences.Select(reference => reference with
                    {
                        WorksheetName = worksheetName
                    }).ToArray()
                }).ToArray()
            };
        }
        catch (IOException exception)
        {
            return PartRowValidator.CreateResponse(
                [],
                [ImportFileAccessGuard.CreateCsvReadError(request.FilePath, exception)],
                []);
        }
    }

    public async Task<ImportResponse> ImportAsync(
        TextReader reader,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (options?.ProjectKind == ProjectKind.StockLength)
        {
            return await ImportStockLengthAsync(reader, options, cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var rowUpdates = new List<PartRowUpdate>();
        var availableColumns = Array.Empty<string>();
        IReadOnlyList<ImportFieldMappingStatus> columnMappings = Array.Empty<ImportFieldMappingStatus>();
        IReadOnlyList<ImportMaterialResolution> materialResolutions = Array.Empty<ImportMaterialResolution>();
        var knownMaterials = await LoadKnownMaterialsAsync(cancellationToken).ConfigureAwait(false);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            DetectDelimiter = false,
            HeaderValidated = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = args => args.Header,
            TrimOptions = TrimOptions.Trim
        };

        try
        {
            using var csv = new CsvReader(reader, configuration);

            if (!await csv.ReadAsync().ConfigureAwait(false))
            {
                errors.Add(new ValidationError("empty-file", "CSV file is empty."));
                return PartRowValidator.CreateResponse([], errors, warnings);
            }

            csv.ReadHeader();

            availableColumns = csv.HeaderRecord ?? Array.Empty<string>();
            var columnPlan = _mappingResolver.ResolveColumns(availableColumns, options, errors);
            columnMappings = columnPlan.FieldMappings;

             if (!columnPlan.HasAllRequiredFields)
             {
                 return PartRowValidator.CreateResponse([], errors, warnings) with
                {
                    AvailableColumns = availableColumns,
                    ColumnMappings = columnMappings
                };
             }

             var rowIndex = 0;
             var hasGroupColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.Group, out var groupSourceColumn);
             var hasSheetNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.SheetNumber, out var sheetNumberSourceColumn);
             var hasRowNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.RowNumber, out var rowNumberSourceColumn);
             var hasColumnNumberColumn = columnPlan.FieldToSource.TryGetValue(ImportFieldNames.ColumnNumber, out var columnNumberSourceColumn);

             while (await csv.ReadAsync().ConfigureAwait(false))
             {
                cancellationToken.ThrowIfCancellationRequested();
                rowIndex++;

                rowUpdates.Add(new PartRowUpdate
                {
                    RowId = $"row-{rowIndex}",
                     ImportedId = csv.GetField(columnPlan.FieldToSource[ImportFieldNames.Id]) ?? string.Empty,
                     Length = csv.GetField(columnPlan.FieldToSource[ImportFieldNames.Length]) ?? string.Empty,
                     Width = csv.GetField(columnPlan.FieldToSource[ImportFieldNames.Width]) ?? string.Empty,
                     Quantity = csv.GetField(columnPlan.FieldToSource[ImportFieldNames.Quantity]) ?? string.Empty,
                     MaterialName = csv.GetField(columnPlan.FieldToSource[ImportFieldNames.Material]) ?? string.Empty,
                     Group = hasGroupColumn ? csv.GetField(groupSourceColumn!) : null,
                     SheetNumber = hasSheetNumberColumn ? csv.GetField(sheetNumberSourceColumn!) : null,
                     RowNumber = hasRowNumberColumn ? csv.GetField(rowNumberSourceColumn!) : null,
                     ColumnNumber = hasColumnNumberColumn ? csv.GetField(columnNumberSourceColumn!) : null,
                     SourceReferences =
                     [
                         new SourceReference
                         {
                             WorksheetName = "CSV",
                             WorksheetPosition = 0,
                             PhysicalRow = csv.Parser.Row,
                             SourceFingerprint = Fingerprint(csv.Parser.Record ?? Array.Empty<string>())
                         }
                     ]
                 });
             }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "CSV header was present, but no data rows were found."));
            }

            var materialPlan = _mappingResolver.ResolveMaterials(rowUpdates, knownMaterials, options, errors);
            rowUpdates = ImportedPartRowMerger
                .MergeCompatibleRows(materialPlan.Updates, hasGroupColumn, hasSheetNumberColumn, hasRowNumberColumn, hasColumnNumberColumn)
                .ToList();
            materialResolutions = materialPlan.Resolutions;
        }
        catch (HeaderValidationException exception)
        {
            errors.Add(new ValidationError("header-validation-failed", exception.Message));
        }
        catch (ReaderException exception)
        {
            errors.Add(new ValidationError("csv-read-failed", exception.Message));
        }
        catch (IOException exception)
        {
            errors.Add(new ValidationError("file-read-failed", exception.Message));
        }

        return _validator.ValidateRows(rowUpdates, knownMaterials, errors, warnings) with
        {
            AvailableColumns = availableColumns,
            ColumnMappings = columnMappings,
            MaterialResolutions = materialResolutions
        };
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

    private async Task<ImportResponse> ImportStockLengthAsync(
        TextReader reader,
        ImportOptions options,
        CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var pieces = new List<RequiredPiece>();
        var availableColumns = Array.Empty<string>();
        IReadOnlyList<ImportFieldMappingStatus> columnMappings = Array.Empty<ImportFieldMappingStatus>();
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            DetectDelimiter = false,
            HeaderValidated = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = args => args.Header,
            TrimOptions = TrimOptions.Trim
        };

        try
        {
            using var csv = new CsvReader(reader, configuration);
            if (!await csv.ReadAsync().ConfigureAwait(false))
            {
                return new ImportResponse
                {
                    Errors = [new ValidationError("empty-file", "CSV file is empty.")]
                };
            }

            csv.ReadHeader();
            availableColumns = csv.HeaderRecord ?? Array.Empty<string>();
            var columnPlan = _mappingResolver.ResolveColumns(availableColumns, options, errors);
            columnMappings = columnPlan.FieldMappings;
            if (!columnPlan.HasAllRequiredFields)
            {
                return new ImportResponse
                {
                    AvailableColumns = availableColumns,
                    ColumnMappings = columnMappings,
                    Errors = errors
                };
            }

            var rowIndex = 0;
            while (await csv.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowIndex++;
                var record = csv.Parser.Record ?? Array.Empty<string>();
                var fingerprint = Fingerprint(record);
                var sourceReference = new SourceReference
                {
                    WorksheetName = "CSV",
                    WorksheetPosition = 0,
                    PhysicalRow = csv.Parser.Row,
                    SourceFingerprint = fingerprint
                };
                var pieceId = CreateRequiredPieceId(sourceReference);
                var rowErrors = new List<string>();
                var quantityText = GetRequiredField(csv, columnPlan, ImportFieldNames.Quantity);
                var lengthText = GetRequiredField(csv, columnPlan, ImportFieldNames.Length);
                var profileNumber = GetRequiredField(csv, columnPlan, ImportFieldNames.ProfileNumber).Trim();

                if (!int.TryParse(quantityText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
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

                pieces.Add(new RequiredPiece
                {
                    RequiredPieceId = pieceId,
                    Quantity = quantity,
                    QuantityText = quantityText.Trim(),
                    Length = length,
                    LengthText = lengthText.Trim(),
                    ProfileNumber = profileNumber,
                    PartName = GetOptionalField(csv, columnPlan, ImportFieldNames.PartName),
                    Finish = GetOptionalField(csv, columnPlan, ImportFieldNames.Finish),
                    PartNumber = GetOptionalField(csv, columnPlan, ImportFieldNames.PartNumber),
                    IsManual = false,
                    ValidationStatus = rowErrors.Count == 0 ? ValidationStatuses.Valid : ValidationStatuses.Error,
                    ValidationMessages = rowErrors,
                    SourceReferences = [sourceReference]
                });
            }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "CSV header was present, but no data rows were found."));
            }
        }
        catch (ReaderException exception)
        {
            errors.Add(new ValidationError("csv-read-failed", exception.Message));
        }
        catch (IOException exception)
        {
            errors.Add(new ValidationError("file-read-failed", exception.Message));
        }

        return new ImportResponse
        {
            Success = errors.Count == 0,
            RequiredPieces = pieces,
            Errors = errors,
            Warnings = warnings,
            AvailableColumns = availableColumns,
            ColumnMappings = columnMappings
        };
    }

    private static string GetRequiredField(
        CsvReader csv,
        ColumnMappingPlan plan,
        string field) =>
        csv.GetField(plan.FieldToSource[field]) ?? string.Empty;

    private static string? GetOptionalField(
        CsvReader csv,
        ColumnMappingPlan plan,
        string field)
    {
        if (!plan.FieldToSource.TryGetValue(field, out var sourceColumn))
        {
            return null;
        }

        var value = csv.GetField(sourceColumn)?.Trim();
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
