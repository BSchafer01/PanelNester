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
  updateOptimizationGroups: 'update-optimization-groups',
  getDesktopAppSettings: 'get-desktop-app-settings',
  updateDesktopAppSettings: 'update-desktop-app-settings',
} as const;

export const toBridgeResponseType = (type: string) => `${type}-response`;

export type BridgeCapability =
  (typeof bridgeMessageTypes)[keyof typeof bridgeMessageTypes];

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
}

export interface ValidationWarning {
  code: string;
  message: string;
  rowId?: string;
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

export interface SourceReference {
  worksheetName: string;
  worksheetPosition: number;
  physicalRow: number;
  sourceFingerprint: string;
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
}

export interface WorksheetPreviewRow {
  rowNumber: number;
  cells: WorksheetPreviewCell[];
}

export interface WorksheetPreviewCell {
  address: string;
  columnNumber: number;
  value: string;
}

export interface WorkbookDiscovery {
  initialWorksheetName: string;
  worksheets: ImportWorksheetDescriptor[];
  macrosPresent: boolean;
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
}

export interface PreviewImportSessionRequest {
  sessionId: string;
  options?: ImportOptions | null;
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
}

export interface FinalizeImportSessionRequest {
  sessionId: string;
  options?: ImportOptions | null;
  newMaterials?: ImportNewMaterialRequest[];
  project: ProjectRecord;
  targetOptimizationGroupId?: string | null;
  worksheets?: ImportWorksheetSelection[];
}

export interface CancelImportSessionRequest {
  sessionId: string;
}

export interface ImportSessionResponse extends ImportFileResponse {
  sessionId: string;
  importSourcePath: string | null;
  importSource?: ImportSourceMetadata | null;
  phase: ImportSessionPhase;
  finalized: boolean;
  project?: ProjectRecord | null;
  workbook?: WorkbookDiscovery | null;
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
  reportSettings: ReportSettings;
  stiffenerTakeoff: StiffenerTakeoffSettings;
}

export interface ProjectMaterialSnapshot extends Material {}

export type OptimizationResultStatus = 'none' | 'valid' | 'stale';

export interface OptimizationGroup {
  optimizationGroupId: string;
  name: string;
  order: number;
  parts: PartRow[];
  lastNestingResult?: NestResponse | null;
  lastBatchNestingResult?: BatchNestResponse | null;
  resultStatus: OptimizationResultStatus;
}

export type OptimizationGroupChangeType =
  | 'create'
  | 'rename'
  | 'reorder'
  | 'movePart'
  | 'delete';

export interface OptimizationGroupChange {
  type: OptimizationGroupChangeType;
  optimizationGroupId?: string | null;
  name?: string | null;
  orderedOptimizationGroupIds?: string[];
  partRowId?: string | null;
  targetOptimizationGroupId?: string | null;
  removeOwnedContent?: boolean;
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
}

export interface ImportWorksheetConfiguration {
  worksheetName: string;
  originalPosition: number;
  headingRange: string;
  columnMappings: ImportColumnMapping[];
  optimizationGroupId?: string | null;
  excludedSourceRows: number[];
}

export interface ImportSourceMetadata {
  importSourcePath: string;
  contentFingerprint: string;
  contentLength: number;
  snapshotCapturedAtUtc: string;
}

export interface ProjectRecord {
  version: number;
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
