export const bridgeMessageTypes = {
  handshake: 'bridge-handshake',
  openFileDialog: 'open-file-dialog',
  importCsv: 'import-csv',
  importFile: 'import-file',
  updatePartRow: 'update-part-row',
  deletePartRow: 'delete-part-row',
  addPartRow: 'add-part-row',
  runNesting: 'run-nesting',
  runBatchNesting: 'run-batch-nesting',
  exportPdfReport: 'export-pdf-report',
  updateReportSettings: 'update-report-settings',
  listMaterials: 'list-materials',
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
  surface: 'PanelNester.WebUI';
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
  validationStatus: ValidationStatus;
  validationMessages: string[];
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

export const optionalImportFieldNames = ['Group'] as const;

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
  columnMappings: ImportFieldMappingStatus[];
  materialResolutions: ImportMaterialResolution[];
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
  filePath: string;
  preview: ImportFileResponse;
  options: ImportOptions;
  newMaterials: ImportNewMaterialRequest[];
  hasPendingChanges: boolean;
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

export interface ProjectMetadata {
  projectName: string;
  projectNumber: string;
  customerName: string;
  estimator: string;
  drafter: string;
  projectManager: string;
  date: string;
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
  revision?: string | null;
  notes?: string | null;
}

export interface ReportSettings {
  companyName?: string | null;
  reportTitle?: string | null;
  projectJobName?: string | null;
  projectJobNumber?: string | null;
  reportDate?: string | null;
  notes?: string | null;
}

export interface ProjectSettings {
  kerfWidth: number;
  reportSettings: ReportSettings;
}

export interface ProjectMaterialSnapshot extends Material {}

export interface ProjectStateRecord {
  sourceFilePath?: string | null;
  parts: PartRow[];
  selectedMaterialId?: string | null;
  lastNestingResult?: NestResponse | null;
  lastBatchNestingResult?: BatchNestResponse | null;
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

export interface ListMaterialsResponse {
  success: boolean;
  materials: Material[];
  error?: BridgeError | null;
  message?: string;
}

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

export interface OpenProjectRequest {}

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
  parts: PartRow[];
  materials: Material[];
  kerfWidth: number;
  selectedMaterialId?: string | null;
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
  success: boolean;
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
  settings: ReportSettings;
  projectMetadata: ProjectFileMetadata;
  materials: ReportMaterialSection[];
  unplacedItems: UnplacedItem[];
  hasResults: boolean;
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
}

export interface ExportPdfReportResponse {
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
  bridgeMessageTypes.openFileDialog,
  bridgeMessageTypes.importCsv,
  bridgeMessageTypes.importFile,
  bridgeMessageTypes.updatePartRow,
  bridgeMessageTypes.deletePartRow,
  bridgeMessageTypes.addPartRow,
  bridgeMessageTypes.runNesting,
  bridgeMessageTypes.runBatchNesting,
  bridgeMessageTypes.exportPdfReport,
  bridgeMessageTypes.updateReportSettings,
  bridgeMessageTypes.listMaterials,
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
  reportDate: '',
  notes: '',
};

export const emptyImportResponse: ImportResponse = {
  success: false,
  parts: [],
  errors: [],
  warnings: [],
  availableColumns: [],
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
