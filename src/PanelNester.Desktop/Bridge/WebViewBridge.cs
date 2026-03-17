using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PanelNester.Desktop.Bridge;

public sealed class WebViewBridge
{
    private const string VirtualHostName = "app.panelnester.local";
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

    private void OnStatusChanged(string status) => StatusChanged?.Invoke(this, status);

    private void OnDocumentTitleChanged(string title) => DocumentTitleChanged?.Invoke(this, title);
}
