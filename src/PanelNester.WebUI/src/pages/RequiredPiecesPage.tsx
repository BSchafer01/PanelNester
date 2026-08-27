import { useEffect, useState } from 'react';
import type {
  InchDisplayFormat,
  ImportMappingSession,
  OptimizationGroup,
  RequiredPiece,
  RequiredPieceChange,
} from '../types/contracts';
import {
  assignSelectedWorksheetsToOptimizationGroup,
  canFinalizeStockLengthWorkbook,
  copyColumnMappingsFromPreviousSelectedWorksheet,
  setWorkbookWorksheetSelected,
  validateRequiredPieceCorrection,
} from './workbookImportDraftState';

interface RequiredPiecesPageProps {
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  inchDisplayFormat: InchDisplayFormat;
  busy: boolean;
  message?: string;
  onCreateOptimizationGroup: (name: string, stockLength: string) => void | Promise<void>;
  onUpdateStockLength: (optimizationGroupId: string, stockLength: string) => void | Promise<void>;
  onCreateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onUpdateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onDeleteRequiredPiece: (optimizationGroupId: string, requiredPieceId: string) => void | Promise<void>;
  onGenerateSelected?: (optimizationGroupId: string) => void | Promise<void>;
  onGenerateAllStale?: () => void | Promise<void>;
  onInchDisplayFormatChange: (format: InchDisplayFormat) => void;
  mappingSession?: ImportMappingSession;
  onImportFile?: () => void | Promise<void>;
  onUpdateImportMappingSession?: (session: ImportMappingSession) => void;
  onPreviewImportMapping?: (session?: ImportMappingSession) => void | Promise<void>;
  onFinalizeImportMapping?: () => void | Promise<void>;
  onCancelImportMapping?: () => void | Promise<void>;
}

interface RequiredPieceFormValues {
  quantity: string;
  length: string;
  profileNumber: string;
  partName: string;
  finish: string;
  partNumber: string;
}

interface RequiredPieceDraft extends RequiredPieceFormValues {
  optimizationGroupId: string;
  requiredPieceId?: string;
}

interface ImportCorrectionDraft extends RequiredPieceFormValues {
  piece: RequiredPiece;
}

const emptyDraft: RequiredPieceDraft = {
  optimizationGroupId: '',
  quantity: '',
  length: '',
  profileNumber: '',
  partName: '',
  finish: '',
  partNumber: '',
};

const displayFormatOptions: Array<{ value: InchDisplayFormat; label: string }> = [
  { value: 'decimal', label: 'Decimal' },
  { value: 'fractional16', label: 'Fractional — nearest 1/16' },
  { value: 'fractional32', label: 'Fractional — nearest 1/32' },
  { value: 'fractional64', label: 'Fractional — nearest 1/64' },
];

function greatestCommonDivisor(left: number, right: number): number {
  let a = Math.abs(left);
  let b = Math.abs(right);
  while (b !== 0) {
    [a, b] = [b, a % b];
  }
  return a || 1;
}

export function formatInches(value: number, format: InchDisplayFormat): string {
  if (format === 'decimal') {
    return `${Number(value.toFixed(6))} in`;
  }

  const denominator = format === 'fractional16' ? 16 : format === 'fractional32' ? 32 : 64;
  const totalNumerator = Math.round(value * denominator);
  const whole = Math.floor(totalNumerator / denominator);
  const numerator = totalNumerator % denominator;
  if (numerator === 0) {
    return `${whole} in`;
  }

  const divisor = greatestCommonDivisor(numerator, denominator);
  const fraction = `${numerator / divisor}/${denominator / divisor}`;
  return `${whole > 0 ? `${whole} ` : ''}${fraction} in`;
}

function toDraft(groupId: string, piece: RequiredPiece): RequiredPieceDraft {
  return {
    optimizationGroupId: groupId,
    requiredPieceId: piece.requiredPieceId,
    quantity: String(piece.quantity),
    length: String(piece.length),
    profileNumber: piece.profileNumber,
    partName: piece.partName ?? '',
    finish: piece.finish ?? '',
    partNumber: piece.partNumber ?? '',
  };
}

export function RequiredPiecesPage({
  optimizationGroups,
  activeOptimizationGroupId,
  inchDisplayFormat,
  busy,
  message,
  onCreateOptimizationGroup,
  onUpdateStockLength,
  onCreateRequiredPiece,
  onUpdateRequiredPiece,
  onDeleteRequiredPiece,
  onGenerateSelected,
  onGenerateAllStale,
  onInchDisplayFormatChange,
  mappingSession,
  onImportFile,
  onUpdateImportMappingSession,
  onPreviewImportMapping,
  onFinalizeImportMapping,
  onCancelImportMapping,
}: RequiredPiecesPageProps) {
  const [newGroupName, setNewGroupName] = useState('');
  const [newStockLength, setNewStockLength] = useState('');
  const [stockLengthDrafts, setStockLengthDrafts] = useState<Record<string, string>>({});
  const [bulkWorksheetGroupId, setBulkWorksheetGroupId] = useState('');
  const [sharedStockLengthDraft, setSharedStockLengthDraft] = useState('');
  const [draft, setDraft] = useState<RequiredPieceDraft>(emptyDraft);
  const [importCorrection, setImportCorrection] = useState<ImportCorrectionDraft | null>(null);
  const activeOptimizationGroup = optimizationGroups.find(
    (group) => group.optimizationGroupId === activeOptimizationGroupId,
  ) ?? optimizationGroups[0];
  const staleGroupCount = optimizationGroups.filter((group) =>
    group.requiredPieces.length > 0 && group.resultStatus !== 'valid').length;

  useEffect(() => {
    if (!draft.optimizationGroupId && optimizationGroups.length > 0) {
      setDraft((current) => ({
        ...current,
        optimizationGroupId: optimizationGroups[0].optimizationGroupId,
      }));
    }
  }, [draft.optimizationGroupId, optimizationGroups]);

  const updateDraft = (field: keyof RequiredPieceDraft, value: string) =>
    setDraft((current) => ({ ...current, [field]: value }));

  const submitRequiredPiece = async () => {
    const change: RequiredPieceChange = {
      type: draft.requiredPieceId ? 'update' : 'create',
      optimizationGroupId: draft.optimizationGroupId,
      ...(draft.requiredPieceId ? { requiredPieceId: draft.requiredPieceId } : {}),
      quantity: draft.quantity,
      length: draft.length,
      profileNumber: draft.profileNumber,
      partName: draft.partName,
      finish: draft.finish,
      partNumber: draft.partNumber,
    };
    if (draft.requiredPieceId) {
      await onUpdateRequiredPiece(change);
    } else {
      await onCreateRequiredPiece(change);
    }
    setDraft({ ...emptyDraft, optimizationGroupId: draft.optimizationGroupId });
  };

  const runAction = (action: () => void | Promise<void>) => {
    void Promise.resolve(action()).catch(() => undefined);
  };

  const activeImportDraft = mappingSession?.worksheets?.find(
    (worksheet) => worksheet.worksheet.worksheetName === mappingSession.activeWorksheetName,
  ) ?? mappingSession?.worksheets?.[0];
  const importedRequiredPieces = activeImportDraft?.preview.requiredPieces ??
    mappingSession?.preview.requiredPieces ?? [];
  const worksheetDrafts = mappingSession?.worksheets ?? [];
  const selectedWorksheetDrafts = worksheetDrafts.filter((worksheet) => worksheet.selected);
  const isWorkbookImport = Boolean(
    mappingSession?.filePath.toLowerCase().endsWith('.xlsx') ||
    mappingSession?.filePath.toLowerCase().endsWith('.xlsm'),
  );
  const assignedOptimizationGroup = optimizationGroups.find(
    (group) => group.optimizationGroupId === activeImportDraft?.optimizationGroupId,
  );
  useEffect(() => {
    setSharedStockLengthDraft(
      assignedOptimizationGroup?.stockLength == null ? '' : String(assignedOptimizationGroup.stockLength),
    );
  }, [assignedOptimizationGroup?.optimizationGroupId, assignedOptimizationGroup?.stockLength]);
  const unresolvedImportErrors = activeImportDraft
    ? activeImportDraft.preview.errors.filter((error) =>
        !activeImportDraft.excludedSourceRows.some((excluded) => excluded.rowId === error.rowId) &&
        !activeImportDraft.partOverrides.some((override) => override.rowId === error.rowId))
    : [];
  const canFinalizeImport = canFinalizeStockLengthWorkbook(worksheetDrafts, optimizationGroups);

  const publishWorksheetDrafts = (
    worksheets: NonNullable<ImportMappingSession['worksheets']>,
    activeWorksheetName = mappingSession?.activeWorksheetName,
  ) => {
    if (!mappingSession || !onUpdateImportMappingSession) return;
    const active = worksheets.find((item) =>
      item.worksheet.worksheetName === activeWorksheetName) ??
      worksheets.find((item) => item.selected);
    onUpdateImportMappingSession({
      ...mappingSession,
      worksheets,
      activeWorksheetName: active?.worksheet.worksheetName,
      preview: active?.preview ?? mappingSession.preview,
      options: active?.options ?? mappingSession.options,
      newMaterials: active?.newMaterials ?? mappingSession.newMaterials,
      hasPendingChanges: active?.hasPendingChanges ?? true,
    });
  };

  const updateActiveImportDraft = (
    update: (draft: NonNullable<ImportMappingSession['worksheets']>[number]) =>
      NonNullable<ImportMappingSession['worksheets']>[number],
  ) => {
    if (!mappingSession?.worksheets || !activeImportDraft || !onUpdateImportMappingSession) return;
    const worksheets = mappingSession.worksheets.map((draft) =>
      draft.worksheet.worksheetName === activeImportDraft.worksheet.worksheetName
        ? update(draft)
        : draft);
    const updatedDraft = worksheets.find((draft) =>
      draft.worksheet.worksheetName === activeImportDraft.worksheet.worksheetName)!;
    publishWorksheetDrafts(worksheets, updatedDraft.worksheet.worksheetName);
  };

  const assignSelectedWorksheets = () => {
    const group = optimizationGroups.find((item) => item.optimizationGroupId === bulkWorksheetGroupId);
    if (!group) return;
    publishWorksheetDrafts(assignSelectedWorksheetsToOptimizationGroup(worksheetDrafts, group));
  };

  const saveSharedStockLength = () => {
    if (!activeImportDraft || !assignedOptimizationGroup) return;
    const worksheetNames = worksheetDrafts
      .filter((item) => item.optimizationGroupId === assignedOptimizationGroup.optimizationGroupId)
      .map((item) => item.worksheet.worksheetName);
    const message = `Change the shared Stock Length for ${assignedOptimizationGroup.name}? This updates every assigned Worksheet: ${worksheetNames.join(', ')}.`;
    if (window.confirm(message)) {
      runAction(() => onUpdateStockLength(
        assignedOptimizationGroup.optimizationGroupId,
        sharedStockLengthDraft || String(assignedOptimizationGroup.stockLength ?? ''),
      ));
    }
  };

  const excludeImportedPiece = (piece: RequiredPiece) => {
    const sourceReference = piece.sourceReferences[0];
    const error = activeImportDraft?.preview.errors.find((item) => item.rowId === piece.requiredPieceId);
    if (!sourceReference || !error) return;
    updateActiveImportDraft((draft) => ({
      ...draft,
      excludedSourceRows: [...draft.excludedSourceRows, {
        rowId: piece.requiredPieceId,
        sourceReference,
        originalValidationError: error,
      }],
    }));
  };

  const beginImportCorrection = (piece: RequiredPiece) => setImportCorrection({
    piece,
    quantity: piece.quantityText ?? String(piece.quantity),
    length: piece.lengthText ?? String(piece.length),
    profileNumber: piece.profileNumber,
    partName: piece.partName ?? '',
    finish: piece.finish ?? '',
    partNumber: piece.partNumber ?? '',
  });

  const saveImportCorrection = () => {
    if (!importCorrection) return;
    const imported = importCorrection.piece;
    const validation = validateRequiredPieceCorrection(
      importCorrection.quantity,
      importCorrection.length,
      importCorrection.profileNumber,
    );
    const currentRequiredPiece: RequiredPiece = {
      ...imported,
      quantity: validation.quantity,
      quantityText: importCorrection.quantity,
      length: validation.length,
      lengthText: importCorrection.length,
      profileNumber: importCorrection.profileNumber,
      partName: importCorrection.partName || null,
      finish: importCorrection.finish || null,
      partNumber: importCorrection.partNumber || null,
      validationStatus: validation.validationStatus,
      validationMessages: validation.validationMessages,
    };
    updateActiveImportDraft((draft) => ({
      ...draft,
      partOverrides: [
        ...draft.partOverrides.filter((partOverride) => partOverride.rowId !== imported.requiredPieceId),
        {
          rowId: imported.requiredPieceId,
          importedRequiredPiece: imported,
          currentRequiredPiece,
          sourceReferences: imported.sourceReferences,
        },
      ],
    }));
    setImportCorrection(null);
  };

  return (
    <div className="stock-length-import">
      <header className="page-header">
        <div>
          <p className="eyebrow">Stock-Length Project</p>
          <h1>Required Pieces</h1>
          <p>Configure Stock Length by Optimization Group, then enter the pieces to cut.</p>
          {message ? <p className="section-note" role="status">{message}</p> : null}
        </div>
        <label className="project-field stock-length-import__format">
          <span>Length Display</span>
          <select
            aria-label="Length Display"
            disabled={busy}
            onChange={(event) => onInchDisplayFormatChange(event.target.value as InchDisplayFormat)}
            value={inchDisplayFormat}
          >
            {displayFormatOptions.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>
        {onGenerateSelected || onGenerateAllStale ? (
          <div className="form-actions">
            {onGenerateSelected ? <button
                className="primary-button"
                disabled={busy || !activeOptimizationGroup || activeOptimizationGroup.requiredPieces.length === 0 || !activeOptimizationGroup.stockLength || activeOptimizationGroup.stockLength <= 0}
                onClick={() => activeOptimizationGroup && runAction(() => onGenerateSelected(activeOptimizationGroup.optimizationGroupId))}
                type="button"
              >Generate Selected</button> : null}
            {onGenerateAllStale ? <button
                className="secondary-button"
                disabled={busy || staleGroupCount === 0}
                onClick={() => runAction(onGenerateAllStale)}
                type="button"
              >Generate All Stale</button> : null}
            <span className="section-note">
              {onGenerateAllStale && staleGroupCount > 0
                ? `${staleGroupCount} stale Optimization Group${staleGroupCount === 1 ? '' : 's'}`
                : !activeOptimizationGroup || activeOptimizationGroup.requiredPieces.length === 0
                ? 'Empty Optimization Group'
                : activeOptimizationGroup.resultStatus === 'valid'
                  ? 'Current Cut Plan'
                  : 'Needs Generation'}
            </span>
          </div>
        ) : null}
      </header>

      <section className="project-card stock-length-import__csv">
        <div className="project-card__header">
          <div>
            <h2>{isWorkbookImport ? 'Import Stock-Length Workbook' : 'Import Stock-Length CSV'}</h2>
            <p className="section-note">Configure each Worksheet independently, assign an Optimization Group, then review its Required Pieces.</p>
          </div>
          <button className="secondary-button" disabled={busy} onClick={() => onImportFile && runAction(onImportFile)} type="button">
            Import CSV or Workbook
          </button>
        </div>
        {mappingSession && activeImportDraft ? (
          <div className="stock-length-import__csv-review">
            {isWorkbookImport ? (
              <div className="workbook-discovery">
                <div className="mapping-resolution-list">
                  {worksheetDrafts.map((draft) => (
                    <div className="mapping-resolution-card" key={`${draft.worksheet.originalPosition}-${draft.worksheet.worksheetName}`}>
                      <label className="checkbox-field">
                        <input
                          checked={draft.selected}
                          disabled={busy}
                          onChange={(event) => publishWorksheetDrafts(
                            setWorkbookWorksheetSelected(
                              worksheetDrafts,
                              draft.worksheet.worksheetName,
                              event.target.checked,
                            ),
                            event.target.checked
                              ? draft.worksheet.worksheetName
                              : mappingSession.activeWorksheetName,
                          )}
                          type="checkbox"
                        />
                        <span>{draft.worksheet.originalPosition}. {draft.worksheet.worksheetName}</span>
                      </label>
                      <button
                        className="secondary-button"
                        disabled={busy || !draft.selected}
                        onClick={() => publishWorksheetDrafts(worksheetDrafts, draft.worksheet.worksheetName)}
                        type="button"
                      >
                        {draft.worksheet.worksheetName === activeImportDraft.worksheet.worksheetName ? 'Configuring' : 'Configure'}
                      </button>
                    </div>
                  ))}
                </div>
                <div className="form-actions">
                  <label className="project-field">
                    <span>Move selected Worksheets to</span>
                    <select
                      aria-label="Optimization Group for selected Worksheets"
                      disabled={busy || selectedWorksheetDrafts.length === 0}
                      onChange={(event) => setBulkWorksheetGroupId(event.target.value)}
                      value={bulkWorksheetGroupId}
                    >
                      <option value="">Choose an Optimization Group</option>
                      {optimizationGroups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}
                    </select>
                  </label>
                  <button className="secondary-button" disabled={busy || !bulkWorksheetGroupId} onClick={assignSelectedWorksheets} type="button">
                    Assign selected Worksheets
                  </button>
                </div>
              </div>
            ) : null}
            <label className="project-field">
              <span>Optimization Group for {activeImportDraft.worksheet.worksheetName}</span>
              <select
                aria-label={`Optimization Group for ${activeImportDraft.worksheet.worksheetName}`}
                disabled={busy}
                onChange={(event) => {
                  const group = optimizationGroups.find((item) => item.optimizationGroupId === event.target.value);
                  updateActiveImportDraft((draft) => ({
                    ...draft,
                    optimizationGroupId: group?.optimizationGroupId ?? '',
                    optimizationGroupName: group?.name ?? '',
                  }));
                }}
                value={activeImportDraft.optimizationGroupId}
              >
                <option value="">Choose an Optimization Group</option>
                {optimizationGroups.map((group) => (
                  <option key={group.optimizationGroupId} value={group.optimizationGroupId}>
                    {group.name}{group.stockLength && group.stockLength > 0 ? ` — ${group.stockLength} in` : ' — Stock Length required'}
                  </option>
                ))}
              </select>
            </label>
            {assignedOptimizationGroup ? (
              <div className="stock-length-import__group-row">
                <span>Shared by {worksheetDrafts.filter((item) => item.optimizationGroupId === assignedOptimizationGroup.optimizationGroupId).map((item) => item.worksheet.worksheetName).join(', ')}</span>
                <label>
                  <span>Stock Length</span>
                  <input
                    aria-label={`Shared Stock Length for ${assignedOptimizationGroup.name}`}
                    disabled={busy}
                    onChange={(event) => setSharedStockLengthDraft(event.target.value)}
                    placeholder={String(assignedOptimizationGroup.stockLength ?? '')}
                    value={sharedStockLengthDraft}
                  />
                </label>
                <button className="secondary-button" disabled={busy} onClick={saveSharedStockLength} type="button">Save shared Stock Length</button>
              </div>
            ) : null}
            {isWorkbookImport ? (
              <div className="project-form-grid">
                <label className="project-field">
                  <span>Heading Range (A1)</span>
                  <input
                    aria-label={`Heading Range for ${activeImportDraft.worksheet.worksheetName}`}
                    disabled={busy}
                    onChange={(event) => updateActiveImportDraft((draft) => ({
                      ...draft,
                      headingRange: event.target.value.toUpperCase(),
                      headingRangeConfirmed: event.target.value.trim().length > 0,
                      hasPendingChanges: true,
                    }))}
                    value={activeImportDraft.headingRange}
                  />
                </label>
                <button
                  className="secondary-button"
                  disabled={busy}
                  onClick={() => {
                    const result = copyColumnMappingsFromPreviousSelectedWorksheet(
                      worksheetDrafts,
                      activeImportDraft.worksheet.worksheetName,
                    );
                    if (!result.error) publishWorksheetDrafts(result.drafts, activeImportDraft.worksheet.worksheetName);
                  }}
                  type="button"
                >Copy Mappings from Previous</button>
              </div>
            ) : null}
            <div className="stock-length-import__mapping-grid">
              {activeImportDraft.preview.columnMappings.map((mapping) => (
                <label className="project-field" key={mapping.targetField}>
                  <span>{mapping.targetField}</span>
                  <select
                    aria-label={`CSV column for ${mapping.targetField}`}
                    disabled={busy}
                    onChange={(event) => updateActiveImportDraft((draft) => ({
                      ...draft,
                      hasPendingChanges: true,
                      options: {
                        ...draft.options,
                        projectKind: 'stockLength',
                        columnMappings: [
                          ...draft.options.columnMappings.filter((item) => item.targetField !== mapping.targetField),
                          ...(event.target.value ? [{ sourceColumn: event.target.value, targetField: mapping.targetField }] : []),
                        ],
                      },
                    }))}
                    value={mapping.sourceColumn ?? ''}
                  >
                    <option value="">Not mapped</option>
                    {activeImportDraft.preview.availableColumns.map((column) => <option key={column} value={column}>{column}</option>)}
                  </select>
                </label>
              ))}
            </div>
            <div className="table-wrap">
              <table><thead><tr><th>Source Reference</th><th>Quantity</th><th>Length</th><th>Profile Number</th><th>Part Number</th><th>Status</th><th>Actions</th></tr></thead><tbody>
                {importedRequiredPieces.map((piece) => {
                  const excluded = activeImportDraft.excludedSourceRows.some((row) => row.rowId === piece.requiredPieceId);
                  const overridden = activeImportDraft.partOverrides.some((partOverride) => partOverride.rowId === piece.requiredPieceId);
                  return <tr key={piece.requiredPieceId}><td>{piece.sourceReferences[0] ? `${piece.sourceReferences[0].worksheetName}!${piece.sourceReferences[0].physicalRow}` : '—'}</td><td>{piece.quantityText ?? piece.quantity}</td><td>{piece.lengthText ?? piece.length}</td><td>{piece.profileNumber}</td><td>{piece.partNumber || '—'}</td><td>{excluded ? 'Excluded' : overridden ? 'Corrected' : piece.validationStatus ?? 'valid'}</td><td>{piece.validationStatus === 'error' && !excluded ? <div className="form-actions"><button aria-label={`Correct source row ${piece.sourceReferences[0]?.physicalRow}`} className="secondary-button" onClick={() => beginImportCorrection(piece)} type="button">Correct</button><button aria-label={`Exclude source row ${piece.sourceReferences[0]?.physicalRow}`} className="danger-button" onClick={() => excludeImportedPiece(piece)} type="button">Exclude</button></div> : null}</td></tr>;
                })}
              </tbody></table>
            </div>
            {importCorrection ? (
              <div className="stock-length-import__correction">
                <h3>Correct source row {importCorrection.piece.sourceReferences[0]?.physicalRow}</h3>
                <div className="project-form-grid stock-length-import__piece-form">
                  {(['quantity', 'length', 'profileNumber', 'partName', 'finish', 'partNumber'] as const).map((field) => {
                    const label = {
                      quantity: 'Quantity', length: 'Length', profileNumber: 'Profile Number',
                      partName: 'Part Name', finish: 'Finish', partNumber: 'Part Number',
                    }[field];
                    return <label className="project-field" key={field}><span>{label}</span><input aria-label={`Corrected ${label}`} disabled={busy} onChange={(event) => setImportCorrection((current) => current ? { ...current, [field]: event.target.value } : current)} value={importCorrection[field]} /></label>;
                  })}
                </div>
                <div className="form-actions">
                  <button className="primary-button" disabled={busy} onClick={saveImportCorrection} type="button">Save Correction</button>
                  <button className="secondary-button" disabled={busy} onClick={() => setImportCorrection(null)} type="button">Cancel Correction</button>
                </div>
              </div>
            ) : null}
            {unresolvedImportErrors.length > 0 ? <p className="section-note" role="alert">Correct or explicitly exclude every invalid Required Piece before finalization.</p> : null}
            {!assignedOptimizationGroup?.stockLength || assignedOptimizationGroup.stockLength <= 0 ? <p className="section-note">Assign the Worksheet to an Optimization Group with a positive Stock Length.</p> : null}
            <div className="form-actions">
              <button className="secondary-button" disabled={busy || !activeImportDraft.hasPendingChanges} onClick={() => onPreviewImportMapping && runAction(() => onPreviewImportMapping(mappingSession))} type="button">Refresh Preview</button>
              <button className="primary-button" disabled={busy || !canFinalizeImport} onClick={() => onFinalizeImportMapping && runAction(onFinalizeImportMapping)} type="button">{isWorkbookImport ? 'Finalize Workbook Import' : 'Finalize CSV Import'}</button>
              <button className="secondary-button" disabled={busy} onClick={() => onCancelImportMapping && runAction(onCancelImportMapping)} type="button">Cancel Import</button>
            </div>
          </div>
        ) : <p className="section-note">Choose a CSV to begin an Import Session.</p>}
      </section>

      <section className="project-card stock-length-import__group-create">
        <div className="project-card__header"><h2>Optimization Groups</h2></div>
        <div className="project-form-grid">
          <label className="project-field">
            <span>Optimization Group name</span>
            <input aria-label="Optimization Group name" onChange={(event) => setNewGroupName(event.target.value)} value={newGroupName} />
          </label>
          <label className="project-field">
            <span>Stock Length (in)</span>
            <input aria-label="Stock Length" inputMode="decimal" onChange={(event) => setNewStockLength(event.target.value)} value={newStockLength} />
          </label>
          <button
            className="primary-button"
            disabled={busy}
            onClick={() => runAction(() => onCreateOptimizationGroup(newGroupName, newStockLength))}
            type="button"
          >
            Add Optimization Group
          </button>
        </div>
        {optimizationGroups.map((group) => {
          const stockLengthValue = stockLengthDrafts[group.optimizationGroupId] ??
            (group.stockLength == null ? '' : String(group.stockLength));
          return (
            <div className="stock-length-import__group-row" key={group.optimizationGroupId}>
              <strong>{group.name}</strong>
              <label>
                <span>Stock Length</span>
                <input
                  aria-label={`Stock Length for ${group.name}`}
                  disabled={busy}
                  onChange={(event) => setStockLengthDrafts((current) => ({
                    ...current,
                    [group.optimizationGroupId]: event.target.value,
                  }))}
                  value={stockLengthValue}
                />
              </label>
              <button
                className="secondary-button"
                disabled={busy}
                onClick={() => runAction(() => onUpdateStockLength(group.optimizationGroupId, stockLengthValue))}
                type="button"
              >Save Stock Length</button>
            </div>
          );
        })}
      </section>

      <section className="project-card">
        <div className="project-card__header"><h2>{draft.requiredPieceId ? 'Edit Required Piece' : 'Add Required Piece'}</h2></div>
        <div className="project-form-grid stock-length-import__piece-form">
          <label className="project-field">
            <span>Optimization Group</span>
            <select aria-label="Optimization Group" disabled={busy} onChange={(event) => updateDraft('optimizationGroupId', event.target.value)} value={draft.optimizationGroupId}>
              <option value="">Choose an Optimization Group</option>
              {optimizationGroups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}
            </select>
          </label>
          {(['quantity', 'length', 'profileNumber', 'partName', 'finish', 'partNumber'] as const).map((field) => {
            const label = {
              quantity: 'Quantity', length: 'Length', profileNumber: 'Profile Number',
              partName: 'Part Name', finish: 'Finish', partNumber: 'Part Number',
            }[field];
            return (
              <label className="project-field" key={field}>
                <span>{label}{field === 'length' ? ' (in)' : ''}</span>
                <input
                  aria-label={label}
                  disabled={busy}
                  onChange={(event) => updateDraft(field, event.target.value)}
                  required={field === 'quantity' || field === 'length' || field === 'profileNumber'}
                  value={draft[field]}
                />
              </label>
            );
          })}
        </div>
        <div className="form-actions">
          <button className="primary-button" disabled={busy} onClick={() => runAction(submitRequiredPiece)} type="button">
            {draft.requiredPieceId ? 'Save Required Piece' : 'Add Required Piece'}
          </button>
          {draft.requiredPieceId ? <button className="secondary-button" onClick={() => setDraft({ ...emptyDraft, optimizationGroupId: draft.optimizationGroupId })} type="button">Cancel</button> : null}
        </div>
      </section>

      <section className="project-card">
        <div className="project-card__header"><h2>Saved Required Pieces</h2></div>
        {optimizationGroups.every((group) => group.requiredPieces.length === 0) ? (
          <p className="section-note">No Required Pieces yet.</p>
        ) : (
          <div className="table-wrap"><table><thead><tr><th>Optimization Group</th><th>Qty</th><th>Length</th><th>Profile Number</th><th>Finish</th><th>Part Name</th><th>Part Number</th><th>Actions</th></tr></thead><tbody>
            {optimizationGroups.flatMap((group) => group.requiredPieces.map((piece) => {
              const stockGroup = group.stockGroups.find((item) => item.requiredPieceIds.includes(piece.requiredPieceId));
              return <tr key={piece.requiredPieceId}><td>{group.name}</td><td>{piece.quantity}</td><td>{formatInches(piece.length, inchDisplayFormat)}</td><td>{piece.profileNumber}</td><td>{stockGroup?.finish || piece.finish || 'No finish specified'}</td><td>{piece.partName || '—'}</td><td>{piece.partNumber || '—'}</td><td><div className="table-actions"><button aria-label={`Edit Required Piece ${piece.requiredPieceId}`} className="secondary-button" onClick={() => setDraft(toDraft(group.optimizationGroupId, piece))} type="button">Edit</button><button aria-label={`Delete Required Piece ${piece.requiredPieceId}`} className="danger-button" onClick={() => runAction(() => onDeleteRequiredPiece(group.optimizationGroupId, piece.requiredPieceId))} type="button">Delete</button></div></td></tr>;
            }))}
          </tbody></table></div>
        )}
      </section>
    </div>
  );
}
