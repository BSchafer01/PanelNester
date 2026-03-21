import { useEffect, useReducer, useRef } from 'react';
import { AppShell } from './components/AppShell';
import { hostBridge } from './bridge/hostBridge';
import { ImportPage } from './pages/ImportPage';
import { MaterialsPage } from './pages/MaterialsPage';
import { OverviewPage } from './pages/OverviewPage';
import { ResultsPage } from './pages/ResultsPage';
import {
  type BatchNestResponse,
  type BridgeError,
  bridgeMessageTypes,
  demoKerfWidth,
  demoMaterial,
  emptyBatchNestResponse,
  emptyImportResponse,
  emptyNestResponse,
  type BridgeCapability,
  type HostBridgeSnapshot,
  type ImportFileResponse,
  type ImportFieldName,
  optionalImportFieldNames,
  requiredImportFieldNames,
  type ImportFileRequest,
  type ImportMappingSession,
  type ImportMaterialResolution,
  type ImportResponse,
  type ImportOptions,
  type Material,
  type MaterialDraft,
  type MaterialLibraryLocation,
  type MaterialLibraryOperationResponse,
  type NestResponse,
  type OpenFileDialogResponse,
  type OpenProjectRequest,
  type PartRowUpdate,
  type ProjectFileMetadata,
  type ProjectMaterialSnapshot,
  type ProjectMetadata,
  type ProjectRecord,
  type ProjectSettings,
  type ReportSettings,
} from './types/contracts';

type AppRoute = 'overview' | 'import' | 'materials' | 'results';

declare global {
  interface Window {
    panelNesterDesktopHost?: {
      openProject: (request: OpenProjectRequest) => void | Promise<void>;
    };
  }
}

const importFileDialogTimeoutMs = 300000;
const importBridgeTimeoutMs = 120000;

interface AppState {
  activeRoute: AppRoute;
  bridge: HostBridgeSnapshot;
  importResponse: ImportResponse;
  nestResponse: NestResponse;
  batchNestResponse: BatchNestResponse;
  materials: Material[];
  materialLibraryLocation?: MaterialLibraryLocation | null;
  selectedMaterialId?: string;
  lastNestMaterial?: Material;
  selectedFilePath?: string;
  importMappingSession?: ImportMappingSession;
  importMessage: string;
  nestingMessage: string;
  materialsMessage: string;
  reportMessage: string;
  importBusy: boolean;
  nestingBusy: boolean;
  materialsBusy: boolean;
  reportBusy: boolean;
  projectMetadata: ProjectMetadata;
  projectSettings: ProjectSettings;
  projectId: string;
  projectFilePath?: string;
  projectMaterialSnapshots: ProjectMaterialSnapshot[];
  projectMessage: string;
  projectBusy: boolean;
  projectDirty: boolean;
  partMutationBusy: boolean;
  lastSavedAt?: string;
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
  | { type: 'materials-failed'; message: string }
  | { type: 'material-selected'; materialId?: string }
  | { type: 'material-created'; material: Material; message: string }
  | { type: 'material-updated'; material: Material; message: string }
  | { type: 'material-deleted'; materialId: string; message: string }
  | { type: 'import-started'; message: string }
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
      message: string;
      selectedMaterialId?: string;
    }
  | { type: 'part-row-operation-started'; message: string }
  | {
      type: 'part-rows-replaced';
      response: ImportResponse;
      message: string;
      selectedMaterialId?: string;
    }
  | { type: 'part-row-operation-failed'; message: string }
  | { type: 'import-failed'; message: string }
  | { type: 'nesting-started'; message: string }
  | {
      type: 'nesting-finished';
      response: NestResponse;
      batchResponse: BatchNestResponse;
      message: string;
      material?: Material;
    }
  | { type: 'nesting-failed'; message: string }
  | {
      type: 'project-created';
      metadata: ProjectMetadata;
      settings: ProjectSettings;
      projectId?: string;
      message: string;
    }
  | {
      type: 'project-opened';
      filePath: string;
      project: ProjectRecord;
      selectedMaterialId?: string;
      lastNestMaterial?: Material;
      message: string;
    }
  | {
      type: 'project-saved';
      filePath: string;
      project: ProjectRecord;
      message: string;
    }
  | { type: 'project-operation-started'; message: string }
  | { type: 'project-operation-finished'; message: string }
  | { type: 'project-operation-failed'; message: string }
  | {
      type: 'project-metadata-changed';
      metadata: ProjectMetadata;
      settings: ProjectSettings;
      message: string;
    }
  | { type: 'project-settings-changed'; settings: ProjectSettings; message: string }
  | { type: 'report-operation-started'; message: string }
  | { type: 'report-operation-finished'; message: string }
  | { type: 'report-operation-failed'; message: string };

const defaultImportMessage =
  'Choose a CSV or XLSX file, review column/material mappings, then finalize the import before nesting.';
const defaultNestingMessage =
  'Select a material for focus if needed, then run nesting when the imported rows are ready.';
const defaultMaterialsMessage =
  'Connect to the desktop host to load the reusable material library.';
const defaultProjectMessage =
  'Use this page to manage metadata, file state, and the material snapshots that travel with a saved project.';
const defaultReportMessage =
  'Edit report fields here, then export once the desktop host exposes the Phase 5 PDF bridge.';

function createDefaultProjectMetadata(): ProjectMetadata {
  return {
    projectName: 'Untitled Project',
    projectNumber: '',
    customerName: '',
    estimator: '',
    drafter: '',
    projectManager: '',
    date: new Date().toISOString().slice(0, 10),
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
  return `${displayName}${isDirty ? ' *' : ''} — PanelNester`;
}

function normalizeReportDate(value?: string | null): string {
  return value?.slice(0, 10) ?? '';
}

function createDefaultReportSettings(metadata: ProjectMetadata): ReportSettings {
  return {
    companyName: metadata.customerName,
    reportTitle: buildDefaultReportTitle(metadata.projectName),
    projectJobName: metadata.projectName,
    projectJobNumber: metadata.projectNumber,
    reportDate: normalizeReportDate(metadata.date),
    notes: metadata.notes,
  };
}

function normalizeReportSettings(
  reportSettings: ReportSettings | null | undefined,
  metadata: ProjectMetadata,
): ReportSettings {
  const defaults = createDefaultReportSettings(metadata);

  return {
    companyName: reportSettings?.companyName?.trim() ?? defaults.companyName,
    reportTitle: reportSettings?.reportTitle?.trim() ?? defaults.reportTitle,
    projectJobName: reportSettings?.projectJobName?.trim() ?? defaults.projectJobName,
    projectJobNumber:
      reportSettings?.projectJobNumber?.trim() ?? defaults.projectJobNumber,
    reportDate: normalizeReportDate(reportSettings?.reportDate ?? defaults.reportDate),
    notes: reportSettings?.notes ?? defaults.notes,
  };
}

function createProjectSettings(metadata: ProjectMetadata): ProjectSettings {
  return {
    kerfWidth: demoKerfWidth,
    reportSettings: createDefaultReportSettings(metadata),
  };
}

function normalizeProjectSettings(
  settings: ProjectSettings | null | undefined,
  metadata: ProjectMetadata,
): ProjectSettings {
  return {
    kerfWidth:
      typeof settings?.kerfWidth === 'number' && settings.kerfWidth >= 0
        ? settings.kerfWidth
        : demoKerfWidth,
    reportSettings: normalizeReportSettings(settings?.reportSettings, metadata),
  };
}

function syncReportSettingsWithMetadata(
  previousMetadata: ProjectMetadata,
  nextMetadata: ProjectMetadata,
  reportSettings: ReportSettings,
): ReportSettings {
  const previousDefaults = createDefaultReportSettings(previousMetadata);
  const nextDefaults = createDefaultReportSettings(nextMetadata);

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
    reportDate: pickValue('reportDate'),
    notes: pickValue('notes'),
  };
}

const initialState: AppState = {
  activeRoute: 'overview',
  bridge: hostBridge.getSnapshot(),
  importResponse: emptyImportResponse,
  nestResponse: emptyNestResponse,
  batchNestResponse: emptyBatchNestResponse,
  materials: [],
  materialLibraryLocation: undefined,
  selectedMaterialId: undefined,
  lastNestMaterial: undefined,
  selectedFilePath: undefined,
  importMappingSession: undefined,
  importMessage: defaultImportMessage,
  nestingMessage: defaultNestingMessage,
  materialsMessage: defaultMaterialsMessage,
  reportMessage: defaultReportMessage,
  importBusy: false,
  nestingBusy: false,
  materialsBusy: false,
  reportBusy: false,
  projectMetadata: createDefaultProjectMetadata(),
  projectSettings: createProjectSettings(createDefaultProjectMetadata()),
  projectId: '',
  projectFilePath: undefined,
  projectMaterialSnapshots: [],
  projectMessage: defaultProjectMessage,
  projectBusy: false,
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
  success: boolean;
  parts: ImportResponse['parts'];
  errors: ImportResponse['errors'];
  warnings: ImportResponse['warnings'];
  availableColumns?: string[];
  columnMappings?: ImportResponse['columnMappings'];
  materialResolutions?: ImportResponse['materialResolutions'];
}): ImportResponse {
  return {
    success: response.success,
    parts: response.parts,
    errors: response.errors,
    warnings: response.warnings,
    availableColumns: response.availableColumns ?? [],
    columnMappings: response.columnMappings ?? [],
    materialResolutions: response.materialResolutions ?? [],
  };
}

function toImportResponse(response: ImportFileResponse): ImportResponse {
  return normalizeImportResponse(response);
}

function buildImportOptionsFromResponse(response: ImportFileResponse): ImportOptions {
  return {
    columnMappings: response.columnMappings
      .filter((mapping) => Boolean(mapping.sourceColumn))
      .map((mapping) => ({
        sourceColumn: mapping.sourceColumn ?? '',
        targetField: mapping.targetField as ImportFieldName,
      })),
    materialMappings: response.materialResolutions
      .filter((resolution) => Boolean(resolution.resolvedMaterialId))
      .map((resolution) => ({
        sourceMaterialName: resolution.sourceMaterialName,
        targetMaterialId: resolution.resolvedMaterialId ?? null,
      })),
  };
}

function createImportMappingSession(
  filePath: string,
  response: ImportFileResponse,
  existing?: ImportMappingSession,
): ImportMappingSession {
  return {
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

function buildProjectRecord(state: AppState): ProjectRecord {
  const materialSnapshots = collectProjectMaterialSnapshots(
    state.materials,
    state.importResponse,
    state.selectedMaterialId,
    state.lastNestMaterial,
    state.projectMaterialSnapshots,
  );
  const batchNestResponse =
    state.batchNestResponse.materialResults.length > 0
      ? state.batchNestResponse
      : buildBatchFromLegacy(
          state.nestResponse,
          state.lastNestMaterial,
          materialSnapshots,
          state.selectedMaterialId,
        );

  return {
    version: 1,
    projectId: state.projectId,
    metadata: mapMetadataToBridge(state.projectMetadata),
    settings: state.projectSettings,
    materialSnapshots,
    state: {
      sourceFilePath: state.selectedFilePath ?? null,
      parts: state.importResponse.parts,
      selectedMaterialId: state.selectedMaterialId ?? null,
      lastNestingResult:
        state.nestResponse.sheets.length > 0 ||
        state.nestResponse.unplacedItems.length > 0
          ? state.nestResponse
          : null,
      lastBatchNestingResult:
        batchNestResponse.materialResults.length > 0 ? batchNestResponse : null,
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
        activeRoute: action.route,
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
        selectedMaterialId: action.selectedMaterialId,
        materialsMessage: action.message,
      };
    case 'materials-failed':
      return {
        ...state,
        materialsBusy: false,
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
      const materials = sortMaterials(
        state.materials.map((material) =>
          material.materialId === action.material.materialId ? action.material : material,
        ),
      );
      const nextState = {
        ...state,
        materialsBusy: false,
        materials,
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
      const nextState = {
        ...state,
        materialsBusy: false,
        materials,
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
        importMessage: action.message,
      };
    case 'import-selection-cancelled':
      return {
        ...state,
        importBusy: false,
        importMessage: action.message,
      };
    case 'import-mapping-ready':
      return {
        ...state,
        importBusy: false,
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
        importMappingSession: undefined,
        importMessage: action.message,
      };
    case 'import-finished':
      return markProjectDirty(
        {
          ...state,
          importBusy: false,
          importMappingSession: undefined,
          selectedFilePath: action.filePath,
          importResponse: action.response,
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
        importMessage: action.message,
      };
    case 'nesting-started':
      return {
        ...state,
        nestingBusy: true,
        nestingMessage: action.message,
      };
    case 'nesting-finished':
      return markProjectDirty(
        {
          ...state,
          nestingBusy: false,
          nestResponse: action.response,
          batchNestResponse: action.batchResponse,
          lastNestMaterial: action.material,
          nestingMessage: action.message,
        },
        'Nesting results changed. Save the project to keep this layout with its material snapshot.',
      );
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
        importResponse: emptyImportResponse,
        nestResponse: emptyNestResponse,
        batchNestResponse: emptyBatchNestResponse,
        selectedMaterialId: undefined,
        lastNestMaterial: undefined,
        selectedFilePath: undefined,
        importMappingSession: undefined,
        importMessage: defaultImportMessage,
        nestingMessage: defaultNestingMessage,
        reportMessage: defaultReportMessage,
        reportBusy: false,
        projectMetadata: action.metadata,
        projectSettings: action.settings,
        projectId: action.projectId ?? '',
        projectFilePath: undefined,
        projectMaterialSnapshots: [],
        projectMessage: action.message,
        projectBusy: false,
        projectDirty: false,
        partMutationBusy: false,
        lastSavedAt: undefined,
      };
    case 'project-opened': {
      const importResponse = getProjectImportResponse(action.project);
      const nestResponse = action.project.state.lastNestingResult ?? emptyNestResponse;
      const projectMetadata = mapMetadataFromBridge(action.project.metadata);
      const projectSettings = normalizeProjectSettings(
        action.project.settings,
        projectMetadata,
      );
      const batchNestResponse = getProjectBatchNestResponse(
        action.project,
        action.lastNestMaterial,
      );

      return {
        ...state,
        activeRoute: 'overview',
        projectBusy: false,
        projectDirty: false,
        projectMetadata,
        projectSettings,
        projectId: action.project.projectId,
        projectFilePath: action.filePath,
        projectMaterialSnapshots: sortByName(action.project.materialSnapshots),
        lastSavedAt: new Date().toISOString(),
        selectedFilePath: action.project.state.sourceFilePath ?? undefined,
        importMappingSession: undefined,
        importResponse,
        nestResponse,
        batchNestResponse,
        selectedMaterialId: action.selectedMaterialId,
        lastNestMaterial: action.lastNestMaterial,
        partMutationBusy: false,
        reportBusy: false,
        projectMessage: action.message,
        reportMessage: defaultReportMessage,
        importMessage:
          importResponse.parts.length > 0
            ? describeImportResult(
                action.project.state.sourceFilePath ?? action.filePath,
                importResponse,
              )
            : defaultImportMessage,
        nestingMessage:
          batchNestResponse.materialResults.length > 1
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
        projectFilePath: action.filePath,
        projectSettings: normalizeProjectSettings(
          action.project.settings,
          mapMetadataFromBridge(action.project.metadata),
        ),
        projectMaterialSnapshots: sortByName(action.project.materialSnapshots),
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
        },
        action.message,
      );
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
    default:
      return state;
  }
}

export default function App() {
  const [state, dispatch] = useReducer(reducer, initialState);
  const materialSelectionRef = useRef({
    importResponse: state.importResponse,
    selectedMaterialId: state.selectedMaterialId,
  });
  const hostReadyNotifiedRef = useRef(false);
  const startupProjectOpenRef = useRef<(request: OpenProjectRequest) => void | Promise<void>>(
    () => undefined,
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

    try {
      const response = await hostBridge.listMaterials();
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
      dispatch({ type: 'materials-failed', message });
      throw new Error(message);
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
      openProject: (request: OpenProjectRequest) => {
        void startupProjectOpenRef.current(request);
      },
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
    document.title = buildWindowTitle(state.projectMetadata.projectName, state.projectDirty);
  }, [state.projectDirty, state.projectMetadata.projectName]);

  const hasCapability = (capability: BridgeCapability): boolean =>
    state.bridge.handshake.capabilities.includes(capability);

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

  const saveProject = async (options?: { saveAs?: boolean }): Promise<boolean> => {
    const canSaveProject = hasCapability(bridgeMessageTypes.saveProject);
    const canSaveProjectAs = hasCapability(bridgeMessageTypes.saveProjectAs);
    const useSaveAs = options?.saveAs || !state.projectFilePath || !canSaveProject;

    if (useSaveAs && !canSaveProjectAs) {
      dispatch({
        type: 'project-operation-failed',
        message:
          'The connected desktop host has not exposed Save As yet. Metadata and dirty tracking stay active in the shell.',
      });
      return false;
    }

    if (!useSaveAs && !canSaveProject) {
      dispatch({
        type: 'project-operation-failed',
        message:
          'The connected desktop host has not exposed Save yet. Use Save As when the capability appears.',
      });
      return false;
    }

    const project = buildProjectRecord(state);

    dispatch({
      type: 'project-operation-started',
      message: useSaveAs ? 'Saving project as…' : 'Saving project…',
    });

    try {
      const response = useSaveAs
        ? await hostBridge.saveProjectAs({
            filePath: state.projectFilePath ?? null,
            suggestedFileName: `${state.projectMetadata.projectName
              .trim()
              .toLowerCase()
              .replace(/[^a-z0-9]+/g, '-')
              .replace(/^-+|-+$/g, '') || 'panelnester-project'}.pnest`,
            project,
          })
        : await hostBridge.saveProject({
            filePath: state.projectFilePath ?? null,
            project,
          });

      if (!response.success) {
        if (response.error?.code === 'cancelled') {
          dispatch({
            type: 'project-operation-finished',
            message: response.message ?? 'Project save was cancelled.',
          });
          return false;
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

      dispatch({
        type: 'project-saved',
        filePath,
        project: response.project ?? project,
        message: response.message ?? `Saved ${fileNameFromPath(filePath)}.`,
      });
      return true;
    } catch (error) {
      dispatch({
        type: 'project-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not save the project.',
        ),
      });
      return false;
    }
  };

  const confirmProjectTransition = async (actionLabel: string): Promise<boolean> => {
    if (!state.projectDirty) {
      return true;
    }

    const saveFirst = window.confirm(
      `Save changes to "${state.projectMetadata.projectName}" before ${actionLabel}? Press OK to save first, or Cancel for discard options.`,
    );

    if (saveFirst) {
      return saveProject();
    }

    return window.confirm(
      `Discard unsaved changes and continue ${actionLabel}?`,
    );
  };

  const createNewProject = async () => {
    const canProceed = await confirmProjectTransition('starting a new project');
    if (!canProceed) {
      return;
    }

    const metadata = createDefaultProjectMetadata();
    const settings = createProjectSettings(metadata);

    if (hasCapability(bridgeMessageTypes.newProject)) {
      dispatch({
        type: 'project-operation-started',
        message: 'Starting a new project…',
      });

      try {
        const response = await hostBridge.newProject({
          metadata: mapMetadataToBridge(metadata),
          settings,
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
              )
            : settings,
          projectId: response.project?.projectId,
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
      message:
        'Started a new project in the UI. Save and Open stay ready to light up when the desktop host exposes the Phase 3 file commands.',
    });
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
    const canProceed = await confirmProjectTransition(actionLabel);
    if (!canProceed) {
      return;
    }

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

      dispatch({
        type: 'project-opened',
        filePath: response.filePath,
        project,
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
  };
  startupProjectOpenRef.current = openProject;

  const importFile = async () => {
    dispatch({
      type: 'import-started',
      message: 'Opening the native file picker and preparing the import review…',
    });

    try {
      const openImportDialog = () =>
        hostBridge.invoke<OpenFileDialogResponse>(
          bridgeMessageTypes.openFileDialog,
          {
            title: 'Select a parts file',
            filters: [
              { name: 'Supported files', extensions: ['csv', 'xlsx'] },
              { name: 'CSV files', extensions: ['csv'] },
              { name: 'Excel workbooks', extensions: ['xlsx'] },
              { name: 'All files', extensions: ['*.*'] },
            ],
          },
          importFileDialogTimeoutMs,
        );
      const invokeImportFile = (request: ImportFileRequest) =>
        hostBridge.invoke<ImportFileResponse>(
          bridgeMessageTypes.importFile,
          request,
          importBridgeTimeoutMs,
        );

      if (hasCapability(bridgeMessageTypes.importFile)) {
        const dialogResponse = hasCapability(bridgeMessageTypes.openFileDialog)
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
          session: createImportMappingSession(filePath, response),
          message: describeImportReview(filePath, importResponse),
        });
        return;
      }

      const dialogResponse = await openImportDialog();

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
    dispatch({
      type: 'import-mapping-updated',
      session,
    });
  };

  const cancelImportMapping = () => {
    dispatch({
      type: 'import-mapping-cancelled',
      message:
        state.importResponse.parts.length > 0
          ? 'Import review cancelled. The current imported payload remains active.'
          : defaultImportMessage,
    });
  };

  const previewImportMapping = async () => {
    const session = state.importMappingSession;
    if (!session) {
      return;
    }

    dispatch({
      type: 'import-started',
      message: `Refreshing the import preview for ${fileNameFromPath(session.filePath)}…`,
    });

    try {
      const response = await hostBridge.invoke<ImportFileResponse>(
        bridgeMessageTypes.importFile,
        {
          filePath: session.filePath,
          options: session.options,
        } satisfies ImportFileRequest,
        importBridgeTimeoutMs,
      );
      const filePath = pickImportFilePath(response, session.filePath) ?? session.filePath;
      const nextSession = createImportMappingSession(filePath, response, {
        ...session,
        hasPendingChanges: false,
      });

      dispatch({
        type: 'import-mapping-ready',
        session: nextSession,
        message: describeImportReview(filePath, toImportResponse(response), nextSession),
      });
    } catch (error) {
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

    dispatch({
      type: 'import-started',
      message: `Finalizing the import for ${fileNameFromPath(session.filePath)}…`,
    });

    try {
      const response = await hostBridge.invoke<ImportFileResponse>(
        bridgeMessageTypes.importFile,
        {
          filePath: session.filePath,
          options: session.options,
          newMaterials: session.newMaterials,
        } satisfies ImportFileRequest,
        importBridgeTimeoutMs,
      );
      const filePath = pickImportFilePath(response, session.filePath) ?? session.filePath;

      if (responseLooksLikeImportPreparationFailure(response)) {
        dispatch({
          type: 'import-mapping-ready',
          session,
          message: getBridgeErrorMessage(
            response.error,
            response.message ?? 'Import mapping could not be finalized.',
          ),
        });
        return;
      }

      const importResponse = toImportResponse(response);
      const syncedMaterials =
        session.newMaterials.length > 0
          ? await loadMaterials({
              message: 'Material library synced after import-time material creation.',
              selectionContext: {
                importResponse,
                selectedMaterialId: state.selectedMaterialId,
              },
            }).catch(() => undefined)
          : undefined;
      const effectiveMaterials = syncedMaterials ?? state.materials;

      dispatch({
        type: 'import-finished',
        filePath,
        response: importResponse,
        selectedMaterialId: pickMaterialId(
          effectiveMaterials,
          importResponse,
          state.selectedMaterialId,
        ),
        message: describeImportResult(filePath, importResponse),
      });
    } catch (error) {
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
  const readyParts = getReadyParts(state.importResponse);
  const readyMaterialCount = new Set(
    readyParts
      .map((part) => part.materialName.trim())
      .filter((name) => name.length > 0),
  ).size;
  const nestableParts = getNestableParts(state.importResponse, selectedMaterial);
  const canRunBatchNesting = hasCapability(bridgeMessageTypes.runBatchNesting);

  const runNesting = async () => {
    if (canRunBatchNesting) {
      if (readyParts.length === 0) {
        dispatch({
          type: 'nesting-failed',
          message: 'No ready imported rows are available for batch nesting.',
        });
        return;
      }

      dispatch({
        type: 'nesting-started',
        message: `Running batch nesting for ${readyParts.length} row(s) across ${readyMaterialCount} material group(s)…`,
      });

      try {
        const batchResponse = await hostBridge.runBatchNesting({
          parts: readyParts,
          materials: state.materials,
          kerfWidth: state.projectSettings.kerfWidth,
          selectedMaterialId: state.selectedMaterialId ?? null,
        });
        const primaryMaterialResult =
          batchResponse.materialResults.find(
            (result) =>
              result.materialId === state.selectedMaterialId ||
              result.materialName === selectedMaterial?.name,
          ) ?? batchResponse.materialResults[0];
        const focusedMaterial =
          selectedMaterial ??
          state.materials.find(
            (material) =>
              material.materialId === primaryMaterialResult?.materialId ||
              material.name === primaryMaterialResult?.materialName,
          );
        const legacyResponse =
          batchResponse.legacyResult ?? primaryMaterialResult?.result ?? emptyNestResponse;

        dispatch({
          type: 'nesting-finished',
          response: legacyResponse,
          batchResponse,
          material: focusedMaterial,
          message: describeBatchNestingResult(batchResponse),
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
        message: `No valid imported rows currently match ${selectedMaterial.name}.`,
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

  const syncReportSettings = async () => {
    if (!hasCapability(bridgeMessageTypes.updateReportSettings)) {
      dispatch({
        type: 'report-operation-failed',
        message:
          'The connected desktop host has not exposed report-settings sync yet. Save the project to preserve the current report fields.',
      });
      return;
    }

    dispatch({
      type: 'report-operation-started',
      message: 'Applying report settings through the desktop bridge…',
    });

    try {
      const project = buildProjectRecord(state);
      const response = await hostBridge.updateReportSettings({
        project,
        reportSettings: state.projectSettings.reportSettings,
      });

      if (!response.success) {
        throw new Error(
          getBridgeErrorMessage(
            response.error,
            response.message ?? 'Report settings could not be applied.',
          ),
        );
      }

      if (response.reportSettings) {
        dispatch({
          type: 'project-settings-changed',
          settings: {
            ...state.projectSettings,
            reportSettings: normalizeReportSettings(
              response.reportSettings,
              state.projectMetadata,
            ),
          },
          message:
            'Report settings changed. Save the project to keep these export fields with the job.',
        });
      }

      dispatch({
        type: 'report-operation-finished',
        message: response.message ?? 'Report settings synced to the desktop host.',
      });
    } catch (error) {
      dispatch({
        type: 'report-operation-failed',
        message: getErrorMessage(
          error,
          'The desktop host could not apply the report settings.',
        ),
      });
    }
  };

  const exportReport = async () => {
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

    if (!hasResult) {
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
      const project = buildProjectRecord(state);
      const response = await hostBridge.exportPdfReport({
        project,
        batchResult: project.state.lastBatchNestingResult ?? null,
        filePath: null,
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

  let content: React.ReactNode;
  switch (state.activeRoute) {
    case 'import':
      content = (
        <ImportPage
          bridge={state.bridge}
          materials={state.materials}
          selectedFilePath={state.selectedFilePath}
          importResponse={state.importResponse}
          mappingSession={state.importMappingSession}
          importMessage={state.importMessage}
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
          onRunNesting={runNesting}
        />
      );
      break;
    case 'materials':
      content = (
        <MaterialsPage
          materials={state.materials}
          materialLibraryLocation={state.materialLibraryLocation}
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
    case 'results':
      content = (
        <ResultsPage
          material={resultsMaterial}
          selectedMaterialId={state.selectedMaterialId}
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
          canSyncReportSettings={hasCapability(bridgeMessageTypes.updateReportSettings)}
          canExportReport={hasCapability(bridgeMessageTypes.exportPdfReport)}
          onReportSettingsChange={updateReportField}
          onSyncReportSettings={syncReportSettings}
          onExportReport={exportReport}
        />
      );
      break;
    case 'overview':
    default:
      content = (
        <OverviewPage
          metadata={state.projectMetadata}
          projectBusy={state.projectBusy}
          projectDirty={state.projectDirty}
          projectFilePath={state.projectFilePath}
          projectMessage={state.projectMessage}
          importResponse={state.importResponse}
          nestResponse={state.nestResponse}
          savedMaterialSnapshots={state.projectMaterialSnapshots}
          kerfWidth={state.projectSettings.kerfWidth}
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
                  ),
                },
                message:
                  'Project metadata changed. Save the project to keep the latest job details with its snapshots.',
              });
            }
          }
          onKerfWidthChange={(value) => {
            dispatch({
              type: 'project-settings-changed',
              settings: {
                ...state.projectSettings,
                kerfWidth: value,
              },
              message: 'Kerf width updated. Save the project to persist this setting.',
            });
          }}
        />
      );
      break;
  }

  const contentClassName =
    state.activeRoute === 'results' ? 'app-route app-route--results' : 'app-route';

  return (
    <AppShell
      activeRoute={state.activeRoute}
      onRouteChange={(route) => dispatch({ type: 'route-changed', route })}
      projectBusy={state.projectBusy}
      onCreateProject={createNewProject}
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
  );
}
