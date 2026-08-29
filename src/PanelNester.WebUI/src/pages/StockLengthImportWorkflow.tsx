import { useEffect, useMemo, useState } from 'react';
import type {
  ImportFieldName,
  ImportMappingSession,
  ImportWorksheetDraft,
  OptimizationGroup,
  RequiredPiece,
  StockLengthImportGroupConfiguration,
} from '../types/contracts';
import {
  applyWorksheetLayoutMappings,
  buildWorksheetLayouts,
  canFinalizeStockLengthWorkbook,
  confirmWorksheetHeadingRange,
  copyColumnMappingsFromPreviousSelectedWorksheet,
  headingRangeFromPreviewCells,
  setWorkbookWorksheetSelected,
  validateRequiredPieceCorrection,
  worksheetSourceColumns,
} from './workbookImportDraftState';
import {
  buildStockLengthImportPlan,
  canReviewStockLengthImport,
  fileNameFromImportPath,
  hasRequiredStockLengthMappings,
  requiredStockLengthFields,
  summarizeStockLengthImport,
} from './requiredPiecesPresentation';

type ImportWorkflowStep = 'worksheets' | 'mapping' | 'review';
type ReviewTab = 'resulting' | 'source' | 'errors' | 'warnings';
type WorksheetStatusFilter = 'all' | 'selected' | 'ready' | 'attention';

interface ImportCorrectionDraft {
  piece: RequiredPiece;
  quantity: string;
  length: string;
  profileNumber: string;
  partName: string;
  finish: string;
  partNumber: string;
}

interface StockLengthImportWorkflowProps {
  session: ImportMappingSession;
  groups: OptimizationGroup[];
  busy: boolean;
  message?: string;
  onReplaceFile: () => void | Promise<void>;
  onUpdateSession: (session: ImportMappingSession) => void;
  onPreview: (
    session?: ImportMappingSession,
    worksheetNames?: string[],
  ) => void | Promise<void>;
  onFinalize: () => void | Promise<void>;
  onCancel: () => void | Promise<void>;
}

function sourceColumns(draft: ImportWorksheetDraft) {
  const detectedColumns = worksheetSourceColumns(draft);
  return detectedColumns.length > 0
    ? detectedColumns
    : draft.preview.availableColumns.map((column) => ({ address: column, heading: column }));
}

function mappedSampleValues(draft: ImportWorksheetDraft, targetField: ImportFieldName): string[] {
  const values = (draft.preview.requiredPieces ?? []).flatMap((piece) => {
    const value = targetField === 'Quantity' ? piece.quantityText ?? `${piece.quantity}`
      : targetField === 'Length' ? piece.lengthText ?? `${piece.length}`
      : targetField === 'Profile Number' ? piece.profileNumber
      : targetField === 'Part Name' ? piece.partName
      : targetField === 'Finish' ? piece.finish
      : targetField === 'Part Number' ? piece.partNumber
      : null;
    return value == null || `${value}`.trim() === '' ? [] : [`${value}`];
  });
  return [...new Set(values)].slice(0, 2);
}

function detectedFieldType(targetField: ImportFieldName): string {
  if (targetField === 'Quantity') return 'Integer';
  if (targetField === 'Length') return 'Length';
  return 'Text';
}

function draftStatus(draft: ImportWorksheetDraft): string {
  if (!draft.selected) return 'Not selected';
  if (!draft.optimizationGroupId || !draft.stockLength || draft.stockLength <= 0) return 'Needs setup';
  if (!draft.headingRangeConfirmed) return 'Needs Heading Range';
  if (!hasRequiredStockLengthMappings(draft)) return 'Needs mapping';
  return 'Ready';
}

function worksheetSetupStatus(draft: ImportWorksheetDraft): string {
  if (!draft.selected) return 'Not selected';
  if (!draft.optimizationGroupId || !draft.stockLength || draft.stockLength <= 0) return 'Needs setup';
  return 'Ready for mapping';
}

const stockLengthApplicationFields: ImportFieldName[] = [
  'Quantity',
  'Length',
  'Profile Number',
  'Part Name',
  'Finish',
  'Part Number',
];

const groupingFields: ImportFieldName[] = [
  'Profile Number',
  'Finish',
  'Part Number',
  'Part Name',
];

function normalizedGroupingValue(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

function groupingValueHash(value: string): string {
  let hash = 2166136261;
  for (const character of value) {
    hash ^= character.codePointAt(0) ?? 0;
    hash = Math.imul(hash, 16777619);
  }
  return (hash >>> 0).toString(36);
}

function groupingValue(piece: RequiredPiece, field: ImportFieldName): string {
  return field === 'Profile Number' ? piece.profileNumber
    : field === 'Finish' ? piece.finish ?? ''
    : field === 'Part Number' ? piece.partNumber ?? ''
    : field === 'Part Name' ? piece.partName ?? ''
    : '';
}

function buildFieldGroupConfigurations(
  session: ImportMappingSession,
  field: ImportFieldName,
  pieces: RequiredPiece[],
  groups: OptimizationGroup[],
): Array<StockLengthImportGroupConfiguration & { pieceCount: number }> {
  const existingConfigurations = new Map(
    (session.stockLengthGrouping?.field === field ? session.stockLengthGrouping.groups : [])
      .map((group) => [normalizedGroupingValue(group.groupingValue), group]),
  );
  const existingGroups = new Map(groups.flatMap((group) =>
    group.importGroupingKey?.field === field
      ? [[group.importGroupingKey.normalizedValue, group] as const]
      : []));
  const usedIds = new Set([
    ...[...existingConfigurations.values()].map((configuration) => configuration.optimizationGroupId),
    ...groups.map((group) => group.optimizationGroupId),
  ]);
  const values = new Map<string, { display: string; count: number }>();
  for (const piece of pieces) {
    const raw = groupingValue(piece, field).trim();
    const normalized = normalizedGroupingValue(raw);
    const current = values.get(normalized);
    values.set(normalized, { display: current?.display || raw, count: (current?.count ?? 0) + piece.quantity });
  }

  return [...values.entries()]
    .sort(([left], [right]) => Number(left === '') - Number(right === '') || (left < right ? -1 : left > right ? 1 : 0))
    .map(([normalized, value]) => {
      const configured = existingConfigurations.get(normalized);
      const existing = existingGroups.get(normalized);
      let generatedId = `import-${session.sessionId}-field-${groupingValueHash(normalized)}`;
      for (let suffix = 2; usedIds.has(generatedId); suffix += 1) {
        generatedId = `import-${session.sessionId}-field-${groupingValueHash(normalized)}-${suffix}`;
      }
      if (!configured && !existing) usedIds.add(generatedId);
      return {
        groupingValue: value.display,
        optimizationGroupId: configured?.optimizationGroupId ?? existing?.optimizationGroupId ?? generatedId,
        name: configured?.name ?? existing?.name ?? (value.display || `Unspecified ${field}`),
        stockLength: configured?.stockLength ?? existing?.stockLength ?? null,
        pieceCount: value.count,
      };
    });
}

export function StockLengthImportWorkflow({
  session,
  groups,
  busy,
  onReplaceFile,
  onUpdateSession,
  onPreview,
  onFinalize,
  onCancel,
}: StockLengthImportWorkflowProps) {
  const [step, setStep] = useState<ImportWorkflowStep>('worksheets');
  const [bulkGroupId, setBulkGroupId] = useState('');
  const [rangeSelection, setRangeSelection] = useState<{ worksheet: string; firstAddress: string } | null>(null);
  const [mappingMessage, setMappingMessage] = useState('');
  const [reviewTab, setReviewTab] = useState<ReviewTab>('resulting');
  const [worksheetQuery, setWorksheetQuery] = useState('');
  const [worksheetStatusFilter, setWorksheetStatusFilter] = useState<WorksheetStatusFilter>('all');
  const [mappingPreviewTab, setMappingPreviewTab] = useState<'source' | 'imported'>('source');
  const [selectedIssueIds, setSelectedIssueIds] = useState<Set<string>>(new Set());
  const [correction, setCorrection] = useState<ImportCorrectionDraft | null>(null);

  useEffect(() => {
    setStep('worksheets');
    setRangeSelection(null);
    setReviewTab('resulting');
    setSelectedIssueIds(new Set());
  }, [session.sessionId]);

  const drafts = session.worksheets ?? [];
  const selectedDrafts = drafts.filter((draft) => draft.selected);
  const activeDraft = drafts.find((draft) => draft.worksheet.worksheetName === session.activeWorksheetName)
    ?? selectedDrafts[0]
    ?? drafts[0];
  const isCsv = session.filePath.toLocaleLowerCase().endsWith('.csv');
  const grouping = session.stockLengthGrouping ?? { mode: 'worksheet' as const, field: null, groups: [] };
  const fieldGrouping = grouping.mode === 'mappedField';
  const review = useMemo(() => summarizeStockLengthImport(drafts), [drafts]);
  const importPlan = useMemo(() => buildStockLengthImportPlan(fieldGrouping
    ? drafts.map((draft) => ({ ...draft, optimizationGroupId: '' }))
    : drafts), [drafts, fieldGrouping]);
  const selectedGroupingField = grouping.field && groupingFields.includes(grouping.field) ? grouping.field : 'Profile Number';
  const fieldGroupConfigurations = useMemo(() => buildFieldGroupConfigurations(
    session,
    selectedGroupingField,
    importPlan.resultingEntries,
    groups,
  ), [session, selectedGroupingField, importPlan.resultingEntries, groups]);
  const layouts = useMemo(() => buildWorksheetLayouts(drafts), [drafts]);
  const activeLayout = layouts.find((layout) =>
    layout.worksheetNames.includes(activeDraft?.worksheet.worksheetName ?? ''));
  const visibleDrafts = useMemo(() => drafts
    .filter((draft) => draft.worksheet.worksheetName.toLocaleLowerCase().includes(worksheetQuery.trim().toLocaleLowerCase()))
    .filter((draft) => worksheetStatusFilter === 'all' ||
      (worksheetStatusFilter === 'selected' && draft.selected) ||
      (worksheetStatusFilter === 'ready' && worksheetSetupStatus(draft) === 'Ready for mapping') ||
      (worksheetStatusFilter === 'attention' && draft.selected && worksheetSetupStatus(draft) !== 'Ready for mapping'))
    .sort((left, right) => Number(right.selected) - Number(left.selected) ||
      left.worksheet.originalPosition - right.worksheet.originalPosition),
  [drafts, worksheetQuery, worksheetStatusFilter]);
  const hasGroupingFieldMappings = selectedDrafts.every((draft) =>
    draft.options.columnMappings.some((mapping) =>
      mapping.targetField === selectedGroupingField && mapping.sourceColumn.trim().length > 0));
  const fieldModeReady = selectedDrafts.length > 0 && selectedDrafts.every((draft) =>
    draft.headingRangeConfirmed && hasRequiredStockLengthMappings(draft)) && hasGroupingFieldMappings;
  const canReview = fieldGrouping ? fieldModeReady : canReviewStockLengthImport(drafts);
  const fieldGroupsReady = fieldGroupConfigurations.length > 0 &&
    fieldGroupConfigurations.every((group) => Boolean(group.stockLength && group.stockLength > 0));
  const canFinalize = (fieldGrouping ? fieldModeReady && fieldGroupsReady : canFinalizeStockLengthWorkbook(drafts, groups)) &&
    review.unresolvedErrors.length === 0;
  const setupStatusFor = (draft: ImportWorksheetDraft) => fieldGrouping
    ? draft.selected ? 'Ready for mapping' : 'Not selected'
    : worksheetSetupStatus(draft);
  const draftStatusFor = (draft: ImportWorksheetDraft) => {
    if (!fieldGrouping) return draftStatus(draft);
    if (!draft.selected) return 'Not selected';
    if (!draft.headingRangeConfirmed) return 'Needs Heading Range';
    if (!hasRequiredStockLengthMappings(draft) || !draft.options.columnMappings.some((mapping) =>
      mapping.targetField === selectedGroupingField && mapping.sourceColumn.trim().length > 0)) return 'Needs mapping';
    return 'Ready';
  };
  const groupChoices = useMemo(() => {
    const choices = new Map<string, Pick<OptimizationGroup, 'optimizationGroupId' | 'name' | 'stockLength'>>();
    for (const group of groups) choices.set(group.optimizationGroupId, group);
    for (const draft of drafts) choices.set(draft.optimizationGroupId, {
      optimizationGroupId: draft.optimizationGroupId,
      name: draft.optimizationGroupName,
      stockLength: draft.stockLength,
    });
    return [...choices.values()].filter((group) => group.optimizationGroupId && group.name);
  }, [drafts, groups]);
  const run = (action: () => void | Promise<void>) => void Promise.resolve(action()).catch(() => undefined);

  const buildSession = (nextDrafts: ImportWorksheetDraft[], activeName?: string): ImportMappingSession => {
    const active = nextDrafts.find((draft) => draft.worksheet.worksheetName === activeName)
      ?? nextDrafts.find((draft) => draft.selected)
      ?? nextDrafts[0];
    return {
      ...session,
      worksheets: nextDrafts,
      activeWorksheetName: active?.worksheet.worksheetName,
      preview: active?.preview ?? session.preview,
      options: active?.options ?? session.options,
      newMaterials: active?.newMaterials ?? session.newMaterials,
      hasPendingChanges: active?.hasPendingChanges ?? session.hasPendingChanges,
    };
  };
  const publish = (nextDrafts: ImportWorksheetDraft[], activeName?: string) => {
    const next = buildSession(nextDrafts, activeName);
    onUpdateSession(next);
    return next;
  };
  const publishFieldGrouping = (
    field: ImportFieldName,
    configurations = buildFieldGroupConfigurations(session, field, importPlan.resultingEntries, groups),
  ) => onUpdateSession({
    ...session,
    stockLengthGrouping: {
      mode: 'mappedField',
      field,
      groups: configurations.map(({ pieceCount: _pieceCount, ...configuration }) => configuration),
    },
  });
  const updateDraft = (worksheetName: string, update: (draft: ImportWorksheetDraft) => ImportWorksheetDraft) =>
    publish(drafts.map((draft) => draft.worksheet.worksheetName === worksheetName ? update(draft) : draft), worksheetName);
  const activate = (worksheetName: string) => publish(drafts, worksheetName);
  const previewActiveLayout = (next: ImportMappingSession) => {
    const nextDrafts = next.worksheets ?? [];
    const activeName = next.activeWorksheetName ?? '';
    const layout = buildWorksheetLayouts(nextDrafts).find((candidate) =>
      candidate.worksheetNames.includes(activeName));
    const worksheetNames = (layout?.drafts ?? nextDrafts.filter((draft) =>
      draft.worksheet.worksheetName === activeName))
      .filter((draft) => draft.selected && draft.headingRangeConfirmed)
      .map((draft) => draft.worksheet.worksheetName);
    return onPreview(next, worksheetNames);
  };

  const selectPreviewCell = (address: string) => {
    if (!activeDraft) return;
    const worksheetName = activeDraft.worksheet.worksheetName;
    if (!rangeSelection || rangeSelection.worksheet !== worksheetName) {
      setRangeSelection({ worksheet: worksheetName, firstAddress: address });
      setMappingMessage(`Selected ${address}. Select the last Heading Range cell.`);
      return;
    }
    const range = headingRangeFromPreviewCells(rangeSelection.firstAddress, address);
    setRangeSelection(null);
    if (!range) {
      setMappingMessage('A Heading Range must be contiguous and contained in one row.');
      return;
    }
    const result = confirmWorksheetHeadingRange(drafts, worksheetName, range);
    if (result.error) {
      publish(result.drafts, worksheetName);
      setMappingMessage(result.error);
    }
    else {
      const next = publish(result.drafts, worksheetName);
      setMappingMessage(`Heading Range set to ${range}. Updating preview and mappings…`);
      run(() => previewActiveLayout(next));
    }
  };

  const excludeIssueRows = (rowIds: string[]) => {
    if (rowIds.length === 0) return;
    const ids = new Set(rowIds);
    publish(drafts.map((draft) => {
      const existing = new Set(draft.excludedSourceRows.map((row) => row.rowId));
      const additions = draft.preview.errors.flatMap((error) => {
        if (!error.rowId || !ids.has(error.rowId) || existing.has(error.rowId)) return [];
        const piece = (draft.preview.requiredPieces ?? []).find((item) => item.requiredPieceId === error.rowId);
        const sourceReference = piece?.sourceReferences[0];
        return sourceReference ? [{ rowId: error.rowId, sourceReference, originalValidationError: error }] : [];
      });
      return additions.length > 0 ? { ...draft, excludedSourceRows: [...draft.excludedSourceRows, ...additions] } : draft;
    }), activeDraft?.worksheet.worksheetName);
    setSelectedIssueIds(new Set());
  };

  const beginCorrection = (piece: RequiredPiece) => setCorrection({
    piece,
    quantity: piece.quantityText ?? `${piece.quantity}`,
    length: piece.lengthText ?? `${piece.length}`,
    profileNumber: piece.profileNumber,
    partName: piece.partName ?? '',
    finish: piece.finish ?? '',
    partNumber: piece.partNumber ?? '',
  });
  const saveCorrection = () => {
    if (!correction) return;
    const validation = validateRequiredPieceCorrection(correction.quantity, correction.length, correction.profileNumber);
    const currentRequiredPiece: RequiredPiece = {
      ...correction.piece,
      quantity: validation.quantity,
      quantityText: correction.quantity,
      length: validation.length,
      lengthText: correction.length,
      profileNumber: correction.profileNumber,
      partName: correction.partName || null,
      finish: correction.finish || null,
      partNumber: correction.partNumber || null,
      validationStatus: validation.validationStatus,
      validationMessages: validation.validationMessages,
    };
    const sourceWorksheet = correction.piece.sourceReferences[0]?.worksheetName;
    if (!sourceWorksheet) return;
    updateDraft(sourceWorksheet, (draft) => ({
      ...draft,
      partOverrides: [
        ...draft.partOverrides.filter((override) => override.rowId !== correction.piece.requiredPieceId),
        {
          rowId: correction.piece.requiredPieceId,
          importedRequiredPiece: correction.piece,
          currentRequiredPiece,
          sourceReferences: correction.piece.sourceReferences,
        },
      ],
    }));
    setCorrection(null);
  };

  const visibleIssues = review.issues.filter((issue) =>
    reviewTab === 'errors' ? issue.kind === 'error' : issue.kind === 'warning');

  return <div className="stock-import-workflow">
    <div className="stock-import-workflow__stepper" aria-label="Import workflow">
      {([['file', 'File'], ['worksheets', isCsv ? 'Source' : 'Worksheets'], ['mapping', 'Map Fields'], ['review', 'Review & Import']] as const).map(([key, label], index) => {
        const active = key === step;
        const complete = key === 'file' || (step === 'mapping' && key === 'worksheets') || (step === 'review' && (key === 'worksheets' || key === 'mapping'));
        return <div className={active ? 'stock-import-workflow__step stock-import-workflow__step--active' : complete ? 'stock-import-workflow__step stock-import-workflow__step--complete' : 'stock-import-workflow__step'} key={key}><span>{complete ? '✓' : index + 1}</span><strong>{label}</strong></div>;
      })}
    </div>
    <header className="stock-import-workflow__header"><div><h1>Import Workflow</h1><p className="section-note">{fileNameFromImportPath(session.filePath)} · {drafts.length} {isCsv ? 'source' : `Worksheet${drafts.length === 1 ? '' : 's'}`}</p><p className="section-note" role="status">{step === 'worksheets' ? `${selectedDrafts.length} selected for mapping.` : step === 'mapping' ? `${layouts.filter((layout) => layout.drafts.every((draft) => draftStatus(draft) === 'Ready')).length} of ${layouts.length} Worksheet Layouts ready.` : `${importPlan.outputEntryCount} resulting entries ready for review.`}</p></div><button className="secondary-button" disabled={busy} onClick={() => run(onReplaceFile)} type="button">Replace file</button></header>

    {step === 'worksheets' ? <section className="stock-import-workflow__layout stock-import-workflow__layout--summary">
      <main className="project-card stock-import-workflow__worksheet-panel"><div className="project-card__header"><div><h2>Select {isCsv ? 'Import Source' : 'Worksheets'}</h2><p className="section-note">Select sources and choose how Optimization Groups are created.</p></div>{!isCsv && !fieldGrouping ? <button className="secondary-button" onClick={() => publish(drafts.map((draft) => ({ ...draft, selected: true, optimizationGroupId: `import-${session.sessionId}-${draft.worksheet.originalPosition}`, optimizationGroupName: draft.worksheet.worksheetName })), activeDraft?.worksheet.worksheetName)} type="button">Create groups from Worksheet names</button> : null}</div>
        <div className="stock-import-workflow__bulk"><label className="project-field"><span>Create Optimization Groups by</span><select aria-label="Create Optimization Groups by" onChange={(event) => { if (event.target.value === 'worksheet') onUpdateSession({ ...session, stockLengthGrouping: { mode: 'worksheet', field: null, groups: [] } }); else publishFieldGrouping(selectedGroupingField); }} value={fieldGrouping ? 'mappedField' : 'worksheet'}><option value="worksheet">Worksheet</option><option value="mappedField">Mapped field</option></select></label>{fieldGrouping ? <label className="project-field"><span>Grouping Field</span><select aria-label="Grouping Field" onChange={(event) => publishFieldGrouping(event.target.value as ImportFieldName)} value={selectedGroupingField}>{groupingFields.map((field) => <option key={field} value={field}>{field}</option>)}</select></label> : null}</div>
        {!isCsv ? <div className="stock-import-workflow__worksheet-tools"><input aria-label="Search Worksheets" onChange={(event) => setWorksheetQuery(event.target.value)} placeholder="Search Worksheets…" value={worksheetQuery} /><select aria-label="Filter Worksheets by status" onChange={(event) => setWorksheetStatusFilter(event.target.value as WorksheetStatusFilter)} value={worksheetStatusFilter}><option value="all">All statuses</option><option value="selected">Selected</option><option value="ready">Ready</option><option value="attention">Needs attention</option></select><button className="secondary-button" onClick={() => publish(drafts.map((draft) => visibleDrafts.includes(draft) ? { ...draft, selected: true } : draft), activeDraft?.worksheet.worksheetName)} type="button">Select All Visible</button><button className="secondary-button" onClick={() => publish(drafts.map((draft) => ({ ...draft, selected: false })), activeDraft?.worksheet.worksheetName)} type="button">Clear Selection</button></div> : null}
        {!isCsv && !fieldGrouping ? <div className="stock-import-workflow__bulk"><label className="project-field"><span>Assign selected Worksheets to</span><select aria-label="Optimization Group for selected Worksheets" onChange={(event) => setBulkGroupId(event.target.value)} value={bulkGroupId}><option value="">Choose an Optimization Group</option>{groupChoices.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}</select></label><button className="secondary-button" disabled={!bulkGroupId} onClick={() => { const group = groupChoices.find((choice) => choice.optimizationGroupId === bulkGroupId); if (group) publish(drafts.map((draft) => draft.selected ? { ...draft, optimizationGroupId: group.optimizationGroupId, optimizationGroupName: group.name, stockLength: group.stockLength } : draft)); }} type="button">Assign to group</button></div> : null}
        <div aria-label="Worksheet selection table" className="table-wrap stock-import-workflow__worksheet-table" role="region"><table><thead><tr><th>Select</th><th>{isCsv ? 'Source' : 'Worksheet'}</th><th>Used rows</th><th>Heading Range</th><th>Optimization Group</th><th>Stock Length (in)</th><th>Status</th></tr></thead><tbody>{visibleDrafts.map((draft) => <tr key={`${draft.worksheet.originalPosition}-${draft.worksheet.worksheetName}`}>
          <td><input aria-label={`Select ${draft.worksheet.worksheetName}`} checked={draft.selected} disabled={isCsv} onChange={(event) => publish(setWorkbookWorksheetSelected(drafts, draft.worksheet.worksheetName, event.target.checked), draft.worksheet.worksheetName)} type="checkbox" /></td>
          <td><strong>{draft.worksheet.worksheetName}</strong></td><td>{draft.worksheet.usedRowCount ?? '—'}</td><td>{draft.selected ? draft.headingRange || 'Not set' : '—'}</td>
          <td>{fieldGrouping ? 'Derived from mapped values' : draft.selected ? <select aria-label={`Optimization Group for ${draft.worksheet.worksheetName}`} onChange={(event) => { const group = groupChoices.find((choice) => choice.optimizationGroupId === event.target.value); updateDraft(draft.worksheet.worksheetName, (current) => ({ ...current, optimizationGroupId: group?.optimizationGroupId ?? '', optimizationGroupName: group?.name ?? '', stockLength: group?.stockLength ?? current.stockLength })); }} value={draft.optimizationGroupId}><option value="">Choose a group</option>{groupChoices.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}</select> : '—'}</td>
          <td>{fieldGrouping ? 'Set after mapping' : draft.selected ? <input aria-label={`Stock Length for ${draft.worksheet.worksheetName}`} inputMode="decimal" onChange={(event) => updateDraft(draft.worksheet.worksheetName, (current) => ({ ...current, stockLength: Number(event.target.value) || null }))} value={draft.stockLength ?? ''} /> : '—'}</td>
          <td><span className={setupStatusFor(draft) === 'Ready for mapping' ? 'status-pill status-pill--ready' : 'status-pill'}>{setupStatusFor(draft)}</span></td>
        </tr>)}</tbody></table></div>
      </main>
      <aside className="project-card stock-import-workflow__summary"><h2>Import Source Summary</h2><dl><div><dt>{isCsv ? 'Sources' : 'Worksheets'}</dt><dd>{drafts.length}</dd></div><div><dt>Selected</dt><dd>{selectedDrafts.length}</dd></div><div><dt>Used rows</dt><dd>{selectedDrafts.reduce((total, draft) => total + (draft.worksheet.usedRowCount ?? 0), 0)}</dd></div><div><dt>File type</dt><dd>{isCsv ? 'CSV' : session.filePath.toLocaleLowerCase().endsWith('.xlsm') ? 'Macro-enabled Workbook' : 'Excel Workbook'}</dd></div></dl><h3>Tips</h3><p className="section-note">{fieldGrouping ? `Matching ${selectedGroupingField} values across selected Worksheets will share an Optimization Group.` : 'Each selected Worksheet belongs to one Optimization Group.'}</p></aside>
    </section> : null}

    {step === 'mapping' && activeDraft ? <section className="stock-import-workflow__mapping-layout">
      <aside className="project-card stock-import-workflow__worksheet-nav"><h2>{isCsv ? 'Source' : 'Worksheet Layouts'}</h2>{layouts.map((layout, index) => <div className="stock-import-workflow__layout-group" key={layout.layoutId}><strong>Layout {index + 1}</strong><span>{layout.worksheetNames.length} Worksheet{layout.worksheetNames.length === 1 ? '' : 's'} · {layout.drafts.every((draft) => draftStatusFor(draft) === 'Ready') ? 'Ready' : 'Needs attention'}</span>{layout.drafts.map((draft) => <button className={draft.worksheet.worksheetName === activeDraft.worksheet.worksheetName ? 'stock-import-workflow__worksheet-button stock-import-workflow__worksheet-button--active' : 'stock-import-workflow__worksheet-button'} key={draft.worksheet.worksheetName} onClick={() => activate(draft.worksheet.worksheetName)} type="button"><strong>{draft.worksheet.worksheetName}</strong><span>{draftStatusFor(draft)}</span></button>)}</div>)}</aside>
      <main className="project-card stock-import-workflow__mapping"><div className="stock-import-workflow__mapping-controls"><div className="project-card__header"><div><h2>Map Fields for {activeLayout ? `Layout ${layouts.indexOf(activeLayout) + 1}` : activeDraft.worksheet.worksheetName}</h2><p className="section-note">This mapping applies to {activeLayout?.worksheetNames.join(', ') ?? activeDraft.worksheet.worksheetName}.</p></div><button className="secondary-button" disabled={busy || !activeDraft.headingRangeConfirmed} onClick={() => run(() => previewActiveLayout(session))} type="button">Auto-map fields</button></div>
        <div className="stock-import-workflow__heading-controls"><label className="project-field"><span>Heading Range</span><input aria-label={`Heading Range for ${activeDraft.worksheet.worksheetName}`} onBlur={(event) => { const result = confirmWorksheetHeadingRange(drafts, activeDraft.worksheet.worksheetName, event.target.value); if (result.error) { publish(result.drafts, activeDraft.worksheet.worksheetName); setMappingMessage(result.error); } else { const next = publish(result.drafts, activeDraft.worksheet.worksheetName); setMappingMessage('Updating preview and mappings…'); run(() => previewActiveLayout(next)); } }} onChange={(event) => updateDraft(activeDraft.worksheet.worksheetName, (draft) => ({ ...draft, headingRange: event.target.value.toUpperCase(), headingRangeConfirmed: false, hasPendingChanges: true }))} value={activeDraft.headingRange} /></label><button className="secondary-button" onClick={() => { const result = copyColumnMappingsFromPreviousSelectedWorksheet(drafts, activeDraft.worksheet.worksheetName); const next = publish(result.drafts, activeDraft.worksheet.worksheetName); setMappingMessage(result.error ?? 'Column Mappings copied. Updating preview…'); if (!result.error) run(() => previewActiveLayout(next)); }} type="button">Copy previous mappings</button></div>
        {mappingMessage ? <p className="section-note" role="status">{mappingMessage}</p> : null}</div>
        <div className="stock-import-workflow__field-table"><div className="stock-import-workflow__field-head"><span>Application field</span><span>Required</span><span>Source</span><span>Sample values</span><span>Detected type</span><span>Status</span></div>
          {stockLengthApplicationFields.map((targetField) => {
            const selected = activeDraft.options.columnMappings.find((mapping) => mapping.targetField === targetField)?.sourceColumn ?? activeDraft.preview.columnMappings.find((mapping) => mapping.targetField === targetField)?.sourceColumn ?? '';
            const required = requiredStockLengthFields.includes(targetField as typeof requiredStockLengthFields[number]) ||
              (fieldGrouping && targetField === selectedGroupingField);
            const samples = mappedSampleValues(activeDraft, targetField);
            return <label className="stock-import-workflow__field-row" key={targetField}><strong>{targetField}</strong><span>{required ? '●' : '—'}</span><select aria-label={`${activeDraft.worksheet.worksheetName} column for ${targetField}`} onChange={(event) => { const mappings = [...activeDraft.options.columnMappings.filter((mapping) => mapping.targetField !== targetField), ...(event.target.value ? [{ targetField, sourceColumn: event.target.value }] : [])]; const nextDrafts = activeLayout ? applyWorksheetLayoutMappings(drafts, activeLayout.layoutId, mappings) : drafts; const next = publish(nextDrafts, activeDraft.worksheet.worksheetName); if (canReviewStockLengthImport(nextDrafts)) { setMappingMessage('Updating Imported Preview…'); run(() => previewActiveLayout(next)); } }} value={selected}><option value="">Not mapped</option>{sourceColumns(activeDraft).map((column) => <option key={column.address} value={column.address}>{column.address} — {column.heading}</option>)}</select><span>{samples.join(', ') || '—'}</span><span>{selected ? detectedFieldType(targetField) : '—'}</span><span className={selected ? 'stock-import-workflow__matched' : required ? 'stock-import-workflow__attention' : ''}>{selected ? 'Matched' : required ? 'Needs mapping' : 'Not mapped'}</span></label>;
          })}
        </div>
      </main>
      <aside className="project-card stock-import-workflow__source-preview"><div className="stock-import-workflow__tabs" role="tablist"><button aria-selected={mappingPreviewTab === 'source'} className={mappingPreviewTab === 'source' ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} onClick={() => setMappingPreviewTab('source')} role="tab" type="button">Source Worksheet</button><button aria-selected={mappingPreviewTab === 'imported'} className={mappingPreviewTab === 'imported' ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} onClick={() => setMappingPreviewTab('imported')} role="tab" type="button">Imported Preview</button></div>{mappingPreviewTab === 'source' ? <><p className="section-note">Select the first and last cell of the Heading Range, or enter its address.</p><div className="worksheet-preview" role="region" aria-label={`${activeDraft.worksheet.worksheetName} cell preview`}><table><thead><tr><th aria-label="Row number" />{(activeDraft.worksheet.previewRows?.[0]?.cells ?? []).map((cell) => <th key={cell.address.replace(/[0-9]+$/, '')} scope="col">{cell.address.replace(/[0-9]+$/, '')}</th>)}</tr></thead><tbody>{(activeDraft.worksheet.previewRows ?? []).map((row) => <tr key={row.rowNumber}><th scope="row">{row.rowNumber}</th>{row.cells.map((cell) => { const headingRow = activeDraft.headingRange.match(/^[A-Z]+(\d+):[A-Z]+\d+$/)?.[1]; const selectedRange = new Set(worksheetSourceColumns(activeDraft).map((column) => `${column.address}${headingRow ?? ''}`)); return <td key={cell.address}><button className={selectedRange.has(cell.address) ? 'worksheet-preview__cell worksheet-preview__cell--heading' : 'worksheet-preview__cell'} onClick={() => selectPreviewCell(cell.address)} type="button">{cell.value || ' '}</button></td>; })}</tr>)}</tbody></table></div></> : <div className="table-wrap worksheet-preview"><table><thead><tr><th>Qty</th><th>Length</th><th>Profile Number</th><th>Part Name</th><th>Finish</th><th>Part Number</th></tr></thead><tbody>{(activeDraft.preview.requiredPieces ?? []).slice(0, 25).map((piece) => <tr key={piece.requiredPieceId}><td>{piece.quantity}</td><td>{piece.lengthText ?? piece.length}</td><td>{piece.profileNumber}</td><td>{piece.partName ?? '—'}</td><td>{piece.finish ?? '—'}</td><td>{piece.partNumber ?? '—'}</td></tr>)}</tbody></table></div>}</aside>
    </section> : null}

    {step === 'review' ? <section className="stock-import-workflow__review-layout">
      <aside className="project-card stock-import-workflow__review-summary"><h2>Import Summary</h2><dl><div><dt>Worksheets</dt><dd>{review.selectedWorksheets.length}</dd></div><div><dt>Source rows</dt><dd>{importPlan.sourceRowCount}</dd></div><div><dt>Valid source rows</dt><dd>{importPlan.validSourceRowCount}</dd></div><div><dt>Required Piece Entries</dt><dd>{importPlan.outputEntryCount}</dd></div><div><dt>Total piece quantity</dt><dd>{importPlan.totalPieceQuantity}</dd></div><div><dt>Optimization Groups</dt><dd>{fieldGrouping ? fieldGroupConfigurations.length : new Set(selectedDrafts.map((draft) => draft.optimizationGroupId)).size}</dd></div><div><dt>Skipped source rows</dt><dd>{importPlan.skippedSourceRowCount}</dd></div><div><dt>Errors</dt><dd>{review.unresolvedErrors.length}</dd></div><div><dt>Warnings</dt><dd>{review.warnings.length}</dd></div></dl></aside>
      <main className="project-card stock-import-workflow__review"><h2>Review & Validate</h2><p><strong>{importPlan.validSourceRowCount} valid source rows will produce {importPlan.outputEntryCount} required-piece entries.</strong></p><p className="section-note">{importPlan.aggregationRule}</p><div className="stock-import-workflow__tabs" role="tablist">{([['resulting', `Resulting Entries (${importPlan.outputEntryCount})`], ['source', `Source Rows (${importPlan.sourceRowCount})`], ['errors', `Errors (${review.unresolvedErrors.length})`], ['warnings', `Warnings (${review.warnings.length})`]] as const).map(([tab, label]) => <button aria-selected={reviewTab === tab} className={reviewTab === tab ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} key={tab} onClick={() => setReviewTab(tab)} role="tab" type="button">{label}</button>)}</div>
        {fieldGrouping ? <section><h3>Optimization Groups from {selectedGroupingField}</h3><p className="section-note">Define the regular Stock Length for every resulting Optimization Group.</p><div className="table-wrap"><table><thead><tr><th>Grouping value</th><th>Optimization Group</th><th>Piece quantity</th><th>Stock Length (in)</th></tr></thead><tbody>{fieldGroupConfigurations.map((configuration) => <tr key={normalizedGroupingValue(configuration.groupingValue)}><td>{configuration.groupingValue || `Unspecified ${selectedGroupingField}`}</td><td>{configuration.name}</td><td>{configuration.pieceCount}</td><td><input aria-label={`Stock Length for ${configuration.name}`} inputMode="decimal" min="0" onChange={(event) => publishFieldGrouping(selectedGroupingField, fieldGroupConfigurations.map((candidate) => normalizedGroupingValue(candidate.groupingValue) === normalizedGroupingValue(configuration.groupingValue) ? { ...candidate, stockLength: Number(event.target.value) || null } : candidate))} required type="number" value={configuration.stockLength ?? ''} /></td></tr>)}</tbody></table></div></section> : null}
        {reviewTab === 'resulting' || reviewTab === 'source' ? <div className="table-wrap"><table><thead><tr><th>Source</th><th>Source rows</th><th>Quantity</th><th>Length</th><th>Profile Number</th><th>Status</th></tr></thead><tbody>{(reviewTab === 'resulting' ? importPlan.resultingEntries : importPlan.sourceRows).map((piece) => { const worksheetNames = [...new Set(piece.sourceReferences.map((reference) => reference.worksheetName))]; return <tr key={piece.requiredPieceId}><td>{worksheetNames.join(', ') || '—'}</td><td>{piece.sourceReferences.length}</td><td>{piece.quantityText ?? piece.quantity}</td><td>{piece.lengthText ?? piece.length}</td><td>{piece.profileNumber}</td><td>{piece.validationStatus ?? 'valid'}</td></tr>; })}</tbody></table></div> : <div className="table-wrap"><table><thead><tr><th>Select</th><th>Worksheet</th><th>Row</th><th>Issue</th><th>Action</th></tr></thead><tbody>{visibleIssues.map(({ worksheetName, issue, kind }) => {
          const issueId = issue.rowId ?? `${worksheetName}:${issue.code}:${issue.message}`;
          const piece = drafts.find((draft) => draft.worksheet.worksheetName === worksheetName)?.preview.requiredPieces?.find((item) => item.requiredPieceId === issue.rowId);
          return <tr key={issueId}><td>{kind === 'error' && issue.rowId ? <input aria-label={`Select ${issueId} for exclusion`} checked={selectedIssueIds.has(issueId)} onChange={() => setSelectedIssueIds((current) => { const next = new Set(current); if (next.has(issueId)) next.delete(issueId); else next.add(issueId); return next; })} type="checkbox" /> : null}</td><td>{worksheetName}</td><td>{issue.location?.physicalRow ?? piece?.sourceReferences[0]?.physicalRow ?? '—'}</td><td>{issue.message}</td><td><div className="table-actions"><button className="secondary-button" onClick={() => { activate(worksheetName); setStep('mapping'); }} type="button">View source</button>{piece && kind === 'error' ? <button className="secondary-button" onClick={() => beginCorrection(piece)} type="button">Correct</button> : null}{issue.rowId && kind === 'error' ? <button className="danger-button" onClick={() => excludeIssueRows([issue.rowId!])} type="button">Exclude</button> : null}</div></td></tr>;
        })}</tbody></table></div>}
        {reviewTab === 'errors' && selectedIssueIds.size > 0 ? <button className="danger-button" onClick={() => excludeIssueRows([...selectedIssueIds])} type="button">Exclude selected ({selectedIssueIds.size})</button> : null}
        <div className="stock-import-workflow__behavior"><h3>Import behavior</h3><p>Valid Required Pieces will be imported. Source rows with errors must be corrected or explicitly excluded.</p></div>
      </main>
    </section> : null}

    {correction ? <div className="stock-length-import__drawer-layer"><button aria-label="Dismiss correction" className="stock-length-import__drawer-backdrop" onClick={() => setCorrection(null)} type="button" /><aside aria-label="Correct source row" aria-modal="true" className="stock-length-import__piece-drawer" role="dialog"><div className="project-card__header"><h2>Correct source row {correction.piece.sourceReferences[0]?.physicalRow}</h2><button className="secondary-button" onClick={() => setCorrection(null)} type="button">Close</button></div><div className="project-form-grid stock-length-import__piece-form">{(['quantity', 'length', 'profileNumber', 'partName', 'finish', 'partNumber'] as const).map((field) => { const label = { quantity: 'Quantity', length: 'Length', profileNumber: 'Profile Number', partName: 'Part Name', finish: 'Finish', partNumber: 'Part Number' }[field]; return <label className="project-field" key={field}><span>{label}</span><input aria-label={`Corrected ${label}`} onChange={(event) => setCorrection((current) => current ? { ...current, [field]: event.target.value } : current)} value={correction[field]} /></label>; })}</div><div className="form-actions"><button className="primary-button" onClick={saveCorrection} type="button">Save correction</button><button className="secondary-button" onClick={() => setCorrection(null)} type="button">Cancel</button></div></aside></div> : null}

    <footer className="stock-import-workflow__footer"><button className="secondary-button" disabled={busy} onClick={() => run(onCancel)} type="button">Cancel Import</button><div className="stock-import-workflow__footer-status">{step === 'worksheets' ? `${selectedDrafts.length} selected` : step === 'mapping' ? `${layouts.filter((layout) => layout.drafts.every((draft) => draftStatusFor(draft) === 'Ready')).length} of ${layouts.length} layouts ready` : `${importPlan.outputEntryCount} entries · ${review.unresolvedErrors.length} errors`}</div><div className="form-actions">{step !== 'worksheets' ? <button className="secondary-button" disabled={busy} onClick={() => setStep(step === 'review' ? 'mapping' : 'worksheets')} type="button">Back</button> : null}{step === 'worksheets' ? <button className="primary-button" disabled={busy || selectedDrafts.length === 0 || (!fieldGrouping && selectedDrafts.some((draft) => !draft.optimizationGroupId || !draft.stockLength || draft.stockLength <= 0))} onClick={() => setStep('mapping')} type="button">Continue to Map Fields →</button> : step === 'mapping' ? <button className="primary-button" disabled={busy || !canReview} onClick={() => { if (fieldGrouping) publishFieldGrouping(selectedGroupingField, fieldGroupConfigurations); setStep('review'); }} type="button">Review {importPlan.outputEntryCount} Entries →</button> : <button className="primary-button" disabled={busy || !canFinalize} onClick={() => run(onFinalize)} type="button">Import {importPlan.outputEntryCount} Required Piece Entries</button>}</div></footer>
  </div>;
}
