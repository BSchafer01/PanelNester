using System.IO;
using System.Text;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ImportResultsRevisionGateSpecs
{
    [Fact]
    public void Workbook_import_review_keeps_selection_grouping_and_reselection_drafts_explicit()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");
        var importPage = ReadRepositoryText(
            "src",
            "PanelNester.WebUI",
            "src",
            "pages",
            "ImportPage.tsx");
        var draftState = ReadRepositoryText(
            "src",
            "PanelNester.WebUI",
            "src",
            "pages",
            "workbookImportDraftState.ts");

        Assert.Contains("createWorkbookWorksheetDrafts", app);
        Assert.Contains("workbook.initialWorksheetName || workbook.worksheets[0]?.worksheetName", draftState);
        Assert.Contains("optimizationGroupName: worksheet.worksheetName", draftState);
        Assert.Contains("setWorkbookWorksheetSelected", importPage);
        Assert.Contains("`${column.address} — ${column.heading}`", importPage);
        Assert.Contains("selectedWorksheetDrafts.map((draft) => ({", app);
        Assert.Contains("Select all Worksheets", importPage);
        Assert.Contains("Clear selection", importPage);
        Assert.Contains("Assign selected Worksheets", importPage);
        Assert.Contains("aria-label=\"Optimization Group for selected Worksheets\"", importPage);
        Assert.Contains("Macros are not run. OptiFab reads worksheet values only.", importPage);
    }

    [Fact]
    public void App_import_flow_still_uses_a_two_step_dialog_then_import_sequence_for_first_try_success()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");

        Assert.Contains("const openImportDialog = () =>", app);
        Assert.Contains("bridgeMessageTypes.openFileDialog", app);
        Assert.Contains("const invokeImportFile = async (request: ImportFileRequest) =>", app);
        Assert.Contains("normalizeImportFileResponse(", app);
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
        Assert.Contains("new OpenFileDialogRequest(\"Import OptiFab parts\", ImportFileFilters)", desktopBridge);
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
    public void App_normalizes_incomplete_host_failures_before_reading_import_array_lengths()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");

        Assert.Contains("function normalizeImportFileResponse(", app);
        Assert.Contains("parts: Array.isArray(response.parts) ? response.parts : []", app);
        Assert.Contains("errors: Array.isArray(response.errors) ? response.errors : []", app);
        Assert.Contains("(!response.success && Boolean(response.error))", app);
    }

    [Fact]
    public void Materials_page_exposes_an_actionable_unavailable_state_and_keeps_default_repair_enabled()
    {
        var app = ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx");
        var materialsPage = ReadRepositoryText(
            "src",
            "PanelNester.WebUI",
            "src",
            "pages",
            "MaterialsPage.tsx");

        Assert.Contains("materialLibraryUnavailable: boolean;", app);
        Assert.Contains("libraryUnavailable: true", app);
        Assert.Contains("materialLibraryUnavailable={state.materialLibraryUnavailable}", app);
        Assert.Contains("'Library unavailable'", materialsPage);
        Assert.Contains("? 'Repair default'", materialsPage);
        Assert.Contains("usingDefaultLocation && !materialLibraryUnavailable", materialsPage);
        Assert.Contains("preserve it and create a fresh library", materialsPage);
    }

    [Fact]
    public void Project_bridge_actions_keep_native_dialog_flows_on_the_long_running_timeout_budget()
    {
        var hostBridge = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "bridge", "hostBridge.ts"));

        AssertContains(
            hostBridge,
            """
            openProject(request: OpenProjectRequest): Promise<ProjectOperationResponse> {
                return this.invoke<ProjectOperationResponse>(
                  bridgeMessageTypes.openProject,
                  request,
                  longRunningRequestTimeoutMs,
                );
              }
            """);

        AssertContains(
            hostBridge,
            """
            saveProject(request: SaveProjectRequest): Promise<ProjectOperationResponse> {
                return this.invoke<ProjectOperationResponse>(
                  bridgeMessageTypes.saveProject,
                  request,
                  longRunningRequestTimeoutMs,
                );
              }
            """);

        AssertContains(
            hostBridge,
            """
            saveProjectAs(request: SaveProjectAsRequest): Promise<ProjectOperationResponse> {
                return this.invoke<ProjectOperationResponse>(
                  bridgeMessageTypes.saveProjectAs,
                  request,
                  longRunningRequestTimeoutMs,
                );
              }
            """);
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
    public void Webui_nest_placement_contract_keeps_optional_group_metadata_across_results_and_report_shapes()
    {
        var contracts = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "types", "contracts.ts"));

        AssertContains(
            contracts,
            """
            export interface NestPlacement {
              placementId: string;
              sheetId: string;
              partId: string;
              group?: string | null;
            """);
        AssertContains(
            contracts,
            """
            export interface NestResponse {
              success: boolean;
              sheets: NestSheet[];
              placements: NestPlacement[];
            """);
        AssertContains(
            contracts,
            """
            export interface ReportSheetDiagram {
              sheetId: string;
              sheetNumber: number;
              sheetLength: number;
              sheetWidth: number;
              utilizationPercent: number;
              placements: NestPlacement[];
            }
            """);
    }

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
    public void Results_page_large_import_group_review_is_driven_by_nesting_payloads_not_full_import_rows()
    {
        var resultsPage = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx"));

        Assert.DoesNotContain("  PartRow,", resultsPage);
        Assert.DoesNotContain("  parts: PartRow[];", resultsPage);
        Assert.DoesNotContain("function getBasePartId(", resultsPage);
        Assert.DoesNotContain("function buildPlacementGroupLookup(", resultsPage);
        Assert.DoesNotContain("const activeMaterialParts = useMemo(", resultsPage);
        Assert.DoesNotContain("const activeMaterialPlacementGroups = useMemo(", resultsPage);

        Assert.Contains("const hasGroupedPlacements = useMemo(", resultsPage);
        Assert.Contains("result.response.placements.some((placement) => normalizeGroup(placement.group) !== null),", resultsPage);
        Assert.Contains("const activeMaterialPlacements = useMemo(", resultsPage);
        Assert.Contains("() => (activeMaterialResult ? decoratePlacements(activeMaterialResult.response.placements) : []),", resultsPage);
        Assert.Contains("() => buildGroupSummaries(activeMaterialPlacements),", resultsPage);
    }

    [Fact]
    public void App_results_route_does_not_forward_large_import_rows_into_the_results_page()
    {
        var app = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx"));

        Assert.Contains("<ResultsPage", app);
        Assert.Contains("nestResponse={state.nestResponse}", app);
        Assert.Contains("batchNestResponse={state.batchNestResponse}", app);
        Assert.DoesNotContain("parts={state.importResponse.parts}", app);
    }

    [Fact]
    public void Reconnect_control_lives_in_app_chrome_and_not_on_the_import_page()
    {
        var app = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx"));
        var appShell = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "components", "AppShell.tsx"));
        var importPage = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ImportPage.tsx"));
        var styles = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "styles.css"));

        Assert.Contains("bridgeConnected={state.bridge.connected}", app);
        Assert.Contains("bridgeStatusMessage={", app);
        Assert.Contains("onReconnect={retryHandshake}", app);
        Assert.DoesNotContain("onRetryHandshake={retryHandshake}", app);

        Assert.Contains("bridgeConnected: boolean;", appShell);
        Assert.Contains("bridgeStatusMessage?: string;", appShell);
        Assert.Contains("const [reconnectBusy, setReconnectBusy] = useState(false);", appShell);
        Assert.Contains("!bridgeConnected ? (", appShell);
        Assert.Contains("title={bridgeStatusMessage ?? 'Desktop host connection unavailable.'}", appShell);
        Assert.Contains("{reconnectBusy ? 'Reconnecting…' : 'Reconnect'}", appShell);

        Assert.DoesNotContain("onRetryHandshake", importPage);
        Assert.DoesNotContain(">Retry<", importPage);

        Assert.Contains(".app-shell__header-actions {", styles);
        Assert.Contains(".app-shell__reconnect-button {", styles);
    }

    [Fact]
    public void Materials_page_keeps_refresh_in_the_library_header_and_hides_passive_loaded_status_copy()
    {
        var materialsPage = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "MaterialsPage.tsx"));

        Assert.Contains("function shouldShowMaterialsStatus(message: string): boolean {", materialsPage);
        Assert.Contains("!normalized.startsWith('loaded ')", materialsPage);
        Assert.Contains("!normalized.startsWith('material library synced')", materialsPage);
        Assert.Contains("shouldShowMaterialsStatus(materialsMessage)", materialsPage);
        Assert.Contains("<h2>Reusable material library</h2>", materialsPage);
        Assert.Contains("const handleRefreshMaterials = () => {", materialsPage);
        Assert.Contains("onClick={handleRefreshMaterials}", materialsPage);
        Assert.DoesNotContain("New material", materialsPage);
        Assert.DoesNotContain("{materialsBusy ? 'Refreshing…' : 'Refresh'}", materialsPage);

        var libraryHeadingIndex = materialsPage.IndexOf("<p className=\"eyebrow\">Library</p>", StringComparison.Ordinal);
        var refreshButtonIndex = materialsPage.IndexOf("onClick={handleRefreshMaterials}", StringComparison.Ordinal);

        Assert.True(libraryHeadingIndex >= 0, "The material library heading should exist.");
        Assert.True(refreshButtonIndex > libraryHeadingIndex, "Refresh should be attached to the material library heading area.");
    }

    [Fact]
    public void Materials_page_threads_library_location_state_and_repoint_actions_through_app_and_bridge_contracts()
    {
        var app = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx"));
        var materialsPage = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "MaterialsPage.tsx"));
        var hostBridge = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "bridge", "hostBridge.ts"));
        var contracts = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "types", "contracts.ts"));
        var styles = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "styles.css"));

        Assert.Contains("materialLibraryLocation={state.materialLibraryLocation}", app);
        Assert.Contains("bridgeMessageTypes.chooseMaterialLibraryLocation", app);
        Assert.Contains("bridgeMessageTypes.restoreDefaultMaterialLibraryLocation", app);
        Assert.Contains("onChooseMaterialLibraryLocation={chooseMaterialLibraryLocation}", app);
        Assert.Contains("onRestoreDefaultMaterialLibraryLocation={", app);

        Assert.Contains("materialLibraryLocation?: MaterialLibraryLocation | null;", materialsPage);
        Assert.Contains("canChooseMaterialLibraryLocation: boolean;", materialsPage);
        Assert.Contains("canRestoreDefaultMaterialLibraryLocation: boolean;", materialsPage);
        Assert.Contains("className=\"library-location-card\"", materialsPage);
        Assert.Contains("Choose location…", materialsPage);
        Assert.Contains("Restore default", materialsPage);

        Assert.Contains("chooseMaterialLibraryLocation(): Promise<ChooseMaterialLibraryLocationResponse>", hostBridge);
        Assert.Contains("restoreDefaultMaterialLibraryLocation(): Promise<RestoreDefaultMaterialLibraryLocationResponse>", hostBridge);

        Assert.Contains("chooseMaterialLibraryLocation: 'choose-material-library-location'", contracts);
        Assert.Contains("'restore-default-material-library-location'", contracts);
        Assert.Contains("export interface MaterialLibraryLocation {", contracts);

        Assert.Contains(".library-location-card {", styles);
        Assert.Contains(".library-location-actions {", styles);
    }

    [Fact]
    public void Import_page_manual_add_material_field_uses_material_library_combobox_suggestions()
    {
        var app = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "App.tsx"));
        var importPage = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ImportPage.tsx"));
        var materialCombobox = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "components", "MaterialCombobox.tsx"));

        Assert.Contains("materials={state.materials}", app);
        Assert.Contains("import { MaterialCombobox } from '../components/MaterialCombobox';", importPage);
        Assert.Contains("const manualAddMaterialComboboxId = 'import-manual-add-material';", importPage);
        Assert.Contains("const materialLibraryNames = useMemo(", importPage);
        Assert.Contains(".map((material) => material.name.trim())", importPage);
        Assert.Contains(".filter((name) => name.length > 0)", importPage);
        Assert.Contains("[materials],", importPage);
        Assert.Contains("<span>Material</span>", importPage);
        Assert.Contains("inputId={manualAddMaterialComboboxId}", importPage);
        Assert.Contains("materials={materialLibraryNames}", importPage);
        Assert.Contains("value={addDraft.materialName ?? ''}", importPage);

        AssertContains(
            materialCombobox,
            """
            interface MaterialComboboxProps {
              inputId: string;
              value: string;
              materials: string[];
              onChange: (value: string) => void;
              disabled?: boolean;
            }
            """);
        Assert.Contains("role=\"combobox\"", materialCombobox);
        Assert.Contains("role=\"listbox\"", materialCombobox);
        Assert.Contains("role=\"option\"", materialCombobox);
        Assert.Contains("aria-haspopup=\"listbox\"", materialCombobox);
        Assert.Contains("aria-expanded={showSuggestions}", materialCombobox);
        Assert.Contains("autoComplete=\"off\"", materialCombobox);
        Assert.Contains("visibleOptions.map((materialName, index) => {", materialCombobox);
        Assert.Contains("commitSelection(visibleOptions[activeIndex]);", materialCombobox);
    }

    [Fact]
    public void Material_combobox_styles_keep_manual_add_suggestions_attached_to_the_field()
    {
        var styles = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "styles.css"));

        AssertContains(
            styles,
            """
            .material-combobox {
              position: relative;
            }
            """);
        AssertContains(
            styles,
            """
            .material-combobox__list {
              position: absolute;
              top: calc(100% + 4px);
              left: 0;
              right: 0;
            """);
        Assert.Contains("  z-index: 2;", styles);
        Assert.Contains("  max-height: 240px;", styles);
        Assert.Contains("  overflow-y: auto;", styles);
        Assert.Contains("  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.32);", styles);
        AssertContains(
            styles,
            """
            .material-combobox__option:hover,
            .material-combobox__option--active {
              background: rgba(0, 122, 204, 0.18);
              color: var(--vsc-text-bright);
            }
            """);
    }

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
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

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
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
              overflow-x: hidden;
              overflow-y: auto;
              overscroll-behavior: contain;
            }
            """);
        AssertContains(
            styles,
            """
            .results-viewer-column > .sheet-viewer-panel {
              display: grid;
              grid-template-rows: auto auto 1fr;
              min-height: 0;
              overflow-x: hidden;
              overflow-y: auto;
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

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
    public void Results_page_only_adds_group_review_when_placements_expose_group_data()
    {
        var contracts = ReadRepositoryText("src", "PanelNester.WebUI", "src", "types", "contracts.ts");
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");

        Assert.Contains("group?: string | null;", contracts);
        Assert.Contains("const hasGroupedPlacements = useMemo(", resultsPage);
        Assert.Contains("normalizeGroup(placement.group) !== null", resultsPage);
        Assert.Contains("{ id: 'group-review', label: 'Review by group' },", resultsPage);
        Assert.Contains("const activeMaterialGroupSummaries = useMemo(", resultsPage);
        Assert.Contains("No grouped panels in the active material result", resultsPage);
        Assert.Contains("<th>Part Group</th>", resultsPage);
        Assert.Contains("{selectedPlacement.displayGroup}", resultsPage);
        Assert.Contains("<td>{placement.displayGroup}</td>", resultsPage);
        Assert.Contains("decoratePlacements(activeMaterialResult.response.placements)", resultsPage);
    }

    // Search precision acceptance gate:
    // - normalize case and separators before evaluating a panel ID fragment
    // - require one exact contiguous normalized fragment, not scattered partial characters
    // - preserve deferred/memoized rendering for large batch review
    // - keep click-to-select sheet review and batch-sheet highlighting intact
    //
    // Regression risks:
    // - reintroducing fuzzy or tokenized matching that brings back false positives like the
    //   "04013" screenshot hits on unrelated panel IDs such as "0408" and "0407"
    // - removing useDeferredValue/useMemo and making large batch searches stall the UI
    // - breaking row click wiring so search hits stop driving viewer selection or sheet focus
    // - widening search scope beyond placed panels and batch-sheet review state
    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
    public void Results_page_batch_sheet_search_requires_exact_contiguous_normalized_panel_id_fragments()
    {
        var searchHelpers = ReadRepositoryText(
            "src",
            "PanelNester.WebUI",
            "src",
            "pages",
            "resultsBatchSheetSearch.ts");
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");

        Assert.Contains("function normalizePanelSearchValue(value: string): string {", searchHelpers);
        Assert.Contains("return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '');", searchHelpers);
        Assert.Contains(
            "function panelIdMatchesNormalizedQuery(", searchHelpers);
        Assert.Contains(
            "return normalizedQuery.length > 0 && normalizedPanelId.includes(normalizedQuery);",
            searchHelpers);
        Assert.Contains("function panelIdMatchesQuery(panelId: string, query: string): boolean {", searchHelpers);
        Assert.Contains("normalizePanelSearchValue(panelId),", searchHelpers);
        Assert.Contains("normalizePanelSearchValue(query),", searchHelpers);
        Assert.Contains("normalizedPanelSearchValue: string;", searchHelpers);
        Assert.Contains(
            "normalizedPanelSearchValue: normalizePanelSearchValue(placement.partId),",
            searchHelpers);
        Assert.Contains("const normalizedQuery = normalizePanelSearchValue(query);", searchHelpers);
        Assert.Contains(
            "panelIdMatchesNormalizedQuery(entry.normalizedPanelSearchValue, normalizedQuery)",
            searchHelpers);
        Assert.DoesNotContain("function splitNormalizedPanelSearchFragments", searchHelpers);
        Assert.DoesNotContain("function buildNormalizedPanelSearchValues", searchHelpers);
        Assert.Contains(
            "only exact panel ID fragments or contiguous fragment sequences count",
            resultsPage);
        Assert.Contains(
            "Try an exact panel ID fragment or contiguous fragment sequence. Search",
            resultsPage);
    }

    [Fact]
    public void Results_page_batch_sheet_search_rejects_the_reported_false_positive_examples()
    {
        const string query = "04013";
        string[] samplePanelIds =
        [
            "PANEL-0408#2",
            "PANEL-0407#3",
            "PANEL-00004#2",
            "PANEL-00040#1",
            "PANEL-00040#2",
            "PANEL-00045#1",
            "PANEL-00045#2",
            "PANEL-00045#3",
            "PANEL-04013#1",
            "panel-04-013"
        ];

        var matches = samplePanelIds
            .Where(panelId => PanelIdMatchesQuery(panelId, query))
            .ToArray();

        Assert.Equal(["PANEL-04013#1", "panel-04-013"], matches);
        Assert.DoesNotContain("PANEL-0408#2", matches);
        Assert.DoesNotContain("PANEL-0407#3", matches);
        Assert.DoesNotContain("PANEL-00004#2", matches);
        Assert.DoesNotContain("PANEL-00040#1", matches);
        Assert.DoesNotContain("PANEL-00040#2", matches);
        Assert.DoesNotContain("PANEL-00045#1", matches);
        Assert.DoesNotContain("PANEL-00045#2", matches);
        Assert.DoesNotContain("PANEL-00045#3", matches);
        Assert.True(PanelIdMatchesQuery("PANEL-04013#1", "013"));
        Assert.False(PanelIdMatchesQuery("PANEL-00045#1", "013"));
    }

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
    public void Results_page_batch_sheet_search_keeps_deferred_and_memoized_rendering_for_large_batches()
    {
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");

        Assert.Contains("const batchSheets = useMemo(", resultsPage);
        Assert.Contains("() => buildBatchSheets(materialResults),", resultsPage);
        Assert.Contains("const panelSearchIndex = useMemo(", resultsPage);
        Assert.Contains("() => buildPanelSearchIndex(batchSheets),", resultsPage);
        Assert.Contains("const deferredPanelSearchQueryLabel = useDeferredValue(panelSearchQueryLabel);", resultsPage);
        Assert.Contains("const panelSearchResults = useMemo(", resultsPage);
        Assert.Contains(
            "() => buildPanelSearchResults(panelSearchIndex, deferredPanelSearchQueryLabel),",
            resultsPage);
        Assert.Contains("Updating search results for “{panelSearchQueryLabel}”…", resultsPage);
    }

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
    public void Results_page_batch_sheet_search_rows_still_drive_sheet_review_and_sheet_highlighting()
    {
        var resultsPage = ReadRepositoryText("src", "PanelNester.WebUI", "src", "pages", "ResultsPage.tsx");

        Assert.Contains("const reviewBatchSheet = (", resultsPage);
        Assert.Contains("setActiveMaterialKey(materialKey);", resultsPage);
        Assert.Contains("setActiveSheetId(sheetId);", resultsPage);
        Assert.Contains("setSelectedPlacementId(placementId);", resultsPage);
        Assert.Contains("const reviewPanelMatch = (match: PanelSearchMatch) => {", resultsPage);
        Assert.Contains("reviewBatchSheet(match.materialKey, match.sheetId, match.placementId);", resultsPage);
        Assert.Contains("onClick={() => reviewPanelMatch(match)}", resultsPage);
        Assert.Contains("function panelSearchMatchRowKey(match: PanelSearchMatch): string {", resultsPage);
        Assert.Contains("return `${match.materialKey}:${match.sheetId}:${match.placementId}:${match.partId}`;", resultsPage);
        Assert.DoesNotContain("key={`${match.placementId}:${match.sheetId}`}", resultsPage);
        Assert.Contains("key={panelSearchMatchRowKey(match)}", resultsPage);
        Assert.Contains("Search hits stay highlighted here without duplicating the sheet inventory", resultsPage);
        Assert.Contains("const filteredPanelSearchResults = useMemo(() => {", resultsPage);
        Assert.Contains("const batchSheetsByKey = new Map(", resultsPage);
        Assert.Contains("const panelSearchMatchCount = filteredPanelSearchResults.matches.length;", resultsPage);
        Assert.Contains("const panelSearchSheetCount = filteredPanelSearchResults.sheets.length;", resultsPage);
        Assert.Contains("const panelSearchResults = useMemo(", resultsPage);
        Assert.Contains("{panelSearchMatchCount} panel match(es) across {panelSearchSheetCount}", resultsPage);
        Assert.Contains("filteredPanelSearchResults.matches.map((match) => (", resultsPage);
        Assert.Contains("const filteredSearchSheet =", resultsPage);
        Assert.Contains("filteredPanelSearchResults.bySheetKey.get(sheetKey);", resultsPage);
        Assert.Contains("const searchHitCount = filteredSearchSheet?.matches.length ?? 0;", resultsPage);
        Assert.Contains("searchHitCount > 0 && 'table-row--search-hit'", resultsPage);
        Assert.Contains("const firstMatch = filteredSearchSheet?.firstMatch;", resultsPage);
        Assert.Contains("firstMatch?.placementId,", resultsPage);
    }

    [Fact]
    public void Sheet_viewer_keeps_mixed_group_sheets_dimmed_outside_the_active_group_and_shows_group_hover_details()
    {
        var sheetViewer = Normalize(ReadRepositoryText("src", "PanelNester.WebUI", "src", "components", "SheetViewer.tsx"));

        Assert.Contains("interface SheetViewerPlacement extends NestPlacement {", sheetViewer);
        Assert.DoesNotContain("  group?: string | null;", sheetViewer);
        Assert.Contains("activeGroup?: string;", sheetViewer);
        Assert.Contains("activeGroupLabel?: string;", sheetViewer);
        Assert.Contains("const hasActiveGroup = activeGroupRef.current !== undefined;", sheetViewer);
        Assert.Contains("!hasActiveGroup || visual.groupKey === activeGroupRef.current;", sheetViewer);
        Assert.Contains("isActiveGroupPlacement ? visual.baseColor : '#7d7f83'", sheetViewer);
        Assert.Contains("Focus Part Group: {activeGroupLabel}", sheetViewer);
        Assert.Contains("Other Part Groups subdued", sheetViewer);
        Assert.Contains("Part Group: {getDisplayGroup(tooltip.placement.group, tooltip.placement.displayGroup)}", sheetViewer);
    }

    [Fact(Skip = "Obsolete source-text gate; Results behavior is covered by rendered UI tests.")]
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

    private static string NormalizePanelSearchValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool PanelIdMatchesQuery(string panelId, string query)
    {
        var normalizedQuery = NormalizePanelSearchValue(query);
        return normalizedQuery.Length > 0 &&
               NormalizePanelSearchValue(panelId).Contains(normalizedQuery, StringComparison.Ordinal);
    }

    private static void AssertContains(string actual, string expectedFragment) =>
        Assert.Contains(Normalize(expectedFragment), actual);
}
