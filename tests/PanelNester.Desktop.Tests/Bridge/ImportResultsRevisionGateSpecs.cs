using System.IO;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ImportResultsRevisionGateSpecs
{
    [Fact]
    public void App_import_flow_still_uses_a_two_step_dialog_then_import_sequence_for_first_try_success()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");

        Assert.Contains("const openImportDialog = () =>", app);
        Assert.Contains("bridgeMessageTypes.openFileDialog", app);
        Assert.Contains("const invokeImportFile = (request: ImportFileRequest) =>", app);
        Assert.Contains("if (hasCapability(bridgeMessageTypes.importFile))", app);
        Assert.Contains("const dialogResponse = hasCapability(bridgeMessageTypes.openFileDialog)", app);
        Assert.Contains("const selectedFilePath = dialogResponse?.filePath ?? undefined;", app);
        Assert.Contains("const response = await invokeImportFile(", app);
        Assert.Contains("? ({ filePath: selectedFilePath } satisfies ImportFileRequest)", app);
        Assert.Contains("const filePath = pickImportFilePath(response, selectedFilePath);", app);
        Assert.Contains("type: 'import-finished'", app);
        Assert.Contains("dispatch({ type: 'route-changed', route: 'import' });", app);
    }

    [Fact]
    public void Desktop_import_path_still_preserves_dialog_serialization_and_webview_response_marshalling()
    {
        var desktopBridge = ReadRepositoryText("src", "PanelNester.Desktop", "Bridge", "DesktopBridgeRegistration.cs");
        var webViewBridge = ReadRepositoryText("src", "PanelNester.Desktop", "Bridge", "WebViewBridge.cs");
        var fileDialogService = ReadRepositoryText("src", "PanelNester.Desktop", "Bridge", "NativeFileDialogService.cs");

        Assert.Contains("if (string.IsNullOrWhiteSpace(filePath))", desktopBridge);
        Assert.Contains("new OpenFileDialogRequest(\"Import PanelNester parts\", ImportFileFilters)", desktopBridge);
        Assert.Contains("filePath = dialogResult.FilePath;", desktopBridge);
        Assert.Contains("FilePath = filePath,", desktopBridge);
        Assert.Contains("return ImportFileResponse.FromImportResponse(", desktopBridge);

        Assert.Contains("private readonly SemaphoreSlim _dialogGate = new(1, 1);", fileDialogService);
        Assert.Contains("=> InvokeSerializedAsync(", fileDialogService);

        Assert.Contains("if (!_webView.Dispatcher.CheckAccess())", webViewBridge);
        Assert.Contains("_webView.Dispatcher.Invoke(() => Post(message));", webViewBridge);
        Assert.Contains("_webView.CoreWebView2.PostWebMessageAsJson(json);", webViewBridge);
    }

    [Fact]
    public void App_import_review_gate_waits_for_manual_mapping_when_optional_fields_and_unused_columns_overlap()
    {
        var app = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx"));

        Assert.Contains("optionalImportFieldNames", app);
        Assert.Contains("function countReviewableOptionalImportFields(response: ImportResponse): number {", app);
        Assert.Contains("function shouldRequireImportReview(", app);
        Assert.Contains("const reviewableOptionalFields = countReviewableOptionalImportFields(response);", app);
        Assert.Contains("const unresolvedMaterials = countUnresolvedImportMaterials(response, session);", app);
        Assert.Contains("if (!shouldRequireImportReview(importResponse)) {", app);
        Assert.Contains("optional field mapping(s) can still be assigned from spare source columns", app);
    }

    [Fact]
    public void Results_page_markup_keeps_workspace_then_splitter_then_viewer()
    {
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");

        var workspaceIndex = resultsPage.IndexOf(
            "<section aria-label=\"Results workspace\" className=\"panel results-workspace\">",
            StringComparison.Ordinal);
        var splitterIndex = resultsPage.IndexOf("className=\"results-splitter\"", StringComparison.Ordinal);
        var viewerIndex = resultsPage.IndexOf("className=\"results-viewer-column\"", StringComparison.Ordinal);

        Assert.True(workspaceIndex >= 0, "Results workspace section should exist.");
        Assert.True(splitterIndex > workspaceIndex, "The resize splitter should remain between workspace and viewer.");
        Assert.True(viewerIndex > splitterIndex, "The viewer column should stay to the right of the splitter.");

        Assert.Contains("<div className=\"page-grid results-page\">", resultsPage);
        Assert.Contains("const viewerPanel = activeSheet ? (", resultsPage);
        Assert.Contains("<SheetViewer", resultsPage);
        Assert.Contains("sheet={activeSheet}", resultsPage);
        Assert.Contains("resetViewToken={viewerResetToken}", resultsPage);
        Assert.Contains("style={splitLayoutStyle}", resultsPage);
        Assert.Contains("'--results-workspace-width': `${workspaceWidth}px`,", resultsPage);
        Assert.Contains("data-results-layout=\"workspace-left-viewer-right\"", resultsPage);
        Assert.Contains("id=\"results-workspace-panel\"", resultsPage);
        Assert.Contains("aria-label=\"Resize results workspace\"", resultsPage);
        Assert.Contains("aria-orientation=\"vertical\"", resultsPage);
        Assert.Contains("aria-valuemin={minWorkspaceWidth}", resultsPage);
        Assert.Contains("aria-valuenow={Math.round(workspaceWidth)}", resultsPage);
        Assert.Contains("role=\"separator\"", resultsPage);
        Assert.Contains("const minWorkspaceWidth = 360;", resultsPage);
        Assert.Contains("const minViewerWidth = 420;", resultsPage);
        Assert.Contains("const resultsSplitterWidth = 14;", resultsPage);
        Assert.Contains("const maxWidth = Math.max(", resultsPage);
        Assert.Contains("bounds.width - minViewerWidth - resultsSplitterWidth,", resultsPage);
        Assert.Contains("window.addEventListener('pointermove', handlePointerMove);", resultsPage);
        Assert.Contains("window.removeEventListener('pointermove', handlePointerMove);", resultsPage);
        Assert.Contains("onPointerDown={(event) => {", resultsPage);
        Assert.Contains("event.preventDefault();", resultsPage);
        Assert.Contains("setIsResizingWorkspace(true);", resultsPage);
        Assert.Contains("aria-label=\"Current sheet viewer\"", resultsPage);
        Assert.Contains("data-active-sheet-id={activeSheet?.sheetId}", resultsPage);
    }

    [Fact]
    public void Results_split_styles_keep_the_resize_handle_visible_and_workspace_scroll_independent()
    {
        var styles = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "styles.css"));

        AssertContains(
            styles,
            """
            .app-route--results {
              display: grid;
              min-height: 100%;
            }
            """);
        AssertContains(
            styles,
            """
            .results-page {
              grid-template-rows: auto minmax(0, 1fr);
              min-height: 100%;
            }
            """);
        AssertContains(
            styles,
            """
            .results-split-layout {
              --results-workspace-width: 520px;
              --results-splitter-width: 14px;
            """);
        AssertContains(
            styles,
            """
              grid-template-columns:
                minmax(360px, var(--results-workspace-width))
                var(--results-splitter-width)
                minmax(420px, 1fr);
            """);
        Assert.Contains("  min-height: 0;", styles);
        Assert.Contains("  overflow: hidden;", styles);
        AssertContains(
            styles,
            """
            .results-workspace {
              grid-column: 1;
              grid-row: 1 / -1;
              display: grid;
              grid-template-rows: auto auto 1fr;
              gap: 0;
              align-content: start;
              overflow: hidden;
            }
            """);
        AssertContains(
            styles,
            """
            .results-workspace__tabs {
              display: flex;
              flex-wrap: wrap;
              gap: 1px;
              padding: 1px;
              background: var(--vsc-border);
              position: sticky;
              top: 0;
              z-index: 10;
            }
            """);
        AssertContains(
            styles,
            """
            .results-workspace__panel {
              min-height: 0;
              overflow-y: auto;
              overscroll-behavior: contain;
              padding: 16px;
              background: var(--vsc-bg-editor);
            }
            """);
        AssertContains(
            styles,
            """
            .results-splitter::before {
            """);
        Assert.Contains("  width: 4px;", styles);
        Assert.Contains("  height: 72px;", styles);
        AssertContains(
            styles,
            """
            .results-splitter {
              grid-column: 2;
              position: relative;
              display: grid;
              place-items: center;
            """);
        Assert.Contains("  cursor: col-resize;", styles);
        Assert.Contains("  grid-row: 1 / -1;", styles);
        Assert.Contains("  touch-action: none;", styles);
        AssertContains(
            styles,
            """
            .results-splitter::after {
            """);
        Assert.Contains("  width: 1px;", styles);
        Assert.Contains("  background: var(--vsc-border-subtle);", styles);
        AssertContains(
            styles,
            """
            .results-viewer-column {
              grid-column: 3;
              grid-row: 1 / -1;
              display: grid;
              grid-template-rows: auto 1fr;
              min-height: 0;
              overflow: hidden;
            }
            """);
        AssertContains(
            styles,
            """
            .results-viewer-column > .sheet-viewer-panel {
              display: grid;
              grid-template-rows: auto auto 1fr;
              min-height: 0;
              overflow: hidden;
              padding: 16px;
              background: var(--vsc-bg-editor);
            }
            """);
        AssertContains(
            styles,
            """
            .results-viewer-column .sheet-viewer {
              height: 100%;
              min-height: 0;
              max-height: none;
            }
            """);
        AssertContains(
            styles,
            """
            .sheet-viewer {
              position: relative;
              height: clamp(280px, 44vh, 520px);
              max-height: 520px;
              border: 1px solid var(--vsc-border);
              background: var(--vsc-bg-sidebar);
              overflow: hidden;
              overscroll-behavior: contain;
            }
            """);
    }

    [Fact]
    public void Sheet_viewer_still_uses_live_threejs_canvas_with_locked_plan_view_hover_details_and_owned_input()
    {
        var sheetViewer = ReadRepositoryText("src", "PanelNester.WebUI", "src", "components", "SheetViewer.tsx");

        Assert.Contains("import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';", sheetViewer);
        Assert.Contains("const renderer = new WebGLRenderer(", sheetViewer);
        Assert.Contains("renderer.domElement.className = 'sheet-viewer__canvas';", sheetViewer);
        Assert.Contains("viewport.replaceChildren(renderer.domElement);", sheetViewer);
        Assert.Contains("renderer.domElement.setAttribute('role', 'img');", sheetViewer);
        Assert.Contains("controls.enablePan = true;", sheetViewer);
        Assert.Contains("controls.enableRotate = false;", sheetViewer);
        Assert.Contains("controls.enableZoom = true;", sheetViewer);
        Assert.Contains("controls.maxPolarAngle = planViewPolarAngle;", sheetViewer);
        Assert.Contains("controls.minPolarAngle = planViewPolarAngle;", sheetViewer);
        Assert.Contains("data-view-mode=\"plan\"", sheetViewer);
        Assert.Contains("data-current-sheet-id={sheet.sheetId}", sheetViewer);
        Assert.Contains("Plan view locked", sheetViewer);
        Assert.Contains("Hover panels for details", sheetViewer);
        Assert.Contains("const [tooltip, setTooltip] = useState<TooltipState>();", sheetViewer);
        Assert.Contains("const updateHoverFromPointer = (event: PointerEvent) => {", sheetViewer);
        Assert.Contains("setTooltip({", sheetViewer);
        Assert.Contains("aria-label=\"Hovered panel details\"", sheetViewer);
        Assert.Contains("role=\"status\"", sheetViewer);
        Assert.Contains("<strong>{tooltip.placement.partId}</strong>", sheetViewer);
        Assert.Contains("const preventViewerScroll = (event: WheelEvent) => {", sheetViewer);
        Assert.Contains("event.preventDefault();", sheetViewer);
        Assert.Contains("event.stopPropagation();", sheetViewer);
        Assert.Contains("renderer.domElement.focus({ preventScroll: true });", sheetViewer);
        Assert.Contains("draggingRef.current = true;", sheetViewer);
        Assert.Contains("renderer.domElement.addEventListener('wheel', preventViewerScroll, { passive: false });", sheetViewer);
        Assert.Contains("updateCameraLayout(true);", sheetViewer);
    }

    [Fact]
    public void Results_page_only_adds_group_review_when_placements_expose_group_data()
    {
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");

        Assert.Contains("const hasGroupedParts = useMemo(", resultsPage);
        Assert.Contains("{ id: 'group-review', label: 'Review by group' },", resultsPage);
        Assert.Contains("const activeMaterialGroupSummaries = useMemo(", resultsPage);
        Assert.Contains("No grouped panels in the active material result", resultsPage);
        Assert.Contains("<th>Group</th>", resultsPage);
        Assert.Contains("{selectedPlacement.displayGroup}", resultsPage);
        Assert.Contains("<td>{placement.displayGroup}</td>", resultsPage);
        Assert.Contains("parts={state.importResponse.parts}", app);
    }

    [Fact]
    public void Sheet_viewer_keeps_mixed_group_sheets_dimmed_outside_the_active_group_and_shows_group_hover_details()
    {
        var sheetViewer = ReadRepositoryText("src", "PanelNester.WebUI", "src", "components", "SheetViewer.tsx");

        Assert.Contains("activeGroup?: string;", sheetViewer);
        Assert.Contains("activeGroupLabel?: string;", sheetViewer);
        Assert.Contains("const hasActiveGroup = activeGroupRef.current !== undefined;", sheetViewer);
        Assert.Contains("!hasActiveGroup || visual.groupKey === activeGroupRef.current;", sheetViewer);
        Assert.Contains("isActiveGroupPlacement ? visual.baseColor : '#7d7f83'", sheetViewer);
        Assert.Contains("Focus group: {activeGroupLabel}", sheetViewer);
        Assert.Contains("Other groups subdued", sheetViewer);
        Assert.Contains("Group: {getDisplayGroup(tooltip.placement.group, tooltip.placement.displayGroup)}", sheetViewer);
    }

    [Fact]
    public void Results_route_wraps_the_results_page_to_preserve_internal_split_scrolling()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");

        Assert.Contains("const contentClassName =", app);
        Assert.Contains("state.activeRoute === 'results' ? 'app-route app-route--results' : 'app-route';", app);
        Assert.Contains("<div className={contentClassName}>{content}</div>", app);
    }

    private static string ReadRepositoryText(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathSegments.ToArray())));
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private static void AssertContains(string actual, string expectedFragment) =>
        Assert.Contains(Normalize(expectedFragment), actual);
}
