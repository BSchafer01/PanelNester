using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

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
            return new ImportSessionResult(
                session.ImportSource,
                new ImportResponse { Success = true });
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
        CancellationToken cancellationToken)
    {
        var session = GetSession(sessionId);
        try
        {
            var response = await ImportSnapshotAsync(session, options, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var operationToken = session.BeginOperation(cancellationToken);

        try
        {
            return await ImportSnapshotFileAsync(session, options, operationToken).ConfigureAwait(false);
        }
        finally
        {
            session.CompleteOperation();
        }
    }

    private async Task<ImportResponse> ImportSnapshotFileAsync(
        ImportSessionSnapshot session,
        ImportOptions? options,
        CancellationToken operationToken)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        var snapshotPath = Path.Combine(SnapshotDirectory, $"{Guid.NewGuid():N}{session.Extension}");
        try
        {
            await File.WriteAllBytesAsync(snapshotPath, session.GetContents(), operationToken).ConfigureAwait(false);
            var response = await _importService.ImportAsync(
                    new ImportRequest
                    {
                        FilePath = snapshotPath,
                        Options = options ?? new ImportOptions()
                    },
                    operationToken)
                .ConfigureAwait(false);
            return RestoreImportSourceIdentity(session, response);
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
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
        }
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

        public async Task<ImportSessionResult> ImportAsync(ImportOptions? options)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var response = await _owner
                .ImportSnapshotFileAsync(_session, options, CancellationToken)
                .ConfigureAwait(false);
            return new ImportSessionResult(_session.ImportSource, response);
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

internal sealed record ImportSessionResult(ImportSourceMetadata ImportSource, ImportResponse Response);

internal sealed class ImportSessionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
