using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Desktop.Bridge;

internal sealed class ImportSessionCoordinator
{
    private static readonly string SnapshotDirectory = Path.Combine(
        Path.GetTempPath(),
        "PanelNester.ImportSessions");

    private readonly IImportService _importService;
    private readonly ConcurrentDictionary<string, ImportSessionSnapshot> _sessions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cancelledSessionIds =
        new(StringComparer.Ordinal);

    public ImportSessionCoordinator(IImportService importService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    public async Task<ImportSessionResult> BeginAsync(
        string sessionId,
        string importSourcePath,
        ProjectKind projectKind,
        CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(importSourcePath);

        if (_cancelledSessionIds.TryRemove(sessionId, out _))
        {
            throw new OperationCanceledException("The Import Session was cancelled before snapshot capture began.");
        }

        var normalizedPath = Path.GetFullPath(importSourcePath.Trim());
        var session = new ImportSessionSnapshot(normalizedPath, Path.GetExtension(normalizedPath), projectKind);
        if (!_sessions.TryAdd(sessionId, session))
        {
            session.Release();
            throw new ImportSessionException(
                "import-session-exists",
                $"Import Session '{sessionId}' already exists.");
        }

        if (_cancelledSessionIds.TryRemove(sessionId, out _))
        {
            Release(sessionId);
            throw new OperationCanceledException("The Import Session was cancelled before snapshot capture began.");
        }

        try
        {
            session.ReportProgress(WorkbookImportPhase.OpeningWorkbook, "Opening workbook");
            if (session.IsWorkbook)
            {
                try
                {
                    await session.PreflightAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (WorkbookSafetyException exception)
                {
                    throw new ImportSessionException("workbook-safety-ceiling-exceeded", exception.Message);
                }
                catch (EncryptedWorkbookException exception)
                {
                    throw new ImportSessionException("encrypted-workbook", exception.Message);
                }
            }
            await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
            session.ReportProgress(WorkbookImportPhase.InspectingWorksheets, "Inspecting Worksheets");
            WorkbookDiscovery? workbook;
            try
            {
                workbook = await DiscoverWorkbookAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (EncryptedWorkbookException exception)
            {
                throw new ImportSessionException("encrypted-workbook", exception.Message);
            }
            workbook = session.AttachPreflight(workbook);
            session.RecordWorkbookDiscovery(workbook);
            return new ImportSessionResult(
                session.ImportSource,
                new ImportResponse { Success = true },
                workbook);
        }
        catch (WorkbookSafetyException exception)
        {
            Release(sessionId);
            throw new ImportSessionException("workbook-safety-ceiling-exceeded", exception.Message);
        }
        catch
        {
            Release(sessionId);
            throw;
        }
    }

    public async Task<ImportSessionResult> PreviewAsync(
        string sessionId,
        ImportOptions? options,
        IReadOnlyList<ImportNewMaterialRequest> newMaterials,
        string? worksheetName,
        string? headingRange,
        CancellationToken cancellationToken)
    {
        var session = GetSession(sessionId);
        try
        {
            var effectiveOptions = (options ?? new ImportOptions()) with
            {
                ProjectKind = session.ProjectKind
            };
            session.ReportWorksheetProgress(worksheetName);
            var response = await ImportSnapshotAsync(session, effectiveOptions, worksheetName, headingRange, cancellationToken).ConfigureAwait(false);
            session.ReportProgress(WorkbookImportPhase.Validating, "Validating");
            session.RecordWorksheetPreview(worksheetName, effectiveOptions, newMaterials, response);
            return new ImportSessionResult(session.ImportSource, response);
        }
        catch
        {
            Release(sessionId);
            throw;
        }
    }

    public ImportSessionFinalization BeginFinalization(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var session = GetSession(sessionId);
        var operationToken = session.BeginOperation(cancellationToken);
        return new ImportSessionFinalization(this, sessionId, session, operationToken);
    }

    public bool Cancel(string sessionId)
    {
        ValidateSessionId(sessionId);
        if (Release(sessionId))
        {
            return true;
        }

        _cancelledSessionIds.TryAdd(sessionId, 0);
        return false;
    }

    public ImportSessionProgressSnapshot GetProgress(string sessionId)
    {
        var session = GetSession(sessionId);
        return session.GetProgress();
    }

    private async Task<ImportResponse> ImportSnapshotAsync(
        ImportSessionSnapshot session,
        ImportOptions? options,
        string? worksheetName,
        string? headingRange,
        CancellationToken cancellationToken)
    {
        var operationToken = session.BeginOperation(cancellationToken);

        try
        {
            return await ImportSnapshotFileAsync(session, options, worksheetName, headingRange, operationToken).ConfigureAwait(false);
        }
        finally
        {
            session.CompleteOperation();
        }
    }

    private async Task<ImportResponse> ImportSnapshotFileAsync(
        ImportSessionSnapshot session,
        ImportOptions? options,
        string? worksheetName,
        string? headingRange,
        CancellationToken operationToken)
    {
        var response = await WithSnapshotFileAsync(
            session,
            operationToken,
            snapshotPath =>
            {
                var request = new ImportRequest
                {
                    FilePath = snapshotPath,
                    Options = (options ?? new ImportOptions()) with { ProjectKind = session.ProjectKind },
                    WorksheetName = worksheetName,
                    HeadingRange = headingRange
                };
                return _importService is IWorkbookImportProgressService progressService
                    ? progressService.ImportAsync(
                        request,
                        new InlineProgress<WorkbookImportProgress>(session.ReportImportServiceProgress),
                        operationToken)
                    : _importService.ImportAsync(request, operationToken);
            }).ConfigureAwait(false);

        return RestoreImportSourceIdentity(session, response);
    }

    private static async Task<TResult> WithSnapshotFileAsync<TResult>(
        ImportSessionSnapshot session,
        CancellationToken cancellationToken,
        Func<string, Task<TResult>> operation)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        var snapshotPath = Path.Combine(SnapshotDirectory, $"{Guid.NewGuid():N}{session.Extension}");
        try
        {
            await File.WriteAllBytesAsync(snapshotPath, session.GetContents(), cancellationToken)
                .ConfigureAwait(false);
            return await operation(snapshotPath).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
    }

    private async Task<WorkbookDiscovery?> DiscoverWorkbookAsync(
        ImportSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        if (string.Equals(session.Extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
            session.ProjectKind == ProjectKind.StockLength)
        {
            var response = await ImportSnapshotFileAsync(
                    session,
                    new ImportOptions { ProjectKind = ProjectKind.StockLength },
                    null,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Worksheet is null
                ? null
                : new WorkbookDiscovery
                {
                    InitialWorksheetName = response.Worksheet.WorksheetName,
                    Worksheets = [response.Worksheet]
                };
        }

        if (!string.Equals(session.Extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(session.Extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await WithSnapshotFileAsync(
                session,
                cancellationToken,
                snapshotPath =>
                {
                    var assessment = WorkbookPackagePreflight.Inspect(
                        snapshotPath,
                        WorkbookSafetyLimits.DesktopDefault,
                        cancellationToken);
                    session.RecordPreflight(assessment);
                    return new WorkbookDiscoveryService().DiscoverAsync(
                        snapshotPath,
                        session.ProjectKind,
                        cancellationToken);
                })
            .ConfigureAwait(false);
    }

    private static ImportResponse RestoreImportSourceIdentity(
        ImportSessionSnapshot session,
        ImportResponse response)
    {
        if (!string.Equals(session.Extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        var worksheetName = Path.GetFileName(session.ImportSourcePath);
        return response with
        {
            Worksheet = response.Worksheet is null
                ? null
                : response.Worksheet with { WorksheetName = worksheetName },
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

    private ImportSessionSnapshot GetSession(string sessionId)
    {
        ValidateSessionId(sessionId);
        return _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new ImportSessionException(
                "import-session-not-found",
                $"Import Session '{sessionId}' was not found or has already ended.");
    }

    private bool Release(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return false;
        }

        session.Release();
        return true;
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ImportSessionException(
                "import-session-id-required",
                "Import Session operations require a sessionId.");
        }
    }

    internal sealed class ImportSessionSnapshot(
        string importSourcePath,
        string extension,
        ProjectKind projectKind = ProjectKind.Sheet)
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _operationCancellation;
        private byte[]? _contents;
        private ImportSourceMetadata? _importSource;
        private readonly Dictionary<string, ReadyWorksheetPreview> _readyWorksheetPreviews =
            new(StringComparer.Ordinal);
        private bool _released;

        public string ImportSourcePath { get; } = importSourcePath;

        public string Extension { get; } = extension;

        public ProjectKind ProjectKind { get; } = projectKind;

        private WorkbookDiscovery? _workbook;
        private WorkbookPreflightAssessment? _preflight;
        private WorkbookImportProgress? _progress;
        private readonly List<WorkbookImportProgress> _progressHistory = [];

        public bool IsWorkbook =>
            string.Equals(Extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Extension, ".xlsm", StringComparison.OrdinalIgnoreCase);

        public ImportSourceMetadata ImportSource
        {
            get
            {
                lock (_gate)
                {
                    return _importSource ?? throw new ImportSessionException(
                        "import-snapshot-not-ready",
                        "The Import Snapshot has not finished capturing.");
                }
            }
        }

        public async Task CaptureAsync(CancellationToken cancellationToken)
        {
            var operationToken = BeginOperation(cancellationToken);
            try
            {
                await using var source = new FileStream(
                    ImportSourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    useAsync: true);
                byte[] contents;
                if (IsWorkbook)
                {
                    if (source.Length > WorkbookSafetyLimits.DesktopDefault.MaximumCompressedBytes)
                    {
                        throw new WorkbookSafetyException(
                            "Workbook compressed size changed after preflight and is now above the desktop safety ceiling.");
                    }

                    contents = GC.AllocateUninitializedArray<byte>(checked((int)source.Length));
                    var offset = 0;
                    while (offset < contents.Length)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        var read = await source
                            .ReadAsync(contents.AsMemory(offset), operationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            Array.Resize(ref contents, offset);
                            break;
                        }
                        offset += read;
                    }
                }
                else
                {
                    using var snapshot = new MemoryStream();
                    await source.CopyToAsync(snapshot, operationToken).ConfigureAwait(false);
                    contents = snapshot.ToArray();
                }

                lock (_gate)
                {
                    operationToken.ThrowIfCancellationRequested();
                    _contents = contents;
                    _importSource = new ImportSourceMetadata
                    {
                        ImportSourcePath = ImportSourcePath,
                        ContentFingerprint = Convert.ToHexString(SHA256.HashData(contents)),
                        ContentLength = contents.LongLength,
                        SnapshotCapturedAtUtc = DateTime.UtcNow
                    };
                }
            }
            finally
            {
                CompleteOperation();
            }
        }

        public async Task PreflightAsync(CancellationToken cancellationToken)
        {
            var operationToken = BeginOperation(cancellationToken);
            try
            {
                var assessment = await Task.Run(
                        () => WorkbookPackagePreflight.Inspect(
                            ImportSourcePath,
                            WorkbookSafetyLimits.DesktopDefault,
                            operationToken),
                        operationToken)
                    .ConfigureAwait(false);
                RecordPreflight(assessment);
                ReportProgress(
                    WorkbookImportPhase.OpeningWorkbook,
                    "Opening workbook",
                    preflight: assessment);
            }
            finally
            {
                CompleteOperation();
            }
        }

        public void RecordPreflight(WorkbookPreflightAssessment assessment)
        {
            ArgumentNullException.ThrowIfNull(assessment);
            lock (_gate)
            {
                _preflight = assessment;
            }
        }

        public WorkbookDiscovery? AttachPreflight(WorkbookDiscovery? workbook)
        {
            lock (_gate)
            {
                return workbook is null ? null : workbook with { Preflight = _preflight };
            }
        }

        public void RecordWorkbookDiscovery(WorkbookDiscovery? workbook)
        {
            lock (_gate)
            {
                _workbook = workbook;
            }

            if (workbook is not null)
            {
                ReportProgress(
                    WorkbookImportPhase.InspectingWorksheets,
                    "Inspecting Worksheets",
                    workbook.Worksheets.Count,
                    workbook.Worksheets.Count);
            }
        }

        public void ReportWorksheetProgress(string? worksheetName)
        {
            WorkbookDiscovery? workbook;
            lock (_gate)
            {
                workbook = _workbook;
            }

            var worksheets = workbook?.Worksheets ?? Array.Empty<ImportWorksheetDescriptor>();
            var index = string.IsNullOrWhiteSpace(worksheetName)
                ? -1
                : Array.FindIndex(
                    worksheets.ToArray(),
                    worksheet => string.Equals(worksheet.WorksheetName, worksheetName, StringComparison.Ordinal));
            ReportProgress(
                WorkbookImportPhase.ReadingWorksheet,
                index >= 0 && worksheets.Count > 0
                    ? $"Reading Worksheet {index + 1} of {worksheets.Count}"
                    : "Reading Worksheet",
                index >= 0 ? index + 1 : null,
                worksheets.Count > 0 ? worksheets.Count : null,
                worksheetName);
        }

        public void ReportImportServiceProgress(WorkbookImportProgress progress)
        {
            if (progress.Phase != WorkbookImportPhase.Validating)
            {
                return;
            }

            WorkbookImportProgress? current;
            lock (_gate)
            {
                current = _progress;
            }
            ReportProgress(
                WorkbookImportPhase.Validating,
                progress.Label,
                current?.Current,
                current?.Total,
                progress.WorksheetName ?? current?.WorksheetName);
        }

        public void ReportProgress(
            WorkbookImportPhase phase,
            string label,
            int? current = null,
            int? total = null,
            string? worksheetName = null,
            WorkbookPreflightAssessment? preflight = null)
        {
            lock (_gate)
            {
                var progress = new WorkbookImportProgress
                {
                    Phase = phase,
                    Label = label,
                    Current = current,
                    Total = total,
                    WorksheetName = worksheetName,
                    Preflight = preflight ?? _progress?.Preflight
                };
                _progress = progress;
                _progressHistory.Add(progress);
            }
        }

        public ImportSessionProgressSnapshot GetProgress()
        {
            lock (_gate)
            {
                return new ImportSessionProgressSnapshot(_progress, _progressHistory.ToArray());
            }
        }

        public byte[] GetContents()
        {
            lock (_gate)
            {
                return _contents ?? throw new ImportSessionException(
                    "import-snapshot-not-ready",
                    "The Import Snapshot has not finished capturing.");
            }
        }

        public void RecordWorksheetPreview(
            string? requestedWorksheetName,
            ImportOptions? options,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials,
            ImportResponse response)
        {
            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(requestedWorksheetName))
                {
                    _readyWorksheetPreviews.Remove(requestedWorksheetName);
                }

                var worksheet = response.Worksheet;
                if (worksheet is null ||
                    string.IsNullOrWhiteSpace(worksheet.HeadingRange) ||
                    !HasAllRequiredMappings(response.ColumnMappings, options?.ProjectKind ?? ProjectKind))
                {
                    return;
                }
                var projectKind = options?.ProjectKind ?? ProjectKind.Sheet;
                var materialResolutions = projectKind == ProjectKind.StockLength
                    ? Array.Empty<ReadyMaterialResolution>()
                    : BuildReadyMaterialResolutions(response, newMaterials);
                if (materialResolutions is null)
                {
                    return;
                }

                _readyWorksheetPreviews[worksheet.WorksheetName] = new ReadyWorksheetPreview(
                    worksheet.WorksheetName,
                    worksheet.OriginalPosition,
                    worksheet.HeadingRange,
                    BuildColumnMappingSignature(response.ColumnMappings.Select(mapping =>
                        (mapping.TargetField, mapping.SourceColumn))),
                    materialResolutions,
                    response.Errors,
                    response.Parts
                        .Select(part => (Id: part.RowId, part.SourceReferences))
                        .Concat(response.RequiredPieces.Select(piece =>
                            (Id: piece.RequiredPieceId, piece.SourceReferences)))
                        .ToDictionary(item => item.Id, item => item.SourceReferences, StringComparer.Ordinal),
                    response.Parts.ToDictionary(
                        part => part.RowId,
                        part => part.MaterialName,
                        StringComparer.Ordinal));
            }
        }

        public void EnsureWorksheetReady(
            ImportWorksheetSelection selection,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials)
        {
            lock (_gate)
            {
                var mappingSignature = BuildColumnMappingSignature(
                    (selection.Options?.ColumnMappings ?? Array.Empty<ImportColumnMapping>())
                        .Select(mapping => (mapping.TargetField, (string?)mapping.SourceColumn)));
                var isStockLength = selection.Options?.ProjectKind == ProjectKind.StockLength;
                if (!_readyWorksheetPreviews.TryGetValue(selection.WorksheetName, out var preview) ||
                    preview.OriginalPosition != selection.OriginalPosition ||
                    !string.Equals(preview.HeadingRange, selection.HeadingRange, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(preview.ColumnMappingSignature, mappingSignature, StringComparison.Ordinal) ||
                    (!isStockLength && !MaterialResolutionsMatch(preview, selection, newMaterials)))
                {
                    throw new ImportSessionException(
                        "import-worksheet-not-ready",
                        $"Worksheet '{selection.WorksheetName}' must be previewed with its confirmed Heading Range and Column Mappings before finalization.");
                }

                var unresolvedError = preview.Errors.FirstOrDefault(error =>
                    !(isStockLength && IsMaterialValidationError(error.Code)) &&
                    !string.Equals(error.Code, "material-not-found", StringComparison.Ordinal) &&
                    !IsResolved(error, selection, preview.SourceReferencesByRowId));
                if (unresolvedError is not null)
                {
                    throw new ImportSessionException(
                        "import-worksheet-has-blockers",
                        $"Worksheet '{selection.WorksheetName}' still has unresolved validation errors.");
                }
            }
        }

        private static bool IsMaterialValidationError(string code) =>
            string.Equals(code, "material-not-found", StringComparison.Ordinal) ||
            string.Equals(code, "material-name-required", StringComparison.Ordinal) ||
            string.Equals(code, "missing-material", StringComparison.Ordinal);

        private static bool IsResolved(
            ValidationError error,
            ImportWorksheetSelection selection,
            IReadOnlyDictionary<string, IReadOnlyList<SourceReference>> sourceReferencesByRowId)
        {
            if (string.IsNullOrWhiteSpace(error.RowId) ||
                !sourceReferencesByRowId.TryGetValue(error.RowId, out var sourceReferences))
            {
                return false;
            }

            var excluded = selection.ExcludedSourceRows.Any(row =>
                string.Equals(row.RowId, error.RowId, StringComparison.Ordinal) &&
                sourceReferences.Any(reference => reference.MatchesIdentity(row.SourceReference)));
            if (excluded)
            {
                return true;
            }

            return selection.PartOverrides.Any(partOverride =>
                string.Equals(partOverride.RowId, error.RowId, StringComparison.Ordinal) &&
                partOverride.CurrentValues.ValidationStatus != ValidationStatuses.Error &&
                partOverride.SourceReferences.Count > 0 &&
                partOverride.SourceReferences.All(overrideReference =>
                    sourceReferences.Any(reference => reference.MatchesIdentity(overrideReference))));
        }

        public CancellationToken BeginOperation(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_released)
                {
                    throw new OperationCanceledException("The Import Session has been released.");
                }

                if (_operationCancellation is not null)
                {
                    throw new ImportSessionException(
                        "import-session-busy",
                        "The Import Session already has an operation in progress.");
                }

                _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                return _operationCancellation.Token;
            }
        }

        public void CompleteOperation()
        {
            lock (_gate)
            {
                _operationCancellation?.Dispose();
                _operationCancellation = null;
                if (_released)
                {
                    ClearContents();
                }
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                _released = true;
                _operationCancellation?.Cancel();
                if (_operationCancellation is null)
                {
                    ClearContents();
                }
            }
        }

        private void ClearContents()
        {
            if (_contents is not null)
            {
                Array.Clear(_contents);
                _contents = null;
            }

            _importSource = null;
            _readyWorksheetPreviews.Clear();
            _workbook = null;
            _preflight = null;
        }

        private static bool HasAllRequiredMappings(
            IReadOnlyList<ImportFieldMappingStatus> mappings,
            ProjectKind projectKind)
        {
            var mappedFields = mappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                .Select(mapping => mapping.TargetField)
                .ToHashSet(StringComparer.Ordinal);
            return ImportFieldNames.RequiredFor(projectKind).All(mappedFields.Contains);
        }

        private static string BuildColumnMappingSignature(
            IEnumerable<(string TargetField, string? SourceColumn)> mappings) =>
            string.Join(
                '\u001f',
                mappings
                    .Where(mapping =>
                        !string.IsNullOrWhiteSpace(mapping.TargetField) &&
                        !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                    .Select(mapping => $"{mapping.TargetField.Trim()}\u001e{mapping.SourceColumn!.Trim()}")
                    .OrderBy(value => value, StringComparer.Ordinal));

        private static IReadOnlyList<ReadyMaterialResolution>? BuildReadyMaterialResolutions(
            ImportResponse response,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials)
        {
            var hasMaterialErrors = false;
            foreach (var error in response.Errors)
            {
                if (!string.Equals(error.Code, "material-not-found", StringComparison.Ordinal))
                {
                    if (error.RowId is null || error.Location is null)
                    {
                        return null;
                    }
                    continue;
                }

                hasMaterialErrors = true;
            }

            var plannedMaterials = newMaterials
                .Where(material =>
                    !string.IsNullOrWhiteSpace(material.SourceMaterialName) &&
                    material.Material is not null)
                .GroupBy(material => material.SourceMaterialName.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Material!, StringComparer.Ordinal);
            var ready = new List<ReadyMaterialResolution>();
            var hasResolvableMaterialError = false;
            foreach (var resolution in response.MaterialResolutions)
            {
                var sourceName = resolution.SourceMaterialName.Trim();
                if (!string.IsNullOrWhiteSpace(resolution.ResolvedMaterialId))
                {
                    ready.Add(new ReadyMaterialResolution(
                        sourceName,
                        "resolved",
                        resolution.ResolvedMaterialId));
                    continue;
                }

                if (plannedMaterials.TryGetValue(sourceName, out var plannedMaterial))
                {
                    hasResolvableMaterialError = true;
                    ready.Add(new ReadyMaterialResolution(
                        sourceName,
                        "new",
                        SerializeMaterialDefinition(plannedMaterial)));
                    continue;
                }

                hasResolvableMaterialError = true;
                ready.Add(new ReadyMaterialResolution(sourceName, "unresolved", null));
            }

            if (hasMaterialErrors && !hasResolvableMaterialError)
            {
                return null;
            }

            return ready;
        }

        private static bool MaterialResolutionsMatch(
            ReadyWorksheetPreview worksheetPreview,
            ImportWorksheetSelection selection,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials)
        {
            var explicitMappings = (selection.Options?.MaterialMappings ?? Array.Empty<ImportMaterialMapping>())
                .Where(mapping =>
                    !string.IsNullOrWhiteSpace(mapping.SourceMaterialName) &&
                    !string.IsNullOrWhiteSpace(mapping.TargetMaterialId))
                .GroupBy(mapping => mapping.SourceMaterialName.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().TargetMaterialId!, StringComparer.Ordinal);
            var plannedMaterials = newMaterials
                .Where(material =>
                    !string.IsNullOrWhiteSpace(material.SourceMaterialName) &&
                    material.Material is not null)
                .GroupBy(material => material.SourceMaterialName.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Material!, StringComparer.Ordinal);

            var ignoredMaterials = selection.IgnoredMaterialNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var preview in worksheetPreview.MaterialResolutions)
            {
                if (ignoredMaterials.Contains(preview.SourceMaterialName))
                {
                    if (!AllMaterialRowsAreExcluded(
                        preview.SourceMaterialName,
                        worksheetPreview,
                        selection))
                    {
                        return false;
                    }
                    continue;
                }

                if (explicitMappings.TryGetValue(preview.SourceMaterialName, out var targetMaterialId))
                {
                    if (preview.Kind != "resolved" ||
                        !string.Equals(preview.Value, targetMaterialId, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    continue;
                }

                if (plannedMaterials.TryGetValue(preview.SourceMaterialName, out var plannedMaterial))
                {
                    if (preview.Kind != "new" ||
                        !string.Equals(
                            preview.Value,
                            SerializeMaterialDefinition(plannedMaterial),
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                    continue;
                }

                if (preview.Kind != "resolved")
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllMaterialRowsAreExcluded(
            string sourceMaterialName,
            ReadyWorksheetPreview preview,
            ImportWorksheetSelection selection)
        {
            var matchingRowIds = preview.MaterialNamesByRowId
                .Where(item => string.Equals(item.Value, sourceMaterialName, StringComparison.Ordinal))
                .Select(item => item.Key)
                .ToArray();
            return matchingRowIds.Length > 0 && matchingRowIds.All(rowId =>
                preview.SourceReferencesByRowId.TryGetValue(rowId, out var references) &&
                selection.ExcludedSourceRows.Any(excluded =>
                    string.Equals(excluded.RowId, rowId, StringComparison.Ordinal) &&
                    references.Any(reference => reference.MatchesIdentity(excluded.SourceReference))));
        }

        private static string SerializeMaterialDefinition(Material material) =>
            System.Text.Json.JsonSerializer.Serialize(material, BridgeJson.SerializerOptions);

        private sealed record ReadyWorksheetPreview(
            string WorksheetName,
            int OriginalPosition,
            string HeadingRange,
            string ColumnMappingSignature,
            IReadOnlyList<ReadyMaterialResolution> MaterialResolutions,
            IReadOnlyList<ValidationError> Errors,
            IReadOnlyDictionary<string, IReadOnlyList<SourceReference>> SourceReferencesByRowId,
            IReadOnlyDictionary<string, string> MaterialNamesByRowId);

        private sealed record ReadyMaterialResolution(
            string SourceMaterialName,
            string Kind,
            string? Value);
    }

    internal sealed class ImportSessionFinalization : IDisposable
    {
        private readonly ImportSessionCoordinator _owner;
        private readonly string _sessionId;
        private readonly ImportSessionSnapshot _session;
        private bool _disposed;

        internal ImportSessionFinalization(
            ImportSessionCoordinator owner,
            string sessionId,
            ImportSessionSnapshot session,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _sessionId = sessionId;
            _session = session;
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public bool IsWorkbook =>
            _session.IsWorkbook;

        public void ReportProgress(
            WorkbookImportPhase phase,
            string label,
            int? current = null,
            int? total = null,
            string? worksheetName = null) =>
            _session.ReportProgress(phase, label, current, total, worksheetName);

        public void ReportWorksheetProgress(string worksheetName) =>
            _session.ReportWorksheetProgress(worksheetName);

        public ImportSessionProgressSnapshot GetProgress() => _session.GetProgress();

        public async Task<ImportSessionResult> ImportAsync(
            ImportOptions? options,
            string? worksheetName = null,
            string? headingRange = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var response = await _owner
                .ImportSnapshotFileAsync(_session, options, worksheetName, headingRange, CancellationToken)
                .ConfigureAwait(false);
            return new ImportSessionResult(_session.ImportSource, response);
        }

        public void EnsureWorksheetReady(
            ImportWorksheetSelection selection,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _session.EnsureWorksheetReady(selection, newMaterials);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session.CompleteOperation();
            _owner.Release(_sessionId);
        }
    }
}

internal sealed record ImportSessionResult(
    ImportSourceMetadata ImportSource,
    ImportResponse Response,
    WorkbookDiscovery? Workbook = null)
{
    public WorkbookImportProgress? Progress { get; init; }

    public IReadOnlyList<WorkbookImportProgress> ProgressHistory { get; init; } =
        Array.Empty<WorkbookImportProgress>();
}

internal sealed record ImportSessionProgressSnapshot(
    WorkbookImportProgress? Progress,
    IReadOnlyList<WorkbookImportProgress> History);

internal sealed class ImportSessionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
