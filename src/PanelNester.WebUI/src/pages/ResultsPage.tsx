import {
  Suspense,
  lazy,
  useDeferredValue,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { StatusPill } from '../components/StatusPill';
import { StockItemViewer } from '../components/StockItemViewer';
import { ThemedSelect } from '../components/ThemedSelect';
import {
  buildBatchSheets,
  buildPanelSearchIndex,
  buildPanelSearchResults,
  compareLabels,
  decoratePlacements,
  sheetLookupKey,
} from './resultsBatchSheetSearch';
import { getResultsOptimizationGroups, getSheetDisplayId } from './resultsPresentation';
import type {
  BatchNestResponse,
  Material,
  NestResponse,
  OptimizationGroup,
  ProjectKind,
  ProjectMaterialSnapshot,
  ReportSettings,
  StiffenerTakeoffReportData,
  StiffenerTakeoffSettings,
  UnplacedItem,
} from '../types/contracts';

interface ResultsPageProps {
  projectKind?: ProjectKind;
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  material?: Material;
  selectedMaterialId?: string;
  companyLogoPath?: string | null;
  kerfWidth: number;
  nestResponse: NestResponse;
  batchNestResponse: BatchNestResponse;
  statusMessage: string;
  savedMaterialSnapshots: ProjectMaterialSnapshot[];
  pendingMaterialSnapshots: ProjectMaterialSnapshot[];
  projectDirty: boolean;
  reportSettings: ReportSettings;
  reportMessage: string;
  reportBusy: boolean;
  showStiffenerControls: boolean;
  stiffenerTakeoffEnabled: boolean;
  stiffenerTakeoffSettings: StiffenerTakeoffSettings;
  stiffenerTakeoffReport: StiffenerTakeoffReportData | null;
  stiffenerMessage: string;
  stiffenerBusy: boolean;
  canSyncReportSettings: boolean;
  canExportReport: boolean;
  canExportExcelReport: boolean;
  canPreviewStiffenerTakeoff: boolean;
  canExportStiffenerReport: boolean;
  onReportSettingsChange: (field: keyof ReportSettings, value: string) => void;
  onStiffenerTakeoffChange: (settings: StiffenerTakeoffSettings) => void;
  onPickCompanyLogo: () => Promise<string | undefined>;
  onSaveDesktopAppSettings: (settings: {
    companyLogoPath?: string | null;
    companyName?: string | null;
  }) => Promise<boolean>;
  onExportReport: (overrides?: ReportExportOverrides) => Promise<void>;
  onExportExcelReport: (overrides?: ReportExportOverrides) => Promise<void>;
  onExportStiffenerReport: (
    overrides?: StiffenerExportOverrides,
  ) => Promise<void>;
  onSelectOptimizationGroup: (optimizationGroupId: string) => void;
}

interface StockLengthResultsProps {
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  onSelectOptimizationGroup: (optimizationGroupId: string) => void;
}

function formatCutPlanStatus(status: string): string {
  return status.length > 0 ? `${status[0].toUpperCase()}${status.slice(1)}` : status;
}

export function StockLengthResults({
  optimizationGroups,
  activeOptimizationGroupId,
  onSelectOptimizationGroup,
}: StockLengthResultsProps) {
  const orderedGroups = [...optimizationGroups]
    .sort((left, right) => left.order - right.order);
  const activeGroup = orderedGroups.find(
    (group) => group.optimizationGroupId === activeOptimizationGroupId,
  ) ?? orderedGroups[0];
  const result = activeGroup?.resultStatus === 'valid'
    ? activeGroup.lastStockLengthOptimizationResult
    : null;
  const stockItems = result?.cutPlans.flatMap((plan) => [...plan.stockItems]
    .sort((left, right) => left.stockItemNumber - right.stockItemNumber)
    .map((item) => ({ plan, item }))) ?? [];
  const unplaced = result?.cutPlans.flatMap((plan) => plan.unplacedPieceInstances) ?? [];
  const [selectedStockItemKey, setSelectedStockItemKey] = useState<string>();
  const [selectedPieceInstanceId, setSelectedPieceInstanceId] = useState<string>();
  const stockItemEntries = stockItems.map(({ plan, item }) => ({
    plan,
    item,
    key: `${plan.cutPlanId}\u0000${item.stockItemId}`,
  }));
  const selectedStockItem = stockItemEntries.find(
    ({ key }) => key === selectedStockItemKey,
  ) ?? stockItemEntries[0];

  useEffect(() => {
    setSelectedStockItemKey((current) => (
      stockItemEntries.some(({ key }) => key === current)
        ? current
        : stockItemEntries[0]?.key
    ));
  }, [result]);

  useEffect(() => {
    setSelectedPieceInstanceId((current) => (
      selectedStockItem?.item.cutSequence.some(
        (piece) => piece.pieceInstanceId === current,
      )
        ? current
        : undefined
    ));
  }, [selectedStockItem?.key]);

  return (
    <div className="results-explorer stock-length-results">
      <header className="page-header">
        <div>
          <p className="eyebrow">Stock-Length Results</p>
          <h1>{activeGroup?.name ?? 'Selected Optimization Group'} Cut Plan</h1>
          <p>{result?.description ?? (activeGroup?.requiredPieces.length ? 'Needs Generation' : 'Empty Optimization Group')}</p>
        </div>
        <label className="project-field">
          <span>Optimization Group</span>
          <select aria-label="Select Optimization Group results" onChange={(event) => onSelectOptimizationGroup(event.target.value)} value={activeGroup?.optimizationGroupId ?? ''}>
            {orderedGroups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.order + 1}. {group.name}</option>)}
          </select>
        </label>
      </header>
      {result ? (
        <>
          <section className="project-card">
            <div className="project-card__header"><h2>Stock Items</h2><StatusPill label={formatCutPlanStatus(result.status)} tone={result.status === 'complete' ? 'ok' : result.status === 'partial' ? 'warn' : 'error'} /></div>
            <div className="table-wrap"><table><thead><tr><th>Stock Item</th><th>Profile Number</th><th>Finish</th><th>Stock Length</th><th>Piece Length</th><th>Saw Loss</th><th>Remainder</th><th>Utilization</th><th>Status</th></tr></thead><tbody>
              {stockItemEntries.map(({ plan, item, key }) => <tr aria-label={`Stock Item ${item.stockItemNumber}`} aria-selected={selectedStockItem?.key === key} key={key} onClick={() => setSelectedStockItemKey(key)} tabIndex={0}><td>{item.stockItemNumber}</td><td>{plan.stockGroup.profileNumber}</td><td>{plan.stockGroup.finish || 'No finish specified'}</td><td>{item.stockLength} in</td><td>{item.pieceLength} in</td><td>{item.sawLoss} in</td><td>{item.remainder} in</td><td>{item.utilizationPercent.toFixed(1)}%</td><td>{formatCutPlanStatus(plan.status)}</td></tr>)}
            </tbody></table></div>
          </section>
          {selectedStockItem ? (
            <StockItemViewer
              finish={selectedStockItem.plan.stockGroup.finish}
              onSelectPieceInstance={setSelectedPieceInstanceId}
              pieceInstances={selectedStockItem.item.cutSequence}
              profileNumber={selectedStockItem.plan.stockGroup.profileNumber}
              selectedPieceInstanceId={selectedPieceInstanceId}
              stockItem={selectedStockItem.item}
            />
          ) : null}
          <section className="project-card"><div className="project-card__header"><h2>Unplaced</h2></div>
            {unplaced.length > 0 ? <div className="table-wrap"><table><thead><tr><th>Piece Instance</th><th>Length</th><th>Reason</th></tr></thead><tbody>{unplaced.map((item) => <tr key={item.pieceInstance.pieceInstanceId}><td>{item.pieceInstance.pieceInstanceId}</td><td>{item.pieceInstance.length} in</td><td>{item.reasonDescription}</td></tr>)}</tbody></table></div> : <p className="section-note">Every Piece Instance was placed.</p>}
          </section>
        </>
      ) : <div className="empty-state"><strong>{activeGroup?.requiredPieces.length ? 'Needs Generation' : 'Empty Optimization Group'}</strong><span>{activeGroup?.requiredPieces.length ? 'Generate Selected to create a deterministic heuristic Cut Plan.' : 'Add Required Pieces before generating.'}</span></div>}
    </div>
  );
}

interface MaterialResultView {
  key: string;
  materialName: string;
  materialId?: string;
  response: NestResponse;
}

interface ReportDraft {
  companyLogoPath: string;
  companyName: string;
  reportTitle: string;
  projectJobName: string;
  projectJobNumber: string;
  releaseId: string;
  status: string;
  reportDate: string;
  notes: string;
}

interface StiffenerExportDraft extends ReportDraft {
  stiffenerReportTitle: string;
  extrusion: string;
  stiffenerReleaseId: string;
  poNumber: string;
  color: string;
  colorNumber: string;
  manufacturer: string;
  stiffenerStatus: string;
}

interface ReportExportOverrides {
  companyLogoPath?: string | null;
  reportSettings?: ReportSettings;
}

interface StiffenerExportOverrides {
  companyLogoPath?: string | null;
  reportSettings: ReportSettings;
  stiffenerTakeoff: StiffenerTakeoffSettings;
}

interface SplitButtonProps {
  label: string;
  busyLabel: string;
  busy: boolean;
  disabled: boolean;
  tone?: 'primary' | 'secondary';
  menuOpen: boolean;
  onToggleMenu: () => void;
  onPrimaryAction: () => void;
  onOpenOverrides: () => void;
}

interface UnplacedRow extends UnplacedItem {
  materialKey: string;
  materialName: string;
}

type ResultsDrawerTab = 'unplaced' | 'stiffeners';

const SheetViewer = lazy(async () => {
  const module = await import('../components/SheetViewer');
  return { default: module.SheetViewer };
});

function createLegacyMaterialResult(
  material: Material | undefined,
  nestResponse: NestResponse,
): MaterialResultView[] {
  if (nestResponse.sheets.length === 0 && nestResponse.unplacedItems.length === 0) {
    return [];
  }

  const materialName =
    material?.name ?? nestResponse.sheets[0]?.materialName ?? 'Imported material';

  return [
    {
      key: material?.materialId ?? materialName,
      materialId: material?.materialId,
      materialName,
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
      materialId: result.materialId ?? undefined,
      materialName: result.materialName,
      response: result.result,
    }));
  }

  return createLegacyMaterialResult(material, nestResponse);
}

function createReportDraft(reportSettings: ReportSettings): ReportDraft {
  return {
    companyLogoPath: '',
    companyName: reportSettings.companyName ?? '',
    reportTitle: reportSettings.reportTitle ?? '',
    projectJobName: reportSettings.projectJobName ?? '',
    projectJobNumber: reportSettings.projectJobNumber ?? '',
    releaseId: reportSettings.releaseId ?? '',
    status: reportSettings.status ?? '',
    reportDate: reportSettings.reportDate ?? '',
    notes: reportSettings.notes ?? '',
  };
}

function createStiffenerDraft(
  companyLogoPath: string | null | undefined,
  reportSettings: ReportSettings,
  settings: StiffenerTakeoffSettings,
): StiffenerExportDraft {
  return {
    ...createReportDraft(reportSettings),
    companyLogoPath: companyLogoPath ?? '',
    stiffenerReportTitle: settings.reportTitle ?? '',
    extrusion: settings.extrusion ?? '',
    stiffenerReleaseId: settings.releaseId ?? '',
    poNumber: settings.poNumber ?? '',
    color: settings.color ?? '',
    colorNumber: settings.colorNumber ?? '',
    manufacturer: settings.manufacturer ?? '',
    stiffenerStatus: settings.status ?? '',
  };
}

function toReportSettings(draft: ReportDraft): ReportSettings {
  return {
    companyName: draft.companyName,
    reportTitle: draft.reportTitle,
    projectJobName: draft.projectJobName,
    projectJobNumber: draft.projectJobNumber,
    releaseId: draft.releaseId,
    status: draft.status,
    reportDate: draft.reportDate,
    notes: draft.notes,
  };
}

function fileNameFromPath(value: string): string {
  const parts = value.split(/[\\/]/);
  return parts[parts.length - 1] ?? value;
}

function SplitButton({
  label,
  busyLabel,
  busy,
  disabled,
  tone = 'secondary',
  menuOpen,
  onToggleMenu,
  onPrimaryAction,
  onOpenOverrides,
}: SplitButtonProps) {
  const primaryClassName =
    tone === 'primary'
      ? 'primary-button module-action-button module-action-button--primary'
      : 'secondary-button module-action-button';
  const toggleClassName =
    tone === 'primary'
      ? 'primary-button module-split-button__toggle'
      : 'secondary-button module-split-button__toggle';

  return (
    <div className="module-split-button">
      <button
        className={primaryClassName}
        disabled={disabled}
        onClick={onPrimaryAction}
        type="button"
      >
        {busy ? busyLabel : label}
      </button>
      <button
        aria-expanded={menuOpen}
        className={toggleClassName}
        disabled={disabled}
        onClick={onToggleMenu}
        type="button"
      >
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {menuOpen ? (
        <div className="module-split-button__menu">
          <button
            className="module-split-button__menu-item"
            onClick={onOpenOverrides}
            type="button"
          >
            Override parameters
          </button>
        </div>
      ) : null}
    </div>
  );
}

interface LogoFieldProps {
  value: string;
  disabled?: boolean;
  onChoose: () => Promise<void>;
  onClear: () => void;
}

function LogoField({ value, disabled, onChoose, onClear }: LogoFieldProps) {
  return (
    <label className="field field--wide">
      <span>Company logo</span>
      <div className="results-logo-field">
        <input
          disabled
          placeholder="No logo selected"
          type="text"
          value={value ? fileNameFromPath(value) : ''}
        />
        <div className="results-logo-field__actions">
          <button
            className="secondary-button"
            disabled={disabled}
            onClick={() => void onChoose()}
            type="button"
          >
            Choose
          </button>
          <button
            className="secondary-button"
            disabled={disabled || value.length === 0}
            onClick={onClear}
            type="button"
          >
            Clear
          </button>
        </div>
      </div>
      <small>Global app setting shared across report exports.</small>
    </label>
  );
}

function formatDimension(value: number): string {
  return Number.isInteger(value)
    ? `${value}`
    : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

function formatArea(value: number): string {
  return value.toLocaleString(undefined, {
    maximumFractionDigits: value >= 100 ? 0 : 2,
  });
}

function itemLabel(partId: string): string {
  return partId.trim().length > 0 ? partId : 'Run';
}

export function ResultsPage({
  projectKind,
  optimizationGroups,
  activeOptimizationGroupId,
  material,
  selectedMaterialId,
  companyLogoPath,
  kerfWidth,
  nestResponse,
  batchNestResponse,
  statusMessage,
  projectDirty,
  reportSettings,
  reportMessage,
  reportBusy,
  showStiffenerControls,
  stiffenerTakeoffEnabled,
  stiffenerTakeoffSettings,
  stiffenerTakeoffReport,
  stiffenerMessage,
  stiffenerBusy,
  canSyncReportSettings,
  canExportReport,
  canExportExcelReport,
  canPreviewStiffenerTakeoff,
  canExportStiffenerReport,
  onReportSettingsChange,
  onStiffenerTakeoffChange,
  onPickCompanyLogo,
  onSaveDesktopAppSettings,
  onExportReport,
  onExportExcelReport,
  onExportStiffenerReport,
  onSelectOptimizationGroup,
}: ResultsPageProps) {
  const orderedOptimizationGroups = useMemo(
    () => getResultsOptimizationGroups(optimizationGroups),
    [optimizationGroups],
  );
  const activeOptimizationGroup =
    orderedOptimizationGroups.find(
      (group) => group.optimizationGroupId === activeOptimizationGroupId,
    ) ?? orderedOptimizationGroups[0];
  const activeOptimizationGroupFailure =
    activeOptimizationGroup?.lastBatchNestingResult?.optimizationGroupResults?.find(
      (result) =>
        result.optimizationGroupId === activeOptimizationGroup.optimizationGroupId,
    )?.failureMessage;
  const completedOptimizationGroups = orderedOptimizationGroups.filter(
    (group) =>
      group.resultStatus === 'valid' &&
      (group.lastBatchNestingResult || group.lastNestingResult),
  );
  const projectSheetCount = completedOptimizationGroups.reduce((total, group) => {
    const batchSheets = group.lastBatchNestingResult?.materialResults.reduce(
      (groupTotal, result) => groupTotal + result.result.summary.totalSheets,
      0,
    );
    return total + (batchSheets ?? group.lastNestingResult?.summary.totalSheets ?? 0);
  }, 0);
  const materialResults = useMemo(
    () => buildMaterialResults(batchNestResponse, material, nestResponse),
    [batchNestResponse, material, nestResponse],
  );
  const batchSheets = useMemo(() => buildBatchSheets(materialResults), [materialResults]);
  const panelSearchIndex = useMemo(
    () => buildPanelSearchIndex(batchSheets),
    [batchSheets],
  );
  const [materialFilterKey, setMaterialFilterKey] = useState('all');
  const [panelSearchQuery, setPanelSearchQuery] = useState('');
  const deferredPanelSearchQuery = useDeferredValue(panelSearchQuery.trim());
  const [activeSheetKey, setActiveSheetKey] = useState<string>();
  const [selectedPlacementId, setSelectedPlacementId] = useState<string>();
  const [drawerTab, setDrawerTab] = useState<ResultsDrawerTab | null>(null);
  const [reportDialogOpen, setReportDialogOpen] = useState(false);
  const [stiffenerDialogOpen, setStiffenerDialogOpen] = useState(false);
  const [openMenu, setOpenMenu] = useState<'report' | 'stiffener' | null>(null);

  useEffect(() => {
    if (!showStiffenerControls) {
      setDrawerTab((current) => (current === 'stiffeners' ? null : current));
      setStiffenerDialogOpen(false);
      setOpenMenu((current) => (current === 'stiffener' ? null : current));
    }
  }, [showStiffenerControls]);
  const [reportDraft, setReportDraft] = useState<ReportDraft>(() =>
    {
      const draft = createReportDraft(reportSettings);
      return {
        ...draft,
        companyLogoPath: companyLogoPath ?? '',
      };
    },
  );
  const [stiffenerDraft, setStiffenerDraft] = useState<StiffenerExportDraft>(() =>
    createStiffenerDraft(companyLogoPath, reportSettings, stiffenerTakeoffSettings),
  );
  const materialFilterOptions = useMemo(
    () => [
      { value: 'all', label: 'All materials' },
      ...materialResults.map((result) => ({
        value: result.key,
        label: result.materialName,
      })),
    ],
    [materialResults],
  );
  const panelSearchResults = useMemo(
    () => buildPanelSearchResults(panelSearchIndex, deferredPanelSearchQuery),
    [deferredPanelSearchQuery, panelSearchIndex],
  );

  useEffect(() => {
    const preferredMaterial =
      materialResults.find(
        (result) =>
          (selectedMaterialId && result.materialId === selectedMaterialId) ||
          (material?.materialId && result.materialId === material.materialId) ||
          result.materialName === material?.name,
      ) ?? materialResults[0];

    setMaterialFilterKey((current) => {
      if (current === 'all') {
        return preferredMaterial?.key ?? 'all';
      }

      return materialResults.some((result) => result.key === current)
        ? current
        : preferredMaterial?.key ?? 'all';
    });
  }, [material, materialResults, selectedMaterialId]);

  useEffect(() => {
    if (!reportDialogOpen) {
      return;
    }

    const draft = createReportDraft(reportSettings);
    setReportDraft({
      ...draft,
      companyLogoPath: companyLogoPath ?? '',
    });
  }, [companyLogoPath, reportDialogOpen, reportSettings]);

  useEffect(() => {
    if (!stiffenerDialogOpen) {
      return;
    }

    setStiffenerDraft(
      createStiffenerDraft(companyLogoPath, reportSettings, stiffenerTakeoffSettings),
    );
  }, [companyLogoPath, reportSettings, stiffenerDialogOpen, stiffenerTakeoffSettings]);

  useEffect(() => {
    if (!openMenu) {
      return;
    }

    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target;
      if (!(target instanceof Element) || !target.closest('.module-split-button')) {
        setOpenMenu(null);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [openMenu]);

  const filteredSheets = useMemo(() => {
    const materialScopedSheets = batchSheets.filter(
      (sheet) => materialFilterKey === 'all' || sheet.materialKey === materialFilterKey,
    );

    if (deferredPanelSearchQuery.length === 0) {
      return [...materialScopedSheets].sort(
        (left, right) =>
          compareLabels(left.materialName, right.materialName) ||
          left.sheet.sheetNumber - right.sheet.sheetNumber,
      );
    }

    return materialScopedSheets
      .filter((sheet) =>
        panelSearchResults.firstMatchBySheet.has(
          sheetLookupKey(sheet.materialKey, sheet.sheet.sheetId),
        ),
      )
      .sort(
        (left, right) =>
          compareLabels(left.materialName, right.materialName) ||
          left.sheet.sheetNumber - right.sheet.sheetNumber,
      );
  }, [
    batchSheets,
    deferredPanelSearchQuery,
    materialFilterKey,
    panelSearchResults.firstMatchBySheet,
  ]);

  useEffect(() => {
    const firstSheetKey = filteredSheets[0]
      ? sheetLookupKey(filteredSheets[0].materialKey, filteredSheets[0].sheet.sheetId)
      : undefined;

    setActiveSheetKey((current) =>
      current &&
      filteredSheets.some(
        (sheet) => sheetLookupKey(sheet.materialKey, sheet.sheet.sheetId) === current,
      )
        ? current
        : firstSheetKey,
    );
  }, [filteredSheets]);

  const activeSheetView =
    filteredSheets.find(
      (sheet) => sheetLookupKey(sheet.materialKey, sheet.sheet.sheetId) === activeSheetKey,
    ) ?? filteredSheets[0];
  const activeMaterialResult =
    materialResults.find((result) => result.key === activeSheetView?.materialKey) ??
    materialResults.find((result) => result.key === materialFilterKey) ??
    materialResults[0];
  const activeMaterialPlacements = useMemo(
    () => decoratePlacements(activeMaterialResult?.response.placements ?? []),
    [activeMaterialResult],
  );
  const activeSheetPlacements = useMemo(
    () =>
      activeMaterialPlacements.filter(
        (placement) => placement.sheetId === activeSheetView?.sheet.sheetId,
      ),
    [activeMaterialPlacements, activeSheetView?.sheet.sheetId],
  );

  useEffect(() => {
    setSelectedPlacementId((current) =>
      current &&
      activeSheetPlacements.some((placement) => placement.placementId === current)
        ? current
        : undefined,
    );
  }, [activeSheetPlacements]);

  const selectedPlacement = activeSheetPlacements.find(
    (placement) => placement.placementId === selectedPlacementId,
  );
  const sheetSearchMatches =
    activeSheetView && deferredPanelSearchQuery.length > 0
      ? panelSearchResults.sheetCounts.get(
          sheetLookupKey(activeSheetView.materialKey, activeSheetView.sheet.sheetId),
        ) ?? 0
      : 0;
  const totalPlacedArea = activeSheetPlacements.reduce(
    (sum, placement) => sum + placement.width * placement.height,
    0,
  );
  const sheetArea = activeSheetView
    ? activeSheetView.sheet.sheetLength * activeSheetView.sheet.sheetWidth
    : 0;
  const scrapArea = Math.max(sheetArea - totalPlacedArea, 0);
  const unplacedRows = useMemo<UnplacedRow[]>(
    () =>
      materialResults
        .filter((result) => materialFilterKey === 'all' || result.key === materialFilterKey)
        .flatMap((result) =>
          result.response.unplacedItems.map((item) => ({
            ...item,
            materialKey: result.key,
            materialName: result.materialName,
          })),
        )
        .filter((item) =>
          deferredPanelSearchQuery.length === 0
            ? true
            : item.partId.toLowerCase().includes(deferredPanelSearchQuery.toLowerCase()),
        ),
    [deferredPanelSearchQuery, materialFilterKey, materialResults],
  );
  const hasOutput =
    materialResults.length > 0 ||
    nestResponse.sheets.length > 0 ||
    nestResponse.unplacedItems.length > 0;
  const stiffenerSummary = stiffenerTakeoffReport?.overallSummary;
  const activeStiffenerGroup = stiffenerTakeoffReport?.optimizationGroups.find(
    (group) => group.optimizationGroupId === activeOptimizationGroup?.optimizationGroupId,
  );
  const stiffenerNote = !stiffenerTakeoffEnabled
    ? 'Enable stiffener takeoff in Project settings to populate this tab.'
    : !canPreviewStiffenerTakeoff
      ? 'The connected desktop host has not exposed stiffener preview yet.'
      : !canExportStiffenerReport
        ? 'Preview is available, but standalone stiffener PDF export is not exposed in this host.'
        : 'Preview and export both use the current project stiffener settings and ready imported rows.';

  const applyReportDraft = () => {
    const nextValues = [
      ['companyName', reportDraft.companyName],
      ['reportTitle', reportDraft.reportTitle],
      ['projectJobName', reportDraft.projectJobName],
      ['projectJobNumber', reportDraft.projectJobNumber],
      ['releaseId', reportDraft.releaseId],
      ['status', reportDraft.status],
      ['reportDate', reportDraft.reportDate],
      ['notes', reportDraft.notes],
    ] as const;

    nextValues.forEach(([field, value]) => {
      if ((reportSettings[field] ?? '') !== value) {
        onReportSettingsChange(field, value);
      }
    });

    void (async () => {
      const saved = await onSaveDesktopAppSettings({
        companyLogoPath:
          reportDraft.companyLogoPath.trim().length > 0
            ? reportDraft.companyLogoPath.trim()
            : null,
        companyName: reportDraft.companyName.trim() || null,
      });
      if (saved) {
        setReportDialogOpen(false);
      }
    })();
  };

  const exportReportFromDialog = async () => {
    await onExportReport({
      companyLogoPath:
        reportDraft.companyLogoPath.trim().length > 0
          ? reportDraft.companyLogoPath.trim()
          : null,
      reportSettings: {
        companyName: reportDraft.companyName,
        reportTitle: reportDraft.reportTitle,
        projectJobName: reportDraft.projectJobName,
        projectJobNumber: reportDraft.projectJobNumber,
        releaseId: reportDraft.releaseId,
        status: reportDraft.status,
        reportDate: reportDraft.reportDate,
        notes: reportDraft.notes,
      },
    });
    setReportDialogOpen(false);
  };

  const syncReportDraft = (draft: ReportDraft) => {
    const nextValues = [
      ['companyName', draft.companyName],
      ['reportTitle', draft.reportTitle],
      ['projectJobName', draft.projectJobName],
      ['projectJobNumber', draft.projectJobNumber],
      ['releaseId', draft.releaseId],
      ['status', draft.status],
      ['reportDate', draft.reportDate],
      ['notes', draft.notes],
    ] as const;

    nextValues.forEach(([field, value]) => {
      if ((reportSettings[field] ?? '') !== value) {
        onReportSettingsChange(field, value);
      }
    });
  };

  const saveStiffenerDraft = () => {
    syncReportDraft(stiffenerDraft);
    if (
      (stiffenerTakeoffSettings.reportTitle ?? '') !== stiffenerDraft.stiffenerReportTitle ||
      (stiffenerTakeoffSettings.extrusion ?? '') !== stiffenerDraft.extrusion ||
      (stiffenerTakeoffSettings.releaseId ?? '') !== stiffenerDraft.stiffenerReleaseId ||
      (stiffenerTakeoffSettings.poNumber ?? '') !== stiffenerDraft.poNumber ||
      (stiffenerTakeoffSettings.color ?? '') !== stiffenerDraft.color ||
      (stiffenerTakeoffSettings.colorNumber ?? '') !== stiffenerDraft.colorNumber ||
      (stiffenerTakeoffSettings.manufacturer ?? '') !== stiffenerDraft.manufacturer ||
      (stiffenerTakeoffSettings.status ?? '') !== stiffenerDraft.stiffenerStatus
    ) {
      onStiffenerTakeoffChange({
        ...stiffenerTakeoffSettings,
        reportTitle: stiffenerDraft.stiffenerReportTitle,
        extrusion: stiffenerDraft.extrusion,
        releaseId: stiffenerDraft.stiffenerReleaseId,
        poNumber: stiffenerDraft.poNumber,
        color: stiffenerDraft.color,
        colorNumber: stiffenerDraft.colorNumber,
        manufacturer: stiffenerDraft.manufacturer,
        status: stiffenerDraft.stiffenerStatus,
      });
    }

    void (async () => {
      const saved = await onSaveDesktopAppSettings({
        companyLogoPath:
          stiffenerDraft.companyLogoPath.trim().length > 0
            ? stiffenerDraft.companyLogoPath.trim()
            : null,
        companyName: stiffenerDraft.companyName.trim() || null,
      });
      if (saved) {
        setStiffenerDialogOpen(false);
      }
    })();
  };

  const exportStiffenerFromDialog = async () => {
    await onExportStiffenerReport({
      companyLogoPath:
        stiffenerDraft.companyLogoPath.trim().length > 0
          ? stiffenerDraft.companyLogoPath.trim()
          : null,
      reportSettings: toReportSettings(stiffenerDraft),
      stiffenerTakeoff: {
        ...stiffenerTakeoffSettings,
        reportTitle: stiffenerDraft.stiffenerReportTitle,
        extrusion: stiffenerDraft.extrusion,
        releaseId: stiffenerDraft.stiffenerReleaseId,
        poNumber: stiffenerDraft.poNumber,
        color: stiffenerDraft.color,
        colorNumber: stiffenerDraft.colorNumber,
        manufacturer: stiffenerDraft.manufacturer,
        status: stiffenerDraft.stiffenerStatus,
      },
    });
    setStiffenerDialogOpen(false);
  };

  const sheetHeaderLabel = activeSheetView
    ? `${getSheetDisplayId(activeSheetView.sheet)} | ${activeSheetView.materialName}`
    : 'No sheet selected';
  const sheetSummaryLabel = activeSheetView
    ? `${formatDimension(activeSheetView.sheet.sheetLength)} x ${formatDimension(activeSheetView.sheet.sheetWidth)} in`
    : 'Waiting for a nesting result';

  if (projectKind === 'stockLength') {
    return <StockLengthResults optimizationGroups={optimizationGroups} activeOptimizationGroupId={activeOptimizationGroupId} onSelectOptimizationGroup={onSelectOptimizationGroup} />;
  }

  return (
    <div className="results-explorer">
      <div className="results-explorer__layout">
        <aside className="results-sidebar">
          <div className="results-sidebar__header">
            <p className="eyebrow">Batch Explorer</p>
            <h2>{activeOptimizationGroup?.name ?? 'Explore sheets'}</h2>
            <p className="section-note">{statusMessage}</p>
            {activeOptimizationGroup?.resultStatus === 'stale' ? (
              <p className="section-note">
                Stored panels for {activeOptimizationGroup.name} are stale and hidden.
                Re-run this Optimization Group to inspect current results.
              </p>
            ) : null}
            {activeOptimizationGroup?.resultStatus !== 'stale' &&
            activeOptimizationGroupFailure ? (
              <p className="section-note">
                {activeOptimizationGroup.name} failed: {activeOptimizationGroupFailure}
              </p>
            ) : null}
            <p className="section-note">
              Project summary: {completedOptimizationGroups.length} of{' '}
              {orderedOptimizationGroups.length} Optimization Group(s) have results with{' '}
              {projectSheetCount} isolated sheet(s). Panels are never shared between groups.
            </p>
          </div>

          <div className="results-sidebar__filters">
            <label className="field">
              <span>Optimization Group</span>
              <ThemedSelect
                ariaLabel="Select Optimization Group results"
                disabled={orderedOptimizationGroups.length === 0}
                onChange={onSelectOptimizationGroup}
                options={orderedOptimizationGroups.map((group) => ({
                  value: group.optimizationGroupId,
                  label: `${group.order + 1}. ${group.name}`,
                }))}
                value={activeOptimizationGroup?.optimizationGroupId ?? ''}
              />
            </label>
            <label className="field">
              <span>Filter by material</span>
              <ThemedSelect
                ariaLabel="Filter results by material"
                disabled={materialResults.length === 0}
                onChange={setMaterialFilterKey}
                options={materialFilterOptions}
                value={materialFilterKey}
              />
            </label>

            <label className="module-search results-sidebar__search">
              <svg aria-hidden="true" viewBox="0 0 24 24">
                <circle cx="11" cy="11" r="5.5" />
                <path d="m15.5 15.5 3 3" />
              </svg>
              <input
                onChange={(event) => setPanelSearchQuery(event.target.value)}
                placeholder="Find panel ID..."
                type="search"
                value={panelSearchQuery}
              />
            </label>
          </div>

          <div className="results-sidebar__sheet-list">
            <div className="results-sidebar__section-head">
              <span>Available sheets</span>
              <small>
                {filteredSheets.length} shown
                {deferredPanelSearchQuery.length > 0
                  ? ` · ${panelSearchResults.totalMatchCount} match(es)`
                  : ''}
              </small>
            </div>

            {filteredSheets.length > 0 ? (
              <div className="results-sheet-table-shell">
                <table className="results-sheet-table">
                  <thead>
                    <tr>
                      <th>Sheet ID</th>
                      <th>Size</th>
                      <th>Util.</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredSheets.map((sheet) => {
                      const key = sheetLookupKey(sheet.materialKey, sheet.sheet.sheetId);
                      const isActive = key === activeSheetKey;
                      const hitCount = panelSearchResults.sheetCounts.get(key) ?? 0;

                      return (
                        <tr
                          className={isActive ? 'table-row--active' : undefined}
                          key={key}
                          onClick={() => {
                            setActiveSheetKey(key);
                            if (hitCount > 0) {
                              const firstMatch =
                                panelSearchResults.firstMatchBySheet.get(key);
                              setSelectedPlacementId(firstMatch?.placementId);
                            }
                          }}
                        >
                          <td>
                            <div className="results-sheet-row">
                              <strong>{getSheetDisplayId(sheet.sheet)}</strong>
                              <span>{sheet.materialName}</span>
                              {hitCount > 0 ? <small>{hitCount} hit(s)</small> : null}
                            </div>
                          </td>
                          <td>
                            {formatDimension(sheet.sheet.sheetLength)}x
                            {formatDimension(sheet.sheet.sheetWidth)}
                          </td>
                          <td>{sheet.sheet.utilizationPercent.toFixed(1)}%</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="empty-state">
                <strong>
                  {hasOutput
                    ? 'No sheets match this filter'
                    : 'No sheet layouts available yet'}
                </strong>
                <span>
                  {hasOutput
                    ? 'Try a different material filter or widen the panel search.'
                    : 'Run nesting from Import to populate the batch explorer.'}
                </span>
              </div>
            )}
          </div>

          <div className="results-sidebar__drawer">
            <div className="results-drawer-tabs" role="tablist" aria-label="Results details">
              <button
                aria-expanded={drawerTab === 'unplaced'}
                aria-selected={drawerTab === 'unplaced'}
                className={
                  drawerTab === 'unplaced'
                    ? 'results-drawer-tab results-drawer-tab--active'
                    : 'results-drawer-tab'
                }
                onClick={() =>
                  setDrawerTab((current) =>
                    current === 'unplaced' ? null : 'unplaced',
                  )
                }
                role="tab"
                type="button"
              >
                Unplaced
                {unplacedRows.length > 0 ? (
                  <span className="results-drawer-tab__badge">{unplacedRows.length}</span>
                ) : null}
              </button>
              {showStiffenerControls ? (
                <button
                  aria-expanded={drawerTab === 'stiffeners'}
                  aria-selected={drawerTab === 'stiffeners'}
                  className={
                    drawerTab === 'stiffeners'
                      ? 'results-drawer-tab results-drawer-tab--active'
                      : 'results-drawer-tab'
                  }
                  onClick={() =>
                    setDrawerTab((current) =>
                      current === 'stiffeners' ? null : 'stiffeners',
                    )
                  }
                  role="tab"
                  type="button"
                >
                  Stiffeners
                </button>
              ) : null}
            </div>

            {drawerTab ? (
              <div className="results-drawer-panel">
                {drawerTab === 'unplaced' ? (
                  unplacedRows.length > 0 ? (
                    <div className="results-drawer-table-shell">
                      <table className="results-drawer-table">
                        <thead>
                          <tr>
                            <th>Part</th>
                            <th>Material</th>
                            <th>Reason</th>
                          </tr>
                        </thead>
                        <tbody>
                          {unplacedRows.map((item) => (
                            <tr
                              key={`${item.materialKey}:${item.partId}:${item.reasonCode}:${item.reasonDescription}`}
                            >
                              <td>{itemLabel(item.partId)}</td>
                              <td>{item.materialName}</td>
                              <td>
                                <strong>{item.reasonCode}</strong>
                                <br />
                                <span>{item.reasonDescription}</span>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="empty-state">
                      <strong>No unplaced panels in scope</strong>
                      <span>Any layout failures for the current filter will appear here.</span>
                    </div>
                  )
                ) : (
                  <div className="results-stiffeners">
                    <div className="results-sidebar__section-head">
                      <span>Stiffener report</span>
                      <small>{stiffenerTakeoffEnabled ? 'Enabled' : 'Disabled'}</small>
                    </div>

                    <p className="section-note">{stiffenerMessage}</p>
                    <p className="section-note">{stiffenerNote}</p>

                    <div className="results-stiffeners__content">
                      {stiffenerSummary && stiffenerTakeoffReport?.hasTakeoff ? (
                        <>
                        <div className="stats-grid results-stiffener-stats">
                          <article className="stat-card">
                            <span>Eligible</span>
                            <strong>{stiffenerSummary.eligiblePanelCount}</strong>
                          </article>
                          <article className="stat-card">
                            <span>Stiffeners</span>
                            <strong>{stiffenerSummary.totalStiffenerCount}</strong>
                          </article>
                          <article className="stat-card">
                            <span>Linear feet</span>
                            <strong>{stiffenerSummary.totalLinearFeet.toFixed(1)}</strong>
                          </article>
                          <article className="stat-card">
                            <span>Stock count</span>
                            <strong>{stiffenerSummary.requiredStockCount}</strong>
                          </article>
                        </div>

                        <div className="results-stiffeners__table-shell">
                          <table className="results-drawer-table">
                            <thead>
                              <tr>
                                <th>Length</th>
                                <th>Pieces</th>
                              </tr>
                            </thead>
                            <tbody>
                              {stiffenerTakeoffReport.overallLengths.map((length) => (
                                <tr key={length.label}>
                                  <td>{length.label}</td>
                                  <td>{length.pieceCount}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                        {activeStiffenerGroup ? (
                          <>
                            <div className="results-sidebar__section-head">
                              <span>{activeStiffenerGroup.name}</span>
                              <small>Optimization Group</small>
                            </div>
                            <div className="stats-grid results-stiffener-stats">
                              <article className="stat-card">
                                <span>Eligible</span>
                                <strong>{activeStiffenerGroup.summary.eligiblePanelCount}</strong>
                              </article>
                              <article className="stat-card">
                                <span>Stiffeners</span>
                                <strong>{activeStiffenerGroup.summary.totalStiffenerCount}</strong>
                              </article>
                              <article className="stat-card">
                                <span>Linear feet</span>
                                <strong>{activeStiffenerGroup.summary.totalLinearFeet.toFixed(1)}</strong>
                              </article>
                              <article className="stat-card">
                                <span>Stock count</span>
                                <strong>{activeStiffenerGroup.summary.requiredStockCount}</strong>
                              </article>
                            </div>
                          </>
                        ) : null}
                        </>
                      ) : (
                        <div className="empty-state">
                          <strong>No stiffener takeoff loaded</strong>
                          <span>
                            {stiffenerTakeoffEnabled
                              ? 'When the current ready rows require stiffeners, their rollup will appear here.'
                              : 'Enable stiffener takeoff on Project Setup to use this report.'}
                          </span>
                        </div>
                      )}
                    </div>

                    <div className="form-actions">
                      <SplitButton
                        busy={stiffenerBusy}
                        busyLabel="Working…"
                        disabled={!canExportStiffenerReport || stiffenerBusy}
                        label="Export stiffener PDF"
                        menuOpen={openMenu === 'stiffener'}
                        onOpenOverrides={() => {
                          setOpenMenu(null);
                          setStiffenerDialogOpen(true);
                        }}
                        onPrimaryAction={() => void onExportStiffenerReport()}
                        onToggleMenu={() =>
                          setOpenMenu((current) =>
                            current === 'stiffener' ? null : 'stiffener',
                          )
                        }
                      />
                    </div>
                  </div>
                )}
              </div>
            ) : null}
          </div>
        </aside>

        <section className="results-stage">
          <div className="results-stage__toolbar">
            <div className="results-stage__identity">
              <strong>{sheetHeaderLabel}</strong>
              <span>{sheetSummaryLabel}</span>
              <small>
                Kerf {formatDimension(kerfWidth)} in
                {projectDirty ? ' · Unsaved changes' : ' · Saved result context'}
              </small>
            </div>

            <div className="results-stage__actions">
              <SplitButton
                busy={reportBusy}
                busyLabel="Exporting…"
                disabled={!canExportReport || reportBusy || !hasOutput}
                label="Export PDF"
                menuOpen={openMenu === 'report'}
                onOpenOverrides={() => {
                  setOpenMenu(null);
                  setReportDialogOpen(true);
                }}
                onPrimaryAction={() => void onExportReport()}
                onToggleMenu={() =>
                  setOpenMenu((current) =>
                    current === 'report' ? null : 'report',
                  )
                }
                tone="primary"
              />
              <button
                className="secondary-button module-action-button"
                disabled={!canExportExcelReport || reportBusy || !hasOutput}
                onClick={() => void onExportExcelReport()}
                type="button"
              >
                {reportBusy ? 'Exporting…' : 'Export Excel'}
              </button>
            </div>
          </div>

          {activeSheetView ? (
            <div className="results-viewer-frame">
              <Suspense
                fallback={
                  <div className="empty-state">
                    <strong>Loading viewer…</strong>
                    <span>The active sheet is being prepared for inspection.</span>
                  </div>
                }
              >
                <SheetViewer
                  materialName={activeSheetView.materialName}
                  onSelectPlacement={setSelectedPlacementId}
                  placements={activeMaterialPlacements}
                  selectedPlacementId={selectedPlacementId}
                  sheet={activeSheetView.sheet}
                  showChrome={false}
                />
              </Suspense>

              <div className="results-viewer-metrics">
                <article className="results-viewer-metric">
                  <span>Sheet utilization</span>
                  <strong>{activeSheetView.sheet.utilizationPercent.toFixed(1)}%</strong>
                </article>
                <article className="results-viewer-metric">
                  <span>Scrap area</span>
                  <strong>{formatArea(scrapArea)} sq in</strong>
                </article>
                <article className="results-viewer-metric">
                  <span>Placements</span>
                  <strong>{activeSheetPlacements.length}</strong>
                </article>
                {sheetSearchMatches > 0 ? (
                  <article className="results-viewer-metric">
                    <span>Search hits</span>
                    <strong>{sheetSearchMatches}</strong>
                  </article>
                ) : null}
              </div>
            </div>
          ) : (
            <div className="module-panel">
              <div className="empty-state">
                <strong>No sheet selected</strong>
                <span>Choose a sheet from the explorer to inspect the layout here.</span>
              </div>
            </div>
          )}

          <section className="results-inspection">
            <div className="results-inspection__header">
              <div className="results-sidebar__section-head">
                <span>Placement inspection</span>
                <small>{activeSheetPlacements.length} entities detected</small>
              </div>
              {selectedPlacement ? (
                <StatusPill label={`${selectedPlacement.partId} selected`} tone="ok" />
              ) : null}
            </div>

            {activeSheetPlacements.length > 0 ? (
              <div className="results-inspection__table-shell">
                <table className="results-inspection__table">
                  <thead>
                    <tr>
                      <th>Part ID</th>
                      <th>Pos (x, y)</th>
                      <th>Size</th>
                      <th>Rotation</th>
                      <th>Part Group</th>
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
                        <td>
                          <div className="results-placement-cell">
                            <i />
                            <strong>{placement.partId}</strong>
                          </div>
                        </td>
                        <td>
                          {placement.x.toFixed(2)}, {placement.y.toFixed(2)}
                        </td>
                        <td>
                          {formatDimension(placement.width)} x {formatDimension(placement.height)}
                        </td>
                        <td>{placement.rotated90 ? '90.00°' : '0.00°'}</td>
                        <td>{placement.displayGroup}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="empty-state">
                <strong>No placements on the active sheet</strong>
                <span>
                  Successful layouts will populate the viewer and inspection table together.
                </span>
              </div>
            )}
          </section>
        </section>
      </div>

      {reportDialogOpen ? (
        <div
          className="results-dialog-backdrop"
          onClick={() => setReportDialogOpen(false)}
          role="presentation"
        >
          <div
            aria-modal="true"
            className="results-dialog"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
          >
            <div className="results-dialog__header">
              <div>
                <p className="eyebrow">Report Options</p>
                <h3>Define report overrides</h3>
              </div>
              {!canSyncReportSettings ? (
                <StatusPill label="Local only" tone="warn" />
              ) : null}
            </div>

            <p className="section-note">{reportMessage}</p>
            <div className="form-grid form-grid--two-column">
              <LogoField
                disabled={reportBusy}
                onChoose={async () => {
                  const nextPath = await onPickCompanyLogo();
                  if (nextPath !== undefined) {
                    setReportDraft((current) => ({
                      ...current,
                      companyLogoPath: nextPath,
                    }));
                  }
                }}
                onClear={() =>
                  setReportDraft((current) => ({
                    ...current,
                    companyLogoPath: '',
                  }))
                }
                value={reportDraft.companyLogoPath}
              />
              <label className="field">
                <span>Company name</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      companyName: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.companyName}
                />
              </label>
              <label className="field">
                <span>Report title</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      reportTitle: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.reportTitle}
                />
              </label>
              <label className="field">
                <span>Project / job name</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      projectJobName: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.projectJobName}
                />
              </label>
              <label className="field">
                <span>Project / job number</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      projectJobNumber: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.projectJobNumber}
                />
              </label>
              <label className="field">
                <span>Release</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      releaseId: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.releaseId}
                />
              </label>
              <label className="field">
                <span>Status</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      status: event.target.value,
                    }))
                  }
                  type="text"
                  value={reportDraft.status}
                />
              </label>
              <label className="field">
                <span>Report date</span>
                <input
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      reportDate: event.target.value,
                    }))
                  }
                  type="date"
                  value={reportDraft.reportDate}
                />
              </label>
              <label className="field field--wide">
                <span>Notes</span>
                <textarea
                  onChange={(event) =>
                    setReportDraft((current) => ({
                      ...current,
                      notes: event.target.value,
                    }))
                  }
                  value={reportDraft.notes}
                />
              </label>
            </div>

            <div className="form-actions">
              <button
                className="secondary-button"
                onClick={() => setReportDialogOpen(false)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="primary-button"
                onClick={() => void exportReportFromDialog()}
                type="button"
              >
                Export PDF
              </button>
              <button
                className="secondary-button"
                onClick={() =>
                  void onExportExcelReport({
                    companyLogoPath: reportDraft.companyLogoPath,
                    reportSettings: toReportSettings(reportDraft),
                  }).then(() => setReportDialogOpen(false))
                }
                type="button"
              >
                Export Excel
              </button>
              <button
                className="secondary-button"
                onClick={applyReportDraft}
                type="button"
              >
                Save changes
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {showStiffenerControls && stiffenerDialogOpen ? (
        <div
          className="results-dialog-backdrop"
          onClick={() => setStiffenerDialogOpen(false)}
          role="presentation"
        >
          <div
            aria-modal="true"
            className="results-dialog"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
          >
            <div className="results-dialog__header">
              <div>
                <p className="eyebrow">Stiffener Overrides</p>
                <h3>Adjust export parameters</h3>
              </div>
            </div>

            <p className="section-note">
              Use temporary overrides for this export or save them back to the project.
            </p>
            <div className="form-grid form-grid--two-column">
              <LogoField
                disabled={stiffenerBusy}
                onChoose={async () => {
                  const nextPath = await onPickCompanyLogo();
                  if (nextPath !== undefined) {
                    setStiffenerDraft((current) => ({
                      ...current,
                      companyLogoPath: nextPath,
                    }));
                  }
                }}
                onClear={() =>
                  setStiffenerDraft((current) => ({
                    ...current,
                    companyLogoPath: '',
                  }))
                }
                value={stiffenerDraft.companyLogoPath}
              />
              <label className="field">
                <span>Company name</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      companyName: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.companyName}
                />
              </label>
              <label className="field">
                <span>Stiffener report title</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      stiffenerReportTitle: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.stiffenerReportTitle}
                />
              </label>
              <label className="field">
                <span>Project / job name</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      projectJobName: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.projectJobName}
                />
              </label>
              <label className="field">
                <span>Project / job number</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      projectJobNumber: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.projectJobNumber}
                />
              </label>
              <label className="field">
                <span>Release</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      stiffenerReleaseId: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.stiffenerReleaseId}
                />
              </label>
              <label className="field">
                <span>Status</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      stiffenerStatus: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.stiffenerStatus}
                />
              </label>
              <label className="field">
                <span>Report date</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      reportDate: event.target.value,
                    }))
                  }
                  type="date"
                  value={stiffenerDraft.reportDate}
                />
              </label>
              <label className="field">
                <span>P.O. #</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      poNumber: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.poNumber}
                />
              </label>
              <label className="field field--wide">
                <span>Extrusion</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      extrusion: event.target.value,
                    }))
                  }
                  placeholder="e.g. 1 x 2 aluminum tube"
                  type="text"
                  value={stiffenerDraft.extrusion}
                />
              </label>
              <label className="field">
                <span>Color</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      color: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.color}
                />
              </label>
              <label className="field">
                <span>Color #</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      colorNumber: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.colorNumber}
                />
              </label>
              <label className="field field--wide">
                <span>Manufacturer</span>
                <input
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      manufacturer: event.target.value,
                    }))
                  }
                  type="text"
                  value={stiffenerDraft.manufacturer}
                />
              </label>
              <label className="field field--wide">
                <span>Notes</span>
                <textarea
                  onChange={(event) =>
                    setStiffenerDraft((current) => ({
                      ...current,
                      notes: event.target.value,
                    }))
                  }
                  value={stiffenerDraft.notes}
                />
              </label>
            </div>

            <div className="form-actions">
              <button
                className="secondary-button"
                onClick={() => setStiffenerDialogOpen(false)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="primary-button"
                onClick={() => void exportStiffenerFromDialog()}
                type="button"
              >
                Export PDF
              </button>
              <button
                className="secondary-button"
                onClick={saveStiffenerDraft}
                type="button"
              >
                Save changes
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
