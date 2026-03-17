import {
  Suspense,
  lazy,
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from 'react';
import { StatusPill } from '../components/StatusPill';
import type {
  BatchNestResponse,
  Material,
  NestPlacement,
  NestResponse,
  PartRow,
  ProjectMaterialSnapshot,
  ReportSettings,
} from '../types/contracts';

interface ResultsPageProps {
  material?: Material;
  selectedMaterialId?: string;
  kerfWidth: number;
  nestResponse: NestResponse;
  batchNestResponse: BatchNestResponse;
  parts: PartRow[];
  statusMessage: string;
  savedMaterialSnapshots: ProjectMaterialSnapshot[];
  pendingMaterialSnapshots: ProjectMaterialSnapshot[];
  projectDirty: boolean;
  reportSettings: ReportSettings;
  reportMessage: string;
  reportBusy: boolean;
  canSyncReportSettings: boolean;
  canExportReport: boolean;
  onReportSettingsChange: (field: keyof ReportSettings, value: string) => void;
  onSyncReportSettings: () => Promise<void>;
  onExportReport: () => Promise<void>;
}

interface MaterialResultView {
  key: string;
  materialName: string;
  materialId?: string;
  response: NestResponse;
}

interface ResultsPlacement extends NestPlacement {
  group?: string | null;
  displayGroup: string;
}

interface GroupResultView {
  key: string;
  label: string;
  placements: ResultsPlacement[];
  sheetIds: string[];
}

const SheetViewer = lazy(async () => {
  const module = await import('../components/SheetViewer');
  return { default: module.SheetViewer };
});

const baseWorkspaceTabs = [
  { id: 'report-fields', label: 'Report fields' },
  { id: 'summary-by-material', label: 'Summary by material' },
  { id: 'group-review', label: 'Review by group' },
  { id: 'sheet-detail', label: 'Sheet detail' },
  { id: 'placement-inspection', label: 'Placement inspection' },
  { id: 'unplaced', label: 'Unplaced' },
] as const;

type ResultsWorkspaceTabId = (typeof baseWorkspaceTabs)[number]['id'];

const minWorkspaceWidth = 360;
const resultsSplitterWidth = 14;
const minViewerWidth = 420;

function itemLabel(partId: string): string {
  return partId.trim().length > 0 ? partId : 'Run';
}

function normalizeGroup(value?: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length > 0 ? trimmed : null;
}

function getGroupKey(value?: string | null): string {
  return normalizeGroup(value) ?? '';
}

function getDisplayGroup(value?: string | null): string {
  return normalizeGroup(value) ?? 'Ungrouped';
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function getBasePartId(part: PartRow): string {
  const importedId = part.importedId.trim();
  return importedId.length > 0 ? importedId : part.rowId;
}

function buildPlacementGroupLookup(parts: PartRow[]): Map<string, string | null> {
  const lookup = new Map<string, string | null>();

  for (const part of parts) {
    const group = normalizeGroup(part.group);
    const basePartId = getBasePartId(part);
    const partCount = Math.max(part.quantity, 1);

    if (partCount === 1) {
      if (!lookup.has(basePartId)) {
        lookup.set(basePartId, group);
      }
      continue;
    }

    for (let instanceNumber = 1; instanceNumber <= partCount; instanceNumber += 1) {
      const partId = `${basePartId}#${instanceNumber}`;
      if (!lookup.has(partId)) {
        lookup.set(partId, group);
      }
    }
  }

  return lookup;
}

function decoratePlacements(
  placements: NestPlacement[],
  placementGroups: Map<string, string | null>,
): ResultsPlacement[] {
  return placements.map((placement) => {
    const group = placementGroups.get(placement.partId) ?? null;

    return {
      ...placement,
      group,
      displayGroup: getDisplayGroup(group),
    };
  });
}

function buildGroupSummaries(
  parts: PartRow[],
  placements: ResultsPlacement[],
): GroupResultView[] {
  const namedGroupOrder: string[] = [];
  const seenNamedGroups = new Set<string>();
  let includeUngrouped = false;

  const registerNamedGroup = (value?: string | null) => {
    const normalized = normalizeGroup(value);
    if (!normalized) {
      includeUngrouped = true;
      return;
    }

    if (seenNamedGroups.has(normalized)) {
      return;
    }

    seenNamedGroups.add(normalized);
    namedGroupOrder.push(normalized);
  };

  for (const part of parts) {
    registerNamedGroup(part.group);
  }

  for (const placement of placements) {
    registerNamedGroup(placement.group);
  }

  if (namedGroupOrder.length === 0) {
    return [];
  }

  const placementsByGroup = new Map<string, ResultsPlacement[]>();
  for (const placement of placements) {
    const groupKey = getGroupKey(placement.group);
    const groupedPlacements = placementsByGroup.get(groupKey) ?? [];
    groupedPlacements.push(placement);
    placementsByGroup.set(groupKey, groupedPlacements);
  }

  const orderedGroupKeys = includeUngrouped
    ? [...namedGroupOrder, '']
    : namedGroupOrder;

  return orderedGroupKeys
    .map((groupKey) => {
      const groupedPlacements = placementsByGroup.get(groupKey) ?? [];
      if (groupedPlacements.length === 0) {
        return null;
      }

      return {
        key: groupKey,
        label: getDisplayGroup(groupKey),
        placements: groupedPlacements,
        sheetIds: Array.from(
          new Set(groupedPlacements.map((placement) => placement.sheetId)),
        ),
      } satisfies GroupResultView;
    })
    .filter((group): group is GroupResultView => group !== null);
}

function createLegacyMaterialResult(
  material: Material | undefined,
  nestResponse: NestResponse,
): MaterialResultView[] {
  if (nestResponse.sheets.length === 0 && nestResponse.unplacedItems.length === 0) {
    return [];
  }

  const materialName =
    material?.name ?? nestResponse.sheets[0]?.materialName ?? 'Imported material';
  const materialId = material?.materialId;

  return [
    {
      key: materialId ?? materialName,
      materialName,
      materialId,
      response: nestResponse,
    },
  ];
}

function buildMaterialResults(
  batchNestResponse: BatchNestResponse,
  material: Material | undefined,
  nestResponse: NestResponse,
): MaterialResultView[] {
  if (batchNestResponse.materialResults.length > 0) {
    return batchNestResponse.materialResults.map((result) => ({
      key: result.materialId ?? result.materialName,
      materialName: result.materialName,
      materialId: result.materialId ?? undefined,
      response: result.result,
    }));
  }

  return createLegacyMaterialResult(material, nestResponse);
}

function findSnapshotMaterial(
  snapshots: ProjectMaterialSnapshot[],
  materialId?: string,
  materialName?: string,
): ProjectMaterialSnapshot | undefined {
  if (materialId) {
    const byId = snapshots.find((snapshot) => snapshot.materialId === materialId);
    if (byId) {
      return byId;
    }
  }

  if (materialName) {
    return snapshots.find((snapshot) => snapshot.name === materialName);
  }

  return undefined;
}

function sumMaterialResults(materialResults: MaterialResultView[]) {
  return materialResults.reduce(
    (totals, result) => ({
      totalSheets: totals.totalSheets + result.response.summary.totalSheets,
      totalPlaced: totals.totalPlaced + result.response.summary.totalPlaced,
      totalUnplaced: totals.totalUnplaced + result.response.summary.totalUnplaced,
      utilizationTotal:
        totals.utilizationTotal + result.response.summary.overallUtilization,
    }),
    {
      totalSheets: 0,
      totalPlaced: 0,
      totalUnplaced: 0,
      utilizationTotal: 0,
    },
  );
}

export function ResultsPage({
  material,
  selectedMaterialId,
  kerfWidth,
  nestResponse,
  batchNestResponse,
  parts,
  statusMessage,
  savedMaterialSnapshots,
  pendingMaterialSnapshots,
  projectDirty,
  reportSettings,
  reportMessage,
  reportBusy,
  canSyncReportSettings,
  canExportReport,
  onReportSettingsChange,
  onExportReport,
}: ResultsPageProps) {
  const materialResults = useMemo(
    () => buildMaterialResults(batchNestResponse, material, nestResponse),
    [batchNestResponse, material, nestResponse],
  );
  const hasGroupedParts = useMemo(
    () => parts.some((part) => normalizeGroup(part.group) !== null),
    [parts],
  );
  const workspaceTabs = useMemo(
    () =>
      hasGroupedParts
        ? baseWorkspaceTabs
        : baseWorkspaceTabs.filter((tab) => tab.id !== 'group-review'),
    [hasGroupedParts],
  );
  const totals = useMemo(() => sumMaterialResults(materialResults), [materialResults]);
  const averageUtilization =
    materialResults.length > 0
      ? totals.utilizationTotal / materialResults.length
      : nestResponse.summary.overallUtilization;
  const hasSheets =
    materialResults.some((result) => result.response.sheets.length > 0) ||
    nestResponse.sheets.length > 0;
  const hasPlacements =
    materialResults.some((result) => result.response.placements.length > 0) ||
    nestResponse.placements.length > 0;
  const hasOutput =
    materialResults.length > 0 ||
    nestResponse.sheets.length > 0 ||
    nestResponse.unplacedItems.length > 0;
  const hasEmptyResult = hasOutput && !hasSheets && !hasPlacements;
  const emptyRunNote = nestResponse.unplacedItems.find(
    (item) => item.reasonCode === 'empty-run',
  );

  const [activeMaterialKey, setActiveMaterialKey] = useState<string>();
  const [activeSheetId, setActiveSheetId] = useState<string>();
  const [activeGroupKey, setActiveGroupKey] = useState<string>();
  const [selectedPlacementId, setSelectedPlacementId] = useState<string>();
  const [activeWorkspaceTab, setActiveWorkspaceTab] =
    useState<ResultsWorkspaceTabId>('report-fields');
  const [workspaceWidth, setWorkspaceWidth] = useState(520);
  const [isResizingWorkspace, setIsResizingWorkspace] = useState(false);
  const splitLayoutRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const preferred =
      materialResults.find(
        (result) =>
          (selectedMaterialId && result.materialId === selectedMaterialId) ||
          (material?.materialId && result.materialId === material.materialId) ||
          result.materialName === material?.name,
      ) ?? materialResults[0];

    setActiveMaterialKey((current) =>
      current && materialResults.some((result) => result.key === current)
        ? current
        : preferred?.key,
    );
  }, [material, materialResults, selectedMaterialId]);

  useEffect(() => {
    if (workspaceTabs.some((tab) => tab.id === activeWorkspaceTab)) {
      return;
    }

    setActiveWorkspaceTab('summary-by-material');
  }, [activeWorkspaceTab, workspaceTabs]);

  const activeMaterialResult =
    materialResults.find((result) => result.key === activeMaterialKey) ?? materialResults[0];
  const activeMaterialParts = useMemo(
    () =>
      activeMaterialResult
        ? parts.filter((part) => part.materialName === activeMaterialResult.materialName)
        : [],
    [activeMaterialResult, parts],
  );
  const activeMaterialPlacementGroups = useMemo(
    () => buildPlacementGroupLookup(activeMaterialParts),
    [activeMaterialParts],
  );
  const activeMaterialPlacements = useMemo(
    () =>
      activeMaterialResult
        ? decoratePlacements(
            activeMaterialResult.response.placements,
            activeMaterialPlacementGroups,
          )
        : [],
    [activeMaterialPlacementGroups, activeMaterialResult],
  );
  const activeMaterialGroupSummaries = useMemo(
    () => buildGroupSummaries(activeMaterialParts, activeMaterialPlacements),
    [activeMaterialParts, activeMaterialPlacements],
  );

  useEffect(() => {
    const preferredGroupKey = activeMaterialGroupSummaries[0]?.key;
    setActiveGroupKey((current) =>
      current !== undefined &&
      activeMaterialGroupSummaries.some((group) => group.key === current)
        ? current
        : preferredGroupKey,
    );
  }, [activeMaterialGroupSummaries]);

  const activeGroupSummary =
    activeMaterialGroupSummaries.find((group) => group.key === activeGroupKey) ??
    activeMaterialGroupSummaries[0];

  useEffect(() => {
    if (activeWorkspaceTab !== 'group-review' || !activeGroupSummary) {
      return;
    }

    setActiveSheetId((current) =>
      current && activeGroupSummary.sheetIds.includes(current)
        ? current
        : activeGroupSummary.sheetIds[0],
    );
    setSelectedPlacementId((current) =>
      current &&
      activeGroupSummary.placements.some((placement) => placement.placementId === current)
        ? current
        : undefined,
    );
  }, [activeGroupSummary, activeWorkspaceTab]);

  useEffect(() => {
    const firstSheetId = activeMaterialResult?.response.sheets[0]?.sheetId;
    setActiveSheetId((current) =>
      current &&
      activeMaterialResult?.response.sheets.some((sheet) => sheet.sheetId === current)
        ? current
        : firstSheetId,
    );
    setSelectedPlacementId(undefined);
  }, [activeMaterialResult]);

  useEffect(() => {
    if (!isResizingWorkspace) {
      return undefined;
    }

    const previousCursor = document.body.style.cursor;
    const previousUserSelect = document.body.style.userSelect;
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';

    const handlePointerMove = (event: PointerEvent) => {
      const layout = splitLayoutRef.current;
      if (!layout) {
        return;
      }

      const bounds = layout.getBoundingClientRect();
      const maxWidth = Math.max(
        minWorkspaceWidth,
        bounds.width - minViewerWidth - resultsSplitterWidth,
      );
      setWorkspaceWidth(
        clamp(event.clientX - bounds.left, minWorkspaceWidth, maxWidth),
      );
    };

    const stopResizing = () => {
      setIsResizingWorkspace(false);
    };

    window.addEventListener('pointermove', handlePointerMove);
    window.addEventListener('pointerup', stopResizing);
    window.addEventListener('pointercancel', stopResizing);

    return () => {
      document.body.style.cursor = previousCursor;
      document.body.style.userSelect = previousUserSelect;
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('pointerup', stopResizing);
      window.removeEventListener('pointercancel', stopResizing);
    };
  }, [isResizingWorkspace]);

  const activeSheet =
    activeMaterialResult?.response.sheets.find((sheet) => sheet.sheetId === activeSheetId) ??
    activeMaterialResult?.response.sheets[0] ??
    null;
  const activeSheetPlacements = useMemo(
    () =>
      activeMaterialPlacements.filter((placement) => placement.sheetId === activeSheet?.sheetId),
    [activeMaterialPlacements, activeSheet?.sheetId],
  );
  const selectedPlacement = activeMaterialPlacements.find(
    (placement) => placement.placementId === selectedPlacementId,
  );
  const activeGroupSheetRows = useMemo(() => {
    if (!activeMaterialResult || !activeGroupSummary) {
      return [];
    }

    return activeMaterialResult.response.sheets
      .filter((sheet) => activeGroupSummary.sheetIds.includes(sheet.sheetId))
      .map((sheet) => {
        const sheetPlacements = activeMaterialPlacements.filter(
          (placement) => placement.sheetId === sheet.sheetId,
        );
        const selectedGroupPlacements = sheetPlacements.filter(
          (placement) => getGroupKey(placement.group) === activeGroupSummary.key,
        );

        return {
          otherGroupCount: sheetPlacements.length - selectedGroupPlacements.length,
          selectedGroupCount: selectedGroupPlacements.length,
          sheet,
        };
      });
  }, [activeGroupSummary, activeMaterialPlacements, activeMaterialResult]);
  const viewerActiveGroupKey =
    activeWorkspaceTab === 'group-review' ? activeGroupSummary?.key : undefined;
  const viewerActiveGroupLabel =
    activeWorkspaceTab === 'group-review' ? activeGroupSummary?.label : undefined;

  const savedSnapshot = activeMaterialResult
    ? findSnapshotMaterial(
        savedMaterialSnapshots,
        activeMaterialResult.materialId,
        activeMaterialResult.materialName,
      )
    : undefined;
  const pendingSnapshot = activeMaterialResult
    ? findSnapshotMaterial(
        pendingMaterialSnapshots,
        activeMaterialResult.materialId,
        activeMaterialResult.materialName,
      )
    : undefined;
  const displaySnapshot = projectDirty
    ? pendingSnapshot ?? savedSnapshot
    : savedSnapshot ?? pendingSnapshot;
  const snapshotStatus: 'saved' | 'pending' | 'none' = displaySnapshot
    ? projectDirty && pendingSnapshot
      ? 'pending'
      : savedSnapshot
        ? 'saved'
        : 'pending'
    : 'none';
  const exportReady = canExportReport && hasOutput;
  const resultStatusTone = hasEmptyResult
    ? 'warn'
    : hasOutput
      ? batchNestResponse.success || nestResponse.success
        ? 'ok'
        : 'warn'
      : 'muted';
  const resultStatusLabel = hasEmptyResult
    ? 'No sheets produced'
    : hasOutput
      ? batchNestResponse.materialResults.length > 1
        ? 'Batch results ready'
        : 'Results ready'
      : 'Pending';
  const exportStatusLabel = canExportReport
    ? exportReady
      ? hasEmptyResult
        ? 'Empty-result export ready'
        : 'Export ready'
      : 'No exportable result'
    : 'Export bridge pending';
  const reportFieldsNote = !canExportReport
    ? 'PDF export will light up when the desktop host exposes the Phase 5 export bridge.'
    : hasOutput
      ? 'PDF export uses the values shown here. No separate apply step is required.'
      : 'PDF export uses the values shown here. Run nesting to generate an exportable result.';
  const viewerResetToken = `${activeMaterialResult?.key ?? 'none'}:${activeSheet?.sheetId ?? 'none'}`;
  const splitLayoutStyle = {
    '--results-workspace-width': `${workspaceWidth}px`,
    '--results-splitter-width': `${resultsSplitterWidth}px`,
  } as CSSProperties & {
    '--results-workspace-width': string;
    '--results-splitter-width': string;
  };

  const viewerPanel = activeSheet ? (
    <Suspense
      fallback={
        <section className="panel sheet-viewer-panel">
          <div className="section-header">
            <div>
              <p className="eyebrow">Viewer</p>
              <h3>Sheet layout viewer</h3>
            </div>
          </div>
          <div className="empty-state">
            <strong>Loading viewer</strong>
            <span>Preparing the interactive sheet viewport.</span>
          </div>
        </section>
      }
    >
      <SheetViewer
        activeGroup={viewerActiveGroupKey}
        activeGroupLabel={viewerActiveGroupLabel}
        materialName={activeMaterialResult?.materialName ?? material?.name ?? 'Material'}
        onSelectPlacement={setSelectedPlacementId}
        placements={activeMaterialPlacements}
        resetViewToken={viewerResetToken}
        selectedPlacementId={selectedPlacementId}
        sheet={activeSheet}
      />
    </Suspense>
  ) : (
    <section className="panel sheet-viewer-panel">
      <div className="section-header">
        <div>
          <p className="eyebrow">Viewer</p>
          <h3>Sheet layout viewer</h3>
        </div>
      </div>
      <div className="viewer-placeholder__canvas" aria-hidden="true">
        <div className="viewer-placeholder__sheet-outline">
          <span>{hasEmptyResult ? 'No sheet layouts were produced' : 'Choose a sheet to inspect'}</span>
        </div>
      </div>
      <div className="empty-state">
        <strong>{hasEmptyResult ? 'This run finished without any sheet layouts' : 'No sheet selected'}</strong>
        <span>
          {hasEmptyResult
            ? emptyRunNote?.reasonDescription ??
              'The current run did not produce any placed panels. Review the unplaced reasons below or return to Import to adjust the input.'
            : 'Choose a material result and sheet to inspect the layout.'}
        </span>
      </div>
    </section>
  );

  return (
    <div className="page-grid results-page">
      <section className="panel hero-panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Results</p>
            <h2>Multi-material layouts, sheet inspection, and report export</h2>
          </div>
          <div className="status-row">
            <StatusPill tone={resultStatusTone} label={resultStatusLabel} />
            <StatusPill
              tone={exportReady ? 'ok' : canExportReport ? 'warn' : 'muted'}
              label={exportStatusLabel}
            />
          </div>
        </div>

        <p className="muted">{statusMessage}</p>

        {hasEmptyResult ? (
          <div className="results-empty-intent">
            <strong>No panels were placed in this run</strong>
            <span>
              {activeMaterialResult
                ? `The current ${activeMaterialResult.materialName} result has no sheets to inspect yet. Review the unplaced reasons below or return to Import to correct the input.`
                : 'This run finished without producing any sheet layouts. Review the unplaced reasons below or return to Import to correct the input.'}
            </span>
            <div className="token-list">
              <span className="token">0 sheets generated</span>
              <span className="token">Viewer stays in empty-state mode</span>
              <span className="token">
                {exportReady
                  ? 'PDF export can capture this empty result'
                  : 'PDF export unlocks when the host bridge is available'}
              </span>
            </div>
          </div>
        ) : null}

        <div className="stats-grid">
          <article className="stat-card">
            <span>Materials</span>
            <strong>{materialResults.length}</strong>
          </article>
          <article className="stat-card">
            <span>Sheets</span>
            <strong>{totals.totalSheets}</strong>
          </article>
          <article className="stat-card">
            <span>Placed</span>
            <strong>{totals.totalPlaced}</strong>
          </article>
          <article className="stat-card">
            <span>Unplaced</span>
            <strong>{totals.totalUnplaced}</strong>
          </article>
          <article className="stat-card">
            <span>Avg utilization</span>
            <strong>{averageUtilization.toFixed(1)}%</strong>
          </article>
          <article className="stat-card">
            <span>Kerf</span>
            <strong>{kerfWidth}"</strong>
          </article>
        </div>
      </section>

      <div
        className={`results-split-layout${isResizingWorkspace ? ' results-split-layout--resizing' : ''}`}
        data-results-layout="workspace-left-viewer-right"
        ref={splitLayoutRef}
        style={splitLayoutStyle}
      >
        <section aria-label="Results workspace" className="panel results-workspace">
          <div className="results-workspace__header">
            <div>
              <p className="eyebrow">Workspace</p>
              <h3>Review results without leaving the layout view</h3>
            </div>
          </div>

          <div
            aria-label="Results detail views"
            className="results-workspace__tabs"
            role="tablist"
          >
            {workspaceTabs.map((tab) => {
              const isActive = tab.id === activeWorkspaceTab;

              return (
                <button
                  aria-selected={isActive}
                  className={
                    isActive
                      ? 'results-workspace__tab results-workspace__tab--active'
                      : 'results-workspace__tab'
                  }
                  key={tab.id}
                  onClick={() => setActiveWorkspaceTab(tab.id)}
                  role="tab"
                  type="button"
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          <div className="results-workspace__panel" id="results-workspace-panel">
            {activeWorkspaceTab === 'report-fields' ? (
              <div className="results-tab-panel" role="tabpanel">
                <div className="section-header">
                  <div>
                    <p className="eyebrow">Report</p>
                    <h3>Editable export fields</h3>
                  </div>
                  <div className="button-row">
                    <button
                      className="primary-button"
                      disabled={!exportReady || reportBusy}
                      onClick={() => void onExportReport()}
                      type="button"
                    >
                      {reportBusy ? 'Exporting…' : 'Export PDF report'}
                    </button>
                  </div>
                </div>

                <p className="muted">{reportMessage}</p>

                <div className="form-grid form-grid--two-column">
                  <label className="field">
                    <span>Company name</span>
                    <input
                      onChange={(event) =>
                        onReportSettingsChange('companyName', event.target.value)
                      }
                      type="text"
                      value={reportSettings.companyName ?? ''}
                    />
                  </label>

                  <label className="field">
                    <span>Report title</span>
                    <input
                      onChange={(event) =>
                        onReportSettingsChange('reportTitle', event.target.value)
                      }
                      type="text"
                      value={reportSettings.reportTitle ?? ''}
                    />
                  </label>

                  <label className="field">
                    <span>Project / job name</span>
                    <input
                      onChange={(event) =>
                        onReportSettingsChange('projectJobName', event.target.value)
                      }
                      type="text"
                      value={reportSettings.projectJobName ?? ''}
                    />
                  </label>

                  <label className="field">
                    <span>Project / job number</span>
                    <input
                      onChange={(event) =>
                        onReportSettingsChange('projectJobNumber', event.target.value)
                      }
                      type="text"
                      value={reportSettings.projectJobNumber ?? ''}
                    />
                  </label>

                  <label className="field">
                    <span>Report date</span>
                    <input
                      onChange={(event) =>
                        onReportSettingsChange('reportDate', event.target.value)
                      }
                      type="date"
                      value={reportSettings.reportDate ?? ''}
                    />
                  </label>

                  <label className="field field--wide">
                    <span>Notes</span>
                    <textarea
                      onChange={(event) => onReportSettingsChange('notes', event.target.value)}
                      value={reportSettings.notes ?? ''}
                    />
                  </label>
                </div>

                <p className="section-note">
                  {reportFieldsNote}
                  {canSyncReportSettings ? ' The host no longer needs a separate apply step.' : ''}
                </p>
              </div>
            ) : null}

            {activeWorkspaceTab === 'summary-by-material' ? (
              <div className="results-tab-panel" role="tabpanel">
                <div className="results-tab-subsection">
                  <p className="eyebrow">Summary by material</p>
                  <h3>Choose the result set to inspect</h3>
                  {materialResults.length > 0 ? (
                    <>
                      <label className="field">
                        <span>Active material</span>
                        <select
                          className="results-material-selector"
                          onChange={(event) => setActiveMaterialKey(event.target.value)}
                          value={activeMaterialResult?.key ?? ''}
                        >
                          {materialResults.map((result) => (
                            <option key={result.key} value={result.key}>
                              {result.materialName} — {result.response.summary.totalSheets} sheet(s) · {result.response.summary.totalPlaced} placed
                            </option>
                          ))}
                        </select>
                      </label>

                      <div className="table-shell">
                        <table>
                          <thead>
                            <tr>
                              <th>Material</th>
                              <th>Sheets</th>
                              <th>Placed</th>
                              <th>Unplaced</th>
                              <th>Utilization</th>
                            </tr>
                          </thead>
                          <tbody>
                            {materialResults.map((result) => (
                              <tr
                                className={
                                  result.key === activeMaterialResult?.key
                                    ? 'table-row--active'
                                    : undefined
                                }
                                key={result.key}
                                onClick={() => setActiveMaterialKey(result.key)}
                              >
                                <td>{result.materialName}</td>
                                <td>{result.response.summary.totalSheets}</td>
                                <td>{result.response.summary.totalPlaced}</td>
                                <td>{result.response.summary.totalUnplaced}</td>
                                <td>{result.response.summary.overallUtilization.toFixed(1)}%</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </>
                  ) : (
                    <div className="empty-state">
                      <strong>
                        {hasEmptyResult ? 'No sheet groups were produced' : 'No results yet'}
                      </strong>
                      <span>
                        {hasEmptyResult
                          ? 'This nesting attempt completed without any generated sheets. The unplaced section explains what blocked layout creation.'
                          : 'Run nesting from the Import page to populate sheets, placements, and export data.'}
                      </span>
                    </div>
                  )}
                </div>

                <div className="results-tab-subsection">
                  <p className="eyebrow">Material snapshot</p>
                  <h3>
                    Saved context for {activeMaterialResult?.materialName ?? 'the current result'}
                  </h3>
                  {displaySnapshot ? (
                    <>
                      <p className="muted">
                        {snapshotStatus === 'saved'
                          ? 'These settings already live inside the saved project file.'
                          : 'These are the current live settings that will be written on the next save.'}
                      </p>
                      <dl className="definition-list">
                        <div>
                          <dt>Material</dt>
                          <dd>{displaySnapshot.name}</dd>
                        </div>
                        <div>
                          <dt>Sheet</dt>
                          <dd>
                            {displaySnapshot.sheetLength}" × {displaySnapshot.sheetWidth}"
                          </dd>
                        </div>
                        <div>
                          <dt>Rotation</dt>
                          <dd>{displaySnapshot.allowRotation ? 'Allowed' : 'Locked'}</dd>
                        </div>
                        <div>
                          <dt>Spacing</dt>
                          <dd>{displaySnapshot.defaultSpacing}"</dd>
                        </div>
                        <div>
                          <dt>Edge margin</dt>
                          <dd>{displaySnapshot.defaultEdgeMargin}"</dd>
                        </div>
                      </dl>
                    </>
                  ) : (
                    <div className="empty-state">
                      <strong>No material snapshot available</strong>
                      <span>
                        Save the project to pin the material settings that explain this layout.
                      </span>
                    </div>
                  )}
                </div>
              </div>
            ) : null}

            {activeWorkspaceTab === 'group-review' ? (
              <div className="results-tab-panel" role="tabpanel">
                <div className="results-tab-subsection">
                  <p className="eyebrow">Review by group</p>
                  <h3>
                    {activeMaterialResult
                      ? `${activeMaterialResult.materialName} grouped panel review`
                      : 'Grouped panel review'}
                  </h3>

                  {materialResults.length > 0 ? (
                    <label className="field">
                      <span>Active material</span>
                      <select
                        className="results-material-selector"
                        onChange={(event) => setActiveMaterialKey(event.target.value)}
                        value={activeMaterialResult?.key ?? ''}
                      >
                        {materialResults.map((result) => (
                          <option key={result.key} value={result.key}>
                            {result.materialName}
                          </option>
                        ))}
                      </select>
                    </label>
                  ) : null}

                  {activeMaterialGroupSummaries.length > 0 ? (
                    <>
                      <label className="field">
                        <span>Active group</span>
                        <select
                          className="results-sheet-selector"
                          onChange={(event) => setActiveGroupKey(event.target.value)}
                          value={activeGroupSummary?.key ?? ''}
                        >
                          {activeMaterialGroupSummaries.map((group) => (
                            <option key={group.key || '__ungrouped__'} value={group.key}>
                              {group.label} — {group.placements.length} panel(s) · {group.sheetIds.length} sheet(s)
                            </option>
                          ))}
                        </select>
                      </label>

                      <div className="selection-summary">
                        <span>Viewer focus</span>
                        <strong>{activeGroupSummary?.label ?? 'Choose a group'}</strong>
                        <small>
                          {activeGroupSummary?.placements.length ?? 0} placed panel(s) across{' '}
                          {activeGroupSummary?.sheetIds.length ?? 0} sheet(s). The viewer keeps this
                          group at full color and subdues every other group.
                        </small>
                      </div>

                      <div className="table-shell">
                        <table>
                          <thead>
                            <tr>
                              <th>Group</th>
                              <th>Placed panels</th>
                              <th>Sheets touched</th>
                            </tr>
                          </thead>
                          <tbody>
                            {activeMaterialGroupSummaries.map((group) => (
                              <tr
                                className={
                                  group.key === activeGroupSummary?.key
                                    ? 'table-row--active'
                                    : undefined
                                }
                                key={group.key || '__ungrouped__'}
                                onClick={() => setActiveGroupKey(group.key)}
                              >
                                <td>{group.label}</td>
                                <td>{group.placements.length}</td>
                                <td>{group.sheetIds.length}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </>
                  ) : (
                    <div className="empty-state">
                      <strong>No grouped panels in the active material result</strong>
                      <span>
                        {materialResults.length > 0
                          ? 'Another material result carries grouped panels. Switch materials to review them here.'
                          : 'Run nesting on the current grouped import to unlock group-focused review.'}
                      </span>
                    </div>
                  )}
                </div>

                {activeGroupSummary ? (
                  <div className="results-tab-subsection">
                    <p className="eyebrow">Sheets touched by {activeGroupSummary.label}</p>
                    <h3>Keep the current material context while stepping sheet by sheet</h3>
                    <p className="section-note">
                      Mixed-group sheets stay readable: {activeGroupSummary.label} renders normally,
                      while every other group fades back until you hover it for detail.
                    </p>

                    {activeGroupSheetRows.length > 0 ? (
                      <div className="table-shell">
                        <table>
                          <thead>
                            <tr>
                              <th>Sheet</th>
                              <th>{activeGroupSummary.label}</th>
                              <th>Other groups</th>
                              <th>Utilization</th>
                            </tr>
                          </thead>
                          <tbody>
                            {activeGroupSheetRows.map((row) => (
                              <tr
                                className={
                                  row.sheet.sheetId === activeSheet?.sheetId
                                    ? 'table-row--active'
                                    : undefined
                                }
                                key={row.sheet.sheetId}
                                onClick={() => setActiveSheetId(row.sheet.sheetId)}
                              >
                                <td>#{row.sheet.sheetNumber}</td>
                                <td>{row.selectedGroupCount}</td>
                                <td>{row.otherGroupCount}</td>
                                <td>{row.sheet.utilizationPercent.toFixed(1)}%</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <div className="empty-state">
                        <strong>No placed sheets for this group yet</strong>
                        <span>
                          Unplaced items still appear on the Unplaced tab when a grouped run cannot
                          produce layout for this selection.
                        </span>
                      </div>
                    )}
                  </div>
                ) : null}
              </div>
            ) : null}

            {activeWorkspaceTab === 'sheet-detail' ? (
              <div className="results-tab-panel" role="tabpanel">
                <p className="eyebrow">Sheet detail</p>
                <h3>
                  {activeMaterialResult
                    ? `${activeMaterialResult.materialName} sheet-by-sheet`
                    : 'Sheet-by-sheet'}
                </h3>

                {activeMaterialResult && activeMaterialResult.response.sheets.length > 0 ? (
                  <>
                    <label className="field">
                      <span>Active sheet</span>
                      <select
                        className="results-sheet-selector"
                        onChange={(event) => setActiveSheetId(event.target.value)}
                        value={activeSheet?.sheetId ?? ''}
                      >
                        {activeMaterialResult.response.sheets.map((sheet) => (
                          <option key={sheet.sheetId} value={sheet.sheetId}>
                            Sheet #{sheet.sheetNumber} — {sheet.utilizationPercent.toFixed(1)}% utilized
                          </option>
                        ))}
                      </select>
                    </label>

                    <div className="table-shell">
                      <table>
                        <thead>
                          <tr>
                            <th>Sheet</th>
                            <th>Size</th>
                            <th>Utilization</th>
                            <th>Placements</th>
                          </tr>
                        </thead>
                        <tbody>
                          {activeMaterialResult.response.sheets.map((sheet) => (
                            <tr
                              className={
                                sheet.sheetId === activeSheet?.sheetId
                                  ? 'table-row--active'
                                  : undefined
                              }
                              key={sheet.sheetId}
                              onClick={() => setActiveSheetId(sheet.sheetId)}
                            >
                              <td>#{sheet.sheetNumber}</td>
                              <td>
                                {sheet.sheetLength}" × {sheet.sheetWidth}"
                              </td>
                              <td>{sheet.utilizationPercent.toFixed(1)}%</td>
                              <td>
                                {
                                  activeMaterialResult.response.placements.filter(
                                    (placement) => placement.sheetId === sheet.sheetId,
                                  ).length
                                }
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </>
                ) : (
                  <div className="empty-state">
                    <strong>
                      {hasEmptyResult
                        ? 'No sheets were produced for this run'
                        : 'No sheets for this material'}
                    </strong>
                    <span>
                      {hasEmptyResult
                        ? 'The viewer stays in an intentional empty state until a run generates at least one sheet.'
                        : 'Partial runs still keep unplaced items visible in the workspace.'}
                    </span>
                  </div>
                )}
              </div>
            ) : null}

            {activeWorkspaceTab === 'placement-inspection' ? (
              <div className="results-tab-panel" role="tabpanel">
                <p className="eyebrow">Placement inspection</p>
                <h3>Coordinates and click-to-inspect detail</h3>
                {selectedPlacement ? (
                  <div className="selection-summary">
                    <span>Selected part</span>
                    <strong>{selectedPlacement.partId}</strong>
                    <small>
                      {selectedPlacement.width}" × {selectedPlacement.height}" at (
                      {selectedPlacement.x}", {selectedPlacement.y}") ·{' '}
                      {selectedPlacement.rotated90 ? 'Rotated 90°' : 'Not rotated'} ·{' '}
                      {selectedPlacement.displayGroup}
                    </small>
                  </div>
                ) : (
                  <p className="section-note">
                    {hasEmptyResult
                      ? 'No placements were created in this run yet.'
                      : 'Click a placement in the viewer or table to pin its dimensions and origin here.'}
                  </p>
                )}

                {activeSheetPlacements.length > 0 ? (
                  <div className="table-shell">
                    <table>
                      <thead>
                          <tr>
                            <th>Part</th>
                            <th>Group</th>
                            <th>Origin</th>
                            <th>Size</th>
                            <th>Rotated</th>
                        </tr>
                      </thead>
                      <tbody>
                        {activeSheetPlacements.map((placement) => (
                          <tr
                            className={
                              placement.placementId === selectedPlacementId
                                ? 'table-row--active'
                                : undefined
                            }
                            key={placement.placementId}
                            onClick={() =>
                              setSelectedPlacementId((current) =>
                                current === placement.placementId
                                  ? undefined
                                  : placement.placementId,
                              )
                            }
                          >
                            <td>{placement.partId}</td>
                            <td>{placement.displayGroup}</td>
                            <td>
                              ({placement.x}", {placement.y}")
                            </td>
                            <td>
                              {placement.width}" × {placement.height}"
                            </td>
                            <td>{placement.rotated90 ? 'Yes' : 'No'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  <div className="empty-state">
                    <strong>No placements on the selected sheet</strong>
                    <span>
                      {activeSheet
                        ? 'Successful layouts populate the table and the viewer together.'
                        : 'Generate a sheet layout to inspect placed parts here.'}
                    </span>
                  </div>
                )}
              </div>
            ) : null}

            {activeWorkspaceTab === 'unplaced' ? (
              <div className="results-tab-panel" role="tabpanel">
                <p className="eyebrow">Unplaced</p>
                <h3>Failure reasons stay visible</h3>
                {activeMaterialResult && activeMaterialResult.response.unplacedItems.length > 0 ? (
                  <ul className="feature-list">
                    {activeMaterialResult.response.unplacedItems.map((item) => (
                      <li key={`${item.partId}-${item.reasonCode}-${item.reasonDescription}`}>
                        <strong>
                          {itemLabel(
                            item.partId ||
                              (item.reasonCode === 'empty-run' ? 'Nesting run' : 'Run'),
                          )}
                        </strong>{' '}
                        ({item.reasonCode}): {item.reasonDescription}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="empty-state">
                    <strong>No unplaced items for this material</strong>
                    <span>
                      Oversized parts, invalid input, and space failures surface here when they occur.
                    </span>
                  </div>
                )}
              </div>
            ) : null}
          </div>
        </section>

        <div
          aria-label="Resize results workspace"
          aria-orientation="vertical"
          aria-valuemin={minWorkspaceWidth}
          aria-valuenow={Math.round(workspaceWidth)}
          className="results-splitter"
          onPointerDown={(event) => {
            if (!event.isPrimary || event.button !== 0) {
              return;
            }
            event.preventDefault();
            setIsResizingWorkspace(true);
          }}
          role="separator"
          tabIndex={-1}
          title="Drag to resize the workspace and viewer columns"
        />

        <div
          aria-label="Current sheet viewer"
          className="results-viewer-column"
          data-active-sheet-id={activeSheet?.sheetId}
        >
          {viewerPanel}
        </div>
      </div>
    </div>
  );
}
