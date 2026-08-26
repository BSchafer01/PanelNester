using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Bridge;

internal static class ProjectImportFinalizer
{
    public static Project FinalizeWorkbook(
        Project project,
        ImportSourceMetadata importSource,
        IReadOnlyList<FinalizedWorksheetImport> worksheetImports)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentNullException.ThrowIfNull(worksheetImports);

        var orderedImports = worksheetImports
            .OrderBy(item => item.Selection.OriginalPosition)
            .ToArray();
        if (orderedImports.Length == 0)
        {
            throw new ImportSessionException(
                "import-worksheet-selection-required",
                "Select at least one Worksheet before finalizing the Import Session.");
        }

        if (orderedImports
            .GroupBy(item => item.Selection.OriginalPosition)
            .Any(group => group.Count() > 1))
        {
            throw new ImportSessionException(
                "import-worksheet-selection-duplicate",
                "Each selected Worksheet may be finalized only once.");
        }

        if (orderedImports.Any(item => string.IsNullOrWhiteSpace(item.Selection.OptimizationGroupId)))
        {
            throw new ImportSessionException(
                "import-optimization-group-required",
                "Every selected Worksheet must belong to an Optimization Group.");
        }

        var groups = project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => UpdateParts(
                group,
                group.Parts.Where(part => part.IsManual).ToArray()))
            .ToList();

        foreach (var worksheetImport in orderedImports)
        {
            var selection = worksheetImport.Selection;
            var groupIndex = groups.FindIndex(group => string.Equals(
                group.OptimizationGroupId,
                selection.OptimizationGroupId,
                StringComparison.Ordinal));
            if (groupIndex < 0)
            {
                groupIndex = groups.Count;
                var requestedName = string.IsNullOrWhiteSpace(selection.OptimizationGroupName)
                    ? selection.WorksheetName
                    : selection.OptimizationGroupName.Trim();
                groups.Add(new OptimizationGroup
                {
                    OptimizationGroupId = selection.OptimizationGroupId,
                    Name = MakeUniqueGroupName(requestedName, groups),
                    Order = groupIndex
                });
            }

            groups[groupIndex] = UpdateParts(
                groups[groupIndex],
                groups[groupIndex].Parts.Concat(worksheetImport.Response.Parts).ToArray());
        }

        var normalizedGroups = groups
            .Select(group => UpdateParts(group, CombineCompatibleImportedParts(group.Parts)))
            .Select((group, order) => group with { Order = order })
            .ToArray();
        var importedParts = normalizedGroups
            .SelectMany(group => group.Parts)
            .Where(part => !part.IsManual)
            .ToArray();
        var resolvedMaterialMappings = orderedImports
            .SelectMany(item => item.Response.MaterialResolutions)
            .Where(resolution =>
                !string.IsNullOrWhiteSpace(resolution.SourceMaterialName) &&
                !string.IsNullOrWhiteSpace(resolution.ResolvedMaterialId))
            .GroupBy(resolution => resolution.SourceMaterialName.Trim(), StringComparer.Ordinal)
            .Select(group => new ImportMaterialMapping
            {
                SourceMaterialName = group.Key,
                TargetMaterialId = group.First().ResolvedMaterialId
            })
            .ToArray();
        var configuration = new ImportConfiguration
        {
            Options = orderedImports[0].Options with
            {
                MaterialMappings = resolvedMaterialMappings
            },
            Worksheets = orderedImports.Select(item => new ImportWorksheetConfiguration
            {
                WorksheetName = item.Selection.WorksheetName,
                OriginalPosition = item.Selection.OriginalPosition,
                HeadingRange = item.Response.Worksheet?.HeadingRange ?? string.Empty,
                ColumnMappings = item.Response.ColumnMappings
                    .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                    .Select(mapping => new ImportColumnMapping
                    {
                        SourceColumn = mapping.SourceColumn!,
                        TargetField = mapping.TargetField
                    })
                    .ToArray(),
                OptimizationGroupId = item.Selection.OptimizationGroupId,
                ExcludedSourceRows = Array.Empty<int>()
            }).ToArray()
        };

        var compatibilityGroup = normalizedGroups.FirstOrDefault();
        return project with
        {
            State = project.State with
            {
                SourceFilePath = importSource.ImportSourcePath,
                ImportSource = importSource,
                ImportConfiguration = configuration,
                Parts = importedParts,
                OptimizationGroups = normalizedGroups,
                LastNestingResult = compatibilityGroup?.LastNestingResult,
                LastBatchNestingResult = compatibilityGroup?.LastBatchNestingResult
            }
        };
    }

    public static Project Finalize(
        Project project,
        ImportSourceMetadata importSource,
        ImportOptions importOptions,
        ImportResponse importResponse,
        string? targetOptimizationGroupId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(importSource.ImportSourcePath);
        ArgumentNullException.ThrowIfNull(importOptions);
        ArgumentNullException.ThrowIfNull(importResponse);
        var parts = importResponse.Parts;

        var nextPartsById = parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var assignedIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => SynchronizeExistingParts(group, nextPartsById, assignedIds))
            .ToArray();
        var unassignedParts = parts.Where(part => !assignedIds.Contains(part.RowId)).ToArray();

        if (groups.Length > 0 && unassignedParts.Length > 0)
        {
            var targetIndex = Array.FindIndex(
                groups,
                group => string.Equals(
                    group.OptimizationGroupId,
                    targetOptimizationGroupId,
                    StringComparison.Ordinal));
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            groups[targetIndex] = ClearResults(groups[targetIndex] with
            {
                Parts = groups[targetIndex].Parts.Concat(unassignedParts).ToArray()
            });
        }

        var compatibilityGroup = groups.FirstOrDefault();
        return project with
        {
            State = project.State with
            {
                SourceFilePath = importSource.ImportSourcePath,
                ImportSource = importSource,
                ImportConfiguration = BuildImportConfiguration(
                    importOptions,
                    importResponse,
                    groups.FirstOrDefault(group => string.Equals(
                        group.OptimizationGroupId,
                        targetOptimizationGroupId,
                        StringComparison.Ordinal))?.OptimizationGroupId ??
                    groups.FirstOrDefault()?.OptimizationGroupId),
                Parts = parts.ToArray(),
                OptimizationGroups = groups,
                LastNestingResult = compatibilityGroup?.LastNestingResult,
                LastBatchNestingResult = compatibilityGroup?.LastBatchNestingResult
            }
        };
    }

    private static ImportConfiguration BuildImportConfiguration(
        ImportOptions importOptions,
        ImportResponse importResponse,
        string? optimizationGroupId)
    {
        var resolvedColumnMappings = importResponse.ColumnMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
            .Select(mapping => new ImportColumnMapping
            {
                SourceColumn = mapping.SourceColumn!,
                TargetField = mapping.TargetField
            })
            .ToArray();
        var exactOptions = importOptions with { ColumnMappings = resolvedColumnMappings };
        var worksheet = importResponse.Worksheet;

        return new ImportConfiguration
        {
            Options = exactOptions,
            Worksheets = worksheet is null
                ? Array.Empty<ImportWorksheetConfiguration>()
                :
                [
                    new ImportWorksheetConfiguration
                    {
                        WorksheetName = worksheet.WorksheetName,
                        OriginalPosition = worksheet.OriginalPosition,
                        HeadingRange = worksheet.HeadingRange,
                        ColumnMappings = resolvedColumnMappings,
                        OptimizationGroupId = optimizationGroupId,
                        ExcludedSourceRows = Array.Empty<int>()
                    }
                ]
        };
    }

    private static OptimizationGroup SynchronizeExistingParts(
        OptimizationGroup group,
        IReadOnlyDictionary<string, PartRow> nextPartsById,
        ISet<string> assignedIds)
    {
        var parts = group.Parts
            .Select(part => nextPartsById.GetValueOrDefault(part.RowId))
            .Where(part => part is not null)
            .Cast<PartRow>()
            .ToArray();
        foreach (var part in parts)
        {
            assignedIds.Add(part.RowId);
        }

        return UpdateParts(group, parts);
    }

    private static OptimizationGroup UpdateParts(
        OptimizationGroup group,
        IReadOnlyList<PartRow> parts)
    {
        var changed = parts.Count != group.Parts.Count ||
                      parts.Where((part, index) => !part.Equals(group.Parts[index])).Any();
        return changed ? ClearResults(group with { Parts = parts }) : group;
    }

    private static OptimizationGroup ClearResults(OptimizationGroup group) =>
        group with
        {
            LastNestingResult = null,
            LastBatchNestingResult = null,
            ResultStatus = OptimizationResultStatus.None
        };

    private static IReadOnlyList<PartRow> CombineCompatibleImportedParts(
        IReadOnlyList<PartRow> parts)
    {
        var combined = new List<PartRow>(parts.Count);
        var indexByKey = new Dictionary<ImportedPartCompatibilityKey, int>();
        foreach (var part in parts)
        {
            if (part.IsManual ||
                part.Quantity <= 0 ||
                string.Equals(part.ValidationStatus, ValidationStatuses.Error, StringComparison.Ordinal))
            {
                combined.Add(part);
                continue;
            }

            var key = new ImportedPartCompatibilityKey(
                part.ImportedId,
                part.Length,
                part.Width,
                part.MaterialName,
                part.Group,
                part.SheetNumber,
                part.RowNumber,
                part.ColumnNumber);
            if (!indexByKey.TryGetValue(key, out var existingIndex))
            {
                indexByKey[key] = combined.Count;
                combined.Add(part);
                continue;
            }

            var existing = combined[existingIndex];
            var quantity = checked(existing.Quantity + part.Quantity);
            combined[existingIndex] = existing with
            {
                Quantity = quantity,
                QuantityText = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SourceReferences = existing.SourceReferences.Concat(part.SourceReferences).ToArray()
            };
        }

        return combined;
    }

    private readonly record struct ImportedPartCompatibilityKey(
        string ImportedId,
        decimal Length,
        decimal Width,
        string MaterialName,
        string? PartGroup,
        string? SheetNumber,
        int? RowNumber,
        int? ColumnNumber);

    private static string MakeUniqueGroupName(
        string requestedName,
        IReadOnlyList<OptimizationGroup> existingGroups)
    {
        var names = existingGroups
            .Select(group => group.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requestedName))
        {
            return requestedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{requestedName} ({suffix})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}

internal sealed record FinalizedWorksheetImport(
    ImportWorksheetSelection Selection,
    ImportOptions Options,
    ImportResponse Response);
