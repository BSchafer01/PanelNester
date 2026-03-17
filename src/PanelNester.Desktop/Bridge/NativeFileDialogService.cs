using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PanelNester.Desktop.Bridge;

public sealed class NativeFileDialogService : IFileDialogService
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly Dispatcher? _dispatcher;
    private readonly Func<Window?> _ownerWindowAccessor;
    private readonly Func<OpenFileDialog, Window?, bool?> _openDialog;
    private readonly Func<SaveFileDialog, Window?, bool?> _saveDialog;

    public NativeFileDialogService()
        : this(
            Application.Current?.Dispatcher,
            ResolveOwnerWindow,
            static (dialog, owner) => owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner),
            static (dialog, owner) => owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner))
    {
    }

    internal NativeFileDialogService(
        Dispatcher? dispatcher,
        Func<Window?> ownerWindowAccessor,
        Func<OpenFileDialog, Window?, bool?> openDialog,
        Func<SaveFileDialog, Window?, bool?> saveDialog)
    {
        _dispatcher = dispatcher;
        _ownerWindowAccessor = ownerWindowAccessor ?? throw new ArgumentNullException(nameof(ownerWindowAccessor));
        _openDialog = openDialog ?? throw new ArgumentNullException(nameof(openDialog));
        _saveDialog = saveDialog ?? throw new ArgumentNullException(nameof(saveDialog));
    }

    public Task<OpenFileDialogResponse> OpenAsync(
        OpenFileDialogRequest request,
        CancellationToken cancellationToken = default)
        => InvokeSerializedAsync(
            () =>
                InvokeOnDispatcherAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var dialog = new OpenFileDialog
                    {
                        Title = string.IsNullOrWhiteSpace(request.Title) ? "Select an import file" : request.Title,
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = false,
                        Filter = BuildFilter(
                            request.Filters,
                            "Supported import files (*.csv, *.xlsx)|*.csv;*.xlsx|CSV files (*.csv)|*.csv|Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*")
                    };

                    var selected = _openDialog(dialog, _ownerWindowAccessor());
                    return selected == true
                        ? new OpenFileDialogResponse(true, dialog.FileName, null, "File selected.")
                        : OpenFileDialogResponse.Cancelled();
                }, cancellationToken),
            cancellationToken);

    public Task<SaveFileDialogResponse> SaveAsync(
        SaveFileDialogRequest request,
        CancellationToken cancellationToken = default)
        => InvokeSerializedAsync(
            () =>
                InvokeOnDispatcherAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var defaultExtension = ResolveDefaultExtension(request);

                    var dialog = new SaveFileDialog
                    {
                        Title = string.IsNullOrWhiteSpace(request.Title) ? "Save PanelNester project" : request.Title,
                        AddExtension = true,
                        CheckPathExists = true,
                        OverwritePrompt = true,
                        DefaultExt = defaultExtension,
                        FileName = ResolveFileName(request, defaultExtension),
                        Filter = BuildFilter(request.Filters, BuildSaveFallbackFilter(defaultExtension))
                    };

                    var selected = _saveDialog(dialog, _ownerWindowAccessor());
                    return selected == true
                        ? new SaveFileDialogResponse(true, dialog.FileName, null, "File path selected.")
                        : SaveFileDialogResponse.Cancelled();
                }, cancellationToken),
            cancellationToken);

    private async Task<T> InvokeSerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _dialogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private Task<T> InvokeOnDispatcherAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }

    private static Window? ResolveOwnerWindow()
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? application.MainWindow;
    }

    private static string BuildFilter(IReadOnlyList<FileDialogFilter>? filters, string fallback)
    {
        if (filters is null || filters.Count == 0)
        {
            return fallback;
        }

        var segments = new List<string>();
        foreach (var filter in filters)
        {
            if (filter.Extensions.Count == 0)
            {
                continue;
            }

            var patterns = filter.Extensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(NormalizePattern)
                .ToArray();

            if (patterns.Length == 0)
            {
                continue;
            }

            segments.Add($"{filter.Name} ({string.Join(", ", patterns)})");
            segments.Add(string.Join(";", patterns));
        }

        return segments.Count == 0
            ? fallback
            : string.Join("|", segments);
    }

    private static string NormalizePattern(string extension)
    {
        var trimmed = extension.Trim();
        if (string.Equals(trimmed, "*", StringComparison.Ordinal) ||
            string.Equals(trimmed, "*.*", StringComparison.Ordinal))
        {
            return "*.*";
        }

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            return $"*{trimmed}";
        }

        return $"*.{trimmed.TrimStart('*')}";
    }

    private static string ResolveDefaultExtension(SaveFileDialogRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DefaultExtension))
        {
            return NormalizeExtension(request.DefaultExtension);
        }

        var fileExtension = Path.GetExtension(request.FileName);
        if (!string.IsNullOrWhiteSpace(fileExtension))
        {
            return NormalizeExtension(fileExtension);
        }

        var firstFilterExtension = request.Filters?
            .SelectMany(filter => filter.Extensions)
            .FirstOrDefault(extension =>
                !string.IsNullOrWhiteSpace(extension) &&
                !string.Equals(extension, "*", StringComparison.Ordinal) &&
                !string.Equals(extension, "*.*", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(firstFilterExtension)
            ? ".pnest"
            : NormalizeExtension(firstFilterExtension);
    }

    private static string ResolveFileName(SaveFileDialogRequest request, string defaultExtension)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return $"panelnester-project{defaultExtension}";
        }

        return string.IsNullOrWhiteSpace(Path.GetExtension(request.FileName))
            ? $"{request.FileName}{defaultExtension}"
            : request.FileName;
    }

    private static string BuildSaveFallbackFilter(string defaultExtension) =>
        string.Equals(defaultExtension, ".pdf", StringComparison.OrdinalIgnoreCase)
            ? "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
            : "PanelNester project files (*.pnest)|*.pnest|All files (*.*)|*.*";

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? trimmed
            : $".{trimmed.TrimStart('*')}";
    }
}
