using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
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

        await using var stream = File.OpenRead(request.FilePath);
        using var reader = new StreamReader(stream);
        return await ImportAsync(reader, request.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportResponse> ImportAsync(
        TextReader reader,
        ImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

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
                     Group = hasGroupColumn ? csv.GetField(groupSourceColumn!) : null
                 });
             }

            if (rowIndex == 0)
            {
                warnings.Add(new ValidationWarning("no-data-rows", "CSV header was present, but no data rows were found."));
            }

            var materialPlan = _mappingResolver.ResolveMaterials(rowUpdates, knownMaterials, options, errors);
            rowUpdates = materialPlan.Updates.ToList();
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
}
