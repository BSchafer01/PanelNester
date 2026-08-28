import type {
  ImportConfiguration,
  ImportMappingSession,
  ImportSourceMetadata,
  ImportResultCounts,
  InchDisplayFormat,
  OptimizationGroup,
  RequiredPieceChange,
  StockLengthGenerationProgress,
} from '../types/contracts';
import { RequiredPiecesWorkspace, formatInches } from './RequiredPiecesWorkspace';
import { StockLengthImportWorkflow } from './StockLengthImportWorkflow';

export { formatInches };

interface RequiredPiecesPageProps {
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  inchDisplayFormat: InchDisplayFormat;
  busy: boolean;
  generationBusy?: boolean;
  generationProgress?: StockLengthGenerationProgress;
  projectDirty?: boolean;
  message?: string;
  importSource?: ImportSourceMetadata;
  importConfiguration?: ImportConfiguration;
  lastImportReceipt?: ImportResultCounts;
  onCreateOptimizationGroup: (name: string, stockLength: string) => void | Promise<void>;
  onUpdateStockLength: (optimizationGroupId: string, stockLength: string) => void | Promise<void>;
  onCreateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onUpdateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onDeleteRequiredPiece: (optimizationGroupId: string, requiredPieceId: string) => void | Promise<void>;
  onGenerateSelected?: (optimizationGroupId: string) => void | Promise<void>;
  onGenerateSelectedGroups?: (optimizationGroupIds: string[]) => void | Promise<void>;
  onGenerateAllStale?: () => void | Promise<void>;
  onCancelGeneration?: () => void | Promise<void>;
  onInchDisplayFormatChange: (format: InchDisplayFormat) => void;
  mappingSession?: ImportMappingSession;
  onImportFile?: (filePath?: string) => void | Promise<void>;
  onImportDroppedFile?: (file: File) => void | Promise<void>;
  onReimportFile?: () => void | Promise<void>;
  onUndoImport?: () => void | Promise<void>;
  onUpdateImportMappingSession?: (session: ImportMappingSession) => void;
  onPreviewImportMapping?: (
    session?: ImportMappingSession,
    worksheetNames?: string[],
  ) => void | Promise<void>;
  onFinalizeImportMapping?: () => void | Promise<void>;
  onCancelImportMapping?: () => void | Promise<void>;
}

export function RequiredPiecesPage({
  optimizationGroups,
  activeOptimizationGroupId,
  inchDisplayFormat,
  busy,
  generationBusy = false,
  generationProgress,
  projectDirty = false,
  message,
  importSource,
  importConfiguration,
  lastImportReceipt,
  onCreateOptimizationGroup,
  onUpdateStockLength,
  onCreateRequiredPiece,
  onUpdateRequiredPiece,
  onDeleteRequiredPiece,
  onGenerateSelected,
  onGenerateSelectedGroups,
  onGenerateAllStale,
  onCancelGeneration,
  onInchDisplayFormatChange,
  mappingSession,
  onImportFile,
  onImportDroppedFile,
  onReimportFile,
  onUndoImport,
  onUpdateImportMappingSession,
  onPreviewImportMapping,
  onFinalizeImportMapping,
  onCancelImportMapping,
}: RequiredPiecesPageProps) {
  if (
    mappingSession &&
    onImportFile &&
    onUpdateImportMappingSession &&
    onPreviewImportMapping &&
    onFinalizeImportMapping &&
    onCancelImportMapping
  ) {
    return <StockLengthImportWorkflow
      busy={busy}
      groups={optimizationGroups}
      message={message}
      onCancel={onCancelImportMapping}
      onFinalize={onFinalizeImportMapping}
      onPreview={onPreviewImportMapping}
      onReplaceFile={onImportFile}
      onUpdateSession={onUpdateImportMappingSession}
      session={mappingSession}
    />;
  }

  const changeRequiredPiece = (change: RequiredPieceChange) =>
    change.type === 'update' ? onUpdateRequiredPiece(change) : onCreateRequiredPiece(change);
  const generateSelected = onGenerateSelectedGroups ?? (onGenerateSelected
    ? async (groupIds: string[]) => {
        for (const groupId of groupIds) await onGenerateSelected(groupId);
      }
    : undefined);

  return <>
    {generationBusy && generationProgress ? <div className="generation-progress stock-length-workspace__generation" role="status"><span>{generationProgress.label}</span><progress aria-label="Cut Plan generation progress" max={generationProgress.totalOptimizationGroups || 1} value={generationProgress.completedOptimizationGroups} />{onCancelGeneration ? <button className="secondary-button" onClick={() => void onCancelGeneration()} type="button">Cancel Generation</button> : null}</div> : null}
    <RequiredPiecesWorkspace
      activeGroupId={activeOptimizationGroupId}
      busy={busy}
      groups={optimizationGroups}
      importConfiguration={importConfiguration}
      importSource={importSource}
      lastImportReceipt={lastImportReceipt}
      inchDisplayFormat={inchDisplayFormat}
      message={message}
      onCreateGroup={onCreateOptimizationGroup}
      onDeletePiece={onDeleteRequiredPiece}
      onGenerateAll={onGenerateAllStale}
      onGenerateSelected={generateSelected}
      onImportFile={onImportFile}
      onImportDroppedFile={onImportDroppedFile}
      onInchDisplayFormatChange={onInchDisplayFormatChange}
      onReimportFile={onReimportFile}
      onUndoImport={onUndoImport}
      onRequiredPieceChange={changeRequiredPiece}
      onUpdateStockLength={onUpdateStockLength}
      projectDirty={projectDirty}
    />
  </>;
}
