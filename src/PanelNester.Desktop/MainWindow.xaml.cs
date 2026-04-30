using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
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
    internal const string DefaultWindowTitle = "Untitled Project — OptiFab";
    internal const double DefaultMinWindowWidth = 720;
    internal const double DefaultMinWindowHeight = 520;

    private readonly IFileDialogService _fileDialogService;
    private readonly DesktopAppSettingsStore _desktopAppSettingsStore;
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
    private readonly IExcelReportExporter _excelReportExporter;
    private readonly IStiffenerTakeoffService _stiffenerTakeoffService;
    private readonly IStiffenerPdfReportExporter _stiffenerPdfReportExporter;
    private string? _initialProjectPath;
    private WebViewBridge? _bridge;
    private bool _allowCloseWithoutPrompt;
    private bool _closePromptActive;
    private bool _hasUnsavedProjectChanges;
    private bool _initialized;
    private HwndSource? _windowSource;

    public MainWindow(string? initialProjectPath = null)
    {
        InitializeComponent();

        _fileDialogService = new NativeFileDialogService();
        _desktopAppSettingsStore = new DesktopAppSettingsStore(DesktopStoragePaths.AppSettingsFilePath);
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
        _excelReportExporter = new ClosedXmlExcelReportExporter();
        _stiffenerTakeoffService = new StiffenerTakeoffService();
        _stiffenerPdfReportExporter = new QuestPdfStiffenerReportExporter();
        UpdateWindowTitle(null);
        SourceInitialized += HandleSourceInitialized;
        Activated += (_, _) => ApplyNativeFrameTheme();
        Closing += HandleClosing;
        Closed += HandleClosed;
        PreviewKeyDown += HandlePreviewKeyDown;
        UpdateWindowStatePresentation();
        UpdateMaximizedContentMargin();
        _initialProjectPath = initialProjectPath;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowFitsCurrentMonitor();

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
                _excelReportExporter,
                _stiffenerTakeoffService,
                _stiffenerPdfReportExporter,
                () => contentLocation,
                _desktopAppSettingsStore,
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
                "Install the Microsoft Edge WebView2 Runtime to load the OptiFab web shell inside the desktop host.");
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
        EnsureWindowFitsCurrentMonitor();
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

    private async void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_bridge is null)
        {
            return;
        }

        var shortcut = ResolveProjectShortcut(e.Key, Keyboard.Modifiers);
        if (shortcut is null)
        {
            return;
        }

        e.Handled = true;

        try
        {
            switch (shortcut)
            {
                case "new":
                    await _bridge.CreateNewProjectAsync();
                    break;
                case "open":
                    await _bridge.InvokeOpenProjectPickerAsync();
                    break;
                case "save":
                    await _bridge.SaveProjectAsync();
                    break;
                case "saveAs":
                    await _bridge.SaveProjectAsAsync();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Project shortcut '{shortcut}' failed: {ex}");
        }
    }

    private async void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (_allowCloseWithoutPrompt || !_hasUnsavedProjectChanges)
        {
            return;
        }

        if (_closePromptActive)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closePromptActive = true;

        try
        {
            var closeDecision = ThemedPromptWindow.Show(
                this,
                new ThemedPromptOptions
                {
                    Title = "Unsaved Changes",
                    Headline = "Save before closing?",
                    Message = "Do you want to save changes to this project?",
                    Tone = ThemedPromptTone.Warning,
                    PrimaryButtonText = "Yes",
                    SecondaryButtonText = "No",
                    CancelButtonText = "Cancel",
                    DefaultResult = ThemedPromptResult.Primary
                });

            switch (closeDecision)
            {
                case ThemedPromptResult.Primary:
                {
                    var saveResult = await SaveProjectBeforeCloseAsync();
                    if (saveResult.Saved)
                    {
                        CloseWithoutPrompt();
                        return;
                    }

                    if (saveResult.Failed)
                    {
                        ShowSaveFailedDialog(
                            saveResult.Message ?? "The project could not be saved before closing.");
                    }

                    return;
                }
                case ThemedPromptResult.Secondary:
                    CloseWithoutPrompt();
                    return;
                default:
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowSaveFailedDialog(
                $"The project could not be saved before closing. {ex.Message}".Trim());
        }
        finally
        {
            _closePromptActive = false;
        }
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

    private void HandleSourceInitialized(object? sender, EventArgs e)
    {
        ApplyNativeFrameTheme();
        EnsureWindowFitsCurrentMonitor();
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        _windowSource?.RemoveHook(WndProc);
        _windowSource = null;
        SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
    }

    private void HandleDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(EnsureWindowFitsCurrentMonitor);
            return;
        }

        EnsureWindowFitsCurrentMonitor();
    }

    private void EnsureWindowFitsCurrentMonitor()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var workArea = GetCurrentMonitorWorkArea();
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        var minimumSize = new Size(
            Math.Min(DefaultMinWindowWidth, workArea.Width),
            Math.Min(DefaultMinWindowHeight, workArea.Height));

        MinWidth = minimumSize.Width;
        MinHeight = minimumSize.Height;

        var constrainedBounds = ConstrainWindowBounds(
            new Rect(Left, Top, Width, Height),
            workArea,
            minimumSize);

        Left = constrainedBounds.Left;
        Top = constrainedBounds.Top;
        Width = constrainedBounds.Width;
        Height = constrainedBounds.Height;
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        return TransformFromDevicePixels(
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Work.Right,
            monitorInfo.Work.Bottom);
    }

    private Rect TransformFromDevicePixels(int left, int top, int right, int bottom)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return new Rect(left, top, right - left, bottom - top);
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(left, top));
        var bottomRight = transform.Transform(new Point(right, bottom));
        return new Rect(topLeft, bottomRight);
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

    internal static bool HasUnsavedProjectChanges(string? documentTitle) =>
        ResolveWindowTitle(documentTitle).EndsWith(" * — OptiFab", StringComparison.Ordinal);

    internal static string? ResolveProjectShortcut(Key key, ModifierKeys modifiers)
    {
        if (!modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))
        {
            return null;
        }

        return key switch
        {
            Key.N when !modifiers.HasFlag(ModifierKeys.Shift) => "new",
            Key.O when !modifiers.HasFlag(ModifierKeys.Shift) => "open",
            Key.S when modifiers.HasFlag(ModifierKeys.Shift) => "saveAs",
            Key.S => "save",
            _ => null
        };
    }

    internal static Rect ConstrainWindowBounds(Rect desiredBounds, Rect workArea, Size minimumSize)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return desiredBounds;
        }

        var minimumWidth = Math.Max(0, Math.Min(minimumSize.Width, workArea.Width));
        var minimumHeight = Math.Max(0, Math.Min(minimumSize.Height, workArea.Height));

        var width = ClampToRange(desiredBounds.Width, minimumWidth, workArea.Width);
        var height = ClampToRange(desiredBounds.Height, minimumHeight, workArea.Height);
        var left = NormalizeCoordinate(desiredBounds.Left, workArea.Left);
        var top = NormalizeCoordinate(desiredBounds.Top, workArea.Top);
        var maxLeft = workArea.Right - width;
        var maxTop = workArea.Bottom - height;

        left = ClampToRange(left, workArea.Left, maxLeft);
        top = ClampToRange(top, workArea.Top, maxTop);

        return new Rect(left, top, width, height);
    }

    private static double ClampToRange(double value, double minimum, double maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minimum;
        }

        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private static double NormalizeCoordinate(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : value;
    }

    private void UpdateWindowTitle(string? documentTitle)
    {
        var title = ResolveWindowTitle(documentTitle);
        _hasUnsavedProjectChanges = HasUnsavedProjectChanges(documentTitle);
        Title = title;

        if (WindowTitleTextBlock is not null)
        {
            WindowTitleTextBlock.Text = title;
        }
    }

    private async Task<DesktopCloseSaveResult> SaveProjectBeforeCloseAsync()
    {
        if (_bridge is null)
        {
            return new DesktopCloseSaveResult(
                "failed",
                "Project save before close is unavailable because the web shell is not ready.");
        }

        return await _bridge.SaveProjectBeforeCloseAsync();
    }

    private void ShowSaveFailedDialog(string message)
    {
        _ = ThemedPromptWindow.Show(
            this,
            new ThemedPromptOptions
            {
                Title = "Save Failed",
                Headline = "Close was canceled",
                Message = message,
                Tone = ThemedPromptTone.Error,
                PrimaryButtonText = "OK",
                DefaultResult = ThemedPromptResult.Primary
            });
    }

    private void CloseWithoutPrompt()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _allowCloseWithoutPrompt = true;
                Close();
            }));
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WindowMessageGetMinMaxInfo)
        {
            UpdateMaximizedBounds(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private static void UpdateMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        if (hwnd == IntPtr.Zero || lParam == IntPtr.Zero)
        {
            return;
        }

        var monitorHandle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.Work;
        var monitorArea = monitorInfo.Monitor;

        minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;
        minMaxInfo.MaxTrackSize.X = minMaxInfo.MaxSize.X;
        minMaxInfo.MaxTrackSize.Y = minMaxInfo.MaxSize.Y;

        Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
    }

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int WindowMessageGetMinMaxInfo = 0x0024;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
