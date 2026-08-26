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
        CancellationToken cancellationToken)
    {
        ValidateSessionId(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(importSourcePath);

        if (_cancelledSessionIds.TryRemove(sessionId, out _))
        {
            throw new OperationCanceledException("The Import Session was cancelled before snapshot capture began.");
        }

        var normalizedPath = Path.GetFullPath(importSourcePath.Trim());
        var session = new ImportSessionSnapshot(normalizedPath, Path.GetExtension(normalizedPath));
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
            await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var workbook = await DiscoverWorkbookAsync(session, cancellationToken).ConfigureAwait(false);
            return new ImportSessionResult(
                session.ImportSource,
                new ImportResponse { Success = true },
                workbook);
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
            var response = await ImportSnapshotAsync(session, options, worksheetName, headingRange, cancellationToken).ConfigureAwait(false);
            session.RecordWorksheetPreview(worksheetName, options, newMaterials, response);
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
            snapshotPath => _importService.ImportAsync(
                new ImportRequest
                {
                    FilePath = snapshotPath,
                    Options = options ?? new ImportOptions(),
                    WorksheetName = worksheetName,
                    HeadingRange = headingRange
                },
                operationToken)).ConfigureAwait(false);

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

    private static async Task<WorkbookDiscovery?> DiscoverWorkbookAsync(
        ImportSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(session.Extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(session.Extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await WithSnapshotFileAsync(
                session,
                cancellationToken,
                snapshotPath => new WorkbookDiscoveryService().DiscoverAsync(snapshotPath, cancellationToken))
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

    internal sealed class ImportSessionSnapshot(string importSourcePath, string extension)
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
                using var snapshot = new MemoryStream();
                await source.CopyToAsync(snapshot, operationToken).ConfigureAwait(false);
                var contents = snapshot.ToArray();

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
                    !HasAllRequiredMappings(response.ColumnMappings))
                {
                    return;
                }
                var materialResolutions = BuildReadyMaterialResolutions(
                    response,
                    newMaterials);
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
                    materialResolutions);
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
                if (!_readyWorksheetPreviews.TryGetValue(selection.WorksheetName, out var preview) ||
                    preview.OriginalPosition != selection.OriginalPosition ||
                    !string.Equals(preview.HeadingRange, selection.HeadingRange, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(preview.ColumnMappingSignature, mappingSignature, StringComparison.Ordinal) ||
                    !MaterialResolutionsMatch(preview.MaterialResolutions, selection.Options, newMaterials))
                {
                    throw new ImportSessionException(
                        "import-worksheet-not-ready",
                        $"Worksheet '{selection.WorksheetName}' must be previewed with its confirmed Heading Range and Column Mappings before finalization.");
                }
            }
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
        }

        private static bool HasAllRequiredMappings(
            IReadOnlyList<ImportFieldMappingStatus> mappings)
        {
            var mappedFields = mappings
                .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceColumn))
                .Select(mapping => mapping.TargetField)
                .ToHashSet(StringComparer.Ordinal);
            return ImportFieldNames.Required.All(mappedFields.Contains);
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
                    return null;
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
            var hasUnresolvedResolution = false;
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
                    hasUnresolvedResolution = true;
                    ready.Add(new ReadyMaterialResolution(
                        sourceName,
                        "new",
                        SerializeMaterialDefinition(plannedMaterial)));
                    continue;
                }

                return null;
            }

            if (hasMaterialErrors && !hasUnresolvedResolution)
            {
                return null;
            }

            return ready;
        }

        private static bool MaterialResolutionsMatch(
            IReadOnlyList<ReadyMaterialResolution> previewResolutions,
            ImportOptions? options,
            IReadOnlyList<ImportNewMaterialRequest> newMaterials)
        {
            var explicitMappings = (options?.MaterialMappings ?? Array.Empty<ImportMaterialMapping>())
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

            foreach (var preview in previewResolutions)
            {
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

        private static string SerializeMaterialDefinition(Material material) =>
            System.Text.Json.JsonSerializer.Serialize(material, BridgeJson.SerializerOptions);

        private sealed record ReadyWorksheetPreview(
            string WorksheetName,
            int OriginalPosition,
            string HeadingRange,
            string ColumnMappingSignature,
            IReadOnlyList<ReadyMaterialResolution> MaterialResolutions);

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
            string.Equals(_session.Extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_session.Extension, ".xlsm", StringComparison.OrdinalIgnoreCase);

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
    WorkbookDiscovery? Workbook = null);

internal sealed class ImportSessionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
