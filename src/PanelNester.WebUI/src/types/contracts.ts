export const bridgeMessageTypes = {
  handshake: 'bridge-handshake',
  bridgeUiReady: 'bridge-ui-ready',
  openFileDialog: 'open-file-dialog',
  importCsv: 'import-csv',
  importFile: 'import-file',
  beginImportSession: 'begin-import-session',
  previewImportSession: 'preview-import-session',
  finalizeImportSession: 'finalize-import-session',
  cancelImportSession: 'cancel-import-session',
  getImportSessionProgress: 'get-import-session-progress',
  updatePartRow: 'update-part-row',
  deletePartRow: 'delete-part-row',
  addPartRow: 'add-part-row',
  runNesting: 'run-nesting',
  runBatchNesting: 'run-batch-nesting',
  getStiffenerTakeoff: 'get-stiffener-takeoff',
  getExtrusionLayout: 'get-extrusion-layout',
  updateExtrusionLayout: 'update-extrusion-layout',
  getExtrusionReport: 'get-extrusion-report',
  exportPdfReport: 'export-pdf-report',
  exportExcelReport: 'export-excel-report',
  exportStiffenerPdfReport: 'export-stiffener-pdf-report',
  exportExtrusionPdfReport: 'export-extrusion-pdf-report',
  exportExtrusionExcelReport: 'export-extrusion-excel-report',
  updateReportSettings: 'update-report-settings',
  listMaterials: 'list-materials',
  chooseMaterialLibraryLocation: 'choose-material-library-location',
  restoreDefaultMaterialLibraryLocation:
    'restore-default-material-library-location',
  getMaterial: 'get-material',
  createMaterial: 'create-material',
  updateMaterial: 'update-material',
  deleteMaterial: 'delete-material',
  newProject: 'new-project',
  openProject: 'open-project',
  saveProject: 'save-project',
  saveProjectAs: 'save-project-as',
  getProjectMetadata: 'get-project-metadata',
  updateProjectMetadata: 'update-project-metadata',
  changeProjectKind: 'change-project-kind',
  updateOptimizationGroups: 'update-optimization-groups',
  updateRequiredPieces: 'update-required-pieces',
  generateSelectedCutPlan: 'generate-selected-cut-plan',
  generateAllStaleCutPlans: 'generate-all-stale-cut-plans',
  cancelCutPlanGeneration: 'cancel-cut-plan-generation',
  getCutPlanGenerationProgress: 'get-cut-plan-generation-progress',
  getDesktopAppSettings: 'get-desktop-app-settings',
  updateDesktopAppSettings: 'update-desktop-app-settings',
} as const;

export const toBridgeResponseType = (type: string) => `${type}-response`;

export type BridgeCapability =
  (typeof bridgeMessageTypes)[keyof typeof bridgeMessageTypes];

export type ProjectKind = 'sheet' | 'stockLength';

export interface BridgeMessage<TPayload = unknown> {
  type: string;
  requestId?: string;
  payload: TPayload;
}

export interface BridgeError {
  code: string;
  message: string;
  userMessage?: string | null;
}

export interface BridgeHandshakeRequest {
  surface: 'OptiFab.WebUI';
  version: string;
  requestedCapabilities: BridgeCapability[];
}

export interface BridgeHandshakeResponse {
  success: boolean;
  hostName: string;
  hostVersion?: string;
  bridgeMode: 'webview2' | 'standalone';
  capabilities: BridgeCapability[];
  message?: string;
}

export interface BridgeUiReadyRequest {}

export interface BridgeOperationResponse {
  success: boolean;
  message: string;
  error?: BridgeError | null;
}

export interface FileDialogFilter {
  name: string;
  extensions: string[];
}

export interface OpenFileDialogRequest {
  title?: string;
  filters?: FileDialogFilter[];
}

export interface OpenFileDialogResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ValidationError {
  code: string;
  message: string;
  rowId?: string;
  location?: WorksheetRowLocation | null;
}

export interface ValidationWarning {
  code: string;
  message: string;
  rowId?: string;
  location?: WorksheetRowLocation | null;
}

export interface WorksheetRowLocation {
  worksheetName: string;
  worksheetPosition: number;
  physicalRow: number;
}

export type ValidationStatus = 'valid' | 'warning' | 'error';

export interface PartRow {
  rowId: string;
  importedId: string;
  lengthText?: string | null;
  length: number;
  widthText?: string | null;
  width: number;
  quantityText?: string | null;
  quantity: number;
  materialName: string;
  group?: string | null;
  isManual: boolean;
  sheetNumber?: string | null;
  rowNumber?: number | null;
  columnNumber?: number | null;
  validationStatus: ValidationStatus;
  validationMessages: string[];
  sourceReferences?: SourceReference[];
}

export interface SourceReference extends WorksheetRowLocation {
  sourceFingerprint: string;
}

export interface PartOverride {
  rowId: string;
  importedValues?: PartRow;
  currentValues?: PartRow;
  importedRequiredPiece?: RequiredPiece | null;
  currentRequiredPiece?: RequiredPiece | null;
  sourceReferences: SourceReference[];
}

export interface ExcludedSourceRow {
  rowId: string;
  sourceReference: SourceReference;
  originalValidationError: ValidationError;
  sourceRow?: PartRow;
}

export interface PersistedExcludedSourceRow
  extends Omit<ExcludedSourceRow, 'sourceRow'> {
  sourceRow?: PartRow;
}

export interface ImportRequest {
  filePath: string;
}

export const requiredImportFieldNames = [
  'Id',
  'Length',
  'Width',
  'Quantity',
  'Material',
] as const;

export const optionalImportFieldNames = [
  'Group',
  'Sheet Number',
  'Row Number',
  'Column Number',
] as const;

export const importFieldNames = [
  ...requiredImportFieldNames,
  ...optionalImportFieldNames,
  'Profile Number',
  'Part Name',
  'Finish',
  'Part Number',
] as const;

export type ImportFieldName = (typeof importFieldNames)[number];

export const importMaterialResolutionStatuses = [
  'resolved',
  'unresolved',
  'created',
] as const;

export type ImportMaterialResolutionStatus =
  (typeof importMaterialResolutionStatuses)[number];

export interface ImportColumnMapping {
  sourceColumn: string;
  targetField: ImportFieldName;
}

export interface ImportMaterialMapping {
  sourceMaterialName: string;
  targetMaterialId?: string | null;
}

export interface ImportOptions {
  projectKind?: ProjectKind;
  columnMappings: ImportColumnMapping[];
  materialMappings: ImportMaterialMapping[];
}

export interface ImportFieldMappingStatus {
  targetField: ImportFieldName;
  sourceColumn?: string | null;
  suggestedSourceColumn?: string | null;
}

export interface ImportMaterialResolution {
  sourceMaterialName: string;
  status: ImportMaterialResolutionStatus;
  resolvedMaterialId?: string | null;
  resolvedMaterialName?: string | null;
}

export interface ImportResponse {
  success: boolean;
  parts: PartRow[];
  requiredPieces?: RequiredPiece[];
  errors: ValidationError[];
  warnings: ValidationWarning[];
  availableColumns: string[];
  sourceColumns: ImportSourceColumn[];
  columnMappings: ImportFieldMappingStatus[];
  materialResolutions: ImportMaterialResolution[];
  worksheet?: ImportWorksheetDescriptor | null;
}

export interface ImportSourceColumn {
  address: string;
  heading: string;
}

export interface ImportWorksheetDescriptor {
  worksheetName: string;
  originalPosition: number;
  headingRange: string;
  headingRangeDetectionStatus: HeadingRangeDetectionStatus;
  headingRangeCandidates: HeadingRangeCandidate[];
  previewRows: WorksheetPreviewRow[];
}

export type HeadingRangeDetectionStatus =
  | 'none'
  | 'low-confidence'
  | 'tied'
  | 'unique-high-confidence';

export interface HeadingRangeCandidate {
  address: string;
  confidence: number;
  isHighConfidence: boolean;
  isTied: boolean;
}

export interface WorksheetPreviewRow {
  rowNumber: number;
  cells: WorksheetPreviewCell[];
}

export interface WorksheetPreviewCell {
  address: string;
  columnNumber: number;
  value: string;
  isHidden: boolean;
  isFormula: boolean;
}

export interface WorkbookDiscovery {
  initialWorksheetName: string;
  worksheets: ImportWorksheetDescriptor[];
  macrosPresent: boolean;
  preflight?: WorkbookPreflightAssessment | null;
}

export interface WorkbookPreflightAssessment {
  compressedBytes: number;
  uncompressedBytes: number;
  packageEntryCount: number;
  largestEntryBytes: number;
  compressionRatio: number;
  warnings: string[];
}

export type WorkbookImportPhase =
  | 'preflight'
  | 'openingWorkbook'
  | 'inspectingWorksheets'
  | 'readingWorksheet'
  | 'validating'
  | 'combiningParts'
  | 'finalizing';

export interface WorkbookImportProgress {
  phase: WorkbookImportPhase;
  label: string;
  current?: number | null;
  total?: number | null;
  worksheetName?: string | null;
  preflight?: WorkbookPreflightAssessment | null;
  isDeterminate: boolean;
}

export interface ImportNewMaterialRequest {
  sourceMaterialName: string;
  material: MaterialDraft;
}

export interface ImportFileRequest {
  filePath?: string | null;
  options?: ImportOptions | null;
  newMaterials?: ImportNewMaterialRequest[];
}

export interface ImportFileResponse extends ImportResponse {
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface PartRowUpdate {
  rowId?: string | null;
  importedId: string;
  length: string;
  width: string;
  quantity: string;
  materialName: string;
  group?: string | null;
  isManual?: boolean;
  sheetNumber?: string | null;
  rowNumber?: string | null;
  columnNumber?: string | null;
  sourceReferences?: SourceReference[];
}

export type ImportSessionPhase =
  | 'opening'
  | 'reading'
  | 'validating'
  | 'finalizing'
  | 'finalized'
  | 'cancelled'
  | 'failed';

export interface BeginImportSessionRequest {
  sessionId: string;
  importSourcePath?: string | null;
  projectKind?: ProjectKind;
}

export interface PreviewImportSessionRequest {
  sessionId: string;
  options?: ImportOptions | null;
  newMaterials?: ImportNewMaterialRequest[];
  worksheetName?: string | null;
  headingRange?: string | null;
}

export interface ImportWorksheetSelection {
  worksheetName: string;
  originalPosition: number;
  options?: ImportOptions | null;
  optimizationGroupId: string;
  optimizationGroupName: string;
  headingRange: string;
  excludedSourceRows?: ExcludedSourceRow[];
  ignoredMaterialNames?: string[];
  partOverrides?: PartOverride[];
}

export interface FinalizeImportSessionRequest {
  sessionId: string;
  options?: ImportOptions | null;
  newMaterials?: ImportNewMaterialRequest[];
  project: ProjectRecord;
  replaceExistingImportSource?: boolean;
  targetOptimizationGroupId?: string | null;
  worksheets?: ImportWorksheetSelection[];
}

export interface CancelImportSessionRequest {
  sessionId: string;
}

export interface GetImportSessionProgressRequest {
  sessionId: string;
}

export interface GetImportSessionProgressResponse {
  success: boolean;
  sessionId: string;
  progress?: WorkbookImportProgress | null;
  history: WorkbookImportProgress[];
  error?: BridgeError | null;
  message?: string;
}

export interface ImportSessionResponse extends ImportFileResponse {
  sessionId: string;
  importSourcePath: string | null;
  importSource?: ImportSourceMetadata | null;
  phase: ImportSessionPhase;
  finalized: boolean;
  project?: ProjectRecord | null;
  workbook?: WorkbookDiscovery | null;
  previewSummary?: ImportPreviewSummary | null;
  progress?: WorkbookImportProgress | null;
  progressHistory?: WorkbookImportProgress[];
}

export interface ImportPreviewSummary {
  worksheets: ImportWorksheetPreviewSummary[];
  optimizationGroups: ImportOptimizationGroupPreviewSummary[];
}

export interface ImportWorksheetPreviewSummary {
  worksheetName: string;
  originalPosition: number;
  sourceRowCount: number;
  importedPartCount: number;
  excludedRowCount: number;
  issueCount: number;
}

export interface ImportOptimizationGroupPreviewSummary {
  optimizationGroupId: string;
  name: string;
  sourceRowCount: number;
  combinedPartCount: number;
  mergedRowCount: number;
}

export interface CancelImportSessionResponse {
  success: boolean;
  sessionId: string;
  released: boolean;
  error?: BridgeError | null;
  message?: string;
}

export interface AddPartRowRequest {
  parts: PartRow[];
  part: PartRowUpdate;
}

export interface UpdatePartRowRequest {
  parts: PartRow[];
  part: PartRowUpdate;
}

export interface DeletePartRowRequest {
  parts: PartRow[];
  rowId: string;
}

export interface ImportMappingSession {
  sessionId: string;
  filePath: string;
  preview: ImportFileResponse;
  options: ImportOptions;
  newMaterials: ImportNewMaterialRequest[];
  hasPendingChanges: boolean;
  workbook?: WorkbookDiscovery;
  worksheets?: ImportWorksheetDraft[];
  activeWorksheetName?: string;
}

export interface ImportWorksheetDraft {
  worksheet: ImportWorksheetDescriptor;
  selected: boolean;
  optimizationGroupId: string;
  optimizationGroupName: string;
  preview: ImportFileResponse;
  options: ImportOptions;
  newMaterials: ImportNewMaterialRequest[];
  hasPendingChanges: boolean;
  headingRange: string;
  headingRangeConfirmed: boolean;
  clearedColumnMappingFields?: string[];
  excludedSourceRows: ExcludedSourceRow[];
  ignoredMaterialNames: string[];
  partOverrides: PartOverride[];
}

export interface Material {
  materialId: string;
  name: string;
  colorFinish?: string | null;
  notes?: string | null;
  sheetLength: number;
  sheetWidth: number;
  allowRotation: boolean;
  defaultSpacing: number;
  defaultEdgeMargin: number;
  costPerSheet?: number | null;
}

export interface MaterialDraft {
  materialId?: string;
  name: string;
  colorFinish: string;
  notes: string;
  sheetLength: number;
  sheetWidth: number;
  allowRotation: boolean;
  defaultSpacing: number;
  defaultEdgeMargin: number;
  costPerSheet: number | null;
}

export interface MaterialLibraryLocation {
  currentPath: string;
  defaultPath?: string | null;
  usingDefaultLocation: boolean;
}

export interface ProjectMetadata {
  projectName: string;
  projectNumber: string;
  customerName: string;
  estimator: string;
  drafter: string;
  projectManager: string;
  date: string;
  requiredDate: string;
  revision: string;
  notes: string;
}

export interface ProjectFileMetadata {
  projectName: string;
  projectNumber?: string | null;
  customerName?: string | null;
  estimator?: string | null;
  drafter?: string | null;
  pm?: string | null;
  date?: string | null;
  requiredDate?: string | null;
  revision?: string | null;
  notes?: string | null;
}

export interface ReportSettings {
  companyName?: string | null;
  reportTitle?: string | null;
  projectJobName?: string | null;
  projectJobNumber?: string | null;
  releaseId?: string | null;
  status?: string | null;
  reportDate?: string | null;
  notes?: string | null;
}

export interface StiffenerTakeoffSettings {
  enabled: boolean;
  minimumLengthInches: number;
  minimumWidthInches: number;
  widthDeductionInches: number;
  stockLengthFeet: number;
  reportTitle: string;
  extrusion: string;
  releaseId: string;
  poNumber: string;
  color: string;
  colorNumber: string;
  manufacturer: string;
  status: string;
}

export interface DesktopAppSettings {
  companyLogoPath?: string | null;
  companyName?: string | null;
}

export interface ProjectSettings {
  kerfWidth: number;
  inchDisplayFormat: InchDisplayFormat;
  reportSettings: ReportSettings;
  stiffenerTakeoff: StiffenerTakeoffSettings;
}

export type InchDisplayFormat =
  | 'decimal'
  | 'fractional16'
  | 'fractional32'
  | 'fractional64';

export interface ProjectMaterialSnapshot extends Material {}

export type OptimizationResultStatus = 'none' | 'valid' | 'stale';
export type OptimizationGroupOrigin = 'project' | 'importSource';

export interface OptimizationGroup {
  optimizationGroupId: string;
  name: string;
  order: number;
  origin?: OptimizationGroupOrigin;
  parts: PartRow[];
  stockLength?: number | null;
  requiredPieces: RequiredPiece[];
  stockGroups: StockGroup[];
  lastStockLengthOptimizationResult?: StockLengthOptimizationResult | null;
  lastStockLengthGenerationError?: ValidationError | null;
  lastNestingResult?: NestResponse | null;
  lastBatchNestingResult?: BatchNestResponse | null;
  resultStatus: OptimizationResultStatus;
}

export type OptimizationGroupChangeType =
  | 'create'
  | 'rename'
  | 'reorder'
  | 'movePart'
  | 'updateStockLength'
  | 'delete';

export interface OptimizationGroupChange {
  type: OptimizationGroupChangeType;
  optimizationGroupId?: string | null;
  name?: string | null;
  stockLength?: string | null;
  orderedOptimizationGroupIds?: string[];
  partRowId?: string | null;
  targetOptimizationGroupId?: string | null;
  removeOwnedContent?: boolean;
}

export interface RequiredPiece {
  requiredPieceId: string;
  quantity: number;
  quantityText?: string | null;
  length: number;
  lengthText?: string | null;
  profileNumber: string;
  partName?: string | null;
  finish?: string | null;
  partNumber?: string | null;
  isManual: boolean;
  validationStatus?: ValidationStatus;
  validationMessages?: string[];
  sourceReferences: SourceReference[];
}

export interface StockGroup {
  profileNumber: string;
  finish?: string | null;
  requiredPieceIds: string[];
}

export type CutPlanStatus = 'complete' | 'partial' | 'failed';

export interface StockLengthOptimizationResult {
  optimizationGroupId: string;
  status: CutPlanStatus;
  description: string;
  cutPlans: CutPlan[];
}

export interface CutPlan {
  cutPlanId: string;
  stockGroup: StockGroup;
  status: CutPlanStatus;
  stockItems: StockItem[];
  unplacedPieceInstances: UnplacedPieceInstance[];
}

export interface StockItem {
  stockItemId: string;
  stockItemNumber: number;
  stockLength: number;
  pieceLength: number;
  sawLoss: number;
  remainder: number;
  utilizationPercent: number;
  cutSequence: PieceInstance[];
}

export interface PieceInstance {
  pieceInstanceId: string;
  requiredPieceId: string;
  instanceNumber: number;
  length: number;
  profileNumber: string;
  finish?: string | null;
  partNumber?: string | null;
  partName?: string | null;
  sourceReferences: SourceReference[];
}

export interface UnplacedPieceInstance {
  pieceInstance: PieceInstance;
  reasonCode: string;
  reasonDescription: string;
}

export type RequiredPieceChangeType = 'create' | 'update' | 'delete';

export interface RequiredPieceChange {
  type: RequiredPieceChangeType;
  optimizationGroupId?: string | null;
  requiredPieceId?: string | null;
  quantity?: string;
  length?: string;
  profileNumber?: string;
  partName?: string | null;
  finish?: string | null;
  partNumber?: string | null;
}

export interface ProjectStateRecord {
  sourceFilePath?: string | null;
  importSource?: ImportSourceMetadata | null;
  importConfiguration?: ImportConfiguration | null;
  optimizationGroups: OptimizationGroup[];
  parts: PartRow[];
  selectedMaterialId?: string | null;
  lastNestingResult?: NestResponse | null;
  lastBatchNestingResult?: BatchNestResponse | null;
  extrusionLayout: ExtrusionLayoutState;
}

export interface ImportConfiguration {
  options: ImportOptions;
  worksheets: ImportWorksheetConfiguration[];
  partOverrides: PartOverride[];
}

export interface ImportWorksheetConfiguration {
  worksheetName: string;
  originalPosition: number;
  headingRange: string;
  columnMappings: ImportColumnMapping[];
  optimizationGroupId?: string | null;
  excludedSourceRows: PersistedExcludedSourceRow[];
}

export interface ImportSourceMetadata {
  importSourcePath: string;
  contentFingerprint: string;
  contentLength: number;
  snapshotCapturedAtUtc: string;
}

export interface ProjectRecord {
  version: number;
  projectKind: ProjectKind;
  projectId: string;
  metadata: ProjectFileMetadata;
  settings: ProjectSettings;
  materialSnapshots: ProjectMaterialSnapshot[];
  state: ProjectStateRecord;
}

export interface ListMaterialsRequest {}

export interface MaterialLibraryOperationResponse {
  success: boolean;
  materials: Material[];
  libraryLocation?: MaterialLibraryLocation | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ListMaterialsResponse extends MaterialLibraryOperationResponse {}

export interface ChooseMaterialLibraryLocationRequest {}

export interface ChooseMaterialLibraryLocationResponse
  extends MaterialLibraryOperationResponse {}

export interface RestoreDefaultMaterialLibraryLocationRequest {}

export interface RestoreDefaultMaterialLibraryLocationResponse
  extends MaterialLibraryOperationResponse {}

export interface GetMaterialRequest {
  materialId: string;
}

export interface MaterialRecordResponse {
  success: boolean;
  material: Material | null;
  error?: BridgeError | null;
  message?: string;
}

export interface CreateMaterialRequest {
  material: MaterialDraft;
}

export interface UpdateMaterialRequest {
  material: Material;
}

export interface DeleteMaterialRequest {
  materialId: string;
  selectedMaterialId?: string | null;
  importedMaterialNames?: string[];
}

export interface DeleteMaterialResponse {
  success: boolean;
  materialId: string;
  error?: BridgeError | null;
  message?: string;
}

export interface NewProjectRequest {
  metadata?: ProjectFileMetadata | null;
  settings?: ProjectSettings | null;
  projectKind?: ProjectKind;
}

export interface ChangeProjectKindRequest {
  project: ProjectRecord;
  projectKind: ProjectKind;
}

export interface ChangeProjectKindResponse {
  success: boolean;
  project: ProjectRecord | null;
  error?: BridgeError | null;
  message?: string;
}

export interface OpenProjectRequest {
  filePath?: string;
}

export interface SaveProjectRequest {
  filePath?: string | null;
  project: ProjectRecord;
}

export interface SaveProjectAsRequest {
  filePath?: string | null;
  suggestedFileName?: string | null;
  project: ProjectRecord;
}

export interface GetProjectMetadataRequest {
  project: ProjectRecord;
}

export interface GetDesktopAppSettingsRequest {}

export interface GetDesktopAppSettingsResponse {
  success: boolean;
  settings: DesktopAppSettings | null;
  error?: BridgeError | null;
  message?: string;
}

export interface UpdateDesktopAppSettingsRequest {
  settings: DesktopAppSettings;
}

export interface UpdateDesktopAppSettingsResponse {
  success: boolean;
  settings: DesktopAppSettings | null;
  error?: BridgeError | null;
  message?: string;
}

export interface GetStiffenerTakeoffRequest {
  project: ProjectRecord;
}

export interface ExtrusionLayoutState {
  groupingMode?: 'group' | 'sheet-number' | '';
  panelToPanelExtrusionName: string;
  edgeExtrusionName: string;
  panelToPanelStickLengthFeet?: number;
  edgeStickLengthFeet?: number;
  additionalLineItems: ExtrusionAdditionalLineItem[];
  groups: ExtrusionGroupLayout[];
}

export type ExtrusionLineItemQuantityBasis = 'panel-to-panel' | 'edge' | 'both';

export interface ExtrusionAdditionalLineItem {
  id: string;
  name: string;
  quantityBasis: ExtrusionLineItemQuantityBasis;
  stickLengthFeet?: number;
}

export interface ExtrusionGroupLayout {
  optimizationGroupId?: string;
  optimizationGroupName?: string;
  groupName: string;
  rows: number;
  columns: number;
  cells: ExtrusionGridCell[];
  edgeAssignments: ExtrusionEdgeAssignment[];
  jointAssignments: ExtrusionJointAssignment[];
}

export interface ExtrusionPanelInstance {
  optimizationGroupId: string;
  optimizationGroupName: string;
  optimizationGroupOrder: number;
  instanceId: string;
  sourceRowId: string;
  importedId: string;
  quantityIndex: number;
  label: string;
  materialName: string;
  groupName: string;
  sheetGroupName: string;
  sheetNumber?: string | null;
  rowNumber?: number | null;
  columnNumber?: number | null;
  length: number;
  width: number;
  isStale: boolean;
}

export interface ExtrusionGridCell {
  instanceId: string;
  row: number;
  column: number;
}

export interface ExtrusionEdgeAssignment {
  instanceId: string;
  edge: 'top' | 'right' | 'bottom' | 'left';
  extrusionName: string;
  isIgnored?: boolean;
}

export interface ExtrusionJointAssignment {
  jointId: string;
  firstInstanceId: string;
  secondInstanceId: string;
  edge: string;
  extrusionName: string;
  isEnabled?: boolean;
}

export interface ExtrusionLengthSummary {
  category: string;
  extrusionName: string;
  totalLengthInches: number;
  segmentCount: number;
  totalLinearFeet: number;
  stickLengthFeet: number;
  requiredStickCount: number;
}

export interface ExtrusionGroupSummary {
  optimizationGroupId: string;
  optimizationGroupName: string;
  groupName: string;
  lengths: ExtrusionLengthSummary[];
}

export interface ExtrusionSegmentDetail {
  optimizationGroupId: string;
  optimizationGroupName: string;
  groupName: string;
  category: string;
  extrusionName: string;
  location: string;
  lengthInches: number;
}

export interface ExtrusionReportData {
  companyLogoPath?: string | null;
  projectMetadata: ProjectFileMetadata;
  reportSettings: ReportSettings;
  layout: ExtrusionLayoutState;
  panels: ExtrusionPanelInstance[];
  overallLengths: ExtrusionLengthSummary[];
  groups: ExtrusionGroupSummary[];
  optimizationGroups: Array<{
    optimizationGroupId: string;
    name: string;
    order: number;
    overallLengths: ExtrusionLengthSummary[];
    partGroups: ExtrusionGroupSummary[];
  }>;
  segments: ExtrusionSegmentDetail[];
  hasTakeoff: boolean;
}

export interface GetExtrusionLayoutRequest {
  project: ProjectRecord;
}

export interface GetExtrusionLayoutResponse {
  success: boolean;
  layout: ExtrusionLayoutState | null;
  error?: BridgeError | null;
  message?: string;
}

export interface UpdateExtrusionLayoutRequest {
  project: ProjectRecord;
  layout: ExtrusionLayoutState;
}

export interface UpdateExtrusionLayoutResponse {
  success: boolean;
  project: ProjectRecord | null;
  layout: ExtrusionLayoutState | null;
  error?: BridgeError | null;
  message?: string;
}

export interface GetExtrusionReportRequest {
  project: ProjectRecord;
}

export interface GetExtrusionReportResponse {
  success: boolean;
  report: ExtrusionReportData | null;
  error?: BridgeError | null;
  message?: string;
}

export interface UpdateProjectMetadataRequest {
  project: ProjectRecord;
  metadata: ProjectFileMetadata;
  settings?: ProjectSettings | null;
}

export interface ProjectOperationResponse {
  success: boolean;
  filePath: string | null;
  project: ProjectRecord | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ProjectMetadataResponse {
  success: boolean;
  metadata: ProjectFileMetadata | null;
  settings: ProjectSettings | null;
  error?: BridgeError | null;
  message?: string;
}

export interface NestRequest {
  parts: PartRow[];
  material: Material;
  kerfWidth: number;
}

export interface BatchNestRequest {
  optimizationGroups?: OptimizationGroupNestRequest[];
  parts: PartRow[];
  materials: Material[];
  kerfWidth: number;
  selectedMaterialId?: string | null;
}

export interface OptimizationGroupNestRequest {
  optimizationGroupId: string;
  name: string;
  order: number;
  ownedPartRowIds: string[];
  parts: PartRow[];
}

export interface NestSheet {
  sheetId: string;
  sheetNumber: number;
  materialName: string;
  sheetLength: number;
  sheetWidth: number;
  utilizationPercent: number;
}

export interface NestPlacement {
  placementId: string;
  sheetId: string;
  partId: string;
  group?: string | null;
  x: number;
  y: number;
  width: number;
  height: number;
  rotated90: boolean;
}

export const unplacedReasonCodes = [
  'outside-usable-sheet',
  'no-layout-space',
  'invalid-input',
  'empty-run',
] as const;

export type UnplacedReasonCode = (typeof unplacedReasonCodes)[number];

export interface UnplacedItem {
  partId: string;
  reasonCode: UnplacedReasonCode;
  reasonDescription: string;
}

export interface MaterialSummary {
  totalSheets: number;
  totalPlaced: number;
  totalUnplaced: number;
  overallUtilization: number;
}

export interface NestResponse {
  success: boolean;
  sheets: NestSheet[];
  placements: NestPlacement[];
  unplacedItems: UnplacedItem[];
  summary: MaterialSummary;
}

export interface MaterialNestResult {
  materialName: string;
  materialId?: string | null;
  result: NestResponse;
}

export interface BatchNestResponse {
  executionId?: string;
  success: boolean;
  partialSuccess?: boolean;
  legacyResult?: NestResponse | null;
  materialResults: MaterialNestResult[];
  optimizationGroupResults?: OptimizationGroupNestResult[];
}

export interface OptimizationGroupNestResult {
  optimizationResultId: string;
  optimizationGroupId: string;
  name: string;
  order: number;
  success: boolean;
  failureMessage?: string | null;
  inputPartRowIds: string[];
  ownedPartRowIds: string[];
  legacyResult?: NestResponse | null;
  materialResults: MaterialNestResult[];
}

export interface ReportSheetDiagram {
  sheetId: string;
  sheetNumber: number;
  sheetLength: number;
  sheetWidth: number;
  utilizationPercent: number;
  placements: NestPlacement[];
}

export interface ReportMaterialSection {
  materialName: string;
  materialId?: string | null;
  sheetLength: number;
  sheetWidth: number;
  costPerSheet?: number | null;
  summary: MaterialSummary;
  sheets: ReportSheetDiagram[];
  unplacedItems: UnplacedItem[];
}

export interface ReportData {
  companyLogoPath?: string | null;
  settings: ReportSettings;
  projectMetadata: ProjectFileMetadata;
  materials: ReportMaterialSection[];
  unplacedItems: UnplacedItem[];
  hasResults: boolean;
}

export interface UpdateOptimizationGroupsRequest {
  project: ProjectRecord;
  change: OptimizationGroupChange;
}

export interface UpdateOptimizationGroupsResponse {
  success: boolean;
  project: ProjectRecord | null;
  error?: BridgeError | null;
  message?: string;
}

export interface UpdateRequiredPiecesRequest {
  project: ProjectRecord;
  change: RequiredPieceChange;
}

export interface UpdateRequiredPiecesResponse {
  success: boolean;
  project?: ProjectRecord | null;
  error?: BridgeError | null;
  message?: string;
}

export interface GenerateSelectedCutPlanRequest {
  project: ProjectRecord;
  optimizationGroupId: string;
  operationId?: string;
}

export interface GenerateSelectedCutPlanResponse {
  success: boolean;
  project?: ProjectRecord | null;
  result?: StockLengthOptimizationResult | null;
  error?: BridgeError | null;
  message?: string;
}

export interface StockLengthGenerationFailure {
  optimizationGroupId: string;
  code: string;
  message: string;
}

export interface StockLengthGenerationProgress {
  phase: 'optimizationGroups' | 'stockGroups' | 'pieceInstances';
  completedOptimizationGroups: number;
  totalOptimizationGroups: number;
  optimizationGroupId?: string | null;
  completedStockGroups: number;
  totalStockGroups: number;
  completedPieceInstanceSteps: number;
  totalPieceInstanceSteps: number;
  label: string;
}

export interface GenerateAllStaleCutPlansRequest {
  project: ProjectRecord;
  operationId?: string;
}

export interface GenerateAllStaleCutPlansResponse {
  success: boolean;
  project: ProjectRecord;
  failures: StockLengthGenerationFailure[];
  message: string;
}

export interface CancelCutPlanGenerationRequest {
  operationId: string;
}

export interface CancelCutPlanGenerationResponse {
  success: boolean;
  operationId: string;
  cancellationRequested: boolean;
  error?: BridgeError | null;
  message: string;
}

export interface GetCutPlanGenerationProgressRequest {
  operationId: string;
}

export interface GetCutPlanGenerationProgressResponse {
  success: boolean;
  operationId: string;
  progress?: StockLengthGenerationProgress | null;
  error?: BridgeError | null;
  message?: string | null;
}

export interface StiffenerTakeoffLengthSummary {
  label: string;
  lengthInches: number;
  pieceCount: number;
}

export interface StiffenerTakeoffSectionSummary {
  eligiblePanelCount: number;
  totalStiffenerCount: number;
  totalLinearFeet: number;
  stockLengthFeet: number;
  requiredStockCount: number;
}

export interface StiffenerTakeoffMaterialSection {
  materialName: string;
  summary: StiffenerTakeoffSectionSummary;
  lengths: StiffenerTakeoffLengthSummary[];
}

export interface StiffenerTakeoffOptimizationGroupSection {
  optimizationGroupId: string;
  name: string;
  order: number;
  summary: StiffenerTakeoffSectionSummary;
  lengths: StiffenerTakeoffLengthSummary[];
}

export interface StiffenerTakeoffReportData {
  companyLogoPath?: string | null;
  projectMetadata: ProjectFileMetadata;
  reportSettings: ReportSettings;
  settings: StiffenerTakeoffSettings;
  overallSummary: StiffenerTakeoffSectionSummary;
  overallLengths: StiffenerTakeoffLengthSummary[];
  materials: StiffenerTakeoffMaterialSection[];
  optimizationGroups: StiffenerTakeoffOptimizationGroupSection[];
  hasTakeoff: boolean;
}

export interface UpdateReportSettingsRequest {
  project: ProjectRecord;
  reportSettings: ReportSettings;
}

export interface UpdateReportSettingsResponse {
  success: boolean;
  project: ProjectRecord | null;
  reportSettings: ReportSettings | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ExportPdfReportRequest {
  project: ProjectRecord;
  batchResult?: BatchNestResponse | null;
  filePath?: string | null;
  suggestedFileName?: string | null;
  companyLogoPath?: string | null;
  stockLengthScope?: StockLengthReportScope | null;
}

export interface ExportPdfReportResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ExportExcelReportRequest {
  project: ProjectRecord;
  batchResult?: BatchNestResponse | null;
  filePath?: string | null;
  suggestedFileName?: string | null;
  stockLengthScope?: StockLengthReportScope | null;
}

export interface StockLengthReportScope {
  optimizationGroupId?: string | null;
  hasStockGroupFilter: boolean;
  stockGroupProfileNumber?: string | null;
  stockGroupFinish?: string | null;
}

export interface ExportExcelReportResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface GetStiffenerTakeoffResponse {
  success: boolean;
  report: StiffenerTakeoffReportData | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ExportStiffenerPdfReportRequest {
  project: ProjectRecord;
  filePath?: string | null;
  suggestedFileName?: string | null;
  companyLogoPath?: string | null;
}

export interface ExportStiffenerPdfReportResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ExportExtrusionPdfReportRequest {
  project: ProjectRecord;
  filePath?: string | null;
  suggestedFileName?: string | null;
  companyLogoPath?: string | null;
}

export interface ExportExtrusionPdfReportResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface ExportExtrusionExcelReportRequest {
  project: ProjectRecord;
  filePath?: string | null;
  suggestedFileName?: string | null;
}

export interface ExportExtrusionExcelReportResponse {
  success: boolean;
  filePath: string | null;
  error?: BridgeError | null;
  message?: string;
}

export interface HostBridgeSnapshot {
  connected: boolean;
  handshake: BridgeHandshakeResponse;
  lastError?: string;
  lastMessageAt?: string;
}

export const requestedBridgeCapabilities: BridgeCapability[] = [
  bridgeMessageTypes.handshake,
  bridgeMessageTypes.bridgeUiReady,
  bridgeMessageTypes.openFileDialog,
  bridgeMessageTypes.importCsv,
  bridgeMessageTypes.importFile,
  bridgeMessageTypes.beginImportSession,
  bridgeMessageTypes.previewImportSession,
  bridgeMessageTypes.finalizeImportSession,
  bridgeMessageTypes.cancelImportSession,
  bridgeMessageTypes.getImportSessionProgress,
  bridgeMessageTypes.updatePartRow,
  bridgeMessageTypes.deletePartRow,
  bridgeMessageTypes.addPartRow,
  bridgeMessageTypes.runNesting,
  bridgeMessageTypes.runBatchNesting,
  bridgeMessageTypes.getStiffenerTakeoff,
  bridgeMessageTypes.getExtrusionLayout,
  bridgeMessageTypes.updateExtrusionLayout,
  bridgeMessageTypes.getExtrusionReport,
  bridgeMessageTypes.exportPdfReport,
  bridgeMessageTypes.exportExcelReport,
  bridgeMessageTypes.exportStiffenerPdfReport,
  bridgeMessageTypes.exportExtrusionPdfReport,
  bridgeMessageTypes.exportExtrusionExcelReport,
  bridgeMessageTypes.updateReportSettings,
  bridgeMessageTypes.listMaterials,
  bridgeMessageTypes.chooseMaterialLibraryLocation,
  bridgeMessageTypes.restoreDefaultMaterialLibraryLocation,
  bridgeMessageTypes.getMaterial,
  bridgeMessageTypes.createMaterial,
  bridgeMessageTypes.updateMaterial,
  bridgeMessageTypes.deleteMaterial,
  bridgeMessageTypes.newProject,
  bridgeMessageTypes.openProject,
  bridgeMessageTypes.saveProject,
  bridgeMessageTypes.saveProjectAs,
  bridgeMessageTypes.getProjectMetadata,
  bridgeMessageTypes.updateProjectMetadata,
  bridgeMessageTypes.updateOptimizationGroups,
  bridgeMessageTypes.updateRequiredPieces,
  bridgeMessageTypes.generateSelectedCutPlan,
  bridgeMessageTypes.generateAllStaleCutPlans,
  bridgeMessageTypes.cancelCutPlanGeneration,
  bridgeMessageTypes.getCutPlanGenerationProgress,
  bridgeMessageTypes.getDesktopAppSettings,
  bridgeMessageTypes.updateDesktopAppSettings,
];

export const demoMaterial: Material = {
  materialId: 'demo-material',
  name: 'Demo Material',
  colorFinish: 'Phase 2 seed',
  notes: 'Seeded into the local material library on first run.',
  sheetLength: 96,
  sheetWidth: 48,
  allowRotation: true,
  defaultSpacing: 0.125,
  defaultEdgeMargin: 0.5,
  costPerSheet: null,
};

export const demoKerfWidth = 0.0625;

export const emptyReportSettings: ReportSettings = {
  companyName: '',
  reportTitle: '',
  projectJobName: '',
  projectJobNumber: '',
  releaseId: '',
  status: '',
  reportDate: '',
  notes: '',
};

export const defaultStiffenerTakeoffSettings: StiffenerTakeoffSettings = {
  enabled: false,
  minimumLengthInches: 32,
  minimumWidthInches: 32,
  widthDeductionInches: 4,
  stockLengthFeet: 20,
  reportTitle: '',
  extrusion: '',
  releaseId: '',
  poNumber: '',
  color: '',
  colorNumber: '',
  manufacturer: '',
  status: '',
};

export const defaultExtrusionLayoutState: ExtrusionLayoutState = {
  groupingMode: '',
  panelToPanelExtrusionName: 'Panel Joint',
  edgeExtrusionName: 'Perimeter Edge',
  panelToPanelStickLengthFeet: 20,
  edgeStickLengthFeet: 20,
  additionalLineItems: [],
  groups: [],
};

export const emptyImportResponse: ImportResponse = {
  success: false,
  parts: [],
  requiredPieces: [],
  errors: [],
  warnings: [],
    availableColumns: [],
    sourceColumns: [],
  columnMappings: [],
  materialResolutions: [],
};

export const emptyNestResponse: NestResponse = {
  success: false,
  sheets: [],
  placements: [],
  unplacedItems: [],
  summary: {
    totalSheets: 0,
    totalPlaced: 0,
    totalUnplaced: 0,
    overallUtilization: 0,
  },
};

export const emptyBatchNestResponse: BatchNestResponse = {
  success: false,
  legacyResult: null,
  materialResults: [],
};
