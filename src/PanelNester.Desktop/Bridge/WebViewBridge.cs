using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PanelNester.Desktop.Bridge;

public sealed class WebViewBridge
{
    private const string VirtualHostName = "app.optifab.local";
    private const string HostReceiverShim = """
        if (!window.__panelNesterHostReceiverShim) {
            window.__panelNesterHostReceiverShim = true;
            window.chrome.webview.addEventListener('message', event => {
                window.hostBridge?.receive?.(event.data);
            });
        }
        """;

    private readonly WebView2 _webView;
    private readonly BridgeMessageDispatcher _dispatcher;
    private readonly WebUiContentLocation _contentLocation;
    private readonly string _userDataFolder;
    private readonly BridgeHostReadinessGate _hostReadinessGate = new();

    internal Func<string, CancellationToken, Task<string>>? ScriptExecutorOverride { get; set; }

    internal BridgeHostReadinessGate HostReadinessGate => _hostReadinessGate;

    public WebViewBridge(
        WebView2 webView,
        BridgeMessageDispatcher dispatcher,
        WebUiContentLocation contentLocation,
        string userDataFolder)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _contentLocation = contentLocation ?? throw new ArgumentNullException(nameof(contentLocation));
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataFolder);
        _userDataFolder = userDataFolder;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? DocumentTitleChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);
        cancellationToken.ThrowIfCancellationRequested();

        var coreWebView = _webView.CoreWebView2;
        coreWebView.Settings.IsWebMessageEnabled = true;
        coreWebView.WebMessageReceived -= HandleWebMessageReceived;
        coreWebView.WebMessageReceived += HandleWebMessageReceived;
        coreWebView.DocumentTitleChanged -= HandleDocumentTitleChanged;
        coreWebView.DocumentTitleChanged += HandleDocumentTitleChanged;
        coreWebView.NavigationCompleted -= HandleNavigationCompleted;
        coreWebView.NavigationCompleted += HandleNavigationCompleted;
        await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(HostReceiverShim);
        coreWebView.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            _contentLocation.ContentRoot,
            CoreWebView2HostResourceAccessKind.Allow);

        OnStatusChanged($"Loading {_contentLocation.DisplayName}.");
        _webView.Source = new Uri($"https://{VirtualHostName}/index.html");
    }

    public async Task<bool> OpenProjectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await WaitForHostReadyAsync(cancellationToken).ConfigureAwait(true);

        var requestJson = JsonSerializer.Serialize(
            new OpenProjectRequest(filePath.Trim()),
            BridgeJson.SerializerOptions);
        var script = $$"""
            (async () => {
                const desktopHost = window.panelNesterDesktopHost;
                if (!desktopHost?.openProject) {
                    return false;
                }

                await desktopHost.openProject({{requestJson}});
                return true;
            })();
            """;
        var result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> CreateNewProjectAsync(CancellationToken cancellationToken = default) =>
        InvokeDesktopHostActionAsync("createNewProject", cancellationToken);

    public Task<bool> InvokeOpenProjectPickerAsync(CancellationToken cancellationToken = default) =>
        InvokeDesktopHostActionAsync("openProject", cancellationToken, "{}");

    public Task<bool> SaveProjectAsync(CancellationToken cancellationToken = default) =>
        InvokeDesktopHostActionAsync("saveProject", cancellationToken);

    public Task<bool> SaveProjectAsAsync(CancellationToken cancellationToken = default) =>
        InvokeDesktopHostActionAsync("saveProjectAs", cancellationToken);

    public async Task<DesktopCloseSaveResult> SaveProjectBeforeCloseAsync(CancellationToken cancellationToken = default)
    {
        await WaitForHostReadyAsync(cancellationToken).ConfigureAwait(true);

        var script = """
            (async () => {
                const desktopHost = window.panelNesterDesktopHost;
                if (!desktopHost?.saveProjectBeforeClose) {
                    return {
                        status: 'failed',
                        message: 'Project save before close is unavailable.'
                    };
                }

                const result = await desktopHost.saveProjectBeforeClose();
                return result ?? {
                    status: 'failed',
                    message: 'Project save before close did not return a result.'
                };
            })();
            """;
        var result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        var saveResult = JsonSerializer.Deserialize<DesktopCloseSaveResult>(result, BridgeJson.SerializerOptions);

        return saveResult ?? new DesktopCloseSaveResult(
            "failed",
            "Project save before close returned an empty result.");
    }

    public Task WaitForHostReadyAsync(CancellationToken cancellationToken = default) =>
        _hostReadinessGate.WaitAsync(cancellationToken);

    private void HandleNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            OnStatusChanged($"Navigation failed: {e.WebErrorStatus}.");
            return;
        }

        OnDocumentTitleChanged(_webView.CoreWebView2?.DocumentTitle ?? string.Empty);
        OnStatusChanged($"{_contentLocation.DisplayName} ready.");
    }

    private void HandleDocumentTitleChanged(object? sender, object e)
    {
        OnDocumentTitleChanged(_webView.CoreWebView2?.DocumentTitle ?? string.Empty);
    }

    private async void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeMessageEnvelope? request;

        try
        {
            request = JsonSerializer.Deserialize<BridgeMessageEnvelope>(e.WebMessageAsJson, BridgeJson.SerializerOptions);
            if (request is null)
            {
                OnStatusChanged("Ignored empty bridge message.");
                return;
            }

            _hostReadinessGate.TrySignalReady(request);
        }
        catch (JsonException)
        {
            OnStatusChanged("Ignored malformed bridge message.");
            return;
        }

        var response = await _dispatcher.DispatchAsync(request).ConfigureAwait(true);

        if (response is not null)
        {
            Post(response);
        }

        OnStatusChanged($"Handled {request.Type}.");
    }

    private void Post(BridgeMessageEnvelope message)
    {
        if (!_webView.Dispatcher.CheckAccess())
        {
            _webView.Dispatcher.Invoke(() => Post(message));
            return;
        }

        var json = JsonSerializer.Serialize(message, BridgeJson.SerializerOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        if (ScriptExecutorOverride is not null)
        {
            return await ScriptExecutorOverride(script, cancellationToken).ConfigureAwait(true);
        }

        if (_webView.Dispatcher.CheckAccess())
        {
            return await _webView.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(cancellationToken).ConfigureAwait(true);
        }

        return await _webView.Dispatcher
            .InvokeAsync(() => _webView.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(cancellationToken))
            .Task
            .Unwrap()
            .ConfigureAwait(true);
    }

    private void OnStatusChanged(string status) => StatusChanged?.Invoke(this, status);

    private void OnDocumentTitleChanged(string title) => DocumentTitleChanged?.Invoke(this, title);

    private async Task<bool> InvokeDesktopHostActionAsync(
        string actionName,
        CancellationToken cancellationToken,
        string? requestJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);

        await WaitForHostReadyAsync(cancellationToken).ConfigureAwait(true);

        var invocation = requestJson is null ? "await action();" : $"await action({requestJson});";
        var script = $$"""
            (async () => {
                const desktopHost = window.panelNesterDesktopHost;
                const action = desktopHost?.{{actionName}};
                if (typeof action !== 'function') {
                    return false;
                }

                {{invocation}}
                return true;
            })();
            """;
        var result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DesktopCloseSaveResult(string Status, string? Message)
{
    public bool Saved =>
        string.Equals(Status, "saved", StringComparison.Ordinal);

    public bool Cancelled =>
        string.Equals(Status, "cancelled", StringComparison.Ordinal);

    public bool Failed =>
        string.Equals(Status, "failed", StringComparison.Ordinal);
}
