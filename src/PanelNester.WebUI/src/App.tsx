import { useEffect, useReducer, useRef, useState } from 'react';
import { AppShell } from './components/AppShell';
import { ConfirmationDialog } from './components/ConfirmationDialog';
import { NewProjectDialog } from './components/ProjectKindControls';
import { hostBridge } from './bridge/hostBridge';
import { reconcileCutPlanGenerationResponse } from './cutPlanGenerationGuard';
import {
  guardProjectRoute,
  projectKindLabels,
  projectKindSupportsStiffeners,
  type AppRoute,
} from './projectKind';
import { ImportPage } from './pages/ImportPage';
import { RequiredPiecesPage } from './pages/RequiredPiecesPage';
import {
  collectWorkbookNewMaterials,
  createWorkbookWorksheetDrafts,
  mergeRecognizedColumnMappings,
} from './pages/workbookImportDraftState';
import { MaterialsPage } from './pages/MaterialsPage';
import { OverviewPage } from './pages/OverviewPage';
import { ResultsPage } from './pages/ResultsPage';
import { ExtrusionsPage } from './pages/ExtrusionsPage';
import {
  type BatchNestResponse,
  type BridgeError,
  bridgeMessageTypes,
  type DesktopAppSettings,
  defaultExtrusionLayoutState,
  defaultStiffenerTakeoffSettings,
  demoKerfWidth,
  demoMaterial,
  emptyBatchNestResponse,
  emptyImportResponse,
  emptyNestResponse,
  type ExtrusionLayoutState,
  type ExtrusionReportData,
  type BridgeCapability,
  type HostBridgeSnapshot,
  type ImportFileResponse,
  type InchDisplayFormat,
  type ImportFieldName,
  optionalImportFieldNames,
  requiredImportFieldNames,
  type ImportFileRequest,
  type ImportMappingSession,
  type ImportSessionPhase,
  type ImportSessionResponse,
  type ImportResultCounts,
  type ImportMaterialResolution,
  type ImportResponse,
  type ImportOptions,
  type ImportConfiguration,
  type ImportSourceMetadata,
  type Material,
  type MaterialDraft,
  type MaterialLibraryLocation,
  type MaterialLibraryOperationResponse,
  type NestResponse,
  type OpenFileDialogResponse,
  type OptimizationGroup,
  type OptimizationGroupNestResult,
  type OptimizationGroupChange,
  type OpenProjectRequest,
  type PartRow,
  type PartRowUpdate,
  type ProjectFileMetadata,
  type ProjectMaterialSnapshot,
  type ProjectMetadata,
  type ChangeProjectKindResponse,
  type ProjectKind,
  type ProjectRecord,
  type ProjectSettings,
  type ReportSettings,
  type RequiredPieceChange,
  type StiffenerTakeoffReportData,
  type StiffenerTakeoffSettings,
  type StockLengthGenerationProgress,
  type WorkbookImportProgress,
} from './types/contracts';

type ProjectSaveStatus = 'saved' | 'cancelled' | 'failed';

interface DesktopCloseSaveResult {
  status: ProjectSaveStatus;
  message?: string;
}

interface DesktopCloseProjectSavePayload {
  status: 'ready' | 'failed';
  project?: ProjectRecord | null;
  filePath?: string | null;
  suggestedFileName?: string | null;
  message?: string;
}

type UnsavedPromptChoice = 'save' | 'discard' | 'cancel';

declare global {
  interface Window {
    panelNesterDesktopHost?: {
      createNewProject: () => void | Promise<void>;
      openProject: (request: OpenProjectRequest) => void | Promise<void>;
      saveProject: () => void | Promise<void | DesktopCloseSaveResult>;
      saveProjectAs: () => void | Promise<void | DesktopCloseSaveResult>;
      saveProjectBeforeClose: () => Promise<DesktopCloseSaveResult>;
      prepareProjectSaveBeforeClose: () => Promise<DesktopCloseProjectSavePayload>;
    };
  }
}

const importFileDialogTimeoutMs = 300000;
const importBridgeTimeoutMs = 120000;
const nestingBridgeTimeoutMs = 300000;
const currentProjectVersion = 7;

function encodeDroppedImportSource(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('The dropped Import Source could not be read.'));
    reader.onload = () => {
      const result = typeof reader.result === 'string' ? reader.result : '';
      const separator = result.indexOf(',');
      if (separator < 0) {
        reject(new Error('The dropped Import Source could not be encoded.'));
        return;
      }
      resolve(result.slice(separator + 1));
    };
    reader.readAsDataURL(file);
  });
}

interface AppState {
  activeRoute: AppRoute;
  projectKind: ProjectKind;
  bridge: HostBridgeSnapshot;
  importResponse: ImportResponse;
  nestResponse: NestResponse;
  batchNestResponse: BatchNestResponse;
  materials: Material[];
  materialLibraryLocation?: MaterialLibraryLocation | null;
  materialLibraryUnavailable: boolean;
  selectedMaterialId?: string;
  lastNestMaterial?: Material;
  selectedFilePath?: string;
  importSource?: ImportSourceMetadata;
  importConfiguration?: ImportConfiguration;
  lastImportReceipt?: ImportResultCounts;
  preImportProject?: ProjectRecord;
  importMappingSession?: ImportMappingSession;
  importMessage: string;
  importPhase?: ImportSessionPhase;
  importProgress?: WorkbookImportProgress;
  nestingMessage: string;
  materialsMessage: string;
  reportMessage: string;
  stiffenerMessage: string;
  extrusionMessage: string;
  importBusy: boolean;
  nestingBusy: boolean;
  materialsBusy: boolean;
  reportBusy: boolean;
  stiffenerBusy: boolean;
  extrusionBusy: boolean;
  projectMetadata: ProjectMetadata;
  projectSettings: ProjectSettings;
  stiffenerTakeoffReport: StiffenerTakeoffReportData | null;
  extrusionLayout: ExtrusionLayoutState;
  extrusionReport: ExtrusionReportData | null;
  projectId: string;
  projectFilePath?: string;
  projectMaterialSnapshots: ProjectMaterialSnapshot[];
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  projectMessage: string;
  projectBusy: boolean;
  generationBusy: boolean;
  generationProgress?: StockLengthGenerationProgress;
  projectDirty: boolean;
  partMutationBusy: boolean;
  lastSavedAt?: string;
}

interface StiffenerExportOverrides {
  companyLogoPath?: string | null;
  reportSettings?: ReportSettings;
  stiffenerTakeoff?: StiffenerTakeoffSettings;
}

interface ReportExportOverrides {
  companyLogoPath?: string | null;
  reportSettings?: ReportSettings;
  stockLengthScope?: import('./types/contracts').StockLengthReportScope;
}

type AppAction =
  | { type: 'route-changed'; route: AppRoute }
  | { type: 'bridge-updated'; snapshot: HostBridgeSnapshot }
  | { type: 'materials-request-started'; message: string }
  | { type: 'materials-request-finished'; message: string }
  | {
      type: 'materials-loaded';
      materials: Material[];
      materialLibraryLocation: MaterialLibraryLocation | null | undefined;
      selectedMaterialId?: string;
      message: string;
    }
  | {
      type: 'materials-failed';
      message: string;
      materialLibraryLocation?: MaterialLibraryLocation | null;
      libraryUnavailable?: boolean;
    }
  | { type: 'material-selected'; materialId?: string }
  | { type: 'material-created'; material: Material; message: string }
  | { type: 'material-updated'; material: Material; message: string }
  | { type: 'material-deleted'; materialId: string; message: string }
  | { type: 'import-started'; message: string; phase: ImportSessionPhase }
  | { type: 'import-progressed'; progress: WorkbookImportProgress }
  | { type: 'import-selection-cancelled'; message: string }
  | {
      type: 'import-mapping-ready';
      session: ImportMappingSession;
      message: string;
    }
  | { type: 'import-mapping-updated'; session: ImportMappingSession }
  | { type: 'import-mapping-cancelled'; message: string }
  | {
      type: 'import-finished';
      filePath: string;
      response: ImportResponse;
      project?: ProjectRecord;
      resultCounts?: ImportResultCounts;
      undoProject?: ProjectRecord;
      message: string;
      selectedMaterialId?: string;
    }
  | { type: 'part-row-operation-started'; message: string }
  | {
      type: 'part-rows-replaced';
      response: ImportResponse;
      message: string;
      selectedMaterialId?: string;
      targetOptimizationGroupId?: string;
    }
  | { type: 'part-row-operation-failed'; message: string }
  | { type: 'import-failed'; message: string }
  | { type: 'nesting-started'; message: string }
  | {
      type: 'nesting-finished';
      response: NestResponse;
      batchResponse: BatchNestResponse;
      optimizationGroupResults?: OptimizationGroupNestResult[];
      message: string;
      material?: Material;
    }
  | { type: 'nesting-failed'; message: string }
  | {
      type: 'project-created';
      metadata: ProjectMetadata;
      settings: ProjectSettings;
      projectKind: ProjectKind;
      projectId?: string;
      optimizationGroups?: OptimizationGroup[];
      message: string;
    }
  | {
      type: 'project-opened';
      filePath: string;
      project: ProjectRecord;
      settings?: ProjectSettings;
      selectedMaterialId?: string;
      lastNestMaterial?: Material;
      message: string;
    }
  | { type: 'import-undone' }
  | {
      type: 'project-saved';
      filePath: string;
      project: ProjectRecord;
      settings?: ProjectSettings;
      message: string;
    }
  | { type: 'project-operation-started'; message: string }
  | { type: 'project-operation-finished'; message: string }
  | { type: 'project-operation-failed'; message: string }
  | { type: 'generation-operation-started'; message: string }
  | { type: 'generation-progressed'; progress: StockLengthGenerationProgress }
  | { type: 'generation-operation-finished'; message: string }
  | { type: 'project-kind-changed'; project: ProjectRecord; message: string }
  | { type: 'optimization-group-activated'; optimizationGroupId: string }
  | {
      type: 'optimization-groups-updated';
      project: ProjectRecord;
      activeOptimizationGroupId?: string;
      message: string;
    }
  | {
      type: 'project-metadata-changed';
      metadata: ProjectMetadata;
      settings: ProjectSettings;
      message: string;
    }
  | {
      type: 'project-settings-changed';
      settings: ProjectSettings;
      message: string;
      invalidateNestingResults?: boolean;
    }
  | { type: 'project-settings-synced'; settings: ProjectSettings }
  | { type: 'report-operation-started'; message: string }
  | { type: 'report-operation-finished'; message: string }
  | { type: 'report-operation-failed'; message: string }
  | { type: 'stiffener-operation-started'; message: string }
  | {
      type: 'stiffener-operation-finished';
      report: StiffenerTakeoffReportData | null;
      message: string;
    }
  | { type: 'stiffener-operation-failed'; message: string }
  | { type: 'stiffener-operation-cleared'; message: string }
  | { type: 'extrusion-layout-changed'; layout: ExtrusionLayoutState; message: string }
  | { type: 'extrusion-layout-synced'; layout: ExtrusionLayoutState }
  | { type: 'extrusion-operation-started'; message: string }
  | {
      type: 'extrusion-operation-finished';
      layout?: ExtrusionLayoutState;
      report?: ExtrusionReportData | null;
      message: string;
    }
  | { type: 'extrusion-operation-failed'; message: string };

const defaultImportMessage =
  'Choose a CSV file or Excel Workbook, review its import configuration, then finalize before nesting.';
const defaultNestingMessage =
  'Select a material for focus if needed, then run nesting when the imported rows are ready.';
const defaultMaterialsMessage =
  'Connect to the desktop host to load the reusable material library.';
const defaultProjectMessage =
  'Use this page to manage metadata, file state, and the material snapshots that travel with a saved project.';
const defaultReportMessage =
  'Edit report fields here, then export once the desktop host exposes the Phase 5 PDF bridge.';
const defaultStiffenerMessage =
  'Enable stiffener takeoff in Project settings to preview the takeoff and export its standalone PDF.';
const defaultExtrusionMessage =
  'Lay out imported panels by group, assign edge extrusions, then export extrusion summaries.';

const emptyDesktopAppSettings: DesktopAppSettings = {
  companyLogoPath: null,
  companyName: null,
};

function createDefaultProjectMetadata(): ProjectMetadata {
  return {
    projectName: 'Untitled Project',
    projectNumber: '',
    customerName: '',
    estimator: '',
    drafter: '',
    projectManager: '',
    date: new Date().toISOString().slice(0, 10),
    requiredDate: '',
    revision: '',
    notes: '',
  };
}

function buildDefaultReportTitle(projectName: string): string {
  const normalized = projectName.trim();
  return normalized.length > 0 ? `${normalized} Nesting Report` : 'Nesting Report';
}

function buildWindowTitle(projectName: string, isDirty: boolean): string {
  const normalized = projectName.trim();
  const displayName = normalized.length > 0 ? normalized : 'Untitled Project';
  return `${displayName}${isDirty ? ' *' : ''} — OptiFab`;
}

function normalizeReportDate(value?: string | null): string {
  return value?.slice(0, 10) ?? '';
}

function applyReportSettingsToMetadata(
  metadata: ProjectMetadata,
  reportSettings: ReportSettings,
): ProjectMetadata {
  const normalizedReportDate = normalizeReportDate(reportSettings.reportDate);

  return {
    ...metadata,
    projectName: reportSettings.projectJobName?.trim() || metadata.projectName,
    projectNumber: reportSettings.projectJobNumber?.trim() || metadata.projectNumber,
    customerName: reportSettings.companyName?.trim() || metadata.customerName,
    date: normalizedReportDate || metadata.date,
    notes: reportSettings.notes ?? metadata.notes,
  };
}

function createDefaultReportSettings(
  metadata: ProjectMetadata,
  companyNameDefault?: string | null,
): ReportSettings {
  const normalizedCompanyName = companyNameDefault?.trim();

  return {
    companyName:
      normalizedCompanyName && normalizedCompanyName.length > 0
        ? normalizedCompanyName
        : metadata.customerName,
    reportTitle: buildDefaultReportTitle(metadata.projectName),
    projectJobName: metadata.projectName,
    projectJobNumber: metadata.projectNumber,
    releaseId: '',
    status: '',
    reportDate: normalizeReportDate(metadata.date),
    notes: metadata.notes,
  };
}

function normalizeReportSettings(
  reportSettings: ReportSettings | null | undefined,
  metadata: ProjectMetadata,
  companyNameDefault?: string | null,
): ReportSettings {
  const defaults = createDefaultReportSettings(metadata, companyNameDefault);
  const normalizedCompanyName = reportSettings?.companyName?.trim();

  return {
    companyName:
      normalizedCompanyName && normalizedCompanyName.length > 0
        ? normalizedCompanyName
        : defaults.companyName,
    reportTitle: reportSettings?.reportTitle?.trim() ?? defaults.reportTitle,
    projectJobName: reportSettings?.projectJobName?.trim() ?? defaults.projectJobName,
    projectJobNumber:
      reportSettings?.projectJobNumber?.trim() ?? defaults.projectJobNumber,
    releaseId: reportSettings?.releaseId?.trim() ?? defaults.releaseId,
    status: reportSettings?.status?.trim() ?? defaults.status,
    reportDate: normalizeReportDate(reportSettings?.reportDate ?? defaults.reportDate),
    notes: reportSettings?.notes ?? defaults.notes,
  };
}

function createProjectSettings(
  metadata: ProjectMetadata,
  companyNameDefault?: string | null,
  projectKind: ProjectKind = 'sheet',
): ProjectSettings {
  return {
    kerfWidth: projectKind === 'stockLength' ? 0 : demoKerfWidth,
    inchDisplayFormat: 'decimal',
    reportSettings: createDefaultReportSettings(metadata, companyNameDefault),
    stiffenerTakeoff: { ...defaultStiffenerTakeoffSettings },
  };
}

function normalizeStiffenerTakeoffSettings(
  settings: StiffenerTakeoffSettings | null | undefined,
): StiffenerTakeoffSettings {
  return {
    enabled: settings?.enabled ?? defaultStiffenerTakeoffSettings.enabled,
    minimumLengthInches:
      typeof settings?.minimumLengthInches === 'number' &&
      settings.minimumLengthInches >= 0
        ? settings.minimumLengthInches
        : defaultStiffenerTakeoffSettings.minimumLengthInches,
    minimumWidthInches:
      typeof settings?.minimumWidthInches === 'number' &&
      settings.minimumWidthInches >= 0
        ? settings.minimumWidthInches
        : defaultStiffenerTakeoffSettings.minimumWidthInches,
    widthDeductionInches:
      typeof settings?.widthDeductionInches === 'number' &&
      settings.widthDeductionInches >= 0
        ? settings.widthDeductionInches
        : defaultStiffenerTakeoffSettings.widthDeductionInches,
    stockLengthFeet:
      typeof settings?.stockLengthFeet === 'number' && settings.stockLengthFeet > 0
        ? settings.stockLengthFeet
        : defaultStiffenerTakeoffSettings.stockLengthFeet,
    reportTitle:
      settings?.reportTitle?.trim() ?? defaultStiffenerTakeoffSettings.reportTitle,
    extrusion: settings?.extrusion?.trim() ?? defaultStiffenerTakeoffSettings.extrusion,
    releaseId:
      settings?.releaseId?.trim() ?? defaultStiffenerTakeoffSettings.releaseId,
    poNumber:
      settings?.poNumber?.trim() ?? defaultStiffenerTakeoffSettings.poNumber,
    color: settings?.color?.trim() ?? defaultStiffenerTakeoffSettings.color,
    colorNumber:
      settings?.colorNumber?.trim() ?? defaultStiffenerTakeoffSettings.colorNumber,
    manufacturer:
      settings?.manufacturer?.trim() ??
      defaultStiffenerTakeoffSettings.manufacturer,
    status: settings?.status?.trim() ?? defaultStiffenerTakeoffSettings.status,
  };
}

function normalizeProjectSettings(
  settings: ProjectSettings | null | undefined,
  metadata: ProjectMetadata,
  companyNameDefault?: string | null,
  projectKind: ProjectKind = 'sheet',
): ProjectSettings {
  return {
    kerfWidth:
      typeof settings?.kerfWidth === 'number' && settings.kerfWidth >= 0
        ? settings.kerfWidth
        : projectKind === 'stockLength' ? 0 : demoKerfWidth,
    inchDisplayFormat: settings?.inchDisplayFormat ?? 'decimal',
    reportSettings: normalizeReportSettings(
      settings?.reportSettings,
      metadata,
      companyNameDefault,
    ),
    stiffenerTakeoff: normalizeStiffenerTakeoffSettings(
      settings?.stiffenerTakeoff,
    ),
  };
}

function syncReportSettingsWithMetadata(
  previousMetadata: ProjectMetadata,
  nextMetadata: ProjectMetadata,
  reportSettings: ReportSettings,
  companyNameDefault?: string | null,
): ReportSettings {
  const previousDefaults = createDefaultReportSettings(
    previousMetadata,
    companyNameDefault,
  );
  const nextDefaults = createDefaultReportSettings(nextMetadata, companyNameDefault);

  const pickValue = <TKey extends keyof ReportSettings>(key: TKey) => {
    const currentValue = reportSettings[key];
    return currentValue === undefined || currentValue === previousDefaults[key]
      ? nextDefaults[key]
      : currentValue;
  };

  return {
    companyName: pickValue('companyName'),
    reportTitle: pickValue('reportTitle'),
    projectJobName: pickValue('projectJobName'),
    projectJobNumber: pickValue('projectJobNumber'),
    releaseId: pickValue('releaseId'),
    status: pickValue('status'),
    reportDate: pickValue('reportDate'),
    notes: pickValue('notes'),
  };
}

const initialState: AppState = {
  activeRoute: 'overview',
  projectKind: 'sheet',
  bridge: hostBridge.getSnapshot(),
  importResponse: emptyImportResponse,
  nestResponse: emptyNestResponse,
  batchNestResponse: emptyBatchNestResponse,
  materials: [],
  materialLibraryLocation: undefined,
  materialLibraryUnavailable: false,
  selectedMaterialId: undefined,
  lastNestMaterial: undefined,
  selectedFilePath: undefined,
  importSource: undefined,
  importConfiguration: undefined,
  lastImportReceipt: undefined,
  preImportProject: undefined,
  importMappingSession: undefined,
  importMessage: defaultImportMessage,
  nestingMessage: defaultNestingMessage,
  materialsMessage: defaultMaterialsMessage,
  reportMessage: defaultReportMessage,
  stiffenerMessage: defaultStiffenerMessage,
  extrusionMessage: defaultExtrusionMessage,
  importBusy: false,
  nestingBusy: false,
  materialsBusy: false,
  reportBusy: false,
  stiffenerBusy: false,
  extrusionBusy: false,
  projectMetadata: createDefaultProjectMetadata(),
  projectSettings: createProjectSettings(createDefaultProjectMetadata()),
  stiffenerTakeoffReport: null,
  extrusionLayout: defaultExtrusionLayoutState,
  extrusionReport: null,
  projectId: '',
  projectFilePath: undefined,
  projectMaterialSnapshots: [],
  optimizationGroups: [],
  activeOptimizationGroupId: undefined,
  projectMessage: defaultProjectMessage,
  projectBusy: false,
  generationBusy: false,
  generationProgress: undefined,
  projectDirty: false,
  partMutationBusy: false,
  lastSavedAt: undefined,
};

function sortByName<T extends { name: string }>(items: T[]): T[] {
  return [...items].sort((left, right) => left.name.localeCompare(right.name));
}

function sortMaterials(materials: Material[]): Material[] {
  return sortByName(materials);
}

function fileNameFromPath(filePath: string): string {
  const segments = filePath.split(/[\\/]/);
  return segments[segments.length - 1] ?? filePath;
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function getDistinctImportedMaterialNames(importResponse: ImportResponse): string[] {
  return Array.from(
    new Set(
      importResponse.parts
        .map((part) => part.materialName.trim())
        .filter((name) => name.length > 0),
    ),
  );
}

function pickMaterialId(
  materials: Material[],
  importResponse: ImportResponse,
  currentSelectedId?: string,
  preferredMaterialId?: string,
): string | undefined {
  if (
    preferredMaterialId &&
    materials.some((material) => material.materialId === preferredMaterialId)
  ) {
    return preferredMaterialId;
  }

  if (
    currentSelectedId &&
    materials.some((material) => material.materialId === currentSelectedId)
  ) {
    return currentSelectedId;
  }

  const importedMaterialNames = getDistinctImportedMaterialNames(importResponse);
  if (importedMaterialNames.length === 1) {
    const matched = materials.find(
      (material) => material.name === importedMaterialNames[0],
    );

    if (matched) {
      return matched.materialId;
    }
  }

  return materials[0]?.materialId;
}

function mapMetadataToBridge(metadata: ProjectMetadata): ProjectFileMetadata {
  return {
    projectName: metadata.projectName.trim() || 'Untitled Project',
    projectNumber: metadata.projectNumber.trim() || null,
    customerName: metadata.customerName.trim() || null,
    estimator: metadata.estimator.trim() || null,
    drafter: metadata.drafter.trim() || null,
    pm: metadata.projectManager.trim() || null,
    date: metadata.date.trim().length > 0 ? metadata.date : null,
    requiredDate: metadata.requiredDate.trim().length > 0 ? metadata.requiredDate : null,
    revision: metadata.revision.trim() || null,
    notes: metadata.notes.trim() || null,
  };
}

function mapMetadataFromBridge(metadata?: ProjectFileMetadata | null): ProjectMetadata {
  return {
    projectName: metadata?.projectName?.trim() || 'Untitled Project',
    projectNumber: metadata?.projectNumber?.trim() || '',
    customerName: metadata?.customerName?.trim() || '',
    estimator: metadata?.estimator?.trim() || '',
    drafter: metadata?.drafter?.trim() || '',
    projectManager: metadata?.pm?.trim() || '',
    date: metadata?.date?.slice(0, 10) || '',
    requiredDate: metadata?.requiredDate?.slice(0, 10) || '',
    revision: metadata?.revision?.trim() || '',
    notes: metadata?.notes?.trim() || '',
  };
}

function getProjectImportResponse(project: ProjectRecord): ImportResponse {
  const parts = project.state.parts ?? [];
  const warnings = parts.flatMap((part) =>
    part.validationStatus === 'warning'
      ? part.validationMessages.map((message) => ({
          code: 'saved-warning',
          message,
          rowId: part.rowId,
        }))
      : [],
  );
  const errors = parts.flatMap((part) =>
    part.validationStatus === 'error'
      ? part.validationMessages.map((message) => ({
          code: 'saved-error',
          message,
          rowId: part.rowId,
        }))
      : [],
  );

  return {
    success: errors.length === 0,
    parts,
    warnings,
    errors,
    availableColumns: [],
    sourceColumns: [],
    columnMappings: [],
    materialResolutions: [],
  };
}

function getReadyParts(importResponse: ImportResponse): ImportResponse['parts'] {
  return importResponse.parts.filter((part) => part.validationStatus !== 'error');
}

function buildBatchFromLegacy(
  nestResponse: NestResponse,
  material?: Material,
  snapshots: ProjectMaterialSnapshot[] = [],
  selectedMaterialId?: string,
): BatchNestResponse {
  if (nestResponse.sheets.length === 0 && nestResponse.unplacedItems.length === 0) {
    return emptyBatchNestResponse;
  }

  const materialName =
    material?.name ??
    nestResponse.sheets[0]?.materialName ??
    snapshots.find((snapshot) => snapshot.materialId === selectedMaterialId)?.name ??
    snapshots[0]?.name ??
    'Imported material';
  const materialId =
    material?.materialId ??
    snapshots.find((snapshot) => snapshot.name === materialName)?.materialId ??
    selectedMaterialId;

  return {
    success: nestResponse.success,
    legacyResult: nestResponse,
    materialResults: [
      {
        materialName,
        materialId,
        result: nestResponse,
      },
    ],
  };
}

function getProjectBatchNestResponse(
  project: ProjectRecord,
  material?: Material,
): BatchNestResponse {
  if (project.state.lastBatchNestingResult?.materialResults?.length) {
    return project.state.lastBatchNestingResult;
  }

  if (!project.state.lastNestingResult) {
    return emptyBatchNestResponse;
  }

  return buildBatchFromLegacy(
    project.state.lastNestingResult,
    material,
    project.materialSnapshots,
    project.state.selectedMaterialId ?? undefined,
  );
}

function pickOpenedProjectMaterialId(
  materials: Material[],
  project: ProjectRecord,
): string | undefined {
  if (
    project.state.selectedMaterialId &&
    materials.some((material) => material.materialId === project.state.selectedMaterialId)
  ) {
    return project.state.selectedMaterialId;
  }

  if (project.state.parts.length > 0) {
    return pickMaterialId(materials, getProjectImportResponse(project));
  }

  return undefined;
}

function describeImportResult(filePath: string, response: ImportResponse): string {
  const fileName = fileNameFromPath(filePath);
  if (response.success) {
    return `Imported ${response.parts.length} rows from ${fileName} with ${response.warnings.length} warnings.`;
  }

  return `Imported ${response.parts.length} rows from ${fileName}; ${response.errors.length} errors and ${response.warnings.length} warnings still need review.`;
}

function describeValidationState(response: ImportResponse): string {
  if (response.errors.length === 0 && response.warnings.length === 0) {
    return 'All imported rows are currently ready for nesting.';
  }

  if (response.errors.length === 0) {
    return `${response.warnings.length} warning(s) remain for review.`;
  }

  return `${response.errors.length} error(s) and ${response.warnings.length} warning(s) remain to correct.`;
}

function describeRowOperation(actionLabel: string, response: ImportResponse): string {
  return `${actionLabel} ${describeValidationState(response)}`;
}

function pickImportFilePath(response: ImportFileResponse, fallbackFilePath?: string): string | undefined {
  return response.filePath ?? fallbackFilePath;
}

function normalizeImportResponse(response: {
  success?: boolean;
  parts?: ImportResponse['parts'];
  requiredPieces?: ImportResponse['requiredPieces'];
  errors?: ImportResponse['errors'];
  warnings?: ImportResponse['warnings'];
  availableColumns?: string[];
  sourceColumns?: ImportResponse['sourceColumns'];
  columnMappings?: ImportResponse['columnMappings'];
  materialResolutions?: ImportResponse['materialResolutions'];
  worksheet?: ImportResponse['worksheet'];
}): ImportResponse {
  return {
    success: response.success === true,
    parts: Array.isArray(response.parts) ? response.parts : [],
    requiredPieces: Array.isArray(response.requiredPieces) ? response.requiredPieces : [],
    errors: Array.isArray(response.errors) ? response.errors : [],
    warnings: Array.isArray(response.warnings) ? response.warnings : [],
    availableColumns: Array.isArray(response.availableColumns)
      ? response.availableColumns
      : [],
    sourceColumns: Array.isArray(response.sourceColumns)
      ? response.sourceColumns
      : [],
    columnMappings: Array.isArray(response.columnMappings)
      ? response.columnMappings
      : [],
    materialResolutions: Array.isArray(response.materialResolutions)
      ? response.materialResolutions
      : [],
    worksheet: response.worksheet ?? null,
  };
}

function normalizeImportFileResponse(
  response: Partial<ImportFileResponse>,
): ImportFileResponse {
  return {
    ...normalizeImportResponse(response),
    filePath: typeof response.filePath === 'string' ? response.filePath : null,
    error: response.error ?? null,
    message: response.message,
  };
}

function normalizeImportSessionResponse(
  response: Partial<ImportSessionResponse>,
  sessionId: string,
): ImportSessionResponse {
  return {
    ...normalizeImportFileResponse({
      ...response,
      filePath: response.importSourcePath ?? response.filePath,
    }),
    sessionId: response.sessionId ?? sessionId,
    importSourcePath:
      typeof response.importSourcePath === 'string'
        ? response.importSourcePath
        : typeof response.filePath === 'string'
          ? response.filePath
          : null,
    importSource: response.importSource ?? null,
    phase: response.phase ?? 'failed',
    finalized: response.finalized === true,
    project: response.project ?? null,
    workbook: response.workbook ?? null,
    resultCounts: response.resultCounts ?? null,
  };
}

function createImportSessionId(): string {
  return globalThis.crypto?.randomUUID?.() ??
    `import-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function toImportResponse(response: ImportFileResponse): ImportResponse {
  return normalizeImportResponse(response);
}

function buildImportOptionsFromResponse(response: ImportFileResponse): ImportOptions {
  const usedSourceColumns = new Set<string>();

  return {
    columnMappings: response.columnMappings.flatMap((mapping) => {
      const sourceColumn = (
        mapping.sourceColumn ??
        mapping.suggestedSourceColumn ??
        ''
      ).trim();

      if (sourceColumn.length === 0 || usedSourceColumns.has(sourceColumn)) {
        return [];
      }

      usedSourceColumns.add(sourceColumn);
      return [
        {
          sourceColumn,
          targetField: mapping.targetField as ImportFieldName,
        },
      ];
    }),
    materialMappings: response.materialResolutions
      .filter((resolution) => Boolean(resolution.resolvedMaterialId))
      .map((resolution) => ({
        sourceMaterialName: resolution.sourceMaterialName,
        targetMaterialId: resolution.resolvedMaterialId ?? null,
      })),
  };
}

function createImportMappingSession(
  sessionId: string,
  filePath: string,
  response: ImportFileResponse,
  existing?: ImportMappingSession,
): ImportMappingSession {
  return {
    ...existing,
    sessionId,
    filePath,
    preview: response,
    options: existing?.options ?? buildImportOptionsFromResponse(response),
    newMaterials: existing?.newMaterials ?? [],
    hasPendingChanges: false,
  };
}

function countMissingImportFields(response: ImportResponse): number {
  const sourceByField = new Set(
    response.columnMappings
      .filter((mapping) => Boolean(mapping.sourceColumn))
      .map((mapping) => mapping.targetField),
  );

  return requiredImportFieldNames.filter((field) => !sourceByField.has(field)).length;
}

function countReviewableOptionalImportFields(response: ImportResponse): number {
  const mappedSourceColumns = new Set(
    response.columnMappings
      .map((mapping) => mapping.sourceColumn?.trim() ?? '')
      .filter((sourceColumn) => sourceColumn.length > 0),
  );
  const unmatchedOptionalFieldCount = response.columnMappings.filter(
    (mapping) =>
      optionalImportFieldNames.includes(mapping.targetField as (typeof optionalImportFieldNames)[number]) &&
      (mapping.sourceColumn?.trim().length ?? 0) === 0,
  ).length;
  const spareSourceColumnCount = response.availableColumns.filter(
    (column) => !mappedSourceColumns.has(column.trim()),
  ).length;

  return Math.min(unmatchedOptionalFieldCount, spareSourceColumnCount);
}

function hasResolvedImportMaterial(
  resolution: ImportMaterialResolution,
  session?: ImportMappingSession,
): boolean {
  const draftExists =
    session?.newMaterials.some(
      (material) => material.sourceMaterialName === resolution.sourceMaterialName,
    ) ?? false;
  const explicitMappingExists =
    session?.options.materialMappings.some(
      (mapping) =>
        mapping.sourceMaterialName === resolution.sourceMaterialName &&
        Boolean(mapping.targetMaterialId),
    ) ?? false;

  return (
    draftExists ||
    explicitMappingExists ||
    Boolean(resolution.resolvedMaterialId)
  );
}

function countUnresolvedImportMaterials(
  response: ImportResponse,
  session?: ImportMappingSession,
): number {
  return response.materialResolutions.filter(
    (resolution) => !hasResolvedImportMaterial(resolution, session),
  ).length;
}

function shouldRequireImportReview(
  response: ImportResponse,
  session?: ImportMappingSession,
): boolean {
  return (
    countMissingImportFields(response) > 0 ||
    countReviewableOptionalImportFields(response) > 0 ||
    countUnresolvedImportMaterials(response, session) > 0
  );
}

function describeImportReview(
  filePath: string,
  response: ImportResponse,
  session?: ImportMappingSession,
): string {
  const fileName = fileNameFromPath(filePath);
  const missingFields = countMissingImportFields(response);
  const reviewableOptionalFields = countReviewableOptionalImportFields(response);
  const unresolvedMaterials = countUnresolvedImportMaterials(response, session);
  const notes: string[] = [];

  if (missingFields > 0) {
    notes.push(`${missingFields} field mapping(s) still need attention`);
  }

  if (reviewableOptionalFields > 0) {
    notes.push(
      `${reviewableOptionalFields} optional field mapping(s) can still be assigned from spare source columns`,
    );
  }

  if (unresolvedMaterials > 0) {
    notes.push(`${unresolvedMaterials} material resolution(s) still need attention`);
  }

  if (notes.length === 0) {
    return `Review complete for ${fileName}. Finalize the import when the preview looks right.`;
  }

  return `Review ${fileName}. ${notes.join('; ')} before finalizing the import.`;
}

function responseLooksLikeImportPreparationFailure(response: ImportFileResponse): boolean {
  return (
    (!response.success && Boolean(response.error)) ||
    response.parts.length === 0 &&
    response.availableColumns.length === 0 &&
    response.columnMappings.length === 0 &&
    response.materialResolutions.length === 0 &&
    response.errors.length > 0
  );
}

function getBridgeErrorMessage(error?: BridgeError | null, fallback?: string): string {
  return (
    error?.userMessage ??
    error?.message ??
    fallback ??
    'The desktop host could not complete the request.'
  );
}

function describeNestingResult(materialName: string, response: NestResponse): string {
  if (response.success) {
    return `${materialName}: ${response.summary.totalSheets} sheet(s), ${response.summary.totalPlaced} placed part(s), and ${response.summary.totalUnplaced} unplaced item(s).`;
  }

  if (response.unplacedItems.length > 0) {
    return `${materialName}: no full layout was produced. Review ${response.unplacedItems.length} unplaced item(s).`;
  }

  return 'No nesting result is available yet.';
}

function describeBatchNestingResult(response: BatchNestResponse): string {
  if (response.materialResults.length === 0) {
    const legacyResult = response.legacyResult;
    if (
      legacyResult &&
      (legacyResult.sheets.length > 0 ||
        legacyResult.placements.length > 0 ||
        legacyResult.unplacedItems.length > 0)
    ) {
      if (legacyResult.sheets.length === 0 && legacyResult.placements.length === 0) {
        return 'Nesting finished without producing any sheet layouts. Review the empty-result details on Results.';
      }

      return describeNestingResult('Current run', legacyResult);
    }

    return 'No nesting result is available yet.';
  }

  const totals = response.materialResults.reduce(
    (summary, result) => ({
      sheets: summary.sheets + result.result.summary.totalSheets,
      placed: summary.placed + result.result.summary.totalPlaced,
      unplaced: summary.unplaced + result.result.summary.totalUnplaced,
    }),
    {
      sheets: 0,
      placed: 0,
      unplaced: 0,
    },
  );

  return `${response.materialResults.length} material(s): ${totals.sheets} sheet(s), ${totals.placed} placed part(s), and ${totals.unplaced} unplaced item(s).`;
}

function createWorkbookImportMappingSession(
  sessionId: string,
  filePath: string,
  started: ImportSessionResponse,
  preview: ImportSessionResponse,
  projectKind: ProjectKind,
  savedConfiguration?: ImportConfiguration,
  optimizationGroups: OptimizationGroup[] = [],
): ImportMappingSession {
  const workbook = started.workbook!;
  const firstWorksheet =
    workbook.worksheets.find(
      (worksheet) => worksheet.worksheetName === workbook.initialWorksheetName,
    ) ?? workbook.worksheets[0];
  const firstOptions = { ...buildImportOptionsFromResponse(preview), projectKind };
  const worksheets = createWorkbookWorksheetDrafts(
    sessionId,
    workbook,
    preview,
    firstOptions,
    savedConfiguration,
    optimizationGroups,
  );
  const activeWorksheet = worksheets.find((worksheet) => worksheet.selected) ?? worksheets[0];

  return {
    sessionId,
    filePath,
    preview: activeWorksheet?.preview ?? preview,
    options: activeWorksheet?.options ?? firstOptions,
    newMaterials: activeWorksheet?.newMaterials ?? [],
    hasPendingChanges: activeWorksheet?.hasPendingChanges ?? false,
    workbook,
    activeWorksheetName: activeWorksheet?.worksheet.worksheetName ?? firstWorksheet.worksheetName,
    worksheets,
  };
}

function describeOptimizationGroupRun(response: BatchNestResponse): string {
  const groupResults = response.optimizationGroupResults ?? [];
  if (groupResults.length === 0) {
    return describeBatchNestingResult(response);
  }

  const succeeded = groupResults.filter((result) => result.success).length;
  const failed = groupResults.length - succeeded;
  const sheets = groupResults.reduce(
    (total, group) =>
      total +
      group.materialResults.reduce(
        (groupTotal, materialResult) =>
          groupTotal + materialResult.result.summary.totalSheets,
        0,
      ),
    0,
  );

  return failed > 0
    ? `Run All completed with partial success: ${succeeded} Optimization Group(s) succeeded, ${failed} failed, and ${sheets} isolated sheet(s) were retained.`
    : `${succeeded} Optimization Group(s) completed in order with ${sheets} isolated sheet(s).`;
}

function batchForOptimizationGroup(
  response: BatchNestResponse,
  result: OptimizationGroupNestResult | undefined,
): BatchNestResponse {
  if (!result) {
    return response;
  }

  return {
    executionId: response.executionId,
    success: result.success,
    partialSuccess: false,
    legacyResult: result.legacyResult ?? null,
    materialResults: result.materialResults,
    optimizationGroupResults: [result],
  };
}

function getNestableParts(importResponse: ImportResponse, material?: Material): ImportResponse['parts'] {
  if (!material) {
    return [];
  }

  return getReadyParts(importResponse).filter(
    (part) =>
      part.materialName === material.name,
  );
}

function collectProjectMaterialSnapshots(
  materials: Material[],
  importResponse: ImportResponse,
  selectedMaterialId: string | undefined,
  lastNestMaterial: Material | undefined,
  fallbackSnapshots: ProjectMaterialSnapshot[] = [],
): ProjectMaterialSnapshot[] {
  const relevantNames = new Set(getDistinctImportedMaterialNames(importResponse));
  const relevantIds = new Set<string>();

  if (selectedMaterialId) {
    relevantIds.add(selectedMaterialId);
  }

  if (lastNestMaterial?.materialId) {
    relevantIds.add(lastNestMaterial.materialId);
  }

  const snapshots = new Map<string, ProjectMaterialSnapshot>();
  for (const material of materials) {
    if (
      relevantNames.has(material.name) ||
      relevantIds.has(material.materialId)
    ) {
      snapshots.set(material.materialId, { ...material });
    }
  }

  for (const snapshot of [
    ...fallbackSnapshots,
    ...(lastNestMaterial ? [{ ...lastNestMaterial }] : []),
  ]) {
    const relevant =
      relevantIds.has(snapshot.materialId) || relevantNames.has(snapshot.name);

    if (!relevant || snapshots.has(snapshot.materialId)) {
      continue;
    }

    snapshots.set(snapshot.materialId, { ...snapshot });
  }

  return sortByName(Array.from(snapshots.values()));
}

function buildOptimizationGroups(
  state: AppState,
  lastNestingResult: NestResponse | null,
  lastBatchNestingResult: BatchNestResponse | null,
): OptimizationGroup[] {
  if (state.optimizationGroups.length === 0) {
    return [];
  }

  if (state.optimizationGroups.length > 1) {
    if (!lastNestingResult && !lastBatchNestingResult) {
      return state.optimizationGroups;
    }

    return state.optimizationGroups.map((group) =>
      group.optimizationGroupId === state.activeOptimizationGroupId
        ? {
            ...group,
            lastNestingResult,
            lastBatchNestingResult,
            resultStatus: 'valid',
          }
        : group,
    );
  }

  const existingGroup = state.optimizationGroups[0];
  const sourceName = state.selectedFilePath
    ? fileNameFromPath(state.selectedFilePath).replace(/\.[^.]+$/, '')
    : 'Parts';

  return [
    {
      optimizationGroupId:
        existingGroup?.optimizationGroupId || state.projectId || 'optimization-group-1',
      name: existingGroup?.name || sourceName || 'Parts',
      order: existingGroup?.order ?? 0,
      origin: existingGroup?.origin ?? 'project',
      parts: state.importResponse.parts,
      stockLength: existingGroup?.stockLength ?? null,
      requiredPieces: existingGroup?.requiredPieces ?? [],
      stockGroups: existingGroup?.stockGroups ?? [],
      lastNestingResult,
      lastBatchNestingResult,
      resultStatus:
        lastNestingResult || lastBatchNestingResult
          ? 'valid'
          : existingGroup?.resultStatus ?? 'none',
    },
  ];
}

function syncPartsToOptimizationGroups(
  groups: OptimizationGroup[],
  nextParts: ImportResponse['parts'],
  targetOptimizationGroupId?: string,
): OptimizationGroup[] {
  if (groups.length === 0) {
    return groups;
  }

  const nextPartsById = new Map(nextParts.map((part) => [part.rowId, part]));
  const assignedIds = new Set<string>();
  const syncedGroups = groups.map((group) => {
    const parts = group.parts
      .map((part) => nextPartsById.get(part.rowId))
      .filter((part): part is ImportResponse['parts'][number] => Boolean(part));
    parts.forEach((part) => assignedIds.add(part.rowId));
    const changed =
      parts.length !== group.parts.length ||
      parts.some(
        (part, index) =>
          !arePartRowsEqual(part, group.parts[index]),
      );

    return changed
      ? {
          ...group,
          parts,
          lastNestingResult: null,
          lastBatchNestingResult: null,
          resultStatus: 'none' as const,
        }
      : group;
  });
  const unassignedParts = nextParts.filter((part) => !assignedIds.has(part.rowId));
  if (unassignedParts.length === 0) {
    return syncedGroups;
  }

  const targetIndex = Math.max(
    0,
    syncedGroups.findIndex(
      (group) => group.optimizationGroupId === targetOptimizationGroupId,
    ),
  );
  return syncedGroups.map((group, index) =>
    index === targetIndex
      ? {
          ...group,
          parts: [...group.parts, ...unassignedParts],
          lastNestingResult: null,
          lastBatchNestingResult: null,
          resultStatus: 'none' as const,
        }
      : group,
  );
}

function invalidateOptimizationGroupResults(
  groups: OptimizationGroup[],
  isAffected: (group: OptimizationGroup) => boolean,
): OptimizationGroup[] {
  return groups.map((group) =>
    isAffected(group) &&
    (group.lastStockLengthOptimizationResult || group.lastNestingResult || group.lastBatchNestingResult)
      ? { ...group, resultStatus: 'stale' as const }
      : group,
  );
}

function canDisplayOptimizationGroupResult(
  group: OptimizationGroup | undefined,
): boolean {
  return Boolean(group && group.resultStatus !== 'stale');
}

function getOptimizationGroupDisplayState(
  group: OptimizationGroup | undefined,
  currentMessage: string,
): Pick<AppState, 'nestResponse' | 'batchNestResponse' | 'nestingMessage'> {
  const isStale = group?.resultStatus === 'stale';
  return {
    nestResponse: canDisplayOptimizationGroupResult(group)
      ? group?.lastNestingResult ?? emptyNestResponse
      : emptyNestResponse,
    batchNestResponse: canDisplayOptimizationGroupResult(group)
      ? group?.lastBatchNestingResult ?? emptyBatchNestResponse
      : emptyBatchNestResponse,
    nestingMessage: isStale
      ? `${group.name} has stale results. Re-run it before inspecting panels.`
      : currentMessage,
  };
}

function clearActiveResultsWhenAffected(
  state: AppState,
  affectedGroupIds: ReadonlySet<string>,
  staleMessage: string,
): Pick<AppState, 'nestResponse' | 'batchNestResponse' | 'nestingMessage'> {
  return affectedGroupIds.has(state.activeOptimizationGroupId ?? '')
    ? {
        nestResponse: emptyNestResponse,
        batchNestResponse: emptyBatchNestResponse,
        nestingMessage: staleMessage,
      }
    : {
        nestResponse: state.nestResponse,
        batchNestResponse: state.batchNestResponse,
        nestingMessage: state.nestingMessage,
      };
}

function arePartRowsEqual(left: PartRow, right: PartRow | undefined): boolean {
  return Boolean(
    right &&
      left.rowId === right.rowId &&
      left.importedId === right.importedId &&
      left.lengthText === right.lengthText &&
      left.length === right.length &&
      left.widthText === right.widthText &&
      left.width === right.width &&
      left.quantityText === right.quantityText &&
      left.quantity === right.quantity &&
      left.materialName === right.materialName &&
      left.group === right.group &&
      left.isManual === right.isManual &&
      left.sheetNumber === right.sheetNumber &&
      left.rowNumber === right.rowNumber &&
      left.columnNumber === right.columnNumber &&
      left.validationStatus === right.validationStatus &&
      left.validationMessages.length === right.validationMessages.length &&
      left.validationMessages.every(
        (message, index) => message === right.validationMessages[index],
      ),
  );
}

function buildProjectRecord(
  state: AppState,
  settingsOverride?: ProjectSettings,
): ProjectRecord {
  const materialSnapshots = collectProjectMaterialSnapshots(
    state.materials,
    state.importResponse,
    state.selectedMaterialId,
    state.lastNestMaterial,
    state.projectMaterialSnapshots,
  );
  const projectSettings = settingsOverride ?? state.projectSettings;
  const batchNestResponse =
    state.batchNestResponse.materialResults.length > 0
      ? state.batchNestResponse
      : buildBatchFromLegacy(
          state.nestResponse,
          state.lastNestMaterial,
          materialSnapshots,
          state.selectedMaterialId,
        );
  const lastNestingResult =
    state.nestResponse.sheets.length > 0 ||
    state.nestResponse.unplacedItems.length > 0
      ? state.nestResponse
      : null;
  const lastBatchNestingResult =
    batchNestResponse.materialResults.length > 0 ? batchNestResponse : null;

  return {
    version: currentProjectVersion,
    projectKind: state.projectKind,
    projectId: state.projectId,
    metadata: mapMetadataToBridge(state.projectMetadata),
    settings: projectSettings,
    materialSnapshots,
    state: {
      sourceFilePath: state.selectedFilePath ?? null,
      importSource: state.importSource ?? null,
      importConfiguration: state.importConfiguration ?? null,
      optimizationGroups: buildOptimizationGroups(
        state,
        lastNestingResult,
        lastBatchNestingResult,
      ),
      parts: state.importResponse.parts,
      selectedMaterialId: state.selectedMaterialId ?? null,
      lastNestingResult,
      lastBatchNestingResult,
      extrusionLayout: state.extrusionLayout,
    },
  };
}

function hasExistingImportSource(state: AppState): boolean {
  return Boolean(
    state.importSource ||
      state.importConfiguration ||
      (state.selectedFilePath && state.importResponse.parts.length > 0),
  );
}

function buildStiffenerProjectRecord(
  state: AppState,
  settingsOverride?: ProjectSettings,
): ProjectRecord {
  const materialSnapshots = collectProjectMaterialSnapshots(
    state.materials,
    state.importResponse,
    state.selectedMaterialId,
    state.lastNestMaterial,
    state.projectMaterialSnapshots,
  );

  return {
    version: currentProjectVersion,
    projectKind: state.projectKind,
    projectId: state.projectId,
    metadata: mapMetadataToBridge(state.projectMetadata),
    settings: settingsOverride ?? state.projectSettings,
    materialSnapshots,
    state: {
      sourceFilePath: state.selectedFilePath ?? null,
      importSource: state.importSource ?? null,
      importConfiguration: state.importConfiguration ?? null,
      optimizationGroups: buildOptimizationGroups(state, null, null),
      parts: state.importResponse.parts,
      selectedMaterialId: state.selectedMaterialId ?? null,
      lastNestingResult: null,
      lastBatchNestingResult: null,
      extrusionLayout: state.extrusionLayout,
    },
  };
}

function findSnapshotMaterial(
  snapshots: ProjectMaterialSnapshot[],
  materialId?: string | null,
  materialName?: string | null,
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

function isMaterialRelevantToProject(state: AppState, material: Material): boolean {
  return (
    state.selectedMaterialId === material.materialId ||
    state.lastNestMaterial?.materialId === material.materialId ||
    getDistinctImportedMaterialNames(state.importResponse).includes(material.name)
  );
}

function markProjectDirty(nextState: AppState, message: string): AppState {
  return {
    ...nextState,
    projectDirty: true,
    projectMessage: message,
  };
}

function reducer(state: AppState, action: AppAction): AppState {
  switch (action.type) {
    case 'route-changed':
      return {
        ...state,
        activeRoute: guardProjectRoute(state.projectKind, action.route),
      };
    case 'bridge-updated': {
      return {
        ...state,
        bridge: action.snapshot,
      };
    }
    case 'materials-request-started':
      return {
        ...state,
        materialsBusy: true,
        materialsMessage: action.message,
      };
    case 'materials-request-finished':
      return {
        ...state,
        materialsBusy: false,
        materialsMessage: action.message,
      };
    case 'materials-loaded':
      return {
        ...state,
        materialsBusy: false,
        materials: sortMaterials(action.materials),
        materialLibraryLocation: action.materialLibraryLocation,
        materialLibraryUnavailable: false,
        selectedMaterialId: action.selectedMaterialId,
        materialsMessage: action.message,
      };
    case 'materials-failed':
      return {
        ...state,
        materialsBusy: false,
        materialLibraryLocation:
          action.materialLibraryLocation ?? state.materialLibraryLocation,
        materialLibraryUnavailable:
          action.libraryUnavailable ?? state.materialLibraryUnavailable,
        materialsMessage: action.message,
      };
    case 'material-selected':
      return markProjectDirty(
        {
          ...state,
          selectedMaterialId: action.materialId,
        },
        'Active material changed. Save the project to keep this selection.',
      );
    case 'material-created': {
      const materials = sortMaterials([...state.materials, action.material]);
      return markProjectDirty(
        {
          ...state,
          materialsBusy: false,
          materials,
          selectedMaterialId: action.material.materialId,
          materialsMessage: action.message,
        },
        `Material context changed. Save the project to snapshot ${action.material.name}.`,
      );
    }
    case 'material-updated': {
      const existingMaterial = state.materials.find(
        (material) => material.materialId === action.material.materialId,
      );
      const affectedMaterialNames = new Set(
        [existingMaterial?.name, action.material.name].filter(
          (name): name is string => Boolean(name),
        ),
      );
      const affectedGroupIds = new Set(
        state.optimizationGroups
          .filter((group) =>
            group.parts.some((part) => affectedMaterialNames.has(part.materialName)),
          )
          .map((group) => group.optimizationGroupId),
      );
      const materials = sortMaterials(
        state.materials.map((material) =>
          material.materialId === action.material.materialId ? action.material : material,
        ),
      );
      const nextState = {
        ...state,
        materialsBusy: false,
        materials,
        optimizationGroups: invalidateOptimizationGroupResults(
          state.optimizationGroups,
          (group) => affectedGroupIds.has(group.optimizationGroupId),
        ),
        ...clearActiveResultsWhenAffected(
          state,
          affectedGroupIds,
          'Material details changed. Re-run the active Optimization Group before inspecting panels.',
        ),
        materialsMessage: action.message,
      };

      return existingMaterial &&
        (isMaterialRelevantToProject(state, existingMaterial) ||
          isMaterialRelevantToProject(state, action.material))
        ? markProjectDirty(
            nextState,
            `Material details changed. Save the project to refresh ${action.material.name}.`,
          )
        : nextState;
    }
    case 'material-deleted': {
      const deletedMaterial = state.materials.find(
        (material) => material.materialId === action.materialId,
      );
      const materials = state.materials.filter(
        (material) => material.materialId !== action.materialId,
      );
      const affectedGroupIds = new Set(
        state.optimizationGroups
          .filter((group) =>
            group.parts.some(
              (part) => part.materialName === deletedMaterial?.name,
            ),
          )
          .map((group) => group.optimizationGroupId),
      );
      const nextState = {
        ...state,
        materialsBusy: false,
        materials,
        optimizationGroups: invalidateOptimizationGroupResults(
          state.optimizationGroups,
          (group) => affectedGroupIds.has(group.optimizationGroupId),
        ),
        ...clearActiveResultsWhenAffected(
          state,
          affectedGroupIds,
          'A material used by the active Optimization Group was deleted. Re-run after resolving its materials.',
        ),
        selectedMaterialId: pickMaterialId(
          materials,
          state.importResponse,
          state.selectedMaterialId === action.materialId
            ? undefined
            : state.selectedMaterialId,
        ),
        materialsMessage: action.message,
      };

      return deletedMaterial && isMaterialRelevantToProject(state, deletedMaterial)
        ? markProjectDirty(
            nextState,
            'Material context changed. Save the project if the deleted material should remain out of the file snapshot.',
          )
        : nextState;
    }
    case 'import-started':
      return {
        ...state,
        importBusy: true,
        importPhase: action.phase,
        importProgress: undefined,
        importMessage: action.message,
      };
    case 'import-progressed':
      return {
        ...state,
        importProgress: action.progress,
        importMessage: `${action.progress.label}…`,
      };
    case 'import-selection-cancelled':
      return {
        ...state,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        importMessage: action.message,
      };
    case 'import-mapping-ready':
      return {
        ...state,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        activeRoute: 'import',
        importMappingSession: action.session,
        importMessage: action.message,
      };
    case 'import-mapping-updated':
      return {
        ...state,
        importMappingSession: action.session,
      };
    case 'import-mapping-cancelled':
      return {
        ...state,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        importMappingSession: undefined,
        importMessage: action.message,
      };
    case 'import-finished':
      return markProjectDirty(
        {
          ...state,
          importBusy: false,
          importPhase: undefined,
          importProgress: undefined,
          importMappingSession: undefined,
          selectedFilePath: action.filePath,
          importSource: action.project?.state.importSource ?? undefined,
          importConfiguration: action.project?.state.importConfiguration ?? undefined,
          lastImportReceipt: action.resultCounts,
          preImportProject: action.undoProject,
          importResponse: action.response,
          optimizationGroups:
            action.project?.state.optimizationGroups ??
            syncPartsToOptimizationGroups(
              state.optimizationGroups,
              action.response.parts,
              state.activeOptimizationGroupId,
            ),
          nestResponse: emptyNestResponse,
          batchNestResponse: emptyBatchNestResponse,
          lastNestMaterial: undefined,
          selectedMaterialId: action.selectedMaterialId,
          importMessage: action.message,
          nestingMessage:
            'Import is ready. Review or correct rows inline, then run nesting when the ready materials look correct.',
        },
        'Imported rows changed. Save the project to capture the latest source data.',
      );
    case 'part-row-operation-started':
      return {
        ...state,
        partMutationBusy: true,
        importMessage: action.message,
      };
    case 'part-rows-replaced':
      return markProjectDirty(
        {
          ...state,
          partMutationBusy: false,
          importResponse: action.response,
          nestResponse: emptyNestResponse,
          batchNestResponse: emptyBatchNestResponse,
          lastNestMaterial: undefined,
          selectedMaterialId: action.selectedMaterialId,
          optimizationGroups: syncPartsToOptimizationGroups(
            state.optimizationGroups,
            action.response.parts,
            action.targetOptimizationGroupId ?? state.activeOptimizationGroupId,
          ),
          importMessage: action.message,
          nestingMessage:
            'Imported rows changed. Re-run nesting after the corrected rows are ready.',
        },
        'Imported rows changed. Save the project to capture the latest source data.',
      );
    case 'part-row-operation-failed':
      return {
        ...state,
        partMutationBusy: false,
        importMessage: action.message,
      };
    case 'import-failed':
      return {
        ...state,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        importMessage: action.message,
      };
    case 'nesting-started':
      return {
        ...state,
        nestingBusy: true,
        nestingMessage: action.message,
      };
    case 'nesting-finished': {
      const groupResults = action.optimizationGroupResults ?? [];
      const resultsByGroupId = new Map(
        groupResults.map((result) => [result.optimizationGroupId, result]),
      );
      return markProjectDirty(
        {
          ...state,
          nestingBusy: false,
          nestResponse: action.response,
          batchNestResponse: action.batchResponse,
          lastNestMaterial: action.material,
          nestingMessage: action.message,
          optimizationGroups: state.optimizationGroups.map((group) => {
            const groupResult = resultsByGroupId.get(group.optimizationGroupId);
            if (groupResult) {
              return {
                ...group,
                lastNestingResult: groupResult.legacyResult ?? null,
                lastBatchNestingResult: batchForOptimizationGroup(
                  action.batchResponse,
                  groupResult,
                ),
                resultStatus: 'valid',
              };
            }

            return groupResults.length === 0 &&
              group.optimizationGroupId === state.activeOptimizationGroupId
              ? {
                  ...group,
                  lastNestingResult: action.response,
                  lastBatchNestingResult: action.batchResponse,
                  resultStatus: 'valid' as const,
                }
              : group;
          }),
        },
        'Nesting results changed. Save the project to keep this layout with its material snapshot.',
      );
    }
    case 'import-undone': {
      if (!state.preImportProject) return state;
      const project = state.preImportProject;
      return markProjectDirty({
        ...state,
        selectedFilePath: project.state.sourceFilePath ?? undefined,
        importSource: project.state.importSource ?? undefined,
        importConfiguration: project.state.importConfiguration ?? undefined,
        importResponse: getProjectImportResponse(project),
        optimizationGroups: project.state.optimizationGroups,
        activeOptimizationGroupId: project.state.optimizationGroups[0]?.optimizationGroupId,
        lastImportReceipt: undefined,
        preImportProject: undefined,
        importMessage: 'The last import was undone. Save the project to keep this change.',
      }, 'Import undone.');
    }
    case 'nesting-failed':
      return {
        ...state,
        nestingBusy: false,
        nestingMessage: action.message,
      };
    case 'project-created':
      return {
        ...state,
        activeRoute: 'overview',
        projectKind: action.projectKind,
        importResponse: emptyImportResponse,
        nestResponse: emptyNestResponse,
        batchNestResponse: emptyBatchNestResponse,
        selectedMaterialId: undefined,
        lastNestMaterial: undefined,
        selectedFilePath: undefined,
        importSource: undefined,
        importConfiguration: undefined,
        importMappingSession: undefined,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        importMessage: defaultImportMessage,
        nestingMessage: defaultNestingMessage,
        reportMessage: defaultReportMessage,
        reportBusy: false,
        stiffenerMessage: defaultStiffenerMessage,
        stiffenerBusy: false,
        extrusionMessage: defaultExtrusionMessage,
        extrusionBusy: false,
        projectMetadata: action.metadata,
        projectSettings: action.settings,
        stiffenerTakeoffReport: null,
        extrusionLayout: defaultExtrusionLayoutState,
        extrusionReport: null,
        projectId: action.projectId ?? '',
        projectFilePath: undefined,
        projectMaterialSnapshots: [],
        optimizationGroups: action.optimizationGroups ?? [],
        activeOptimizationGroupId:
          action.optimizationGroups?.[0]?.optimizationGroupId,
        projectMessage: action.message,
        projectBusy: false,
        projectDirty: false,
        partMutationBusy: false,
        lastSavedAt: undefined,
      };
    case 'project-opened': {
      const importResponse = getProjectImportResponse(action.project);
      const openedGroup = action.project.state.optimizationGroups[0];
      const openedGroupCanDisplay = canDisplayOptimizationGroupResult(openedGroup);
      const nestResponse = openedGroupCanDisplay
        ? openedGroup?.lastNestingResult ??
          action.project.state.lastNestingResult ??
          emptyNestResponse
        : emptyNestResponse;
      const projectMetadata = mapMetadataFromBridge(action.project.metadata);
      const projectSettings =
        action.settings ??
        normalizeProjectSettings(
          action.project.settings,
          projectMetadata,
          undefined,
          action.project.projectKind,
        );
      const batchNestResponse = openedGroupCanDisplay
        ? openedGroup?.lastBatchNestingResult ??
          getProjectBatchNestResponse(action.project, action.lastNestMaterial)
        : emptyBatchNestResponse;

      return {
        ...state,
        activeRoute: 'overview',
        projectKind: action.project.projectKind ?? 'sheet',
        projectBusy: false,
        projectDirty: false,
        projectMetadata,
        projectSettings,
        projectId: action.project.projectId,
        projectFilePath: action.filePath,
        projectMaterialSnapshots: sortByName(action.project.materialSnapshots),
        optimizationGroups: action.project.state.optimizationGroups,
        activeOptimizationGroupId:
          action.project.state.optimizationGroups[0]?.optimizationGroupId,
        lastSavedAt: new Date().toISOString(),
        selectedFilePath: action.project.state.sourceFilePath ?? undefined,
        importSource: action.project.state.importSource ?? undefined,
        importConfiguration: action.project.state.importConfiguration ?? undefined,
        importMappingSession: undefined,
        importBusy: false,
        importPhase: undefined,
        importResponse,
        nestResponse,
        batchNestResponse,
        selectedMaterialId: action.selectedMaterialId,
        lastNestMaterial: action.lastNestMaterial,
        partMutationBusy: false,
        reportBusy: false,
        stiffenerBusy: false,
        extrusionBusy: false,
        extrusionLayout:
          action.project.state.extrusionLayout ?? defaultExtrusionLayoutState,
        extrusionReport: null,
        stiffenerTakeoffReport: null,
        projectMessage: action.message,
        reportMessage: defaultReportMessage,
        stiffenerMessage: defaultStiffenerMessage,
        importMessage:
          importResponse.parts.length > 0
            ? describeImportResult(
                action.project.state.sourceFilePath ?? action.filePath,
                importResponse,
              )
            : defaultImportMessage,
        nestingMessage: !openedGroupCanDisplay
          ? `${openedGroup?.name ?? 'This Optimization Group'} has stale results. Re-run it before inspecting panels.`
          : batchNestResponse.materialResults.length > 1
            ? describeBatchNestingResult(batchNestResponse)
            : nestResponse.sheets.length > 0 || nestResponse.unplacedItems.length > 0
              ? describeNestingResult(
                  action.lastNestMaterial?.name ?? 'Saved project',
                  nestResponse,
                )
            : defaultNestingMessage,
      };
    }
    case 'project-saved':
      return {
        ...state,
        projectBusy: false,
        projectDirty: false,
        projectId: action.project.projectId,
        projectKind: action.project.projectKind ?? 'sheet',
        projectFilePath: action.filePath,
        projectSettings:
          action.settings ??
          normalizeProjectSettings(
            action.project.settings,
            mapMetadataFromBridge(action.project.metadata),
            undefined,
            action.project.projectKind,
          ),
        stiffenerTakeoffReport: null,
        extrusionLayout:
          action.project.state.extrusionLayout ?? state.extrusionLayout,
        extrusionReport: null,
        projectMaterialSnapshots: sortByName(action.project.materialSnapshots),
        optimizationGroups: action.project.state.optimizationGroups,
        activeOptimizationGroupId:
          action.project.state.optimizationGroups.some(
            (group) => group.optimizationGroupId === state.activeOptimizationGroupId,
          )
            ? state.activeOptimizationGroupId
            : action.project.state.optimizationGroups[0]?.optimizationGroupId,
        projectMessage: action.message,
        lastSavedAt: new Date().toISOString(),
      };
    case 'project-operation-started':
      return {
        ...state,
        projectBusy: true,
        projectMessage: action.message,
      };
    case 'project-operation-finished':
      return {
        ...state,
        projectBusy: false,
        projectMessage: action.message,
      };
    case 'project-operation-failed':
      return {
        ...state,
        projectBusy: false,
        projectMessage: action.message,
      };
    case 'generation-operation-started':
      return {
        ...state,
        generationBusy: true,
        generationProgress: undefined,
        projectMessage: action.message,
      };
    case 'generation-progressed':
      return {
        ...state,
        generationProgress: action.progress,
        projectMessage: action.progress.label,
      };
    case 'generation-operation-finished':
      return {
        ...state,
        generationBusy: false,
        generationProgress: undefined,
        projectMessage: action.message,
      };
    case 'project-kind-changed':
      return {
        ...state,
        activeRoute: 'overview',
        projectKind: action.project.projectKind,
        importResponse: emptyImportResponse,
        nestResponse: emptyNestResponse,
        batchNestResponse: emptyBatchNestResponse,
        selectedMaterialId: undefined,
        lastNestMaterial: undefined,
        selectedFilePath: undefined,
        importSource: undefined,
        importConfiguration: undefined,
        importMappingSession: undefined,
        importBusy: false,
        importPhase: undefined,
        importProgress: undefined,
        importMessage: defaultImportMessage,
        nestingMessage: defaultNestingMessage,
        reportMessage: defaultReportMessage,
        stiffenerMessage: defaultStiffenerMessage,
        extrusionMessage: defaultExtrusionMessage,
        projectSettings: normalizeProjectSettings(
          action.project.settings,
          state.projectMetadata,
          undefined,
          action.project.projectKind,
        ),
        projectMaterialSnapshots: [],
        optimizationGroups: [],
        activeOptimizationGroupId: undefined,
        stiffenerTakeoffReport: null,
        extrusionLayout: defaultExtrusionLayoutState,
        extrusionReport: null,
        projectMessage: action.message,
        projectBusy: false,
        projectDirty: true,
        partMutationBusy: false,
      };
    case 'optimization-group-activated': {
      const activeGroup = state.optimizationGroups.find(
        (group) => group.optimizationGroupId === action.optimizationGroupId,
      );
      return {
        ...state,
        activeOptimizationGroupId: action.optimizationGroupId,
        ...getOptimizationGroupDisplayState(activeGroup, state.nestingMessage),
        lastNestMaterial: undefined,
      };
    }
    case 'optimization-groups-updated': {
      const importResponse = getProjectImportResponse(action.project);
      const nextActiveOptimizationGroupId =
        action.activeOptimizationGroupId &&
        action.project.state.optimizationGroups.some(
          (group) =>
            group.optimizationGroupId === action.activeOptimizationGroupId,
        )
          ? action.activeOptimizationGroupId
          : action.project.state.optimizationGroups[0]?.optimizationGroupId;
      const activeGroup = action.project.state.optimizationGroups.find(
        (group) =>
          group.optimizationGroupId === nextActiveOptimizationGroupId,
      );
      return markProjectDirty(
        {
          ...state,
          projectBusy: false,
          generationBusy: false,
          generationProgress: undefined,
          projectId: action.project.projectId,
          optimizationGroups: action.project.state.optimizationGroups,
          activeOptimizationGroupId: nextActiveOptimizationGroupId,
          importResponse,
          ...getOptimizationGroupDisplayState(activeGroup, state.nestingMessage),
          projectMessage: action.message,
        },
        `${action.message} Save the project to persist this change.`,
      );
    }
    case 'project-metadata-changed':
      return markProjectDirty(
        {
          ...state,
          projectMetadata: action.metadata,
          projectSettings: action.settings,
        },
        action.message,
      );
    case 'project-settings-changed':
      return markProjectDirty(
        {
          ...state,
          projectSettings: action.settings,
          optimizationGroups: action.invalidateNestingResults
            ? invalidateOptimizationGroupResults(
                state.optimizationGroups,
                () => true,
              )
            : state.optimizationGroups,
          nestResponse: action.invalidateNestingResults
            ? emptyNestResponse
            : state.nestResponse,
          batchNestResponse: action.invalidateNestingResults
            ? emptyBatchNestResponse
            : state.batchNestResponse,
          nestingMessage: action.invalidateNestingResults
            ? 'Nesting settings changed. Re-run Optimization Groups before inspecting panels.'
            : state.nestingMessage,
        },
        action.message,
      );
    case 'project-settings-synced':
      return {
        ...state,
        projectSettings: action.settings,
      };
    case 'report-operation-started':
      return {
        ...state,
        reportBusy: true,
        reportMessage: action.message,
      };
    case 'report-operation-finished':
      return {
        ...state,
        reportBusy: false,
        reportMessage: action.message,
      };
    case 'report-operation-failed':
      return {
        ...state,
        reportBusy: false,
        reportMessage: action.message,
      };
    case 'stiffener-operation-started':
      return {
        ...state,
        stiffenerBusy: true,
        stiffenerMessage: action.message,
      };
    case 'stiffener-operation-finished':
      return {
        ...state,
        stiffenerBusy: false,
        stiffenerTakeoffReport: action.report,
        stiffenerMessage: action.message,
      };
    case 'stiffener-operation-failed':
      return {
        ...state,
        stiffenerBusy: false,
        stiffenerMessage: action.message,
      };
    case 'stiffener-operation-cleared':
      return {
        ...state,
        stiffenerBusy: false,
        stiffenerTakeoffReport: null,
        stiffenerMessage: action.message,
      };
    case 'extrusion-layout-changed':
      return markProjectDirty(
        {
          ...state,
          extrusionLayout: action.layout,
          extrusionReport: null,
          extrusionMessage: action.message,
        },
        action.message,
      );
    case 'extrusion-layout-synced':
      return {
        ...state,
        extrusionLayout: action.layout,
      };
    case 'extrusion-operation-started':
      return {
        ...state,
        extrusionBusy: true,
        extrusionMessage: action.message,
      };
    case 'extrusion-operation-finished':
      return {
        ...state,
        extrusionBusy: false,
        extrusionLayout: action.layout ?? state.extrusionLayout,
        extrusionReport: action.report ?? state.extrusionReport,
        extrusionMessage: action.message,
      };
    case 'extrusion-operation-failed':
      return {
        ...state,
        extrusionBusy: false,
        extrusionMessage: action.message,
      };
    default:
      return state;
  }
}

export default function App() {
  const [state, dispatch] = useReducer(reducer, initialState);
  const stateRef = useRef(state);
  stateRef.current = state;
  const [desktopAppSettings, setDesktopAppSettings] =
    useState<DesktopAppSettings>(emptyDesktopAppSettings);
  const [desktopAppSettingsLoaded, setDesktopAppSettingsLoaded] = useState(false);
  const [newProjectDialogOpen, setNewProjectDialogOpen] = useState(false);
  const [unsavedPromptActionLabel, setUnsavedPromptActionLabel] = useState<string | null>(
    null,
  );
  const [pendingImportReplacement, setPendingImportReplacement] = useState<{
    requestedFilePath?: string;
    droppedFile?: File;
  } | null>(null);
  const materialSelectionRef = useRef({
    importResponse: state.importResponse,
    selectedMaterialId: state.selectedMaterialId,
  });
  const activeImportSessionIdRef = useRef<string>();
  const activeGenerationOperationIdRef = useRef<string>();
  const hostReadyNotifiedRef = useRef(false);
  const createNewProjectRef = useRef<() => void | Promise<void>>(() => undefined);
  const startupProjectOpenRef = useRef<(request: OpenProjectRequest) => void | Promise<void>>(
    () => undefined,
  );
  const saveProjectRef = useRef<() => Promise<DesktopCloseSaveResult>>(async () => ({
    status: 'failed',
    message: 'Project save is not ready yet.',
  }));
  const saveProjectAsRef = useRef<() => Promise<DesktopCloseSaveResult>>(async () => ({
    status: 'failed',
    message: 'Project save is not ready yet.',
  }));
  const saveProjectBeforeCloseRef = useRef<() => Promise<DesktopCloseSaveResult>>(
    async () => ({
      status: 'failed',
      message: 'Project save is not ready yet.',
    }),
  );
  const prepareProjectSaveBeforeCloseRef = useRef<() => Promise<DesktopCloseProjectSavePayload>>(
    async () => ({
      status: 'failed',
      message: 'Project save is not ready yet.',
    }),
  );
  const unsavedPromptResolverRef = useRef<((choice: UnsavedPromptChoice) => void) | null>(
    null,
  );

  const applyMaterialLibraryResponse = (
    response: MaterialLibraryOperationResponse,
    options?: {
      message?: string;
      preferredMaterialId?: string;
      selectionContext?: {
        importResponse: ImportResponse;
        selectedMaterialId?: string;
      };
    },
  ) => {
    const selectionContext = options?.selectionContext ?? materialSelectionRef.current;

    dispatch({
      type: 'materials-loaded',
      materials: response.materials,
      materialLibraryLocation: response.libraryLocation,
      selectedMaterialId: pickMaterialId(
        response.materials,
        selectionContext.importResponse,
        selectionContext.selectedMaterialId,
        options?.preferredMaterialId,
      ),
      message:
        options?.message ??
        response.message ??
        `Loaded ${response.materials.length} material(s) from the library.`,
    });

    return response.materials;
  };

  const loadMaterials = async (options?: {
    message?: string;
    preferredMaterialId?: string;
    selectionContext?: {
      importResponse: ImportResponse;
      selectedMaterialId?: string;
    };
  }) => {
    if (!hostBridge.getSnapshot().connected) {
      return undefined;
    }

    const selectionContext = options?.selectionContext ?? materialSelectionRef.current;

    dispatch({
      type: 'materials-request-started',
      message: 'Loading the material library…',
    });

    let reportedLocation: MaterialLibraryLocation | null | undefined;
    try {
      const response = await hostBridge.listMaterials();
      reportedLocation = response.libraryLocation;
      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The material library could not be loaded.',
          ),
        );
      }

      return applyMaterialLibraryResponse(response, {
        message: options?.message,
        preferredMaterialId: options?.preferredMaterialId,
        selectionContext,
      });
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not load the material library.',
      );
      dispatch({
        type: 'materials-failed',
        message,
        materialLibraryLocation: reportedLocation,
        libraryUnavailable: true,
      });
      throw new Error(message);
    }
  };

  const loadDesktopAppSettings = async (): Promise<void> => {
    const bridgeSnapshot = hostBridge.getSnapshot();
    if (
      !bridgeSnapshot.connected ||
      !bridgeSnapshot.handshake.capabilities.includes(
        bridgeMessageTypes.getDesktopAppSettings,
      )
    ) {
      setDesktopAppSettings(emptyDesktopAppSettings);
      setDesktopAppSettingsLoaded(true);
      return;
    }

    try {
      const response = await hostBridge.getDesktopAppSettings();
      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not load the application settings.',
          ),
        );
      }

      setDesktopAppSettings({
        companyLogoPath: response.settings?.companyLogoPath ?? null,
        companyName: response.settings?.companyName ?? null,
      });
      setDesktopAppSettingsLoaded(true);
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not load the application settings.',
        ),
      });
    }
  };

  const saveDesktopAppSettings = async (
    nextSettings: DesktopAppSettings,
  ): Promise<boolean> => {
    if (!hasCapability(bridgeMessageTypes.updateDesktopAppSettings)) {
      dispatch({
        type: 'report-operation-failed',
        message:
          'The connected desktop host has not exposed application settings updates yet.',
      });
      return false;
    }

    try {
      let baseSettings = desktopAppSettings;
      if (!desktopAppSettingsLoaded && hasCapability(bridgeMessageTypes.getDesktopAppSettings)) {
        const response = await hostBridge.getDesktopAppSettings();
        if (response.success) {
          baseSettings = {
            companyLogoPath: response.settings?.companyLogoPath ?? null,
            companyName: response.settings?.companyName ?? null,
          };
          setDesktopAppSettings(baseSettings);
          setDesktopAppSettingsLoaded(true);
        }
      }

      const requestSettings: DesktopAppSettings = {
        companyLogoPath:
          typeof nextSettings.companyLogoPath !== 'undefined'
            ? nextSettings.companyLogoPath ?? null
            : baseSettings.companyLogoPath ?? null,
        companyName:
          typeof nextSettings.companyName !== 'undefined'
            ? nextSettings.companyName?.trim() || null
            : baseSettings.companyName ?? null,
      };

      const response = await hostBridge.updateDesktopAppSettings({
        settings: requestSettings,
      });
      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ??
              'The desktop host could not update the application settings.',
          ),
        );
      }

      const resolvedCompanyLogoPath =
        response.settings?.companyLogoPath ??
        (typeof requestSettings.companyLogoPath !== 'undefined'
          ? requestSettings.companyLogoPath ?? null
          : baseSettings.companyLogoPath ?? null);
      const resolvedCompanyName =
        response.settings?.companyName ??
        (typeof requestSettings.companyName !== 'undefined'
          ? requestSettings.companyName?.trim() || null
          : baseSettings.companyName ?? null);

      setDesktopAppSettings({
        companyLogoPath: resolvedCompanyLogoPath,
        companyName: resolvedCompanyName,
      });
      setDesktopAppSettingsLoaded(true);
      return true;
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not update the application settings.',
        ),
      });
      return false;
    }
  };

  const pickCompanyLogoPath = async (): Promise<string | undefined> => {
    if (!hasCapability(bridgeMessageTypes.openFileDialog)) {
      dispatch({
        type: 'report-operation-failed',
        message:
          'The connected desktop host has not exposed file selection for the company logo yet.',
      });
      return undefined;
    }

    try {
      const response = await hostBridge.openFileDialog({
        title: 'Choose company logo',
        filters: [
          {
            name: 'Image files',
            extensions: ['png', 'jpg', 'jpeg', 'bmp', 'gif', 'webp'],
          },
          {
            name: 'All files',
            extensions: ['*.*'],
          },
        ],
      });

      if (!response.success || !response.filePath) {
        return undefined;
      }

      return response.filePath;
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not open the company logo picker.',
        ),
      });
      return undefined;
    }
  };

  useEffect(() => {
    materialSelectionRef.current = {
      importResponse: state.importResponse,
      selectedMaterialId: state.selectedMaterialId,
    };
  }, [state.importResponse, state.selectedMaterialId]);

  useEffect(() => {
    const desktopHost = {
      createNewProject: () => {
        void createNewProjectRef.current();
      },
      openProject: (request: OpenProjectRequest) => {
        void startupProjectOpenRef.current(request);
      },
      saveProject: () => saveProjectRef.current(),
      saveProjectAs: () => saveProjectAsRef.current(),
      saveProjectBeforeClose: () => saveProjectBeforeCloseRef.current(),
      prepareProjectSaveBeforeClose: () => prepareProjectSaveBeforeCloseRef.current(),
    };

    window.panelNesterDesktopHost = desktopHost;
    return () => {
      if (window.panelNesterDesktopHost === desktopHost) {
        delete window.panelNesterDesktopHost;
      }
    };
  }, []);

  useEffect(() => {
    const unsubscribe = hostBridge.subscribe((event) => {
      dispatch({
        type: 'bridge-updated',
        snapshot: event.snapshot,
      });
    });

    void hostBridge.initialize().then((handshake) => {
      dispatch({
        type: 'bridge-updated',
        snapshot: hostBridge.getSnapshot(),
      });

      if (handshake.success) {
        void loadMaterials().catch(() => undefined);
        void loadDesktopAppSettings().catch(() => undefined);
      }
    });

    return unsubscribe;
  }, []);

  useEffect(() => {
    if (hostReadyNotifiedRef.current || !state.bridge.connected) {
      return;
    }

    hostReadyNotifiedRef.current = true;
    void hostBridge.notifyUiReady().catch(() => undefined);
  }, [state.bridge.connected]);

  useEffect(() => {
    if (
      !state.bridge.connected ||
      !state.bridge.handshake.capabilities.includes(
        bridgeMessageTypes.getDesktopAppSettings,
      ) ||
      desktopAppSettingsLoaded
    ) {
      return;
    }

    void loadDesktopAppSettings().catch(() => undefined);
  }, [
    desktopAppSettingsLoaded,
    state.bridge.connected,
    state.bridge.handshake.capabilities,
  ]);

  useEffect(() => {
    const preferredCompanyName = desktopAppSettings.companyName?.trim() ?? '';
    const currentCompanyName =
      state.projectSettings.reportSettings.companyName?.trim() ?? '';
    const metadataCompanyName = state.projectMetadata.customerName.trim();

    if (preferredCompanyName.length === 0 || currentCompanyName === preferredCompanyName) {
      return;
    }

    if (currentCompanyName.length > 0 && currentCompanyName !== metadataCompanyName) {
      return;
    }

    dispatch({
      type: 'project-settings-synced',
      settings: {
        ...state.projectSettings,
        reportSettings: normalizeReportSettings(
          state.projectSettings.reportSettings,
          state.projectMetadata,
          preferredCompanyName,
        ),
      },
    });
  }, [desktopAppSettings.companyName, state.projectMetadata, state.projectSettings]);

  useEffect(() => {
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      if (!state.projectDirty) {
        return;
      }

      event.preventDefault();
      event.returnValue = '';
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [state.projectDirty]);

  useEffect(() => {
    const canPreviewStiffenerTakeoff =
      state.bridge.connected &&
      state.bridge.handshake.capabilities.includes(
        bridgeMessageTypes.getStiffenerTakeoff,
      );

    if (!state.projectSettings.stiffenerTakeoff.enabled) {
      dispatch({
        type: 'stiffener-operation-cleared',
        message: defaultStiffenerMessage,
      });
      return;
    }

    if (!canPreviewStiffenerTakeoff) {
      dispatch({
        type: 'stiffener-operation-failed',
        message: state.bridge.connected
          ? 'The connected desktop host has not exposed stiffener takeoff preview yet.'
          : 'Connect to the desktop host to preview the stiffener takeoff.',
      });
      return;
    }

    let cancelled = false;

    const loadStiffenerTakeoff = async () => {
      dispatch({
        type: 'stiffener-operation-started',
        message: 'Calculating stiffener takeoff…',
      });

      try {
        const response = await hostBridge.getStiffenerTakeoff({
          project: buildStiffenerProjectRecord(state),
        });

        if (cancelled) {
          return;
        }

        if (!response.success || !response.report) {
          throw new Error(
            getBridgeErrorMessage(
              response.error,
              response.message ?? 'The stiffener takeoff could not be calculated.',
            ),
          );
        }

        dispatch({
          type: 'stiffener-operation-finished',
          report: response.report,
          message:
            response.message ??
            (response.report.hasTakeoff
              ? 'Calculated stiffener takeoff.'
              : 'No stiffeners were required for the current ready rows and settings.'),
        });
      } catch (error) {
        if (cancelled) {
          return;
        }

        dispatch({
          type: 'stiffener-operation-failed',
          message: getErrorMessage(
            error,
            'The desktop host could not calculate the stiffener takeoff.',
          ),
        });
      }
    };

    void loadStiffenerTakeoff();

    return () => {
      cancelled = true;
    };
  }, [
    state.importResponse.parts,
    state.projectId,
    state.projectMetadata,
    state.projectSettings.stiffenerTakeoff,
    state.selectedFilePath,
    state.selectedMaterialId,
    state.materials,
    state.lastNestMaterial,
    state.projectMaterialSnapshots,
    state.bridge.connected,
    state.bridge.handshake.capabilities.includes(
      bridgeMessageTypes.getStiffenerTakeoff,
    ),
  ]);

  useEffect(() => {
    document.title = buildWindowTitle(state.projectMetadata.projectName, state.projectDirty);
  }, [state.projectDirty, state.projectMetadata.projectName]);

  const hasCapability = (capability: BridgeCapability): boolean =>
    state.bridge.handshake.capabilities.includes(capability);

  const releaseActiveImportSession = async (): Promise<void> => {
    const sessionId = activeImportSessionIdRef.current;
    activeImportSessionIdRef.current = undefined;
    if (sessionId && hasCapability(bridgeMessageTypes.cancelImportSession)) {
      await hostBridge.cancelImportSession({ sessionId }).catch(() => undefined);
    }
  };

  const trackProgressOperation = async <T, TProgress>(
    enabled: boolean,
    isCurrent: () => boolean,
    readProgress: () => Promise<TProgress | undefined>,
    publishProgress: (progress: TProgress) => void,
    operation: () => Promise<T>,
  ): Promise<T> => {
    if (!enabled) return operation();
    let active = true;
    let pollInFlight = false;
    const poll = async () => {
      if (!active || pollInFlight || !isCurrent()) {
        return;
      }
      pollInFlight = true;
      try {
        const progress = await readProgress();
        if (active && progress) {
          publishProgress(progress);
        }
      } catch {
        // Progress is advisory; the finalized operation response remains authoritative.
      } finally {
        pollInFlight = false;
      }
    };

    void poll();
    const intervalId = window.setInterval(() => void poll(), 250);
    try {
      return await operation();
    } finally {
      active = false;
      window.clearInterval(intervalId);
    }
  };

  const trackImportOperation = async <T,>(
    sessionId: string,
    operation: () => Promise<T>,
  ): Promise<T> =>
    trackProgressOperation(
      hasCapability(bridgeMessageTypes.getImportSessionProgress),
      () => activeImportSessionIdRef.current === sessionId,
      async () => {
        const response = await hostBridge.getImportSessionProgress({ sessionId });
        return response.success ? response.progress ?? undefined : undefined;
      },
      (progress) => dispatch({ type: 'import-progressed', progress }),
      operation,
    );

  const trackGenerationOperation = async <T,>(
    operationId: string,
    operation: () => Promise<T>,
  ): Promise<T> =>
    trackProgressOperation(
      hasCapability(bridgeMessageTypes.getCutPlanGenerationProgress),
      () => activeGenerationOperationIdRef.current === operationId,
      async () => {
        const response = await hostBridge.getCutPlanGenerationProgress({ operationId });
        return response.success ? response.progress ?? undefined : undefined;
      },
      (progress) => dispatch({ type: 'generation-progressed', progress }),
      async () => {
        try {
          return await operation();
        } finally {
          if (activeGenerationOperationIdRef.current === operationId) {
            activeGenerationOperationIdRef.current = undefined;
          }
        }
      },
    );

  const createGenerationOperationId = (): string =>
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID()
      : `cut-plan-${Date.now()}`;

  const cancelCutPlanGeneration = async (): Promise<void> => {
    const operationId = activeGenerationOperationIdRef.current;
    if (!operationId || !hasCapability(bridgeMessageTypes.cancelCutPlanGeneration)) {
      return;
    }

    const response = await hostBridge.cancelCutPlanGeneration({ operationId });
    if (response.cancellationRequested) {
      dispatch({
        type: 'generation-progressed',
        progress: {
          phase: state.generationProgress?.phase ?? 'optimizationGroups',
          completedOptimizationGroups:
            state.generationProgress?.completedOptimizationGroups ?? 0,
          totalOptimizationGroups: state.generationProgress?.totalOptimizationGroups ?? 0,
          optimizationGroupId: state.generationProgress?.optimizationGroupId,
          completedStockGroups: state.generationProgress?.completedStockGroups ?? 0,
          totalStockGroups: state.generationProgress?.totalStockGroups ?? 0,
          completedPieceInstanceSteps:
            state.generationProgress?.completedPieceInstanceSteps ?? 0,
          totalPieceInstanceSteps: state.generationProgress?.totalPieceInstanceSteps ?? 0,
          label: 'Cancelling Cut Plan generation…',
        },
      });
    }
  };

  const retryHandshake = async () => {
    const handshake = await hostBridge.initialize();
    dispatch({
      type: 'bridge-updated',
      snapshot: hostBridge.getSnapshot(),
    });

    if (handshake.success) {
      await loadMaterials({
        message: 'Material library synced with the desktop host.',
      }).catch(() => undefined);
      await loadDesktopAppSettings().catch(() => undefined);
    }
  };

  const chooseMaterialLibraryLocation = async (): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.chooseMaterialLibraryLocation)) {
      dispatch({
        type: 'materials-failed',
        message:
          'The connected desktop host has not exposed material library relocation yet.',
      });
      return;
    }

    dispatch({
      type: 'materials-request-started',
      message: 'Choosing a different material library location…',
    });

    try {
      const response = await hostBridge.chooseMaterialLibraryLocation();
      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'materials-request-finished',
            message: response.message ?? 'Material library location change cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The material library location could not be changed.',
          ),
        );
      }

      applyMaterialLibraryResponse(response, {
        message: response.message ?? 'Material library location updated.',
      });
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not change the material library location.',
      );
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const restoreDefaultMaterialLibraryLocation = async (): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.restoreDefaultMaterialLibraryLocation)) {
      dispatch({
        type: 'materials-failed',
        message:
          'The connected desktop host has not exposed default material library recovery yet.',
      });
      return;
    }

    dispatch({
      type: 'materials-request-started',
      message: 'Restoring the default material library location…',
    });

    try {
      const response = await hostBridge.restoreDefaultMaterialLibraryLocation();
      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ??
              'The default material library location could not be restored.',
          ),
        );
      }

      applyMaterialLibraryResponse(response, {
        message:
          response.message ??
          'Restored the default material library location and reloaded the library.',
      });
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not restore the default material library location.',
      );
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const saveProject = async (
    options?: { saveAs?: boolean },
  ): Promise<DesktopCloseSaveResult> => {
    const canSaveProject = hasCapability(bridgeMessageTypes.saveProject);
    const canSaveProjectAs = hasCapability(bridgeMessageTypes.saveProjectAs);
    const useSaveAs = options?.saveAs || !state.projectFilePath || !canSaveProject;

    if (useSaveAs && !canSaveProjectAs) {
      const message =
        'The connected desktop host has not exposed Save As yet. Metadata and dirty tracking stay active in the shell.';
      dispatch({
        type: 'project-operation-failed',
        message,
      });
      return {
        status: 'failed',
        message,
      };
    }

    if (!useSaveAs && !canSaveProject) {
      const message =
        'The connected desktop host has not exposed Save yet. Use Save As when the capability appears.';
      dispatch({
        type: 'project-operation-failed',
        message,
      });
      return {
        status: 'failed',
        message,
      };
    }

    const project = buildProjectRecord(state);

    dispatch({
      type: 'project-operation-started',
      message: useSaveAs ? 'Saving project as…' : 'Saving project…',
    });

    try {
      const response = useSaveAs
        ? await hostBridge.saveProjectAs({
            filePath: null,
            suggestedFileName: `${state.projectMetadata.projectName
              .trim()
              .toLowerCase()
              .replace(/[^a-z0-9]+/g, '-')
              .replace(/^-+|-+$/g, '') || 'optifab-project'}.pnest`,
            project,
          })
        : await hostBridge.saveProject({
            filePath: state.projectFilePath ?? null,
            project,
          });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          const message = response.message ?? 'Project save was cancelled.';
          dispatch({
            type: 'project-operation-finished',
            message,
          });
          return {
            status: 'cancelled',
            message,
          };
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not save the project.',
          ),
        );
      }

      const filePath = response.filePath ?? state.projectFilePath;
      if (!filePath) {
        throw new Error('The desktop host did not return a project file path.');
      }

      const savedProject = response.project ?? project;
      const savedMetadata = mapMetadataFromBridge(savedProject.metadata);
      const savedSettings = normalizeProjectSettings(
        savedProject.settings,
        savedMetadata,
        desktopAppSettings.companyName,
      );

      dispatch({
        type: 'project-saved',
        filePath,
        project: savedProject,
        settings: savedSettings,
        message: response.message ?? `Saved ${fileNameFromPath(filePath)}.`,
      });
      return {
        status: 'saved',
        message: response.message ?? `Saved ${fileNameFromPath(filePath)}.`,
      };
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not save the project.',
      );
      dispatch({
        type: 'project-operation-failed',
        message,
      });
      return {
        status: 'failed',
        message,
      };
    }
  };

  const runProjectTransition = async (
    actionLabel: string,
    action: () => Promise<void>,
  ): Promise<void> => {
    if (state.projectDirty) {
      const choice = await new Promise<UnsavedPromptChoice>((resolve) => {
        unsavedPromptResolverRef.current = resolve;
        setUnsavedPromptActionLabel(actionLabel);
      });

      if (choice === 'cancel') {
        return;
      }

      if (choice === 'save') {
        const saveResult = await saveProject();
        if (saveResult.status !== 'saved') {
          return;
        }

        await new Promise<void>((resolve) => {
          window.setTimeout(resolve, 0);
        });
      }
    }

    await action();
  };

  const createNewProject = async (projectKind: ProjectKind) => {
    setNewProjectDialogOpen(false);
    await runProjectTransition('starting a new project', async () => {
      await releaseActiveImportSession();
      const metadata = createDefaultProjectMetadata();
      const settings = createProjectSettings(
        metadata,
        desktopAppSettings.companyName,
        projectKind,
      );

      if (hasCapability(bridgeMessageTypes.newProject)) {
        dispatch({
          type: 'project-operation-started',
          message: 'Starting a new project…',
        });

        try {
          const response = await hostBridge.newProject({
            metadata: mapMetadataToBridge(metadata),
            settings,
            projectKind,
          });
          if (!response.success) {
            throw new Error(
              getBridgeErrorMessage(
                response.error,
                response.message ?? 'The desktop host could not create a new project.',
              ),
            );
          }

          dispatch({
            type: 'project-created',
            metadata: response.project
              ? mapMetadataFromBridge(response.project.metadata)
              : metadata,
            settings: response.project
              ? normalizeProjectSettings(
                  response.project.settings,
                  mapMetadataFromBridge(response.project.metadata),
                  desktopAppSettings.companyName,
                  response.project.projectKind,
                )
              : settings,
            projectKind: response.project?.projectKind ?? projectKind,
            projectId: response.project?.projectId,
            optimizationGroups: response.project?.state.optimizationGroups,
            message:
              response.message ??
              'Started a new project. Add metadata, import rows, and save when ready.',
          });
          return;
        } catch (error) {
          dispatch({
            type: 'project-operation-failed',
            message: getErrorMessage(
              error,
              'The desktop host could not create a new project.',
            ),
          });
          return;
        }
      }

      dispatch({
        type: 'project-created',
        metadata,
        settings,
        projectKind,
        message:
          'Started a new project in the UI. Save and Open stay ready to light up when the desktop host exposes the Phase 3 file commands.',
      });
    });
  };
  const requestNewProject = async () => setNewProjectDialogOpen(true);

  const changeProjectKind = async (projectKind: ProjectKind) => {
    if (projectKind === state.projectKind) {
      return;
    }

    await releaseActiveImportSession();
    dispatch({
      type: 'project-operation-started',
      message: 'Changing Project Kind…',
    });

    try {
      const currentProject = buildProjectRecord(state);
      const response: ChangeProjectKindResponse = hasCapability(
        bridgeMessageTypes.changeProjectKind,
      )
        ? await hostBridge.changeProjectKind({ project: currentProject, projectKind })
        : {
            success: true,
            project: {
              ...currentProject,
              projectKind,
              settings: {
                ...createProjectSettings(
                  state.projectMetadata,
                  desktopAppSettings.companyName,
                  projectKind,
                ),
                reportSettings: state.projectSettings.reportSettings,
              },
              materialSnapshots: [],
              state: {
                sourceFilePath: null,
                importSource: null,
                importConfiguration: null,
                optimizationGroups: [],
                parts: [],
                selectedMaterialId: null,
                lastNestingResult: null,
                lastBatchNestingResult: null,
                extrusionLayout: defaultExtrusionLayoutState,
              },
            },
            message: `Changed Project Kind to ${projectKindLabels[projectKind]}.`,
          };

      if (!response.success || !response.project) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Project Kind could not be changed.',
          ),
        );
      }

      dispatch({
        type: 'project-kind-changed',
        project: response.project,
        message:
          response.message ??
          `Changed Project Kind to ${projectKindLabels[projectKind]}. Save the project to persist this change.`,
      });
    } catch (error) {
      dispatch({
        type: 'project-operation-failed',
        message: getErrorMessage(error, 'Project Kind could not be changed.'),
      });
    }
  };

  const openProject = async (request: OpenProjectRequest = {}) => {
    if (!hasCapability(bridgeMessageTypes.openProject)) {
      dispatch({
        type: 'project-operation-failed',
        message:
          'The connected desktop host has not exposed Open Project yet. The shell will keep showing local metadata and snapshot state until that bridge arrives.',
      });
      return;
    }

    const actionLabel = request.filePath
      ? `opening ${fileNameFromPath(request.filePath)}`
      : 'opening another project';

    await runProjectTransition(actionLabel, async () => {
      await releaseActiveImportSession();
      dispatch({
        type: 'project-operation-started',
        message: 'Opening project…',
      });

      try {
        const response = await hostBridge.openProject(request);
        if (!response.success) {
          if (response.error?.code === 'cancelled') {
            dispatch({
              type: 'project-operation-finished',
              message: response.message ?? 'Project selection was cancelled.',
            });
            return;
          }

          throw new Error(
            getBridgeErrorMessage(
              response.error,
              response.message ?? 'The desktop host could not open the project.',
            ),
          );
        }

        if (!response.project || !response.filePath) {
          throw new Error(
            getBridgeErrorMessage(
              response.error,
              response.message ?? 'The desktop host could not open the project.',
            ),
          );
        }

        const project = response.project;
        const selectedMaterialId = pickOpenedProjectMaterialId(state.materials, project);
        const lastNestMaterial =
          state.materials.find(
            (material) =>
              material.materialId ===
              (project.state.selectedMaterialId ?? selectedMaterialId),
          ) ??
          findSnapshotMaterial(
            project.materialSnapshots,
            project.state.selectedMaterialId,
          ) ??
          findSnapshotMaterial(
            project.materialSnapshots,
            undefined,
            project.state.parts[0]?.materialName,
          ) ??
          project.materialSnapshots[0];

        const missingLiveSelection =
          Boolean(project.state.selectedMaterialId) && !selectedMaterialId;
        const openedMetadata = mapMetadataFromBridge(project.metadata);
        const openedSettings = normalizeProjectSettings(
          project.settings,
          openedMetadata,
          desktopAppSettings.companyName,
        );

        dispatch({
          type: 'project-opened',
          filePath: response.filePath,
          project,
          settings: openedSettings,
          selectedMaterialId,
          lastNestMaterial,
          message: missingLiveSelection
            ? `${response.message ?? `Opened ${fileNameFromPath(response.filePath)}.`} Saved material snapshots remain visible here; choose a live library material before rerunning nesting.`
            : response.message ?? `Opened ${fileNameFromPath(response.filePath)}.`,
        });
      } catch (error) {
        dispatch({
          type: 'project-operation-failed',
          message: getErrorMessage(
            error,
            'The desktop host could not open the project.',
          ),
        });
      }
    });
  };
  createNewProjectRef.current = requestNewProject;
  saveProjectBeforeCloseRef.current = async () => saveProject();
  prepareProjectSaveBeforeCloseRef.current = async () => ({
    status: 'ready',
    project: buildProjectRecord(state),
    filePath: state.projectFilePath ?? null,
    suggestedFileName: `${state.projectMetadata.projectName
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'optifab-project'}.pnest`,
  });
  startupProjectOpenRef.current = openProject;
  saveProjectRef.current = () => saveProject();
  saveProjectAsRef.current = () => saveProject({ saveAs: true });

  const importFile = async (
    requestedFilePath?: string,
    replacementConfirmed = false,
    droppedFile?: File,
  ) => {
    const replacingExistingImportSource = hasExistingImportSource(state);
    if (replacingExistingImportSource && !replacementConfirmed) {
      setPendingImportReplacement({ requestedFilePath, droppedFile });
      return;
    }

    dispatch({
      type: 'import-started',
      phase: 'opening',
      message: droppedFile
        ? `Reading the dropped Import Source ${droppedFile.name}…`
        : requestedFilePath
        ? `Preparing to re-import ${fileNameFromPath(requestedFilePath)}…`
        : 'Opening the native file picker and preparing the import review…',
    });

    try {
      const openImportDialog = () =>
        hostBridge.invoke<OpenFileDialogResponse>(
          bridgeMessageTypes.openFileDialog,
          {
            title: 'Select a parts file',
            filters: [
              { name: 'Supported files', extensions: ['csv', 'xlsx', 'xlsm'] },
              { name: 'CSV files', extensions: ['csv'] },
              { name: 'Excel Workbooks', extensions: ['xlsx', 'xlsm'] },
              { name: 'All files', extensions: ['*.*'] },
            ],
          },
          importFileDialogTimeoutMs,
        );
      const invokeImportFile = async (request: ImportFileRequest) =>
        normalizeImportFileResponse(
          await hostBridge.invoke<ImportFileResponse>(
            bridgeMessageTypes.importFile,
            request,
            importBridgeTimeoutMs,
          ),
        );

      const canUseImportSessions =
        hasCapability(bridgeMessageTypes.beginImportSession) &&
        hasCapability(bridgeMessageTypes.previewImportSession) &&
        hasCapability(bridgeMessageTypes.finalizeImportSession) &&
        hasCapability(bridgeMessageTypes.cancelImportSession);
      if (canUseImportSessions) {
        const previousSessionId = activeImportSessionIdRef.current;
        if (previousSessionId) {
          await releaseActiveImportSession();
        }

        const sessionId = createImportSessionId();
        activeImportSessionIdRef.current = sessionId;
        const dialogResponse: OpenFileDialogResponse = droppedFile
          ? { success: true, filePath: droppedFile.name }
          : requestedFilePath
          ? { success: true, filePath: requestedFilePath }
          : await openImportDialog();
        if (activeImportSessionIdRef.current !== sessionId) {
          return;
        }

        const selectedFilePath = dialogResponse.filePath ?? undefined;
        if (!dialogResponse.success || !selectedFilePath) {
          activeImportSessionIdRef.current = undefined;
          dispatch({
            type: 'import-selection-cancelled',
            message: getBridgeErrorMessage(
              dialogResponse.error,
              dialogResponse.message ?? 'File selection was cancelled.',
            ),
          });
          return;
        }

        dispatch({
          type: 'import-started',
          phase: 'reading',
          message: `Reading an immutable snapshot of ${fileNameFromPath(selectedFilePath)}…`,
        });
        const droppedContentBase64 = droppedFile
          ? await encodeDroppedImportSource(droppedFile)
          : undefined;
        const started = normalizeImportSessionResponse(
          await trackImportOperation(sessionId, () =>
            hostBridge.beginImportSession({
              sessionId,
              importSourcePath: droppedFile ? null : selectedFilePath,
              importSourceFileName: droppedFile?.name ?? null,
              importSourceContentBase64: droppedContentBase64 ?? null,
              projectKind: state.projectKind,
            })),
          sessionId,
        );
        if (activeImportSessionIdRef.current !== sessionId) {
          return;
        }
        if (responseLooksLikeImportPreparationFailure(started)) {
          activeImportSessionIdRef.current = undefined;
          await hostBridge.cancelImportSession({ sessionId }).catch(() => undefined);
          dispatch({
            type: 'import-failed',
            message: getBridgeErrorMessage(
              started.error,
              started.message ?? 'The desktop host could not capture the Import Snapshot.',
            ),
          });
          return;
        }

        if (started.workbook && started.workbook.worksheets.length === 0) {
          activeImportSessionIdRef.current = undefined;
          await hostBridge.cancelImportSession({ sessionId }).catch(() => undefined);
          dispatch({
            type: 'import-failed',
            message: 'The Workbook does not contain any visible, nonempty Worksheets.',
          });
          return;
        }

        dispatch({
          type: 'import-started',
          phase: 'validating',
          message: `Validating the snapshot for ${fileNameFromPath(selectedFilePath)}…`,
        });
        const initialWorksheet = started.workbook?.worksheets.find((worksheet) =>
          state.importConfiguration?.worksheets.some((saved) =>
            saved.originalPosition === worksheet.originalPosition &&
            saved.worksheetName === worksheet.worksheetName)) ?? started.workbook?.worksheets[0];
        const savedInitialWorksheet = state.importConfiguration?.worksheets.find((saved) =>
          saved.originalPosition === initialWorksheet?.originalPosition &&
          saved.worksheetName === initialWorksheet?.worksheetName);
        if (started.workbook && !(savedInitialWorksheet?.headingRange || initialWorksheet?.headingRange)) {
          dispatch({
            type: 'import-mapping-ready',
            session: createWorkbookImportMappingSession(
              sessionId,
              selectedFilePath,
              started,
              started,
              state.projectKind,
              state.importConfiguration,
              state.optimizationGroups,
            ),
            message: `Discovered ${started.workbook.worksheets.length} visible, nonempty Worksheet(s). Confirm each Heading Range before previewing mappings.`,
          });
          return;
        }
        const response = normalizeImportSessionResponse(
          await trackImportOperation(sessionId, () =>
            hostBridge.previewImportSession({
              sessionId,
              options: {
                projectKind: state.projectKind,
                columnMappings: savedInitialWorksheet?.columnMappings ?? [],
                materialMappings: state.importConfiguration?.options.materialMappings ?? [],
              },
              worksheetName: initialWorksheet?.worksheetName ?? null,
              headingRange: savedInitialWorksheet?.headingRange ?? initialWorksheet?.headingRange ?? null,
            })),
          sessionId,
        );
        if (activeImportSessionIdRef.current !== sessionId) {
          return;
        }
        if (responseLooksLikeImportPreparationFailure(response)) {
          activeImportSessionIdRef.current = undefined;
          await hostBridge.cancelImportSession({ sessionId }).catch(() => undefined);
          dispatch({
            type: 'import-failed',
            message: getBridgeErrorMessage(
              response.error,
              response.message ?? 'The desktop host could not validate the Import Snapshot.',
            ),
          });
          return;
        }

        const importResponse = toImportResponse(response);
        if (started.workbook) {
          dispatch({
            type: 'import-mapping-ready',
            session: createWorkbookImportMappingSession(
              sessionId,
              selectedFilePath,
              started,
              response,
              state.projectKind,
              state.importConfiguration,
              state.optimizationGroups,
            ),
            message: `Discovered ${started.workbook.worksheets.length} visible, nonempty Worksheet(s). Select and assign Worksheets before finalizing.`,
          });
          return;
        }
        if (shouldRequireImportReview(importResponse)) {
          dispatch({
            type: 'import-mapping-ready',
            session: createImportMappingSession(sessionId, selectedFilePath, response),
            message: describeImportReview(selectedFilePath, importResponse),
          });
          return;
        }

        dispatch({
          type: 'import-started',
          phase: 'finalizing',
          message: `Finalizing the import for ${fileNameFromPath(selectedFilePath)}…`,
        });
        const finalized = normalizeImportSessionResponse(
          await trackImportOperation(sessionId, () =>
            hostBridge.finalizeImportSession({
              sessionId,
              project: buildProjectRecord(state),
              replaceExistingImportSource: replacingExistingImportSource,
              targetOptimizationGroupId: state.activeOptimizationGroupId ?? null,
            })),
          sessionId,
        );
        if (activeImportSessionIdRef.current !== sessionId) {
          return;
        }
        activeImportSessionIdRef.current = undefined;
        if (!finalized.success || !finalized.finalized || !finalized.project) {
          dispatch({
            type: 'import-failed',
            message: getBridgeErrorMessage(
              finalized.error,
              finalized.message ?? 'The Import Session could not be finalized.',
            ),
          });
          return;
        }

        const finalizedImport = toImportResponse(finalized);
        dispatch({
          type: 'import-finished',
          filePath: finalized.filePath ?? selectedFilePath,
          response: finalizedImport,
          project: finalized.project,
          selectedMaterialId: pickMaterialId(
            state.materials,
            finalizedImport,
            state.selectedMaterialId,
          ),
          message: describeImportResult(
            finalized.filePath ?? selectedFilePath,
            finalizedImport,
          ),
        });
        dispatch({ type: 'route-changed', route: 'import' });
        return;
      }

      if (droppedFile) {
        throw new Error('Dropped Import Sources require the current OptiFab desktop import-session bridge.');
      }

      if (hasCapability(bridgeMessageTypes.importFile)) {
        const dialogResponse = requestedFilePath
          ? ({ success: true, filePath: requestedFilePath } satisfies OpenFileDialogResponse)
          : hasCapability(bridgeMessageTypes.openFileDialog)
          ? await openImportDialog()
          : undefined;
        const selectedFilePath = dialogResponse?.filePath ?? undefined;

        if (dialogResponse && (!dialogResponse.success || !selectedFilePath)) {
          dispatch({
            type: 'import-selection-cancelled',
            message: getBridgeErrorMessage(
              dialogResponse.error,
              dialogResponse.message ?? 'File selection was cancelled.',
            ),
          });
          return;
        }

        if (selectedFilePath) {
          dispatch({
            type: 'import-started',
            phase: 'reading',
            message: `Importing ${fileNameFromPath(selectedFilePath)}…`,
          });
        }

        const response = await invokeImportFile(
          selectedFilePath
            ? ({ filePath: selectedFilePath } satisfies ImportFileRequest)
            : {},
        );
        const filePath = pickImportFilePath(response, selectedFilePath);

        if (!filePath) {
          dispatch({
            type: 'import-selection-cancelled',
            message:
              response.message ??
              getBridgeErrorMessage(response.error, 'File selection was cancelled.'),
          });
          return;
        }

        if (responseLooksLikeImportPreparationFailure(response)) {
          dispatch({
            type: 'import-failed',
            message: getBridgeErrorMessage(
              response.error,
              response.message ?? 'The desktop host could not complete the file import.',
            ),
          });
          return;
        }

        const importResponse = toImportResponse(response);
        if (!shouldRequireImportReview(importResponse)) {
          dispatch({
            type: 'import-finished',
            filePath,
            response: importResponse,
            selectedMaterialId: pickMaterialId(
              state.materials,
              importResponse,
              state.selectedMaterialId,
            ),
            message: describeImportResult(filePath, importResponse),
          });
          dispatch({ type: 'route-changed', route: 'import' });
          return;
        }

        dispatch({
          type: 'import-mapping-ready',
          session: createImportMappingSession('legacy-import', filePath, response),
          message: describeImportReview(filePath, importResponse),
        });
        return;
      }

      const dialogResponse: OpenFileDialogResponse = requestedFilePath
        ? { success: true, filePath: requestedFilePath }
        : await openImportDialog();

      if (!dialogResponse.success || !dialogResponse.filePath) {
        dispatch({
          type: 'import-selection-cancelled',
          message: getBridgeErrorMessage(
            dialogResponse.error,
            dialogResponse.message ?? 'File selection was cancelled.',
          ),
        });
        return;
      }

      const importResponse = normalizeImportResponse(
        await hostBridge.invoke<ImportResponse>(
          bridgeMessageTypes.importCsv,
          {
            filePath: dialogResponse.filePath,
          },
        ),
      );

      dispatch({
        type: 'import-finished',
        filePath: dialogResponse.filePath,
        response: importResponse,
        selectedMaterialId: pickMaterialId(
          state.materials,
          importResponse,
          state.selectedMaterialId,
        ),
        message: describeImportResult(dialogResponse.filePath, importResponse),
      });
      dispatch({ type: 'route-changed', route: 'import' });
    } catch (error) {
      dispatch({
        type: 'import-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not complete the file import.',
        ),
      });
    }
  };

  const updateImportMappingSession = (session: ImportMappingSession) => {
    const synchronizedSession = session.worksheets && session.activeWorksheetName
      ? {
          ...session,
          worksheets: session.worksheets.map((draft) =>
            draft.worksheet.worksheetName === session.activeWorksheetName
              ? {
                  ...draft,
                  preview: session.preview,
                  options: session.options,
                  newMaterials: session.newMaterials,
                  hasPendingChanges: session.hasPendingChanges,
                }
              : draft,
          ),
        }
      : session;
    dispatch({
      type: 'import-mapping-updated',
      session: synchronizedSession,
    });
  };

  const cancelImportMapping = async () => {
    const sessionId = state.importMappingSession?.sessionId ?? activeImportSessionIdRef.current;
    if (sessionId && sessionId !== 'legacy-import') {
      activeImportSessionIdRef.current = sessionId;
      await releaseActiveImportSession();
    } else {
      activeImportSessionIdRef.current = undefined;
    }

    dispatch({
      type: 'import-mapping-cancelled',
      message:
        state.importResponse.parts.length > 0
          ? 'Import review cancelled. The current imported payload remains active.'
          : defaultImportMessage,
    });
  };

  const previewImportMapping = async (
    sessionOverride?: ImportMappingSession,
    worksheetNames?: string[],
  ) => {
    const session = sessionOverride ?? state.importMappingSession;
    if (!session) {
      return;
    }

    dispatch({
      type: 'import-started',
      phase: 'validating',
      message: `Refreshing the import preview for ${fileNameFromPath(session.filePath)}…`,
    });

    try {
      const usesImportSession =
        session.sessionId !== 'legacy-import' &&
        hasCapability(bridgeMessageTypes.previewImportSession);
      if (usesImportSession && session.worksheets?.length) {
        const requestedNames = worksheetNames?.length
          ? [...new Set(worksheetNames)]
          : session.activeWorksheetName
            ? [session.activeWorksheetName]
            : [];
        const targets = requestedNames.flatMap((worksheetName) => {
          const draft = session.worksheets?.find(
            (candidate) => candidate.worksheet.worksheetName === worksheetName,
          );
          return draft?.selected ? [draft] : [];
        });
        let nextSession = session;
        let filePath = session.filePath;

        for (const target of targets) {
          const response = normalizeImportSessionResponse(
            await trackImportOperation(session.sessionId, () =>
              hostBridge.previewImportSession({
                sessionId: session.sessionId,
                options: target.options,
                newMaterials: target.newMaterials,
                worksheetName: target.worksheet.worksheetName,
                headingRange: target.headingRange || null,
              })),
            session.sessionId,
          );
          if (activeImportSessionIdRef.current !== session.sessionId) {
            return;
          }
          if (responseLooksLikeImportPreparationFailure(response)) {
            dispatch({
              type: 'import-failed',
              message: getBridgeErrorMessage(
                response.error,
                response.message ?? `The desktop host could not refresh ${target.worksheet.worksheetName}.`,
              ),
            });
            return;
          }

          filePath = pickImportFilePath(response, filePath) ?? filePath;
          const recognizedOptions = buildImportOptionsFromResponse(response);
          const refreshedOptions = mergeRecognizedColumnMappings(
            {
              ...target.options,
              materialMappings: target.options.materialMappings.length > 0
                ? target.options.materialMappings
                : recognizedOptions.materialMappings,
            },
            response,
          );
          nextSession = {
            ...nextSession,
            filePath,
            worksheets: nextSession.worksheets?.map((draft) =>
              draft.worksheet.worksheetName === target.worksheet.worksheetName
                ? {
                    ...draft,
                    preview: response,
                    options: refreshedOptions,
                    hasPendingChanges: false,
                  }
                : draft),
          };
        }

        const activeDraft = nextSession.worksheets?.find(
          (draft) => draft.worksheet.worksheetName === nextSession.activeWorksheetName,
        ) ?? nextSession.worksheets?.find((draft) => draft.selected);
        if (activeDraft) {
          nextSession = {
            ...nextSession,
            preview: activeDraft.preview,
            options: activeDraft.options,
            newMaterials: activeDraft.newMaterials,
            hasPendingChanges: activeDraft.hasPendingChanges,
          };
        }
        dispatch({
          type: 'import-mapping-ready',
          session: nextSession,
          message: describeImportReview(
            filePath,
            toImportResponse(activeDraft?.preview ?? nextSession.preview),
            nextSession,
          ),
        });
        return;
      }

      const response = normalizeImportFileResponse(
        await hostBridge.invoke<ImportFileResponse>(
          bridgeMessageTypes.importFile,
          {
            filePath: session.filePath,
            options: session.options,
          } satisfies ImportFileRequest,
          importBridgeTimeoutMs,
        ),
      );
      const filePath = pickImportFilePath(response, session.filePath) ?? session.filePath;
      if (responseLooksLikeImportPreparationFailure(response)) {
        dispatch({
          type: 'import-failed',
          message: getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not refresh the import preview.',
          ),
        });
        return;
      }
      const recognizedOptions = buildImportOptionsFromResponse(response);
      const refreshedOptions = mergeRecognizedColumnMappings(
        {
          ...session.options,
          materialMappings: session.options.materialMappings.length > 0
            ? session.options.materialMappings
            : recognizedOptions.materialMappings,
        },
        response,
      );
      const nextSession = createImportMappingSession(session.sessionId, filePath, response, {
        ...session,
        options: refreshedOptions,
        hasPendingChanges: false,
      });
      dispatch({
        type: 'import-mapping-ready',
        session: nextSession,
        message: describeImportReview(filePath, toImportResponse(response), nextSession),
      });
    } catch (error) {
      if (
        session.sessionId !== 'legacy-import' &&
        activeImportSessionIdRef.current !== session.sessionId
      ) {
        return;
      }
      dispatch({
        type: 'import-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not refresh the import preview.',
        ),
      });
    }
  };

  const finalizeImportMapping = async () => {
    const session = state.importMappingSession;
    if (!session) {
      return;
    }

    const undoProject = buildProjectRecord(state);
    dispatch({
      type: 'import-started',
      phase: 'finalizing',
      message: `Finalizing the import for ${fileNameFromPath(session.filePath)}…`,
    });

    try {
      const selectedWorksheetDrafts = session.worksheets?.filter((draft) => draft.selected) ?? [];
      const sessionNewMaterials = selectedWorksheetDrafts.length > 0
        ? collectWorkbookNewMaterials(selectedWorksheetDrafts)
        : session.newMaterials;
      const usesImportSession =
        session.sessionId !== 'legacy-import' &&
        hasCapability(bridgeMessageTypes.finalizeImportSession);
      let sessionResponse: ImportSessionResponse | undefined;
      let response: ImportFileResponse;
      if (usesImportSession) {
        sessionResponse = normalizeImportSessionResponse(
          await trackImportOperation(session.sessionId, () =>
            hostBridge.finalizeImportSession({
              sessionId: session.sessionId,
              options: session.options,
              newMaterials: sessionNewMaterials,
              project: buildProjectRecord(state),
              replaceExistingImportSource: hasExistingImportSource(state),
              targetOptimizationGroupId: state.activeOptimizationGroupId ?? null,
              worksheets: selectedWorksheetDrafts.map((draft) => ({
                worksheetName: draft.worksheet.worksheetName,
                originalPosition: draft.worksheet.originalPosition,
                options: draft.options,
                optimizationGroupId: draft.optimizationGroupId,
                optimizationGroupName: draft.optimizationGroupName,
                stockLength: draft.stockLength,
                headingRange: draft.headingRange,
                excludedSourceRows: draft.excludedSourceRows,
                ignoredMaterialNames: draft.ignoredMaterialNames,
                partOverrides: draft.partOverrides,
              })),
            })),
          session.sessionId,
        );
        response = sessionResponse;
      } else {
        response = normalizeImportFileResponse(
          await hostBridge.invoke<ImportFileResponse>(
            bridgeMessageTypes.importFile,
            {
              filePath: session.filePath,
              options: session.options,
              newMaterials: session.newMaterials,
            } satisfies ImportFileRequest,
            importBridgeTimeoutMs,
          ),
        );
      }
      if (usesImportSession && activeImportSessionIdRef.current !== session.sessionId) {
        return;
      }
      const filePath = pickImportFilePath(response, session.filePath) ?? session.filePath;

      if (
        responseLooksLikeImportPreparationFailure(response) ||
        (usesImportSession && (!sessionResponse?.finalized || !sessionResponse.project))
      ) {
        if (usesImportSession) {
          activeImportSessionIdRef.current = undefined;
        }
        const message = getBridgeErrorMessage(
          response.error,
          response.message ?? 'Import mapping could not be finalized.',
        );
        dispatch(
          usesImportSession
            ? { type: 'import-mapping-cancelled', message }
            : { type: 'import-mapping-ready', session, message },
        );
        return;
      }

      const importResponse = toImportResponse(response);
      const syncedMaterials =
        sessionNewMaterials.length > 0
          ? await loadMaterials({
              message: 'Material library synced after import-time material creation.',
              selectionContext: {
                importResponse,
                selectedMaterialId: state.selectedMaterialId,
              },
            }).catch(() => undefined)
          : undefined;
      const effectiveMaterials = syncedMaterials ?? state.materials;
      if (usesImportSession) {
        activeImportSessionIdRef.current = undefined;
      }

      dispatch({
        type: 'import-finished',
        filePath,
        response: importResponse,
        project: sessionResponse?.project ?? undefined,
        resultCounts: sessionResponse?.resultCounts ?? undefined,
        undoProject: sessionResponse?.resultCounts ? undoProject : undefined,
        selectedMaterialId: pickMaterialId(
          effectiveMaterials,
          importResponse,
          state.selectedMaterialId,
        ),
        message: sessionResponse?.resultCounts
          ? `Imported ${sessionResponse.resultCounts.sourceRowCount} source rows as ${sessionResponse.resultCounts.outputEntryCount} required-piece entries from ${sessionResponse.resultCounts.worksheetCount} worksheets.`
          : describeImportResult(filePath, importResponse),
      });
    } catch (error) {
      if (
        session.sessionId !== 'legacy-import' &&
        activeImportSessionIdRef.current !== session.sessionId
      ) {
        return;
      }
      dispatch({
        type: 'import-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not finalize the mapped import.',
        ),
      });
    }
  };

  const replaceImportResponse = (
    response: ImportResponse,
    message: string,
    targetOptimizationGroupId?: string,
  ) => {
    const importResponse = normalizeImportResponse(response);
    dispatch({
      type: 'part-rows-replaced',
      response: importResponse,
      selectedMaterialId: pickMaterialId(
        state.materials,
        importResponse,
        state.selectedMaterialId,
      ),
      message,
      targetOptimizationGroupId,
    });
  };

  const updatePartRow = async (part: PartRowUpdate): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.updatePartRow)) {
      const message = 'Inline row editing is not available from the connected desktop host yet.';
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }

    dispatch({
      type: 'part-row-operation-started',
      message: `Saving row ${part.rowId ?? ''} and revalidating…`.trim(),
    });

    try {
      const response = await hostBridge.updatePartRow({
        parts: state.importResponse.parts,
        part,
      });

      replaceImportResponse(
        response,
        describeRowOperation(`Saved row ${part.rowId ?? part.importedId}.`, response),
      );
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not save the row changes.',
      );
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }
  };

  const deletePartRow = async (rowId: string): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.deletePartRow)) {
      const message = 'Row deletion is not available from the connected desktop host yet.';
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }

    dispatch({
      type: 'part-row-operation-started',
      message: `Deleting ${rowId} and revalidating the remaining rows…`,
    });

    try {
      const response = await hostBridge.deletePartRow({
        parts: state.importResponse.parts,
        rowId,
      });

      replaceImportResponse(
        response,
        describeRowOperation(`Deleted ${rowId}.`, response),
      );
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not delete the selected row.',
      );
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }
  };

  const addPartRow = async (part: PartRowUpdate): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.addPartRow)) {
      const message = 'Manual row entry is not available from the connected desktop host yet.';
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }

    dispatch({
      type: 'part-row-operation-started',
      message: 'Adding a new row and validating it against the current material library…',
    });

    try {
      const response = await hostBridge.addPartRow({
        parts: state.importResponse.parts,
        part,
      });

      replaceImportResponse(
        response,
        describeRowOperation(`Added row ${part.importedId || 'draft'}.`, response),
        state.activeOptimizationGroupId,
      );
    } catch (error) {
      const message = getErrorMessage(
        error,
        'The desktop host could not add the new row.',
      );
      dispatch({ type: 'part-row-operation-failed', message });
      throw new Error(message);
    }
  };

  const loadMaterial = async (materialId: string): Promise<Material> => {
    dispatch({
      type: 'materials-request-started',
      message: 'Loading material details…',
    });

    try {
      const response = await hostBridge.getMaterial({ materialId });
      if (!response.success || !response.material) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Material was not found.',
          ),
        );
      }

      dispatch({
        type: 'materials-request-finished',
        message: response.message ?? `Loaded ${response.material.name}.`,
      });
      return response.material;
    } catch (error) {
      const message = getErrorMessage(
        error,
        'Material details could not be loaded.',
      );
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const createMaterial = async (draft: MaterialDraft): Promise<Material> => {
    dispatch({
      type: 'materials-request-started',
      message: 'Saving the new material…',
    });

    try {
      const response = await hostBridge.createMaterial(draft);
      if (!response.success || !response.material) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Material could not be created.',
          ),
        );
      }

      dispatch({
        type: 'material-created',
        material: response.material,
        message: response.message ?? `Saved ${response.material.name}.`,
      });
      return response.material;
    } catch (error) {
      const message = getErrorMessage(error, 'Material could not be created.');
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const updateMaterial = async (material: Material): Promise<Material> => {
    dispatch({
      type: 'materials-request-started',
      message: 'Saving material changes…',
    });

    try {
      const response = await hostBridge.updateMaterial(material);
      if (!response.success || !response.material) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Material could not be updated.',
          ),
        );
      }

      dispatch({
        type: 'material-updated',
        material: response.material,
        message: response.message ?? `Saved ${response.material.name}.`,
      });
      return response.material;
    } catch (error) {
      const message = getErrorMessage(error, 'Material could not be updated.');
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const deleteMaterial = async (materialId: string): Promise<void> => {
    dispatch({
      type: 'materials-request-started',
      message: 'Deleting material…',
    });

    try {
      const response = await hostBridge.deleteMaterial({
        materialId,
        selectedMaterialId: state.selectedMaterialId,
        importedMaterialNames: getDistinctImportedMaterialNames(state.importResponse),
      });

      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Material could not be deleted.',
          ),
        );
      }

      dispatch({
        type: 'material-deleted',
        materialId,
        message: response.message ?? 'Material deleted.',
      });
    } catch (error) {
      const message = getErrorMessage(error, 'Material could not be deleted.');
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
    }
  };

  const selectedMaterial = state.materials.find(
    (material) => material.materialId === state.selectedMaterialId,
  );
  const activeOptimizationGroup =
    state.optimizationGroups.find(
      (group) =>
        group.optimizationGroupId === state.activeOptimizationGroupId,
    ) ?? state.optimizationGroups[0];
  const activeOptimizationGroupImportResponse = {
    ...state.importResponse,
    parts: activeOptimizationGroup?.parts ?? state.importResponse.parts,
  };
  const readyParts = getReadyParts(activeOptimizationGroupImportResponse);
  const readyMaterialCount = new Set(
    readyParts
      .map((part) => part.materialName.trim())
      .filter((name) => name.length > 0),
  ).size;
  const nestableParts = getNestableParts(
    activeOptimizationGroupImportResponse,
    selectedMaterial,
  );
  const canRunBatchNesting = hasCapability(bridgeMessageTypes.runBatchNesting);

  const runNesting = async (scope: 'active' | 'all' = 'active') => {
    if (canRunBatchNesting) {
      const requestedGroups = (scope === 'all'
        ? [...state.optimizationGroups].sort(
            (left, right) => left.order - right.order,
          )
        : activeOptimizationGroup
          ? [activeOptimizationGroup]
          : []
      )
        .filter((group) =>
          group.parts.some((part) => part.validationStatus !== 'error'),
        )
        .map((group) => ({
          optimizationGroupId: group.optimizationGroupId,
          name: group.name,
          order: group.order,
          ownedPartRowIds: group.parts.map((part) => part.rowId),
          parts: group.parts.filter((part) => part.validationStatus !== 'error'),
        }));
      const requestedPartCount = requestedGroups.reduce(
        (total, group) => total + group.parts.length,
        0,
      );

      if (requestedPartCount === 0) {
        dispatch({
          type: 'nesting-failed',
          message:
            scope === 'all'
              ? 'No ready rows are available in any Optimization Group.'
              : 'No ready rows are available in the active Optimization Group.',
        });
        return;
      }

      dispatch({
        type: 'nesting-started',
        message:
          scope === 'all'
            ? `Running all ${requestedGroups.length} Optimization Groups in explicit order…`
            : `Running ${activeOptimizationGroup?.name ?? 'the active Optimization Group'} for ${readyParts.length} row(s) across ${readyMaterialCount} material(s)…`,
      });

      try {
        const batchResponse = await hostBridge.runBatchNesting({
          optimizationGroups: requestedGroups,
          parts: requestedGroups.flatMap((group) => group.parts),
          materials: state.materials,
          kerfWidth: state.projectSettings.kerfWidth,
          selectedMaterialId: state.selectedMaterialId ?? null,
        });
        const groupResults = batchResponse.optimizationGroupResults ?? [];
        const focusedGroupResult =
          groupResults.find(
            (result) =>
              result.optimizationGroupId === state.activeOptimizationGroupId,
          ) ?? groupResults[0];
        const focusedBatchResponse = batchForOptimizationGroup(
          batchResponse,
          focusedGroupResult,
        );
        const primaryMaterialResult =
          focusedBatchResponse.materialResults.find(
            (result) =>
              result.materialId === state.selectedMaterialId ||
              result.materialName === selectedMaterial?.name,
          ) ?? focusedBatchResponse.materialResults[0];
        const focusedMaterial =
          selectedMaterial ??
          state.materials.find(
            (material) =>
              material.materialId === primaryMaterialResult?.materialId ||
              material.name === primaryMaterialResult?.materialName,
          );
        const legacyResponse =
          focusedBatchResponse.legacyResult ??
          primaryMaterialResult?.result ??
          emptyNestResponse;

        dispatch({
          type: 'nesting-finished',
          response: legacyResponse,
          batchResponse: focusedBatchResponse,
          optimizationGroupResults: groupResults,
          material: focusedMaterial,
          message:
            scope === 'all'
              ? describeOptimizationGroupRun(batchResponse)
              : describeBatchNestingResult(focusedBatchResponse),
        });
        dispatch({ type: 'route-changed', route: 'results' });
      } catch (error) {
        dispatch({
          type: 'nesting-failed',
          message: getErrorMessage(
            error,
            'The desktop host could not complete the batch nesting run.',
          ),
        });
      }
      return;
    }

    if (!selectedMaterial) {
      dispatch({
        type: 'nesting-failed',
        message: 'Select a material from the library before nesting.',
      });
      return;
    }

    if (nestableParts.length === 0) {
      dispatch({
        type: 'nesting-failed',
        message: `No valid rows in the active Optimization Group currently match ${selectedMaterial.name}.`,
      });
      return;
    }

    dispatch({
      type: 'nesting-started',
      message: `Running nesting for ${selectedMaterial.name} on ${nestableParts.length} row(s)…`,
    });

    try {
      const nestResponse = await hostBridge.invoke<NestResponse>(
        bridgeMessageTypes.runNesting,
        {
          parts: nestableParts,
          material: selectedMaterial,
          kerfWidth: state.projectSettings.kerfWidth,
        },
        nestingBridgeTimeoutMs,
      );

      dispatch({
        type: 'nesting-finished',
        response: nestResponse,
        batchResponse: buildBatchFromLegacy(
          nestResponse,
          selectedMaterial,
          pendingProjectSnapshots,
          state.selectedMaterialId,
        ),
        material: selectedMaterial,
        message: describeNestingResult(selectedMaterial.name, nestResponse),
      });
      dispatch({ type: 'route-changed', route: 'results' });
    } catch (error) {
      dispatch({
        type: 'nesting-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not complete the nesting run.',
        ),
      });
    }
  };

  const canRunNesting =
    (canRunBatchNesting
      ? readyParts.length > 0
      : Boolean(selectedMaterial) && nestableParts.length > 0) &&
    !state.importMappingSession &&
    !state.importBusy &&
    !state.materialsBusy &&
    !state.partMutationBusy;
  const canRunAllNesting =
    canRunBatchNesting &&
    state.optimizationGroups.some((group) =>
      group.parts.some((part) => part.validationStatus !== 'error'),
    ) &&
    !state.importMappingSession &&
    !state.importBusy &&
    !state.materialsBusy &&
    !state.partMutationBusy;

  const overviewMaterial =
    selectedMaterial ?? state.materials[0] ?? demoMaterial;
  const resultsMaterial = state.lastNestMaterial ?? overviewMaterial;
  const pendingProjectSnapshots = collectProjectMaterialSnapshots(
    state.materials,
    state.importResponse,
    state.selectedMaterialId,
    state.lastNestMaterial,
    state.projectMaterialSnapshots,
  );

  const updateReportField = (field: keyof ReportSettings, value: string) => {
    dispatch({
      type: 'project-settings-changed',
      settings: {
        ...state.projectSettings,
        reportSettings: {
          ...state.projectSettings.reportSettings,
          [field]: field === 'reportDate' ? normalizeReportDate(value) : value,
        },
      },
      message:
        'Report settings changed. Save the project to keep these export fields with the job.',
    });
  };

  const updateOptimizationGroups = async (
    change: OptimizationGroupChange,
    activeOptimizationGroupId = state.activeOptimizationGroupId,
  ): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.updateOptimizationGroups)) {
      throw new Error('Optimization Group management is not available from the connected desktop host.');
    }

    dispatch({
      type: 'project-operation-started',
      message: 'Updating Optimization Groups…',
    });

    try {
      const response = await hostBridge.updateOptimizationGroups({
        project: buildProjectRecord(state),
        change,
      });
      if (!response.success || !response.project) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Optimization Groups could not be updated.',
          ),
        );
      }

      dispatch({
        type: 'optimization-groups-updated',
        project: response.project,
        activeOptimizationGroupId,
        message: response.message ?? 'Updated Optimization Groups.',
      });
    } catch (error) {
      const message = getErrorMessage(error, 'Optimization Groups could not be updated.');
      dispatch({ type: 'project-operation-failed', message });
      throw new Error(message);
    }
  };

  const movePartToOptimizationGroup = async (
    partRowId: string,
    targetOptimizationGroupId: string,
  ): Promise<void> =>
    updateOptimizationGroups(
      {
        type: 'movePart',
        partRowId,
        targetOptimizationGroupId,
      },
    );

  const updateRequiredPieces = async (change: RequiredPieceChange): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.updateRequiredPieces)) {
      throw new Error('Required Piece management is not available from the connected desktop host.');
    }

    dispatch({ type: 'project-operation-started', message: 'Updating Required Pieces…' });
    try {
      const response = await hostBridge.updateRequiredPieces({
        project: buildProjectRecord(state),
        change,
      });
      if (!response.success || !response.project) {
        throw new Error(getBridgeErrorMessage(
          response.error,
          response.message ?? 'Required Pieces could not be updated.',
        ));
      }

      dispatch({
        type: 'optimization-groups-updated',
        project: response.project,
        activeOptimizationGroupId: change.optimizationGroupId ?? state.activeOptimizationGroupId,
        message: response.message ?? 'Updated Required Pieces.',
      });
    } catch (error) {
      const message = getErrorMessage(error, 'Required Pieces could not be updated.');
      dispatch({ type: 'project-operation-failed', message });
      throw new Error(message);
    }
  };

  const applyCutPlanGenerationResponse = (
    startedProject: ProjectRecord,
    responseProject: ProjectRecord,
    targetOptimizationGroupIds: readonly string[],
    activeOptimizationGroupId: string | undefined,
    message: string,
  ): boolean => {
    const reconciliation = reconcileCutPlanGenerationResponse(
      startedProject,
      buildProjectRecord(stateRef.current),
      responseProject,
      targetOptimizationGroupIds,
    );
    const acceptedCount = targetOptimizationGroupIds.length -
      reconciliation.discardedOptimizationGroupIds.length;
    if (acceptedCount <= 0) {
      dispatch({
        type: 'generation-operation-finished',
        message: 'Cut Plan generation finished after its inputs changed. The obsolete result was ignored.',
      });
      return false;
    }

    const discardedMessage = reconciliation.discardedOptimizationGroupIds.length > 0
      ? ` Ignored ${reconciliation.discardedOptimizationGroupIds.length} obsolete Optimization Group result(s).`
      : '';
    dispatch({
      type: 'optimization-groups-updated',
      project: reconciliation.project,
      activeOptimizationGroupId,
      message: `${message}${discardedMessage}`,
    });
    return true;
  };

  const generateSelectedCutPlan = async (optimizationGroupId: string): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.generateSelectedCutPlan)) {
      throw new Error('Cut Plan generation is not available from the connected desktop host.');
    }

    const operationId = createGenerationOperationId();
    const generationProject = buildProjectRecord(state);
    activeGenerationOperationIdRef.current = operationId;
    dispatch({ type: 'generation-operation-started', message: 'Preparing Cut Plan generation…' });
    try {
      const response = await trackGenerationOperation(
        operationId,
        () => hostBridge.generateSelectedCutPlan({
          project: generationProject,
          optimizationGroupId,
          operationId,
        }),
      );
      if (response.project && !applyCutPlanGenerationResponse(
        generationProject,
        response.project,
        [optimizationGroupId],
        optimizationGroupId,
        response.message ?? (response.success
          ? 'Generated deterministic heuristic Cut Plan.'
          : 'Cut Plan generation reported an application error.'),
      )) {
        return;
      }
      if (!response.success || !response.project || !response.result) {
        throw new Error(getBridgeErrorMessage(
          response.error,
          response.message ?? 'The selected Optimization Group could not generate a Cut Plan.',
        ));
      }
    } catch (error) {
      const message = getErrorMessage(error, 'The selected Optimization Group could not generate a Cut Plan.');
      dispatch({ type: 'generation-operation-finished', message });
      throw new Error(message);
    }
  };

  const generateSelectedCutPlans = async (optimizationGroupIds: string[]): Promise<void> => {
    const targetOptimizationGroupIds = [...new Set(optimizationGroupIds)].filter((id) =>
      state.optimizationGroups.some((group) =>
        group.optimizationGroupId === id &&
        group.requiredPieces.length > 0 &&
        Boolean(group.stockLength && group.stockLength > 0)));
    if (targetOptimizationGroupIds.length === 0) {
      throw new Error('Select at least one ready Optimization Group.');
    }
    if (targetOptimizationGroupIds.length === 1) {
      await generateSelectedCutPlan(targetOptimizationGroupIds[0]);
      return;
    }
    if (!hasCapability(bridgeMessageTypes.generateSelectedCutPlans)) {
      throw new Error('Generating multiple selected Optimization Groups requires an updated desktop host.');
    }

    const operationId = createGenerationOperationId();
    const generationProject = buildProjectRecord(state);
    activeGenerationOperationIdRef.current = operationId;
    dispatch({ type: 'generation-operation-started', message: 'Preparing selected Optimization Groups…' });
    try {
      const response = await trackGenerationOperation(
        operationId,
        () => hostBridge.generateSelectedCutPlans({
          project: generationProject,
          optimizationGroupIds: targetOptimizationGroupIds,
          operationId,
        }),
      );
      applyCutPlanGenerationResponse(
        generationProject,
        response.project,
        targetOptimizationGroupIds,
        targetOptimizationGroupIds[0],
        response.message,
      );
    } catch (error) {
      const message = getErrorMessage(error, 'Selected Optimization Groups could not generate Cut Plans.');
      dispatch({ type: 'generation-operation-finished', message });
      throw new Error(message);
    }
  };

  const generateAllStaleCutPlans = async (): Promise<void> => {
    if (!hasCapability(bridgeMessageTypes.generateAllStaleCutPlans)) {
      throw new Error('Generate All Stale is not available from the connected desktop host.');
    }

    const operationId = createGenerationOperationId();
    const generationProject = buildProjectRecord(state);
    const targetOptimizationGroupIds = generationProject.state.optimizationGroups
      .filter((group) => group.requiredPieces.length > 0 && group.resultStatus !== 'valid')
      .map((group) => group.optimizationGroupId);
    activeGenerationOperationIdRef.current = operationId;
    dispatch({
      type: 'generation-operation-started',
      message: 'Preparing Optimization Groups that Need Generation…',
    });
    try {
      const response = await trackGenerationOperation(
        operationId,
        () => hostBridge.generateAllStaleCutPlans({
          project: generationProject,
          operationId,
        }),
      );
      applyCutPlanGenerationResponse(
        generationProject,
        response.project,
        targetOptimizationGroupIds,
        state.activeOptimizationGroupId,
        response.message,
      );
    } catch (error) {
      const message = getErrorMessage(
        error,
        'Optimization Groups that Need Generation could not be generated.',
      );
      dispatch({ type: 'generation-operation-finished', message });
      throw new Error(message);
    }
  };

  const exportReport = async (overrides?: ReportExportOverrides) => {
    const reportSettingsOverride = overrides?.reportSettings;
    const hasResult =
      state.batchNestResponse.materialResults.length > 0 ||
      state.nestResponse.sheets.length > 0 ||
      state.nestResponse.unplacedItems.length > 0;

    if (!hasCapability(bridgeMessageTypes.exportPdfReport)) {
      dispatch({
        type: 'report-operation-failed',
        message:
          'The connected desktop host has not exposed PDF export yet. The current report fields still save with the project.',
      });
      return;
    }

    if (state.projectKind !== 'stockLength' && !hasResult) {
      dispatch({
        type: 'report-operation-failed',
        message: 'Run nesting before exporting a PDF report.',
      });
      return;
    }

    dispatch({
      type: 'report-operation-started',
      message: 'Exporting PDF report…',
    });

    try {
      const project = buildProjectRecord(
        state,
        reportSettingsOverride
          ? {
              ...state.projectSettings,
              reportSettings: reportSettingsOverride,
            }
          : undefined,
      );
      const response = await hostBridge.exportPdfReport({
        project,
        batchResult: project.state.lastBatchNestingResult ?? null,
        filePath: null,
        companyLogoPath: overrides?.companyLogoPath ?? undefined,
        stockLengthScope: overrides?.stockLengthScope ?? null,
      });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'report-operation-finished',
            message: response.message ?? 'PDF export was cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not export the PDF report.',
          ),
        );
      }

      dispatch({
        type: 'report-operation-finished',
        message:
          response.message ??
          (response.filePath
            ? `Exported PDF report to ${response.filePath}.`
            : 'PDF report exported.'),
      });
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not export the PDF report.',
        ),
      });
    }
  };

  const exportExcelReport = async (overrides?: ReportExportOverrides) => {
    const reportSettingsOverride = overrides?.reportSettings;
    const hasResult =
      state.batchNestResponse.materialResults.length > 0 ||
      state.nestResponse.sheets.length > 0 ||
      state.nestResponse.unplacedItems.length > 0;

    if (!hasCapability(bridgeMessageTypes.exportExcelReport)) {
      dispatch({
        type: 'report-operation-failed',
        message:
          'The connected desktop host has not exposed Excel export yet. The current report fields still save with the project.',
      });
      return;
    }

    if (state.projectKind !== 'stockLength' && !hasResult) {
      dispatch({
        type: 'report-operation-failed',
        message: 'Run nesting before exporting an Excel report.',
      });
      return;
    }

    dispatch({
      type: 'report-operation-started',
      message: 'Exporting Excel report…',
    });

    try {
      const project = buildProjectRecord(
        state,
        reportSettingsOverride
          ? {
              ...state.projectSettings,
              reportSettings: reportSettingsOverride,
            }
          : undefined,
      );
      const response = await hostBridge.exportExcelReport({
        project,
        batchResult: project.state.lastBatchNestingResult ?? null,
        filePath: null,
        stockLengthScope: overrides?.stockLengthScope ?? null,
      });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'report-operation-finished',
            message: response.message ?? 'Excel export was cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not export the Excel report.',
          ),
        );
      }

      dispatch({
        type: 'report-operation-finished',
        message:
          response.message ??
          (response.filePath
            ? `Exported Excel report to ${response.filePath}.`
            : 'Excel report exported.'),
      });
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not export the Excel report.',
        ),
      });
    }
  };

  const exportStiffenerReport = async (
    overrides?: StiffenerExportOverrides,
  ) => {
    const stiffenerSettings =
      overrides?.stiffenerTakeoff ?? state.projectSettings.stiffenerTakeoff;
    const reportSettings =
      overrides?.reportSettings ?? state.projectSettings.reportSettings;

    if (!stiffenerSettings.enabled) {
      dispatch({
        type: 'stiffener-operation-failed',
        message: 'Enable stiffener takeoff before exporting its standalone PDF.',
      });
      return;
    }

    if (!hasCapability(bridgeMessageTypes.exportStiffenerPdfReport)) {
      dispatch({
        type: 'stiffener-operation-failed',
        message:
          'The connected desktop host has not exposed stiffener PDF export yet.',
      });
      return;
    }

    dispatch({
      type: 'stiffener-operation-started',
      message: 'Exporting stiffener PDF report…',
    });

    try {
      const project = {
        ...buildStiffenerProjectRecord(state, {
          ...state.projectSettings,
          reportSettings,
          stiffenerTakeoff: stiffenerSettings,
        }),
        metadata: mapMetadataToBridge(
          applyReportSettingsToMetadata(state.projectMetadata, reportSettings),
        ),
      };
      const response = await hostBridge.exportStiffenerPdfReport({
        project,
        filePath: null,
        companyLogoPath: overrides?.companyLogoPath ?? undefined,
      });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'stiffener-operation-finished',
            report: state.stiffenerTakeoffReport,
            message: response.message ?? 'Stiffener PDF export was cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ??
              'The desktop host could not export the stiffener PDF report.',
          ),
        );
      }

      dispatch({
        type: 'stiffener-operation-finished',
        report: state.stiffenerTakeoffReport,
        message:
          response.message ??
          (response.filePath
            ? `Exported stiffener PDF report to ${response.filePath}.`
            : 'Stiffener PDF report exported.'),
      });
    } catch (error) {
      dispatch({
        type: 'stiffener-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not export the stiffener PDF report.',
        ),
      });
    }
  };

  const exportExtrusionPdfReport = async () => {
    if (!hasCapability(bridgeMessageTypes.exportExtrusionPdfReport)) {
      dispatch({
        type: 'extrusion-operation-failed',
        message: 'The connected desktop host has not exposed extrusion PDF export yet.',
      });
      return;
    }

    dispatch({
      type: 'extrusion-operation-started',
      message: 'Exporting extrusion PDF report...',
    });

    try {
      const response = await hostBridge.exportExtrusionPdfReport({
        project: buildProjectRecord(state),
        filePath: null,
        companyLogoPath: desktopAppSettings.companyLogoPath ?? undefined,
      });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'extrusion-operation-finished',
            message: response.message ?? 'Extrusion PDF export was cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not export the extrusion PDF report.',
          ),
        );
      }

      dispatch({
        type: 'extrusion-operation-finished',
        message: response.message ?? 'Extrusion PDF report exported.',
      });
    } catch (error) {
      dispatch({
        type: 'extrusion-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not export the extrusion PDF report.',
        ),
      });
    }
  };

  const exportExtrusionExcelReport = async () => {
    if (!hasCapability(bridgeMessageTypes.exportExtrusionExcelReport)) {
      dispatch({
        type: 'extrusion-operation-failed',
        message: 'The connected desktop host has not exposed extrusion Excel export yet.',
      });
      return;
    }

    dispatch({
      type: 'extrusion-operation-started',
      message: 'Exporting extrusion Excel report...',
    });

    try {
      const response = await hostBridge.exportExtrusionExcelReport({
        project: buildProjectRecord(state),
        filePath: null,
      });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'extrusion-operation-finished',
            message: response.message ?? 'Extrusion Excel export was cancelled.',
          });
          return;
        }

        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'The desktop host could not export the extrusion Excel report.',
          ),
        );
      }

      dispatch({
        type: 'extrusion-operation-finished',
        message: response.message ?? 'Extrusion Excel report exported.',
      });
    } catch (error) {
      dispatch({
        type: 'extrusion-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not export the extrusion Excel report.',
        ),
      });
    }
  };

  let content: React.ReactNode;
  switch (state.activeRoute) {
    case 'import':
      content = state.projectKind === 'stockLength' ? (
        <RequiredPiecesPage
          activeOptimizationGroupId={state.activeOptimizationGroupId}
          busy={state.projectBusy || state.importBusy || state.generationBusy}
          generationBusy={state.generationBusy}
          generationProgress={state.generationProgress}
          importConfiguration={state.importConfiguration}
          importSource={state.importSource}
          lastImportReceipt={state.lastImportReceipt}
          inchDisplayFormat={state.projectSettings.inchDisplayFormat}
          mappingSession={state.importMappingSession}
          message={state.importMessage || state.projectMessage}
          projectDirty={state.projectDirty}
          onCancelImportMapping={cancelImportMapping}
          onCancelGeneration={cancelCutPlanGeneration}
          onCreateOptimizationGroup={(name, stockLength) =>
            updateOptimizationGroups({ type: 'create', name, stockLength })
          }
          onCreateRequiredPiece={updateRequiredPieces}
          onDeleteRequiredPiece={(optimizationGroupId, requiredPieceId) =>
            updateRequiredPieces({
              type: 'delete',
              optimizationGroupId,
              requiredPieceId,
            })
          }
          onFinalizeImportMapping={finalizeImportMapping}
          onGenerateSelected={generateSelectedCutPlan}
          onGenerateSelectedGroups={generateSelectedCutPlans}
          onGenerateAllStale={generateAllStaleCutPlans}
          onImportDroppedFile={(file) => importFile(undefined, false, file)}
          onImportFile={importFile}
          onReimportFile={state.importSource?.importSourcePath
            ? () => importFile(state.importSource!.importSourcePath)
            : undefined}
          onUndoImport={state.preImportProject ? () => dispatch({ type: 'import-undone' }) : undefined}
          onInchDisplayFormatChange={(inchDisplayFormat: InchDisplayFormat) =>
            dispatch({
              type: 'project-settings-changed',
              settings: { ...state.projectSettings, inchDisplayFormat },
              message: 'Length display changed. Save the project to persist this preference.',
            })
          }
          onPreviewImportMapping={previewImportMapping}
          onUpdateImportMappingSession={updateImportMappingSession}
          onUpdateRequiredPiece={updateRequiredPieces}
          onUpdateStockLength={(optimizationGroupId, stockLength) =>
            updateOptimizationGroups({
              type: 'updateStockLength',
              optimizationGroupId,
              stockLength,
            })
          }
          optimizationGroups={state.optimizationGroups}
        />
      ) : (
        <ImportPage
          bridge={state.bridge}
          materials={state.materials}
          selectedFilePath={state.selectedFilePath}
          importResponse={state.importResponse}
          importSource={state.importSource}
          importConfiguration={state.importConfiguration}
          mappingSession={state.importMappingSession}
          importMessage={state.importMessage}
          importPhase={state.importPhase}
          importProgress={state.importProgress}
          nestingMessage={state.nestingMessage}
          importBusy={state.importBusy}
          partMutationBusy={state.partMutationBusy}
          nestingBusy={state.nestingBusy}
          canImportFiles={
            hasCapability(bridgeMessageTypes.importFile) ||
            hasCapability(bridgeMessageTypes.importCsv)
          }
          canAddRows={
            hasCapability(bridgeMessageTypes.addPartRow) &&
            !state.importMappingSession
          }
          canEditRows={
            hasCapability(bridgeMessageTypes.updatePartRow) &&
            !state.importMappingSession
          }
          canDeleteRows={
            hasCapability(bridgeMessageTypes.deletePartRow) &&
            !state.importMappingSession
          }
          batchNestingEnabled={canRunBatchNesting}
          canRunNesting={canRunNesting}
          canRunAllNesting={canRunAllNesting}
          readyPartCount={readyParts.length}
          readyMaterialCount={readyMaterialCount}
          onImportFile={importFile}
          onUpdateImportMappingSession={updateImportMappingSession}
          onPreviewImportMapping={previewImportMapping}
          onFinalizeImportMapping={finalizeImportMapping}
          onCancelImportMapping={cancelImportMapping}
          onAddPartRow={addPartRow}
          onUpdatePartRow={updatePartRow}
          onDeletePartRow={deletePartRow}
          onRunNesting={() => runNesting('active')}
          onRunAllNesting={() => runNesting('all')}
          optimizationGroups={state.optimizationGroups}
          activeOptimizationGroupId={state.activeOptimizationGroupId}
          onActivateOptimizationGroup={(optimizationGroupId) =>
            dispatch({ type: 'optimization-group-activated', optimizationGroupId })
          }
          onMovePartToOptimizationGroup={movePartToOptimizationGroup}
          canManageOptimizationGroups={hasCapability(
            bridgeMessageTypes.updateOptimizationGroups,
          )}
          onCreateOptimizationGroup={(name) =>
            updateOptimizationGroups({ type: 'create', name })
          }
          onRenameOptimizationGroup={(optimizationGroupId, name) =>
            updateOptimizationGroups({ type: 'rename', optimizationGroupId, name })
          }
          onReorderOptimizationGroups={(orderedOptimizationGroupIds) =>
            updateOptimizationGroups({ type: 'reorder', orderedOptimizationGroupIds })
          }
          onDeleteOptimizationGroup={(optimizationGroupId, removeOwnedContent) =>
            updateOptimizationGroups({
              type: 'delete',
              optimizationGroupId,
              removeOwnedContent,
            })
          }
        />
      );
      break;
    case 'materials':
      content = (
        <MaterialsPage
          materials={state.materials}
          materialLibraryLocation={state.materialLibraryLocation}
          materialLibraryUnavailable={state.materialLibraryUnavailable}
          selectedMaterialId={state.selectedMaterialId}
          importResponse={state.importResponse}
          materialsBusy={state.materialsBusy}
          materialsMessage={state.materialsMessage}
          canChooseMaterialLibraryLocation={hasCapability(
            bridgeMessageTypes.chooseMaterialLibraryLocation,
          )}
          canRestoreDefaultMaterialLibraryLocation={hasCapability(
            bridgeMessageTypes.restoreDefaultMaterialLibraryLocation,
          )}
          onRefreshMaterials={async () => {
            await loadMaterials();
          }}
          onChooseMaterialLibraryLocation={chooseMaterialLibraryLocation}
          onRestoreDefaultMaterialLibraryLocation={
            restoreDefaultMaterialLibraryLocation
          }
          onSelectMaterial={(materialId) =>
            dispatch({ type: 'material-selected', materialId })
          }
          onLoadMaterial={loadMaterial}
          onCreateMaterial={createMaterial}
          onUpdateMaterial={updateMaterial}
          onDeleteMaterial={deleteMaterial}
        />
      );
      break;
    case 'extrusions':
      content = (
        <ExtrusionsPage
          importedRows={state.importResponse.parts}
          optimizationGroups={state.optimizationGroups}
          activeOptimizationGroupId={state.activeOptimizationGroupId}
          layout={state.extrusionLayout}
          statusMessage={state.extrusionMessage}
          busy={state.extrusionBusy}
          canExportPdf={hasCapability(bridgeMessageTypes.exportExtrusionPdfReport)}
          canExportExcel={hasCapability(bridgeMessageTypes.exportExtrusionExcelReport)}
          onLayoutChange={(layout) =>
            dispatch({
              type: 'extrusion-layout-changed',
              layout,
              message:
                'Extrusion layout changed. Save the project to persist these assignments.',
            })
          }
          onLayoutSync={(layout) =>
            dispatch({
              type: 'extrusion-layout-synced',
              layout,
            })
          }
          onExportPdf={exportExtrusionPdfReport}
          onExportExcel={exportExtrusionExcelReport}
        />
      );
      break;
    case 'results':
      content = (
        <ResultsPage
          key={`results-${state.projectId}`}
          projectId={state.projectId}
          projectKind={state.projectKind}
          optimizationGroups={state.optimizationGroups}
          activeOptimizationGroupId={state.activeOptimizationGroupId}
          material={resultsMaterial}
          selectedMaterialId={state.selectedMaterialId}
          companyLogoPath={desktopAppSettings.companyLogoPath ?? null}
          kerfWidth={state.projectSettings.kerfWidth}
          nestResponse={state.nestResponse}
          batchNestResponse={state.batchNestResponse}
          statusMessage={state.nestingMessage}
          savedMaterialSnapshots={state.projectMaterialSnapshots}
          pendingMaterialSnapshots={pendingProjectSnapshots}
          projectDirty={state.projectDirty}
          reportSettings={state.projectSettings.reportSettings}
          reportMessage={state.reportMessage}
          reportBusy={state.reportBusy}
          showStiffenerControls={projectKindSupportsStiffeners(state.projectKind)}
          stiffenerTakeoffEnabled={state.projectSettings.stiffenerTakeoff.enabled}
          stiffenerTakeoffReport={state.stiffenerTakeoffReport}
          stiffenerMessage={state.stiffenerMessage}
          stiffenerBusy={state.stiffenerBusy}
          canSyncReportSettings={hasCapability(bridgeMessageTypes.updateReportSettings)}
          canExportReport={hasCapability(bridgeMessageTypes.exportPdfReport)}
          canExportExcelReport={hasCapability(bridgeMessageTypes.exportExcelReport)}
          canPreviewStiffenerTakeoff={hasCapability(
            bridgeMessageTypes.getStiffenerTakeoff,
          )}
          canExportStiffenerReport={hasCapability(
            bridgeMessageTypes.exportStiffenerPdfReport,
          )}
          stiffenerTakeoffSettings={state.projectSettings.stiffenerTakeoff}
          onReportSettingsChange={updateReportField}
          onStiffenerTakeoffChange={(stiffenerTakeoff) => {
            dispatch({
              type: 'project-settings-changed',
              settings: {
                ...state.projectSettings,
                stiffenerTakeoff,
              },
              message:
                'Stiffener takeoff settings changed. Save the project to persist them.',
            });
          }}
          onPickCompanyLogo={pickCompanyLogoPath}
          onSaveDesktopAppSettings={saveDesktopAppSettings}
          onExportReport={exportReport}
          onExportExcelReport={exportExcelReport}
          onExportStiffenerReport={exportStiffenerReport}
          onSelectOptimizationGroup={(optimizationGroupId) =>
            dispatch({ type: 'optimization-group-activated', optimizationGroupId })
          }
          onReviewOptimizationGroup={(optimizationGroupId) => {
            dispatch({ type: 'optimization-group-activated', optimizationGroupId });
            dispatch({ type: 'route-changed', route: 'import' });
          }}
        />
      );
      break;
    case 'overview':
    default:
      content = (
        <OverviewPage
          projectKind={state.projectKind}
          canChangeProjectKind={
            state.importResponse.parts.length === 0 &&
            state.optimizationGroups.every((group) =>
              group.parts.length === 0 && group.requiredPieces.length === 0
            )
          }
          metadata={state.projectMetadata}
          projectBusy={state.projectBusy}
          projectDirty={state.projectDirty}
          projectFilePath={state.projectFilePath}
          projectMessage={state.projectMessage}
          importResponse={state.importResponse}
          nestResponse={state.nestResponse}
          savedMaterialSnapshots={state.projectMaterialSnapshots}
          kerfWidth={state.projectSettings.kerfWidth}
          reportSettings={state.projectSettings.reportSettings}
          stiffenerTakeoff={state.projectSettings.stiffenerTakeoff}
          companyLogoPath={desktopAppSettings.companyLogoPath ?? null}
          onMetadataChange={(field, value) =>
            {
              const nextMetadata = {
                ...state.projectMetadata,
                [field]: value,
              };

              dispatch({
                type: 'project-metadata-changed',
                metadata: nextMetadata,
                settings: {
                  ...state.projectSettings,
                  reportSettings: syncReportSettingsWithMetadata(
                    state.projectMetadata,
                    nextMetadata,
                    state.projectSettings.reportSettings,
                    desktopAppSettings.companyName,
                  ),
                },
                message:
                  'Project metadata changed. Save the project to keep the latest job details with its snapshots.',
              });
            }
          }
          onProjectKindChange={changeProjectKind}
          onKerfWidthChange={(value) => {
            dispatch({
              type: 'project-settings-changed',
              settings: {
                ...state.projectSettings,
                kerfWidth: value,
              },
              invalidateNestingResults: true,
              message: 'Kerf width updated. Save the project to persist this setting.',
            });
          }}
          onReportSettingsChange={updateReportField}
          onStiffenerTakeoffChange={(stiffenerTakeoff) => {
            dispatch({
              type: 'project-settings-changed',
              settings: {
                ...state.projectSettings,
                stiffenerTakeoff,
              },
              message:
                'Stiffener takeoff settings changed. Save the project to persist them.',
            });
          }}
          onPickCompanyLogo={pickCompanyLogoPath}
          onSaveDesktopAppSettings={saveDesktopAppSettings}
        />
      );
      break;
  }

  const contentClassName =
    state.activeRoute === 'results'
      ? 'app-route app-route--results'
      : state.activeRoute === 'extrusions'
        ? 'app-route app-route--extrusions'
        : state.activeRoute === 'import' &&
            state.projectKind === 'stockLength' &&
            (Boolean(state.importMappingSession) || state.optimizationGroups.some((group) => group.requiredPieces.length > 0))
          ? 'app-route app-route--stock-length-import'
        : 'app-route';

  return (
    <>
      <AppShell
        activeRoute={state.activeRoute}
        projectKind={state.projectKind}
        onRouteChange={(route) => dispatch({ type: 'route-changed', route })}
        projectBusy={state.projectBusy}
        onCreateProject={requestNewProject}
        onOpenProject={openProject}
        onSaveProject={() => saveProject().then(() => undefined)}
        onSaveProjectAs={() => saveProject({ saveAs: true }).then(() => undefined)}
        canOpenProject={hasCapability(bridgeMessageTypes.openProject)}
        canSaveProject={
          hasCapability(bridgeMessageTypes.saveProject) ||
          hasCapability(bridgeMessageTypes.saveProjectAs)
        }
        canSaveProjectAs={hasCapability(bridgeMessageTypes.saveProjectAs)}
        bridgeConnected={state.bridge.connected}
        bridgeStatusMessage={
          state.bridge.lastError ??
          state.bridge.handshake.message ??
          'Desktop host connection unavailable.'
        }
        onReconnect={retryHandshake}
      >
        <div className={contentClassName}>{content}</div>
      </AppShell>
      {newProjectDialogOpen ? (
        <NewProjectDialog
          onCancel={() => setNewProjectDialogOpen(false)}
          onCreate={createNewProject}
        />
      ) : null}
      {pendingImportReplacement ? <ConfirmationDialog
        danger
        message="Imported Worksheets, source-derived parts, saved import configuration, and affected Optimization Results will be removed. Manual parts and their groups will be preserved."
        onCancel={() => {
          setPendingImportReplacement(null);
          dispatch({ type: 'import-selection-cancelled', message: 'Import Source replacement cancelled. The current project is unchanged.' });
        }}
        onConfirm={() => {
          const request = pendingImportReplacement;
          setPendingImportReplacement(null);
          void importFile(request.requestedFilePath, true, request.droppedFile);
        }}
        confirmLabel="Replace Import Source"
        title="Replace the current Import Source?"
      /> : null}
      {unsavedPromptActionLabel ? (
        <div
          className="results-dialog-backdrop"
          onClick={() => {
            setUnsavedPromptActionLabel(null);
            unsavedPromptResolverRef.current?.('cancel');
            unsavedPromptResolverRef.current = null;
          }}
          role="presentation"
        >
          <div
            aria-modal="true"
            className="results-dialog app-confirm-dialog"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
          >
            <div className="results-dialog__header">
              <div>
                <p className="eyebrow">Unsaved Changes</p>
                <h3>Save before continuing?</h3>
              </div>
            </div>
            <p className="section-note">
              Do you want to save changes to this project before {unsavedPromptActionLabel}?
            </p>
            <div className="form-actions">
              <button
                className="secondary-button"
                onClick={() => {
                  setUnsavedPromptActionLabel(null);
                  unsavedPromptResolverRef.current?.('save');
                  unsavedPromptResolverRef.current = null;
                }}
                type="button"
              >
                Yes
              </button>
              <button
                className="secondary-button"
                onClick={() => {
                  setUnsavedPromptActionLabel(null);
                  unsavedPromptResolverRef.current?.('discard');
                  unsavedPromptResolverRef.current = null;
                }}
                type="button"
              >
                No
              </button>
              <button
                className="secondary-button"
                onClick={() => {
                  setUnsavedPromptActionLabel(null);
                  unsavedPromptResolverRef.current?.('cancel');
                  unsavedPromptResolverRef.current = null;
                }}
                type="button"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
