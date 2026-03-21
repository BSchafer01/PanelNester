using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Contracts;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;
using PanelNester.Services.Reporting;

namespace PanelNester.Desktop;

public partial class MainWindow : Window
{
    internal const string DefaultWindowTitle = "Untitled Project — PanelNester";

    private readonly IFileDialogService _fileDialogService;
    private readonly IMaterialRepository _materialRepository;
    private readonly IMaterialLibraryLocationService _materialLibraryLocationService;
    private readonly IMaterialService _materialService;
    private readonly IProjectService _projectService;
    private readonly IImportService _importService;
    private readonly IPartEditorService _partEditorService;
    private readonly INestingService _nestingService;
    private readonly IBatchNestingService _batchNestingService;
    private readonly IReportDataService _reportDataService;
    private readonly IPdfReportExporter _pdfReportExporter;
    private string? _initialProjectPath;
    private WebViewBridge? _bridge;
    private bool _initialized;

    public MainWindow(string? initialProjectPath = null)
    {
        InitializeComponent();

        _fileDialogService = new NativeFileDialogService();
        var materialRepository = new JsonMaterialRepository(
            new JsonMaterialRepositoryOptions
            {
                DefaultFilePath = DesktopStoragePaths.MaterialsFilePath,
                LocationStoreFilePath = DesktopStoragePaths.MaterialLibrarySettingsFilePath
            });
        _materialRepository = materialRepository;
        _materialLibraryLocationService = materialRepository;
        _materialService = new MaterialService(_materialRepository);
        _projectService = new ProjectService(_materialService);
        var validator = new PartRowValidator();
        _importService = new FileImportDispatcher(
            new CsvImportService(_materialRepository, validator),
            new XlsxImportService(_materialRepository, validator));
        _partEditorService = new PartEditorService(_materialRepository, validator);
        _nestingService = new ShelfNestingService();
        _batchNestingService = new BatchNestingService(_nestingService);
        _reportDataService = new ReportDataService();
        _pdfReportExporter = new QuestPdfReportExporter();
        UpdateWindowTitle(null);
        SourceInitialized += (_, _) => ApplyNativeFrameTheme();
        Activated += (_, _) => ApplyNativeFrameTheme();
        UpdateWindowStatePresentation();
        UpdateMaximizedContentMargin();
        _initialProjectPath = initialProjectPath;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await InitializeHostAsync();
    }

    private async Task InitializeHostAsync()
    {
        try
        {
            var contentLocation = WebUiContentResolver.Resolve(AppContext.BaseDirectory);
            var dispatcher = DesktopBridgeRegistration.CreateDefault(
                _fileDialogService,
                _materialService,
                _projectService,
                _importService,
                _partEditorService,
                _nestingService,
                _batchNestingService,
                _reportDataService,
                _pdfReportExporter,
                () => contentLocation,
                materialLibraryLocationService: _materialLibraryLocationService);

            if (_bridge is not null)
            {
                _bridge.DocumentTitleChanged -= HandleBridgeDocumentTitleChanged;
            }

            _bridge = new WebViewBridge(
                ShellWebView,
                dispatcher,
                contentLocation,
                DesktopStoragePaths.WebViewUserDataDirectory);
            _bridge.DocumentTitleChanged += HandleBridgeDocumentTitleChanged;
            await _bridge.InitializeAsync();
            HostErrorOverlay.Visibility = Visibility.Collapsed;
            await TryOpenInitialProjectAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowHostError(
                "WebView2 runtime not found.",
                "Install the Microsoft Edge WebView2 Runtime to load the PanelNester web shell inside the desktop host.");
        }
        catch (Exception ex)
        {
            ShowHostError("Desktop host initialization failed.", ex.Message);
        }
    }

    private async Task TryOpenInitialProjectAsync()
    {
        var initialProjectPath = _initialProjectPath;
        _initialProjectPath = null;

        if (string.IsNullOrWhiteSpace(initialProjectPath) || _bridge is null)
        {
            return;
        }

        try
        {
            await _bridge.OpenProjectAsync(initialProjectPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initial project open failed for '{initialProjectPath}': {ex}");
        }
    }

    private void HandleBridgeDocumentTitleChanged(object? sender, string documentTitle)
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateWindowTitle(documentTitle);
            return;
        }

        _ = Dispatcher.InvokeAsync(() => UpdateWindowTitle(documentTitle));
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        UpdateWindowStatePresentation();
        UpdateMaximizedContentMargin();
        ApplyNativeFrameTheme();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }

        UpdateWindowStatePresentation();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void ShowHostError(string headline, string details)
    {
        UpdateWindowTitle(null);
        HostErrorTextBlock.Text = $"{headline} {details}".Trim();
        HostErrorOverlay.Visibility = Visibility.Visible;
    }

    private void ApplyNativeFrameTheme()
    {
        NativeTitleBarStyler.TryApply(this);
    }

    private void UpdateWindowStatePresentation()
    {
        if (WindowStateToggleGlyph is null || MaximizeRestoreButton is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        WindowStateToggleGlyph.Text = isMaximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }

    private void UpdateMaximizedContentMargin()
    {
        if (ShellContentHost is null)
        {
            return;
        }

        ShellContentHost.Margin = WindowState == WindowState.Maximized
            ? GetMaximizedContentMargin()
            : new Thickness(0);
    }

    private static Thickness GetMaximizedContentMargin()
    {
        var resizeBorder = SystemParameters.WindowResizeBorderThickness;

        return new Thickness(
            Math.Ceiling(resizeBorder.Left),
            0,
            Math.Ceiling(resizeBorder.Right),
            Math.Ceiling(resizeBorder.Bottom));
    }

    internal static string ResolveWindowTitle(string? documentTitle)
    {
        var normalizedTitle = documentTitle?.Trim();
        return string.IsNullOrWhiteSpace(normalizedTitle)
            ? DefaultWindowTitle
            : normalizedTitle;
    }

    private void UpdateWindowTitle(string? documentTitle)
    {
        var title = ResolveWindowTitle(documentTitle);
        Title = title;

        if (WindowTitleTextBlock is not null)
        {
            WindowTitleTextBlock.Text = title;
        }
    }
}
