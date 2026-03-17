using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal sealed class ImportMappingResolver
{
    private static readonly IReadOnlyDictionary<string, string[]> SuggestedHeaderAliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ImportFieldNames.Id] =
            [
                "id",
                "itemid",
                "itemnumber",
                "part",
                "partid",
                "partnumber",
                "partno",
                "pieceid",
                "panelid"
            ],
            [ImportFieldNames.Length] =
            [
                "length",
                "len",
                "partlength",
                "panellength"
            ],
            [ImportFieldNames.Width] =
            [
                "width",
                "wid",
                "partwidth",
                "panelwidth"
            ],
            [ImportFieldNames.Quantity] =
            [
                "quantity",
                "qty",
                "count",
                "pieces",
                "piececount"
            ],
            [ImportFieldNames.Material] =
            [
                "material",
                "materialname",
                "materialtype",
                "sheetmaterial",
                "stock",
                "stockmaterial"
            ],
            [ImportFieldNames.Group] =
            [
                "group",
                "panelgroup",
                "partgroup",
                "nestgroup",
                "batch",
                "batchgroup"
            ]
        };

    public ColumnMappingPlan ResolveColumns(
        IReadOnlyList<string> availableColumns,
        ImportOptions? options,
        ICollection<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(availableColumns);
        ArgumentNullException.ThrowIfNull(errors);

        var availableSet = availableColumns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Select(column => column.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var explicitMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        var explicitTargets = new HashSet<string>(StringComparer.Ordinal);
        var explicitlyAssignedSources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in options?.ColumnMappings ?? [])
        {
            var sourceColumn = mapping.SourceColumn?.Trim() ?? string.Empty;
            var targetField = mapping.TargetField?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourceColumn) || string.IsNullOrWhiteSpace(targetField))
            {
                errors.Add(new ValidationError("invalid-column-mapping", "Column mappings require both a sourceColumn and a targetField."));
                continue;
            }

            if (!ImportFieldNames.All.Contains(targetField, StringComparer.Ordinal))
            {
                errors.Add(new ValidationError("unknown-target-field", $"'{targetField}' is not a supported import field."));
                continue;
            }

            explicitTargets.Add(targetField);

            if (!availableSet.Contains(sourceColumn))
            {
                errors.Add(new ValidationError(
                    "column-mapping-not-found",
                    $"Mapped import column '{sourceColumn}' was not found in the file header."));
                continue;
            }

            if (!explicitMappings.TryAdd(targetField, sourceColumn))
            {
                errors.Add(new ValidationError(
                    "duplicate-column-mapping",
                    $"Import field '{targetField}' was mapped more than once."));
                continue;
            }

            if (!explicitlyAssignedSources.Add(sourceColumn))
            {
                errors.Add(new ValidationError(
                    "duplicate-source-column-mapping",
                    $"Import column '{sourceColumn}' cannot be mapped to more than one field."));
                explicitMappings.Remove(targetField);
            }
        }

        var resolvedSources = explicitMappings.Values.ToHashSet(StringComparer.Ordinal);
        var fieldMappings = new List<ImportFieldMappingStatus>(ImportFieldNames.All.Count);
        var fieldToSource = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasAllRequiredFields = true;

        foreach (var field in ImportFieldNames.Required)
        {
            ResolveFieldMapping(
                field,
                isRequired: true,
                availableColumns,
                availableSet,
                explicitMappings,
                explicitTargets,
                resolvedSources,
                fieldMappings,
                fieldToSource,
                errors,
                ref hasAllRequiredFields);
        }

        foreach (var field in ImportFieldNames.Optional)
        {
            ResolveFieldMapping(
                field,
                isRequired: false,
                availableColumns,
                availableSet,
                explicitMappings,
                explicitTargets,
                resolvedSources,
                fieldMappings,
                fieldToSource,
                errors,
                ref hasAllRequiredFields);
        }

        return new ColumnMappingPlan(fieldMappings, fieldToSource, hasAllRequiredFields);
    }

    public MaterialMappingPlan ResolveMaterials(
        IReadOnlyList<PartRowUpdate> updates,
        IReadOnlyDictionary<string, Material> knownMaterialsByName,
        ImportOptions? options,
        ICollection<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(knownMaterialsByName);
        ArgumentNullException.ThrowIfNull(errors);

        var knownMaterialsById = knownMaterialsByName.Values
            .GroupBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var requestedMappings = new Dictionary<string, Material>(StringComparer.Ordinal);

        foreach (var mapping in options?.MaterialMappings ?? [])
        {
            var sourceMaterialName = mapping.SourceMaterialName?.Trim() ?? string.Empty;
            var targetMaterialId = mapping.TargetMaterialId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourceMaterialName) || string.IsNullOrWhiteSpace(targetMaterialId))
            {
                errors.Add(new ValidationError(
                    "invalid-material-mapping",
                    "Material mappings require both a sourceMaterialName and a targetMaterialId."));
                continue;
            }

            if (!knownMaterialsById.TryGetValue(targetMaterialId, out var targetMaterial))
            {
                errors.Add(new ValidationError(
                    "material-mapping-not-found",
                    $"Mapped material '{sourceMaterialName}' targets unknown material id '{targetMaterialId}'."));
                continue;
            }

            if (!requestedMappings.TryAdd(sourceMaterialName, targetMaterial))
            {
                errors.Add(new ValidationError(
                    "duplicate-material-mapping",
                    $"Import material '{sourceMaterialName}' was mapped more than once."));
            }
        }

        var resolutions = new List<ImportMaterialResolution>();
        var seenSourceMaterials = new HashSet<string>(StringComparer.Ordinal);
        var resolvedUpdates = new List<PartRowUpdate>(updates.Count);

        foreach (var update in updates)
        {
            var sourceMaterialName = update.MaterialName?.Trim() ?? string.Empty;
            var resolution = ResolveMaterial(sourceMaterialName, requestedMappings, knownMaterialsByName);

            resolvedUpdates.Add(resolution.Material is null
                ? update with { MaterialName = sourceMaterialName }
                : update with { MaterialName = resolution.Material.Name });

            if (!string.IsNullOrWhiteSpace(sourceMaterialName) && seenSourceMaterials.Add(sourceMaterialName))
            {
                resolutions.Add(new ImportMaterialResolution
                {
                    SourceMaterialName = sourceMaterialName,
                    Status = resolution.Material is null
                        ? ImportMaterialResolutionStatuses.Unresolved
                        : ImportMaterialResolutionStatuses.Resolved,
                    ResolvedMaterialId = resolution.Material?.MaterialId,
                    ResolvedMaterialName = resolution.Material?.Name
                });
            }
        }

        return new MaterialMappingPlan(resolvedUpdates, resolutions);
    }

    private static string? FindSuggestedSource(
        string field,
        IReadOnlyList<string> availableColumns,
        IReadOnlySet<string> resolvedSources)
    {
        if (!SuggestedHeaderAliases.TryGetValue(field, out var aliases))
        {
            return null;
        }

        var aliasSet = aliases.ToHashSet(StringComparer.Ordinal);

        foreach (var column in availableColumns)
        {
            var trimmedColumn = column?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedColumn) || resolvedSources.Contains(trimmedColumn))
            {
                continue;
            }

            if (aliasSet.Contains(Normalize(trimmedColumn)))
            {
                return trimmedColumn;
            }
        }

        return null;
    }

    private static (Material? Material, bool IsExplicit) ResolveMaterial(
        string sourceMaterialName,
        IReadOnlyDictionary<string, Material> requestedMappings,
        IReadOnlyDictionary<string, Material> knownMaterialsByName)
    {
        if (requestedMappings.TryGetValue(sourceMaterialName, out var mappedMaterial))
        {
            return (mappedMaterial, true);
        }

        return knownMaterialsByName.TryGetValue(sourceMaterialName, out var exactMaterial)
            ? (exactMaterial, false)
            : (null, false);
    }

    private static string Normalize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var bufferIndex = 0;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }

            buffer[bufferIndex++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..bufferIndex]);
    }

    private static void ResolveFieldMapping(
        string field,
        bool isRequired,
        IReadOnlyList<string> availableColumns,
        IReadOnlySet<string> availableSet,
        IReadOnlyDictionary<string, string> explicitMappings,
        IReadOnlySet<string> explicitTargets,
        HashSet<string> resolvedSources,
        ICollection<ImportFieldMappingStatus> fieldMappings,
        IDictionary<string, string> fieldToSource,
        ICollection<ValidationError> errors,
        ref bool hasAllRequiredFields)
    {
        explicitMappings.TryGetValue(field, out var sourceColumn);

        if (sourceColumn is null && !explicitTargets.Contains(field) && availableSet.Contains(field))
        {
            sourceColumn = field;
        }

        if (sourceColumn is not null)
        {
            resolvedSources.Add(sourceColumn);
            fieldToSource[field] = sourceColumn;
        }

        var suggestion = sourceColumn is null
            ? FindSuggestedSource(field, availableColumns, resolvedSources)
            : null;

        if (sourceColumn is null && isRequired)
        {
            hasAllRequiredFields = false;
            errors.Add(suggestion is null
                ? new ValidationError("missing-column", $"Missing required column '{field}'.")
                : new ValidationError("missing-column-mapping", $"Map import column '{suggestion}' to required field '{field}'."));
        }

        fieldMappings.Add(new ImportFieldMappingStatus
        {
            TargetField = field,
            SourceColumn = sourceColumn,
            SuggestedSourceColumn = suggestion
        });
    }
}

internal sealed record ColumnMappingPlan(
    IReadOnlyList<ImportFieldMappingStatus> FieldMappings,
    IReadOnlyDictionary<string, string> FieldToSource,
    bool HasAllRequiredFields);

internal sealed record MaterialMappingPlan(
    IReadOnlyList<PartRowUpdate> Updates,
    IReadOnlyList<ImportMaterialResolution> Resolutions);
