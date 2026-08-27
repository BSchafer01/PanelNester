using PanelNester.Domain.Models;
using PanelNester.Domain.Contracts;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Bridge;

internal static class ProjectImportFinalizer
{
    public static async Task<ImportResponse> ApplyPartOverridesAsync(
        ImportResponse response,
        IReadOnlyList<PartOverride> partOverrides,
        IPartEditorService partEditorService,
        CancellationToken cancellationToken)
    {
        var current = response;
        foreach (var partOverride in partOverrides)
        {
            var importedRequiredPiece = current.RequiredPieces.FirstOrDefault(piece =>
                string.Equals(piece.RequiredPieceId, partOverride.RowId, StringComparison.Ordinal));
            if (importedRequiredPiece is not null && partOverride.CurrentRequiredPiece is not null)
            {
                var matchingPieces = current.RequiredPieces
                    .Where(piece => piece.SourceReferences.Any(reference =>
                        partOverride.SourceReferences.Any(overrideReference =>
                            reference.MatchesIdentity(overrideReference))))
                    .ToArray();
                if (partOverride.SourceReferences.Count == 0 ||
                    !partOverride.SourceReferences.All(overrideReference =>
                        matchingPieces.Any(piece => piece.SourceReferences.Any(reference =>
                            reference.MatchesIdentity(overrideReference)))))
                {
                    continue;
                }

                var matchedIds = matchingPieces
                    .Select(piece => piece.RequiredPieceId)
                    .ToHashSet(StringComparer.Ordinal);
                var importedSourceReferences = matchingPieces
                    .SelectMany(piece => piece.SourceReferences)
                    .ToArray();
                var validatedPiece = ValidateRequiredPieceOverride(
                    importedRequiredPiece with { SourceReferences = importedSourceReferences },
                    partOverride.CurrentRequiredPiece,
                    out var validationErrors);
                var retainedErrors = current.Errors
                    .Where(error => error.RowId is null || !matchedIds.Contains(error.RowId))
                    .ToArray();
                current = current with
                {
                    Success = retainedErrors.Length == 0 && validationErrors.Count == 0,
                    RequiredPieces = current.RequiredPieces
                        .Where(piece => !matchedIds.Contains(piece.RequiredPieceId) ||
                            string.Equals(piece.RequiredPieceId, importedRequiredPiece.RequiredPieceId, StringComparison.Ordinal))
                        .Select(piece => string.Equals(piece.RequiredPieceId, importedRequiredPiece.RequiredPieceId, StringComparison.Ordinal)
                            ? validatedPiece
                            : piece)
                        .ToArray(),
                    Errors = retainedErrors.Concat(validationErrors).ToArray()
                };
                continue;
            }

            var imported = current.Parts.FirstOrDefault(part =>
                string.Equals(part.RowId, partOverride.RowId, StringComparison.Ordinal));
            if (imported is null ||
                partOverride.SourceReferences.Count == 0 ||
                !partOverride.SourceReferences.All(overrideReference =>
                    imported.SourceReferences.Any(reference =>
                        reference.MatchesIdentity(overrideReference))))
            {
                continue;
            }

            var values = partOverride.CurrentValues;
            var validated = await partEditorService.UpdateRowAsync(
                    current.Parts,
                    new PartRowUpdate
                    {
                        RowId = imported.RowId,
                        ImportedId = values.ImportedId,
                        Length = values.LengthText ?? values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Width = values.WidthText ?? values.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Quantity = values.QuantityText ?? values.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        MaterialName = values.MaterialName,
                        Group = values.Group,
                        IsManual = false,
                        SheetNumber = values.SheetNumber,
                        RowNumber = values.RowNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ColumnNumber = values.ColumnNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SourceReferences = imported.SourceReferences
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            current = current with
            {
                Success = validated.Success,
                Parts = validated.Parts,
                Errors = validated.Errors,
                Warnings = validated.Warnings
            };
        }

        return current;
    }

    public static ImportWorksheetSelection ReconcilePartOverrides(
        ImportWorksheetSelection selection,
        ImportResponse importedResponse,
        ImportResponse validatedResponse)
    {
        var importedPartsById = importedResponse.Parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var partsById = validatedResponse.Parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var importedPiecesById = importedResponse.RequiredPieces.ToDictionary(piece => piece.RequiredPieceId, StringComparer.Ordinal);
        var piecesById = validatedResponse.RequiredPieces.ToDictionary(piece => piece.RequiredPieceId, StringComparer.Ordinal);
        return selection with
        {
            PartOverrides = selection.PartOverrides.Select(partOverride =>
                importedPiecesById.TryGetValue(partOverride.RowId, out var importedPiece) &&
                piecesById.TryGetValue(partOverride.RowId, out var currentPiece) &&
                partOverride.SourceReferences.All(overrideReference =>
                    currentPiece.SourceReferences.Any(reference => reference.MatchesIdentity(overrideReference)))
                    ? partOverride with
                    {
                        ImportedRequiredPiece = partOverride.ImportedRequiredPiece ?? importedPiece,
                        CurrentRequiredPiece = currentPiece,
                        SourceReferences = currentPiece.SourceReferences
                    }
                    :
                importedPartsById.TryGetValue(partOverride.RowId, out var importedValues) &&
                partsById.TryGetValue(partOverride.RowId, out var currentValues)
                    ? partOverride with
                    {
                        ImportedValues = importedValues,
                        CurrentValues = currentValues,
                        SourceReferences = currentValues.SourceReferences
                    }
                    : partOverride).ToArray(),
            ExcludedSourceRows = selection.ExcludedSourceRows.Select(excluded =>
            {
                var sourceReferences = importedPartsById.TryGetValue(excluded.RowId, out var importedValues)
                    ? importedValues.SourceReferences
                    : importedPiecesById.TryGetValue(excluded.RowId, out var importedPiece)
                        ? importedPiece.SourceReferences
                        : null;
                if (sourceReferences is null)
                {
                    return excluded;
                }

                var sourceReference = sourceReferences.FirstOrDefault(reference =>
                    reference.MatchesIdentity(excluded.SourceReference));
                var originalError = importedResponse.Errors.FirstOrDefault(error =>
                    string.Equals(error.RowId, excluded.RowId, StringComparison.Ordinal));
                return sourceReference is null || originalError is null
                    ? excluded
                    : excluded with
                    {
                        SourceReference = sourceReference,
                        OriginalValidationError = new SourceRowValidationError
                        {
                            Code = originalError.Code,
                            Message = originalError.Message
                        }
                    };
            }).ToArray()
        };
    }

    public static ImportResponse ResolveSourceRows(
        ImportResponse response,
        ImportWorksheetSelection selection)
    {
        var partsById = response.Parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var piecesById = response.RequiredPieces.ToDictionary(piece => piece.RequiredPieceId, StringComparer.Ordinal);
        var excludedIds = selection.ExcludedSourceRows
            .Where(row =>
                (partsById.TryGetValue(row.RowId, out var part) &&
                 part.SourceReferences.Any(reference => reference.MatchesIdentity(row.SourceReference))) ||
                (piecesById.TryGetValue(row.RowId, out var piece) &&
                 piece.SourceReferences.Any(reference => reference.MatchesIdentity(row.SourceReference))))
            .Select(row => row.RowId)
            .ToHashSet(StringComparer.Ordinal);
        var overrides = selection.PartOverrides
            .Where(partOverride => IsResolvedOverride(partOverride, partsById, piecesById))
            .ToDictionary(partOverride => partOverride.RowId, StringComparer.Ordinal);
        var parts = response.Parts
            .Where(part => !excludedIds.Contains(part.RowId))
            .ToArray();
        var requiredPieces = response.RequiredPieces
            .Where(piece => !excludedIds.Contains(piece.RequiredPieceId))
            .ToArray();
        var resolvedIds = excludedIds.Concat(overrides.Keys).ToHashSet(StringComparer.Ordinal);
        var errors = response.Errors
            .Where(error => error.RowId is null || !resolvedIds.Contains(error.RowId))
            .ToArray();
        var warnings = response.Warnings
            .Where(warning => warning.RowId is null || !excludedIds.Contains(warning.RowId))
            .ToArray();
        return response with
        {
            Success = errors.Length == 0,
            Parts = parts,
            RequiredPieces = requiredPieces,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool IsResolvedOverride(
        PartOverride partOverride,
        IReadOnlyDictionary<string, PartRow> partsById,
        IReadOnlyDictionary<string, RequiredPiece> piecesById)
    {
        IReadOnlyList<SourceReference>? sourceReferences = null;
        var validationStatus = ValidationStatuses.Error;
        if (partsById.TryGetValue(partOverride.RowId, out var part))
        {
            sourceReferences = part.SourceReferences;
            validationStatus = part.ValidationStatus;
        }
        else if (piecesById.TryGetValue(partOverride.RowId, out var piece))
        {
            sourceReferences = piece.SourceReferences;
            validationStatus = piece.ValidationStatus;
        }

        return sourceReferences is { Count: > 0 } &&
            validationStatus != ValidationStatuses.Error &&
            partOverride.SourceReferences.Count > 0 &&
            partOverride.SourceReferences.All(overrideReference =>
                sourceReferences.Any(reference => reference.MatchesIdentity(overrideReference)));
    }

    private static RequiredPiece ValidateRequiredPieceOverride(
        RequiredPiece imported,
        RequiredPiece requested,
        out IReadOnlyList<ValidationError> errors)
    {
        var messages = new List<string>();
        var validationErrors = new List<ValidationError>();
        var sourceReference = imported.SourceReferences.FirstOrDefault();
        void Add(string code, string message)
        {
            messages.Add(message);
            validationErrors.Add(new ValidationError(code, message, imported.RequiredPieceId, sourceReference));
        }

        var quantity = requested.Quantity;
        var quantityText = requested.QuantityText?.Trim();
        if (!string.IsNullOrWhiteSpace(quantityText) &&
            !int.TryParse(quantityText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out quantity))
        {
            Add("invalid-quantity", "Quantity must be an integer value.");
        }
        else if (quantity <= 0)
        {
            Add("quantity-out-of-range", "Quantity must be greater than zero.");
        }

        var length = requested.Length;
        var lengthText = requested.LengthText?.Trim();
        if (!string.IsNullOrWhiteSpace(lengthText) && !InchMeasurementParser.TryParse(lengthText, out length))
        {
            Add("invalid-length", "Length must be a decimal, fraction, or mixed-number inch value.");
        }
        else if (length <= 0)
        {
            Add("length-out-of-range", "Length must be greater than zero.");
        }
        if (string.IsNullOrWhiteSpace(requested.ProfileNumber)) Add("missing-profile-number", "Profile Number is required.");
        errors = validationErrors;
        return requested with
        {
            RequiredPieceId = imported.RequiredPieceId,
            Quantity = quantity,
            QuantityText = quantityText,
            Length = length,
            LengthText = lengthText,
            ProfileNumber = requested.ProfileNumber.Trim(),
            PartName = NormalizeOptional(requested.PartName),
            Finish = NormalizeOptional(requested.Finish),
            PartNumber = NormalizeOptional(requested.PartNumber),
            IsManual = false,
            ValidationStatus = messages.Count == 0 ? ValidationStatuses.Valid : ValidationStatuses.Error,
            ValidationMessages = messages,
            SourceReferences = imported.SourceReferences
        };
    }

    public static Project FinalizeWorkbook(
        Project project,
        ImportSourceMetadata importSource,
        IReadOnlyList<FinalizedWorksheetImport> worksheetImports,
        bool replaceExistingImportSource = false,
        Action<WorkbookImportPhase, string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentNullException.ThrowIfNull(worksheetImports);
        cancellationToken.ThrowIfCancellationRequested();

        var targetProject = PrepareImportSource(project, replaceExistingImportSource);

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

        if (project.ProjectKind == ProjectKind.StockLength)
        {
            return FinalizeStockLength(
                project,
                importSource,
                orderedImports,
                replaceExistingImportSource,
                reportProgress,
                cancellationToken);
        }

        var selectedGroupIds = orderedImports
            .Select(item => item.Selection.OptimizationGroupId)
            .ToHashSet(StringComparer.Ordinal);
        reportProgress?.Invoke(WorkbookImportPhase.CombiningParts, "Combining parts");
        var groups = targetProject.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => selectedGroupIds.Contains(group.OptimizationGroupId)
                ? UpdateParts(group, group.Parts.Where(part => part.IsManual).ToArray())
                : group)
            .ToList();

        foreach (var worksheetImport in orderedImports)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    Order = groupIndex,
                    Origin = OptimizationGroupOrigin.ImportSource
                });
            }

            groups[groupIndex] = UpdateParts(
                groups[groupIndex],
                groups[groupIndex].Parts.Concat(worksheetImport.Response.Parts).ToArray());
        }

        var normalizedGroups = groups
            .Select(group => UpdateParts(
                group,
                CombineCompatibleImportedParts(group.Parts, cancellationToken)))
            .Select((group, order) => group with { Order = order })
            .ToArray();
        var importedParts = normalizedGroups
            .SelectMany(group => group.Parts)
            .Where(part => !part.IsManual)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(WorkbookImportPhase.Finalizing, "Finalizing");
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
            PartOverrides = orderedImports.SelectMany(item => item.Selection.PartOverrides).ToArray(),
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
                ExcludedSourceRows = item.Selection.ExcludedSourceRows
            }).ToArray()
        };

        var compatibilityGroup = normalizedGroups.FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();
        return targetProject with
        {
            State = targetProject.State with
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

    private static Project FinalizeStockLength(
        Project project,
        ImportSourceMetadata importSource,
        IReadOnlyList<FinalizedWorksheetImport> orderedImports,
        bool replaceExistingImportSource,
        Action<WorkbookImportPhase, string>? reportProgress,
        CancellationToken cancellationToken)
    {
        var targetProject = PrepareImportSource(project, replaceExistingImportSource);
        var selectedGroupIds = orderedImports
            .Select(item => item.Selection.OptimizationGroupId)
            .ToHashSet(StringComparer.Ordinal);
        var groups = targetProject.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => selectedGroupIds.Contains(group.OptimizationGroupId)
                ? group with
                {
                    RequiredPieces = group.RequiredPieces.Where(piece => piece.IsManual).ToArray()
                }
                : group)
            .ToList();
        var requestedStockLengths = orderedImports
            .Where(item => item.Selection.StockLength is > 0)
            .GroupBy(item => item.Selection.OptimizationGroupId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Selection.StockLength!.Value).Distinct().ToArray(),
                StringComparer.Ordinal);
        var conflictingGroup = requestedStockLengths.FirstOrDefault(item => item.Value.Length > 1);
        if (!string.IsNullOrEmpty(conflictingGroup.Key))
        {
            throw new ImportSessionException(
                "import-stock-length-conflict",
                "Worksheets assigned to the same Optimization Group must use the same Stock Length.");
        }
        groups = groups.Select(group =>
            requestedStockLengths.TryGetValue(group.OptimizationGroupId, out var lengths)
                ? group with { StockLength = lengths[0] }
                : group).ToList();

        reportProgress?.Invoke(WorkbookImportPhase.CombiningParts, "Combining Required Pieces");
        foreach (var worksheetImport in orderedImports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = worksheetImport.Selection;
            var groupIndex = groups.FindIndex(group => string.Equals(
                group.OptimizationGroupId,
                selection.OptimizationGroupId,
                StringComparison.Ordinal));
            if (groupIndex < 0)
            {
                var requestedName = string.IsNullOrWhiteSpace(selection.OptimizationGroupName)
                    ? selection.WorksheetName
                    : selection.OptimizationGroupName.Trim();
                groupIndex = groups.Count;
                groups.Add(new OptimizationGroup
                {
                    OptimizationGroupId = selection.OptimizationGroupId,
                    Name = MakeUniqueGroupName(requestedName, groups),
                    Order = groupIndex,
                    Origin = OptimizationGroupOrigin.ImportSource,
                    StockLength = selection.StockLength
                });
            }

            if (groups[groupIndex].StockLength is null or <= 0)
            {
                throw new ImportSessionException(
                    "import-stock-length-required",
                    $"Optimization Group '{groups[groupIndex].Name}' requires a positive Stock Length before finalization.");
            }

            groups[groupIndex] = groups[groupIndex] with
            {
                RequiredPieces = groups[groupIndex].RequiredPieces
                    .Concat(worksheetImport.Response.RequiredPieces)
                    .ToArray()
            };
        }

        var previousGroups = project.State.OptimizationGroups.ToDictionary(
            group => group.OptimizationGroupId,
            StringComparer.Ordinal);
        var normalizedGroups = groups
            .Select(group =>
            {
                var pieces = CombineCompatibleImportedRequiredPieces(group.RequiredPieces, cancellationToken);
                var updated = group with
                {
                    RequiredPieces = pieces,
                    StockGroups = BuildStockGroups(pieces)
                };
                if (!previousGroups.TryGetValue(group.OptimizationGroupId, out var previous) ||
                    !HasSameOptimizationInputs(previous.RequiredPieces, updated.RequiredPieces))
                {
                    return ClearResults(updated);
                }

                var preserved = updated with
                {
                    LastStockLengthOptimizationResult = previous.LastStockLengthOptimizationResult,
                    LastStockLengthGenerationError = previous.LastStockLengthGenerationError,
                    LastNestingResult = previous.LastNestingResult,
                    LastBatchNestingResult = previous.LastBatchNestingResult,
                    ResultStatus = previous.ResultStatus
                };
                return preserved.LastStockLengthOptimizationResult is null
                    ? preserved
                    : preserved with
                    {
                        LastStockLengthOptimizationResult = preserved.LastStockLengthOptimizationResult
                            .RefreshRequiredPieceMetadata(previous.RequiredPieces, updated.RequiredPieces)
                    };
            })
            .Select((group, order) => group with { Order = order })
            .ToArray();
        var configuration = new ImportConfiguration
        {
            Options = orderedImports[0].Options with { MaterialMappings = Array.Empty<ImportMaterialMapping>() },
            PartOverrides = orderedImports.SelectMany(item => item.Selection.PartOverrides).ToArray(),
            Worksheets = orderedImports.Select(item => new ImportWorksheetConfiguration
            {
                WorksheetName = item.Selection.WorksheetName,
                OriginalPosition = item.Selection.OriginalPosition,
                HeadingRange = item.Response.Worksheet?.HeadingRange ?? item.Selection.HeadingRange,
                ColumnMappings = item.Response.ColumnMappings
                    .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                    .Select(mapping => new ImportColumnMapping
                    {
                        SourceColumn = mapping.SourceColumn!,
                        TargetField = mapping.TargetField
                    })
                    .ToArray(),
                OptimizationGroupId = item.Selection.OptimizationGroupId,
                ExcludedSourceRows = item.Selection.ExcludedSourceRows
            }).ToArray()
        };

        reportProgress?.Invoke(WorkbookImportPhase.Finalizing, "Finalizing");
        return targetProject with
        {
            MaterialSnapshots = Array.Empty<Material>(),
            State = targetProject.State with
            {
                SourceFilePath = importSource.ImportSourcePath,
                ImportSource = importSource,
                ImportConfiguration = configuration,
                Parts = Array.Empty<PartRow>(),
                OptimizationGroups = normalizedGroups,
                LastNestingResult = null,
                LastBatchNestingResult = null
            }
        };
    }

    private static IReadOnlyList<RequiredPiece> CombineCompatibleImportedRequiredPieces(
        IReadOnlyList<RequiredPiece> pieces,
        CancellationToken cancellationToken)
    {
        var combined = new List<RequiredPiece>(pieces.Count);
        var importedIndexes = new Dictionary<ImportedRequiredPieceCompatibilityKey, int>();
        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece.IsManual || piece.Quantity <= 0 || piece.Length <= 0 ||
                string.Equals(piece.ValidationStatus, ValidationStatuses.Error, StringComparison.Ordinal))
            {
                combined.Add(piece);
                continue;
            }

            var key = new ImportedRequiredPieceCompatibilityKey(
                piece.Length,
                NormalizeCompatibility(piece.ProfileNumber),
                NormalizeCompatibility(piece.PartName),
                NormalizeCompatibility(piece.Finish),
                NormalizeCompatibility(piece.PartNumber));
            if (!importedIndexes.TryGetValue(key, out var index))
            {
                importedIndexes[key] = combined.Count;
                combined.Add(piece with
                {
                    ProfileNumber = piece.ProfileNumber.Trim(),
                    PartName = NormalizeOptional(piece.PartName),
                    Finish = NormalizeOptional(piece.Finish),
                    PartNumber = NormalizeOptional(piece.PartNumber)
                });
                continue;
            }

            var existing = combined[index];
            var quantity = checked(existing.Quantity + piece.Quantity);
            combined[index] = existing with
            {
                Quantity = quantity,
                QuantityText = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SourceReferences = existing.SourceReferences.Concat(piece.SourceReferences).ToArray()
            };
        }

        return combined;
    }

    private static IReadOnlyList<StockGroup> BuildStockGroups(IReadOnlyList<RequiredPiece> pieces) =>
        pieces
            .GroupBy(
                piece => (Profile: NormalizeCompatibility(piece.ProfileNumber), Finish: NormalizeCompatibility(piece.Finish)))
            .Select(group => new StockGroup
            {
                ProfileNumber = group.First().ProfileNumber,
                Finish = group.First().Finish,
                RequiredPieceIds = group.Select(piece => piece.RequiredPieceId).ToArray()
            })
            .ToArray();

    private static string NormalizeCompatibility(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private readonly record struct ImportedRequiredPieceCompatibilityKey(
        decimal Length,
        string ProfileNumber,
        string PartName,
        string Finish,
        string PartNumber);

    public static Project Finalize(
        Project project,
        ImportSourceMetadata importSource,
        ImportOptions importOptions,
        ImportResponse importResponse,
        string? targetOptimizationGroupId,
        bool replaceExistingImportSource = false,
        Action<WorkbookImportPhase, string>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(importSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(importSource.ImportSourcePath);
        ArgumentNullException.ThrowIfNull(importOptions);
        ArgumentNullException.ThrowIfNull(importResponse);
        cancellationToken.ThrowIfCancellationRequested();
        var targetProject = PrepareImportSource(project, replaceExistingImportSource);
        var parts = importResponse.Parts;

        reportProgress?.Invoke(WorkbookImportPhase.CombiningParts, "Combining parts");
        var nextPartsById = parts.ToDictionary(part => part.RowId, StringComparer.Ordinal);
        var assignedIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = targetProject.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .Select(group => SynchronizeExistingParts(group, nextPartsById, assignedIds))
            .ToArray();
        var unassignedParts = parts.Where(part => !assignedIds.Contains(part.RowId)).ToArray();

        if (groups.Length == 0 && unassignedParts.Length > 0)
        {
            groups =
            [
                ImportSourceReplacementService.CreateSourceOptimizationGroup(
                    targetProject,
                    importSource,
                    unassignedParts)
            ];
            unassignedParts = [];
        }

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

        cancellationToken.ThrowIfCancellationRequested();
        reportProgress?.Invoke(WorkbookImportPhase.Finalizing, "Finalizing");
        var compatibilityGroup = groups.FirstOrDefault();
        return targetProject with
        {
            State = targetProject.State with
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
                        ExcludedSourceRows = Array.Empty<ExcludedSourceRow>()
                    }
                ]
        };
    }

    private static Project PrepareImportSource(Project project, bool replacementConfirmed)
    {
        var preparation = ImportSourceReplacementService.Prepare(project, replacementConfirmed);
        if (preparation.ConfirmationRequired)
        {
            throw new ImportSessionException(
                "import-source-replacement-confirmation-required",
                "Replacing the existing Import Source requires explicit confirmation.");
        }

        return preparation.Project;
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
            LastStockLengthOptimizationResult = null,
            LastStockLengthGenerationError = null,
            LastNestingResult = null,
            LastBatchNestingResult = null,
            ResultStatus = OptimizationResultStatus.None
        };

    private static bool HasSameOptimizationInputs(
        IReadOnlyList<RequiredPiece> left,
        IReadOnlyList<RequiredPiece> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.HasSameOptimizationInputs(pair.Second));

    private static IReadOnlyList<PartRow> CombineCompatibleImportedParts(
        IReadOnlyList<PartRow> parts,
        CancellationToken cancellationToken)
    {
        var combined = new List<PartRow>(parts.Count);
        var indexByKey = new Dictionary<ImportedPartCompatibilityKey, int>();
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
