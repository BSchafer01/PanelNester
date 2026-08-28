import { useEffect, useMemo, useState } from 'react';
import type {
  ImportFieldName,
  ImportMappingSession,
  ImportMaterialMapping,
  ImportNewMaterialRequest,
  ImportWorksheetDraft,
  Material,
  MaterialDraft,
  OptimizationGroup,
  PartRow,
} from '../types/contracts';
import {
  applyWorksheetLayoutMappings,
  buildWorksheetLayouts,
  confirmWorksheetHeadingRange,
  copyColumnMappingsFromPreviousSelectedWorksheet,
  editInvalidSourceRow,
  excludeSourceRows,
  headingRangeFromPreviewCells,
  ignoreWorkbookMaterial,
  setWorkbookWorksheetSelected,
  synchronizeWorkbookMaterialResolution,
  worksheetSourceColumns,
} from './workbookImportDraftState';
import {
  buildSheetImportPlan,
  canReviewSheetImport,
  createMaterialDraft,
  hasRequiredSheetMappings,
  isSheetMaterialResolved,
  requiredSheetImportFields,
  sheetImportFields,
  sheetMaterialLabels,
  validateMaterialDraft,
} from './sheetImportPresentation';
import { fileNameFromImportPath } from './requiredPiecesPresentation';

type Step = 'worksheets' | 'mapping' | 'review';
type ReviewTab = 'resulting' | 'source' | 'errors' | 'warnings';
type WorksheetFilter = 'all' | 'selected' | 'ready' | 'attention';

interface SheetCorrection {
  row: PartRow;
  importedId: string;
  length: string;
  width: string;
  quantity: string;
  materialName: string;
  group: string;
  sheetNumber: string;
  rowNumber: string;
  columnNumber: string;
}

export interface SheetProjectImportWorkflowProps {
  session: ImportMappingSession;
  groups: OptimizationGroup[];
  materials: Material[];
  busy: boolean;
  onReplaceFile: () => void | Promise<void>;
  onUpdateSession: (session: ImportMappingSession) => void;
  onPreview: (session?: ImportMappingSession, worksheetNames?: string[]) => void | Promise<void>;
  onFinalize: () => void | Promise<void>;
  onCancel: () => void | Promise<void>;
}

function sourceColumns(draft: ImportWorksheetDraft) {
  const columns = worksheetSourceColumns(draft);
  return columns.length > 0
    ? columns
    : draft.preview.availableColumns.map((address) => ({ address, heading: address }));
}

function sampleValues(draft: ImportWorksheetDraft, field: ImportFieldName): string[] {
  const values = draft.preview.parts.flatMap((part) => {
    const value = field === 'Id' ? part.importedId
      : field === 'Length' ? part.lengthText ?? `${part.length}`
      : field === 'Width' ? part.widthText ?? `${part.width}`
      : field === 'Quantity' ? part.quantityText ?? `${part.quantity}`
      : field === 'Material' ? part.materialName
      : field === 'Group' ? part.group
      : field === 'Sheet Number' ? part.sheetNumber
      : field === 'Row Number' ? part.rowNumber
      : field === 'Column Number' ? part.columnNumber
      : null;
    return value == null || `${value}`.trim() === '' ? [] : [`${value}`];
  });
  return [...new Set(values)].slice(0, 2);
}

function fieldType(field: ImportFieldName): string {
  if (field === 'Quantity' || field === 'Row Number' || field === 'Column Number') return 'Integer';
  if (field === 'Length' || field === 'Width') return 'Length';
  return 'Text';
}

function setupStatus(draft: ImportWorksheetDraft): string {
  if (!draft.selected) return 'Not selected';
  if (!draft.optimizationGroupId) return 'Needs setup';
  return 'Ready for mapping';
}

function draftStatus(draft: ImportWorksheetDraft, allDrafts: ImportWorksheetDraft[]): string {
  if (!draft.selected) return 'Not selected';
  if (!draft.optimizationGroupId) return 'Needs setup';
  if (!draft.headingRangeConfirmed) return 'Needs Heading Range';
  if (!hasRequiredSheetMappings(draft)) return 'Needs mapping';
  if (draft.hasPendingChanges) return 'Updating preview';
  const unresolved = sheetMaterialLabels(allDrafts).some((label) =>
    !isSheetMaterialResolved(allDrafts, label));
  return unresolved ? 'Needs materials' : 'Ready';
}

function materialDraftEntry(sourceMaterialName: string): ImportNewMaterialRequest {
  return { sourceMaterialName, material: createMaterialDraft(sourceMaterialName) };
}

function correctionFor(row: PartRow): SheetCorrection {
  return {
    row,
    importedId: row.importedId,
    length: row.lengthText ?? `${row.length}`,
    width: row.widthText ?? `${row.width}`,
    quantity: row.quantityText ?? `${row.quantity}`,
    materialName: row.materialName,
    group: row.group ?? '',
    sheetNumber: row.sheetNumber ?? '',
    rowNumber: row.rowNumber == null ? '' : `${row.rowNumber}`,
    columnNumber: row.columnNumber == null ? '' : `${row.columnNumber}`,
  };
}

function correctedRow(correction: SheetCorrection): PartRow | null {
  const length = Number(correction.length);
  const width = Number(correction.width);
  const quantity = Number(correction.quantity);
  const rowNumber = correction.rowNumber ? Number(correction.rowNumber) : null;
  const columnNumber = correction.columnNumber ? Number(correction.columnNumber) : null;
  if (!correction.importedId.trim() || !correction.materialName.trim() ||
      !Number.isFinite(length) || length <= 0 || !Number.isFinite(width) || width <= 0 ||
      !Number.isSafeInteger(quantity) || quantity <= 0 ||
      (rowNumber != null && (!Number.isSafeInteger(rowNumber) || rowNumber <= 0)) ||
      (columnNumber != null && (!Number.isSafeInteger(columnNumber) || columnNumber <= 0))) return null;
  return {
    ...correction.row,
    importedId: correction.importedId.trim(),
    length,
    lengthText: correction.length,
    width,
    widthText: correction.width,
    quantity,
    quantityText: correction.quantity,
    materialName: correction.materialName.trim(),
    group: correction.group.trim() || null,
    sheetNumber: correction.sheetNumber.trim() || null,
    rowNumber,
    columnNumber,
    validationStatus: 'valid',
    validationMessages: [],
  };
}

export function SheetProjectImportWorkflow({
  session, groups, materials, busy, onReplaceFile, onUpdateSession,
  onPreview, onFinalize, onCancel,
}: SheetProjectImportWorkflowProps) {
  const [step, setStep] = useState<Step>('worksheets');
  const [bulkGroupId, setBulkGroupId] = useState('');
  const [rangeSelection, setRangeSelection] = useState<{ worksheet: string; firstAddress: string } | null>(null);
  const [mappingMessage, setMappingMessage] = useState('');
  const [reviewTab, setReviewTab] = useState<ReviewTab>('resulting');
  const [worksheetQuery, setWorksheetQuery] = useState('');
  const [worksheetFilter, setWorksheetFilter] = useState<WorksheetFilter>('all');
  const [mappingPreviewTab, setMappingPreviewTab] = useState<'source' | 'imported'>('source');
  const [selectedIssueIds, setSelectedIssueIds] = useState<Set<string>>(new Set());
  const [correction, setCorrection] = useState<SheetCorrection | null>(null);

  useEffect(() => {
    setStep('worksheets');
    setRangeSelection(null);
    setReviewTab('resulting');
    setSelectedIssueIds(new Set());
  }, [session.sessionId]);

  const drafts = session.worksheets ?? [];
  const selectedDrafts = drafts.filter((draft) => draft.selected);
  const activeDraft = drafts.find((draft) => draft.worksheet.worksheetName === session.activeWorksheetName)
    ?? selectedDrafts[0] ?? drafts[0];
  const isCsv = session.filePath.toLocaleLowerCase().endsWith('.csv');
  const layouts = useMemo(() => buildWorksheetLayouts(drafts), [drafts]);
  const activeLayout = layouts.find((layout) =>
    layout.worksheetNames.includes(activeDraft?.worksheet.worksheetName ?? ''));
  const plan = useMemo(() => buildSheetImportPlan(drafts), [drafts]);
  const materialLabels = useMemo(() => sheetMaterialLabels(drafts), [drafts]);
  const canReview = canReviewSheetImport(drafts);
  const canFinalize = canReview && plan.unresolvedErrors.length === 0;
  const visibleDrafts = useMemo(() => drafts
    .filter((draft) => draft.worksheet.worksheetName.toLocaleLowerCase()
      .includes(worksheetQuery.trim().toLocaleLowerCase()))
    .filter((draft) => worksheetFilter === 'all' ||
      (worksheetFilter === 'selected' && draft.selected) ||
      (worksheetFilter === 'ready' && setupStatus(draft) === 'Ready for mapping') ||
      (worksheetFilter === 'attention' && draft.selected && setupStatus(draft) !== 'Ready for mapping'))
    .sort((left, right) => Number(right.selected) - Number(left.selected) ||
      left.worksheet.originalPosition - right.worksheet.originalPosition),
  [drafts, worksheetFilter, worksheetQuery]);
  const groupChoices = useMemo(() => {
    const choices = new Map<string, Pick<OptimizationGroup, 'optimizationGroupId' | 'name'>>();
    for (const group of groups) choices.set(group.optimizationGroupId, group);
    for (const draft of drafts) choices.set(draft.optimizationGroupId, {
      optimizationGroupId: draft.optimizationGroupId,
      name: draft.optimizationGroupName,
    });
    return [...choices.values()].filter((group) => group.optimizationGroupId && group.name);
  }, [drafts, groups]);
  const run = (action: () => void | Promise<void>) => void Promise.resolve(action()).catch(() => undefined);

  const buildSession = (nextDrafts: ImportWorksheetDraft[], activeName?: string): ImportMappingSession => {
    const active = nextDrafts.find((draft) => draft.worksheet.worksheetName === activeName)
      ?? nextDrafts.find((draft) => draft.selected) ?? nextDrafts[0];
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
  const updateDraft = (worksheetName: string, update: (draft: ImportWorksheetDraft) => ImportWorksheetDraft) =>
    publish(drafts.map((draft) => draft.worksheet.worksheetName === worksheetName ? update(draft) : draft), worksheetName);
  const activate = (worksheetName: string) => publish(drafts, worksheetName);
  const previewNames = (next: ImportMappingSession, worksheetNames: string[]) =>
    onPreview(next, worksheetNames.filter((name) =>
      next.worksheets?.some((draft) => draft.selected && draft.worksheet.worksheetName === name)));
  const previewLayout = (next: ImportMappingSession) => {
    const layout = buildWorksheetLayouts(next.worksheets ?? []).find((candidate) =>
      candidate.worksheetNames.includes(next.activeWorksheetName ?? ''));
    return previewNames(next, (layout?.drafts ?? []).filter((draft) =>
      draft.selected && draft.headingRangeConfirmed).map((draft) => draft.worksheet.worksheetName));
  };
  const previewAll = (next: ImportMappingSession) => previewNames(next,
    (next.worksheets ?? []).filter((draft) => draft.selected && draft.headingRangeConfirmed)
      .map((draft) => draft.worksheet.worksheetName));

  const chooseHeadingCell = (address: string) => {
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
    const next = publish(result.drafts, worksheetName);
    setMappingMessage(result.error ?? `Heading Range set to ${range}. Updating preview and mappings…`);
    if (!result.error) run(() => previewLayout(next));
  };

  const updateMaterial = (
    sourceMaterialName: string,
    mapping?: ImportMaterialMapping,
    newMaterial?: ImportNewMaterialRequest,
  ) => {
    const next = publish(synchronizeWorkbookMaterialResolution(
      drafts, sourceMaterialName, mapping, newMaterial), activeDraft?.worksheet.worksheetName);
    run(() => previewAll(next));
  };
  const ignoreMaterial = (sourceMaterialName: string) => {
    const next = publish(ignoreWorkbookMaterial(drafts, sourceMaterialName), activeDraft?.worksheet.worksheetName);
    run(() => previewAll(next));
  };
  const editMaterialDraft = (sourceMaterialName: string, material: MaterialDraft) =>
    publish(synchronizeWorkbookMaterialResolution(
      drafts, sourceMaterialName, undefined, { sourceMaterialName, material }),
    activeDraft?.worksheet.worksheetName);

  const saveCorrection = () => {
    if (!correction) return;
    const row = correctedRow(correction);
    const worksheetName = correction.row.sourceReferences?.[0]?.worksheetName;
    if (!row || !worksheetName) return;
    updateDraft(worksheetName, (draft) => editInvalidSourceRow(draft, row.rowId, row));
    setCorrection(null);
  };
  const excludeIssues = (rowIds: string[]) => {
    const ids = new Set(rowIds);
    publish(drafts.map((draft) => excludeSourceRows(draft,
      draft.preview.parts.filter((part) => ids.has(part.rowId)).map((part) => part.rowId))),
    activeDraft?.worksheet.worksheetName);
    setSelectedIssueIds(new Set());
  };
  const visibleIssues = plan.issues.filter((issue) =>
    reviewTab === 'errors' ? issue.kind === 'error' : issue.kind === 'warning');

  return <div className="stock-import-workflow project-import-workflow">
    <div className="stock-import-workflow__stepper" aria-label="Import workflow">
      {([['file', 'File'], ['worksheets', isCsv ? 'Source' : 'Worksheets'], ['mapping', 'Map Fields'], ['review', 'Review & Import']] as const).map(([key, label], index) => {
        const active = key === step;
        const complete = key === 'file' || (step === 'mapping' && key === 'worksheets') ||
          (step === 'review' && (key === 'worksheets' || key === 'mapping'));
        return <div className={active ? 'stock-import-workflow__step stock-import-workflow__step--active' : complete ? 'stock-import-workflow__step stock-import-workflow__step--complete' : 'stock-import-workflow__step'} key={key}><span>{complete ? '✓' : index + 1}</span><strong>{label}</strong></div>;
      })}
    </div>
    <header className="stock-import-workflow__header"><div><h1>Import Workflow</h1><p className="section-note">{fileNameFromImportPath(session.filePath)} · {drafts.length} {isCsv ? 'source' : `Worksheet${drafts.length === 1 ? '' : 's'}`}</p><p className="section-note" role="status">{step === 'worksheets' ? `${selectedDrafts.length} selected for mapping.` : step === 'mapping' ? `${layouts.filter((layout) => layout.drafts.every((draft) => draftStatus(draft, drafts) === 'Ready')).length} of ${layouts.length} Worksheet Layouts ready.` : `${plan.outputEntryCount} resulting entries ready for review.`}</p></div><button className="secondary-button" disabled={busy} onClick={() => run(onReplaceFile)} type="button">Replace file</button></header>

    {step === 'worksheets' ? <section className="stock-import-workflow__layout stock-import-workflow__layout--summary">
      <main className="project-card stock-import-workflow__worksheet-panel"><div className="project-card__header"><div><h2>Select {isCsv ? 'Import Source' : 'Worksheets'}</h2><p className="section-note">Select sources and assign Optimization Groups.</p></div>{!isCsv ? <button className="secondary-button" onClick={() => publish(drafts.map((draft) => ({ ...draft, selected: true, optimizationGroupId: `import-${session.sessionId}-${draft.worksheet.originalPosition}`, optimizationGroupName: draft.worksheet.worksheetName })), activeDraft?.worksheet.worksheetName)} type="button">Create groups from Worksheet names</button> : null}</div>
        {!isCsv ? <div className="stock-import-workflow__worksheet-tools"><input aria-label="Search Worksheets" onChange={(event) => setWorksheetQuery(event.target.value)} placeholder="Search Worksheets…" value={worksheetQuery} /><select aria-label="Filter Worksheets by status" onChange={(event) => setWorksheetFilter(event.target.value as WorksheetFilter)} value={worksheetFilter}><option value="all">All statuses</option><option value="selected">Selected</option><option value="ready">Ready</option><option value="attention">Needs attention</option></select><button className="secondary-button" onClick={() => publish(drafts.map((draft) => visibleDrafts.includes(draft) ? { ...draft, selected: true } : draft), activeDraft?.worksheet.worksheetName)} type="button">Select All Visible</button><button className="secondary-button" onClick={() => publish(drafts.map((draft) => ({ ...draft, selected: false })), activeDraft?.worksheet.worksheetName)} type="button">Clear Selection</button></div> : null}
        {!isCsv ? <div className="stock-import-workflow__bulk"><label className="project-field"><span>Assign selected Worksheets to</span><select aria-label="Optimization Group for selected Worksheets" onChange={(event) => setBulkGroupId(event.target.value)} value={bulkGroupId}><option value="">Choose an Optimization Group</option>{groupChoices.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}</select></label><button className="secondary-button" disabled={!bulkGroupId} onClick={() => { const group = groupChoices.find((choice) => choice.optimizationGroupId === bulkGroupId); if (group) publish(drafts.map((draft) => draft.selected ? { ...draft, optimizationGroupId: group.optimizationGroupId, optimizationGroupName: group.name } : draft)); }} type="button">Assign to group</button></div> : null}
        <div aria-label="Worksheet selection table" className="table-wrap stock-import-workflow__worksheet-table" role="region"><table><thead><tr><th>Select</th><th>{isCsv ? 'Source' : 'Worksheet'}</th><th>Used rows</th><th>Heading Range</th><th>Optimization Group</th><th>Status</th></tr></thead><tbody>{visibleDrafts.map((draft) => <tr key={`${draft.worksheet.originalPosition}-${draft.worksheet.worksheetName}`}><td><input aria-label={`Select ${draft.worksheet.worksheetName}`} checked={draft.selected} disabled={isCsv} onChange={(event) => publish(setWorkbookWorksheetSelected(drafts, draft.worksheet.worksheetName, event.target.checked), draft.worksheet.worksheetName)} type="checkbox" /></td><td><strong>{draft.worksheet.worksheetName}</strong></td><td>{draft.worksheet.usedRowCount ?? '—'}</td><td>{draft.selected ? draft.headingRange || 'Not set' : '—'}</td><td>{draft.selected ? <select aria-label={`Optimization Group for ${draft.worksheet.worksheetName}`} onChange={(event) => { const group = groupChoices.find((choice) => choice.optimizationGroupId === event.target.value); updateDraft(draft.worksheet.worksheetName, (current) => ({ ...current, optimizationGroupId: group?.optimizationGroupId ?? '', optimizationGroupName: group?.name ?? '' })); }} value={draft.optimizationGroupId}><option value="">Choose a group</option>{groupChoices.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}</select> : '—'}</td><td><span className={setupStatus(draft) === 'Ready for mapping' ? 'status-pill status-pill--ready' : 'status-pill'}>{setupStatus(draft)}</span></td></tr>)}</tbody></table></div>
      </main>
      <aside className="project-card stock-import-workflow__summary"><h2>Import Source Summary</h2><dl><div><dt>{isCsv ? 'Sources' : 'Worksheets'}</dt><dd>{drafts.length}</dd></div><div><dt>Selected</dt><dd>{selectedDrafts.length}</dd></div><div><dt>Used rows</dt><dd>{selectedDrafts.reduce((total, draft) => total + (draft.worksheet.usedRowCount ?? 0), 0)}</dd></div><div><dt>File type</dt><dd>{isCsv ? 'CSV' : session.filePath.toLocaleLowerCase().endsWith('.xlsm') ? 'Macro-enabled Workbook' : 'Excel Workbook'}</dd></div></dl><h3>Tips</h3><p className="section-note">Each selected Worksheet belongs to one Optimization Group. Sheet size comes from each resolved library material.</p></aside>
    </section> : null}

    {step === 'mapping' && activeDraft ? <section className="stock-import-workflow__mapping-layout">
      <aside className="project-card stock-import-workflow__worksheet-nav"><h2>{isCsv ? 'Source' : 'Worksheet Layouts'}</h2>{layouts.map((layout, index) => <div className="stock-import-workflow__layout-group" key={layout.layoutId}><strong>Layout {index + 1}</strong><span>{layout.worksheetNames.length} Worksheet{layout.worksheetNames.length === 1 ? '' : 's'} · {layout.drafts.every((draft) => draftStatus(draft, drafts) === 'Ready') ? 'Ready' : 'Needs attention'}</span>{layout.drafts.map((draft) => <button className={draft.worksheet.worksheetName === activeDraft.worksheet.worksheetName ? 'stock-import-workflow__worksheet-button stock-import-workflow__worksheet-button--active' : 'stock-import-workflow__worksheet-button'} key={draft.worksheet.worksheetName} onClick={() => activate(draft.worksheet.worksheetName)} type="button"><strong>{draft.worksheet.worksheetName}</strong><span>{draftStatus(draft, drafts)}</span></button>)}</div>)}</aside>
      <main className="project-card stock-import-workflow__mapping"><div className="stock-import-workflow__mapping-controls"><div className="project-card__header"><div><h2>Map Fields for {activeLayout ? `Layout ${layouts.indexOf(activeLayout) + 1}` : activeDraft.worksheet.worksheetName}</h2><p className="section-note">This mapping applies to {activeLayout?.worksheetNames.join(', ') ?? activeDraft.worksheet.worksheetName}.</p></div><button className="secondary-button" disabled={busy || !activeDraft.headingRangeConfirmed} onClick={() => run(() => previewLayout(session))} type="button">Auto-map fields</button></div>
        <div className="stock-import-workflow__heading-controls"><label className="project-field"><span>Heading Range</span><input aria-label={`Heading Range for ${activeDraft.worksheet.worksheetName}`} onBlur={(event) => { const result = confirmWorksheetHeadingRange(drafts, activeDraft.worksheet.worksheetName, event.target.value); const next = publish(result.drafts, activeDraft.worksheet.worksheetName); setMappingMessage(result.error ?? 'Updating preview and mappings…'); if (!result.error) run(() => previewLayout(next)); }} onChange={(event) => updateDraft(activeDraft.worksheet.worksheetName, (draft) => ({ ...draft, headingRange: event.target.value.toUpperCase(), headingRangeConfirmed: false, hasPendingChanges: true }))} value={activeDraft.headingRange} /></label><button className="secondary-button" onClick={() => { const result = copyColumnMappingsFromPreviousSelectedWorksheet(drafts, activeDraft.worksheet.worksheetName); const next = publish(result.drafts, activeDraft.worksheet.worksheetName); setMappingMessage(result.error ?? 'Column Mappings copied. Updating preview…'); if (!result.error) run(() => previewLayout(next)); }} type="button">Copy previous mappings</button></div>
        {mappingMessage ? <p className="section-note" role="status">{mappingMessage}</p> : null}</div>
        <div className="stock-import-workflow__field-table"><div className="stock-import-workflow__field-head"><span>Application field</span><span>Required</span><span>Source</span><span>Sample values</span><span>Detected type</span><span>Status</span></div>{sheetImportFields.map((targetField) => {
          const selected = activeDraft.options.columnMappings.find((mapping) => mapping.targetField === targetField)?.sourceColumn ?? activeDraft.preview.columnMappings.find((mapping) => mapping.targetField === targetField)?.sourceColumn ?? '';
          const required = requiredSheetImportFields.includes(targetField);
          const samples = sampleValues(activeDraft, targetField);
          return <label className="stock-import-workflow__field-row" key={targetField}><strong>{targetField === 'Id' ? 'Part ID' : targetField === 'Group' ? 'Part Group' : targetField}</strong><span>{required ? '●' : '—'}</span><select aria-label={`${activeDraft.worksheet.worksheetName} column for ${targetField}`} onChange={(event) => {
            let baseDrafts = drafts;
            if (targetField === 'Material' && event.target.value !== selected) {
              for (const label of sheetMaterialLabels(baseDrafts)) baseDrafts = synchronizeWorkbookMaterialResolution(baseDrafts, label);
              baseDrafts = baseDrafts.map((draft) => draft.selected ? { ...draft, options: { ...draft.options, materialMappings: [] }, newMaterials: [], ignoredMaterialNames: [] } : draft);
            }
            const mappings = [...activeDraft.options.columnMappings.filter((mapping) => mapping.targetField !== targetField && mapping.sourceColumn !== event.target.value), ...(event.target.value ? [{ targetField, sourceColumn: event.target.value }] : [])];
            const nextDrafts = activeLayout ? applyWorksheetLayoutMappings(baseDrafts, activeLayout.layoutId, mappings) : baseDrafts;
            const next = publish(nextDrafts, activeDraft.worksheet.worksheetName);
            if (requiredSheetImportFields.every((field) => mappings.some((mapping) => mapping.targetField === field))) run(() => previewLayout(next));
          }} value={selected}><option value="">Not mapped</option>{sourceColumns(activeDraft).map((column) => <option key={column.address} value={column.address}>{column.address} — {column.heading}</option>)}</select><span>{samples.join(', ') || '—'}</span><span>{selected ? fieldType(targetField) : '—'}</span><span className={selected ? 'stock-import-workflow__matched' : required ? 'stock-import-workflow__attention' : ''}>{selected ? 'Matched' : required ? 'Needs mapping' : 'Not mapped'}</span></label>;
        })}</div>
        <section className="sheet-import-materials"><div className="project-card__header"><div><h2>Resolve Materials</h2><p className="section-note">Associate each exact workbook material label with the library, create it, or ignore all matching rows.</p></div><span>{materialLabels.filter((label) => isSheetMaterialResolved(drafts, label)).length} of {materialLabels.length} resolved</span></div>
          {!hasRequiredSheetMappings(activeDraft) ? <p className="section-note">Map the required fields to discover materials.</p> : materialLabels.length === 0 ? <p className="section-note">No material labels detected yet.</p> : <div className="mapping-resolution-list">{materialLabels.map((label) => {
            const representative = selectedDrafts.find((draft) => draft.newMaterials.some((item) => item.sourceMaterialName === label)) ?? selectedDrafts[0];
            const staged = representative?.newMaterials.find((item) => item.sourceMaterialName === label);
            const ignored = selectedDrafts.some((draft) => draft.ignoredMaterialNames.includes(label));
            const explicit = representative?.options.materialMappings.find((mapping) => mapping.sourceMaterialName === label)?.targetMaterialId ?? '';
            const auto = representative?.preview.materialResolutions.find((resolution) => resolution.sourceMaterialName === label)?.resolvedMaterialId ?? '';
            const selectedId = explicit || auto;
            return <article className="mapping-resolution-card" key={label}><div className="mapping-resolution-card__header"><div><strong>{label}</strong><p>{ignored ? 'Every source row using this label will be excluded.' : staged ? 'This material will be created when the import is finalized.' : selectedId ? 'Resolved to a library material.' : 'Resolution required.'}</p></div><span className={isSheetMaterialResolved(drafts, label) ? 'status-pill status-pill--ready' : 'status-pill'}>{ignored ? 'Ignored' : staged ? 'Create on import' : selectedId ? 'Resolved' : 'Needs resolution'}</span></div>
              {!staged ? <div className="mapping-resolution-card__body"><label className="project-field"><span>Use existing material</span><select aria-label={`Library material for ${label}`} disabled={busy || ignored} onChange={(event) => updateMaterial(label, event.target.value ? { sourceMaterialName: label, targetMaterialId: event.target.value } : undefined)} value={ignored ? '' : selectedId}><option value="">Choose a library material</option>{materials.map((material) => <option key={material.materialId} value={material.materialId}>{material.name}</option>)}</select></label><button className="secondary-button" disabled={busy || ignored} onClick={() => editMaterialDraft(label, materialDraftEntry(label).material)} type="button">Create new material</button><button className="secondary-button" disabled={busy || ignored} onClick={() => ignoreMaterial(label)} type="button">Ignore material</button></div> : <><div className="row-editor-grid">{([
                ['name', 'Material name', 'text'], ['sheetLength', 'Sheet length (in)', 'number'], ['sheetWidth', 'Sheet width (in)', 'number'], ['defaultSpacing', 'Default spacing (in)', 'number'], ['defaultEdgeMargin', 'Default edge margin (in)', 'number'], ['colorFinish', 'Color / finish', 'text'], ['costPerSheet', 'Cost per sheet', 'number'],
              ] as const).map(([field, fieldLabel, type]) => <label className="field" key={field}><span>{fieldLabel}</span><input aria-label={`${fieldLabel} for ${label}`} min={type === 'number' ? 0 : undefined} onChange={(event) => editMaterialDraft(label, { ...staged.material, [field]: type === 'number' ? (event.target.value === '' && field === 'costPerSheet' ? null : Number(event.target.value)) : event.target.value })} step={type === 'number' ? '0.125' : undefined} type={type} value={staged.material[field] ?? ''} /></label>)}<label className="checkbox-field"><input checked={staged.material.allowRotation} onChange={(event) => editMaterialDraft(label, { ...staged.material, allowRotation: event.target.checked })} type="checkbox" /><span>Allow 90° rotation</span></label><label className="field field--wide"><span>Notes</span><textarea onChange={(event) => editMaterialDraft(label, { ...staged.material, notes: event.target.value })} value={staged.material.notes} /></label></div>{validateMaterialDraft(staged.material) ? <p className="mapping-warning">{validateMaterialDraft(staged.material)}</p> : null}<div className="form-actions"><button className="primary-button" disabled={busy || Boolean(validateMaterialDraft(staged.material))} onClick={() => updateMaterial(label, undefined, staged)} type="button">Apply new material</button><button className="secondary-button" disabled={busy} onClick={() => updateMaterial(label)} type="button">Use existing material instead</button></div></>}
            </article>;
          })}</div>}
        </section>
      </main>
      <aside className="project-card stock-import-workflow__source-preview"><div className="stock-import-workflow__tabs" role="tablist"><button aria-selected={mappingPreviewTab === 'source'} className={mappingPreviewTab === 'source' ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} onClick={() => setMappingPreviewTab('source')} role="tab" type="button">Source Worksheet</button><button aria-selected={mappingPreviewTab === 'imported'} className={mappingPreviewTab === 'imported' ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} onClick={() => setMappingPreviewTab('imported')} role="tab" type="button">Imported Preview</button></div>{mappingPreviewTab === 'source' ? <><p className="section-note">Select the first and last cell of the Heading Range, or enter its address.</p><div className="worksheet-preview" role="region" aria-label={`${activeDraft.worksheet.worksheetName} cell preview`}><table><thead><tr><th aria-label="Row number" />{(activeDraft.worksheet.previewRows?.[0]?.cells ?? []).map((cell) => <th key={cell.address.replace(/[0-9]+$/, '')} scope="col">{cell.address.replace(/[0-9]+$/, '')}</th>)}</tr></thead><tbody>{(activeDraft.worksheet.previewRows ?? []).map((row) => <tr key={row.rowNumber}><th scope="row">{row.rowNumber}</th>{row.cells.map((cell) => { const headingRow = activeDraft.headingRange.match(/^[A-Z]+(\d+):[A-Z]+\d+$/)?.[1]; const selectedRange = new Set(worksheetSourceColumns(activeDraft).map((column) => `${column.address}${headingRow ?? ''}`)); return <td key={cell.address}><button className={selectedRange.has(cell.address) ? 'worksheet-preview__cell worksheet-preview__cell--heading' : 'worksheet-preview__cell'} onClick={() => chooseHeadingCell(cell.address)} type="button">{cell.value || ' '}</button></td>; })}</tr>)}</tbody></table></div></> : <div className="table-wrap worksheet-preview"><table><thead><tr><th>Part ID</th><th>Length</th><th>Width</th><th>Qty</th><th>Material</th><th>Part Group</th></tr></thead><tbody>{activeDraft.preview.parts.slice(0, 25).map((part) => <tr key={part.rowId}><td>{part.importedId}</td><td>{part.lengthText ?? part.length}</td><td>{part.widthText ?? part.width}</td><td>{part.quantityText ?? part.quantity}</td><td>{part.materialName}</td><td>{part.group ?? '—'}</td></tr>)}</tbody></table></div>}</aside>
    </section> : null}

    {step === 'review' ? <section className="stock-import-workflow__review-layout">
      <aside className="project-card stock-import-workflow__review-summary"><h2>Import Summary</h2><dl><div><dt>Worksheets</dt><dd>{selectedDrafts.length}</dd></div><div><dt>Source rows</dt><dd>{plan.sourceRowCount}</dd></div><div><dt>Valid source rows</dt><dd>{plan.validSourceRowCount}</dd></div><div><dt>Sheet Part Entries</dt><dd>{plan.outputEntryCount}</dd></div><div><dt>Total part quantity</dt><dd>{plan.totalPartQuantity}</dd></div><div><dt>Skipped source rows</dt><dd>{plan.skippedSourceRowCount}</dd></div><div><dt>Errors</dt><dd>{plan.unresolvedErrors.length}</dd></div><div><dt>Warnings</dt><dd>{plan.warnings.length}</dd></div></dl></aside>
      <main className="project-card stock-import-workflow__review"><h2>Review &amp; Validate</h2><p><strong>{plan.validSourceRowCount} valid source rows will produce {plan.outputEntryCount} sheet-part entries.</strong></p><p className="section-note">Compatible rows in the same Optimization Group are combined; quantities are summed and Source References are retained.</p><div className="stock-import-workflow__tabs" role="tablist">{([['resulting', `Resulting Entries (${plan.outputEntryCount})`], ['source', `Source Rows (${plan.sourceRowCount})`], ['errors', `Errors (${plan.unresolvedErrors.length})`], ['warnings', `Warnings (${plan.warnings.length})`]] as const).map(([tab, label]) => <button aria-selected={reviewTab === tab} className={reviewTab === tab ? 'stock-import-workflow__tab stock-import-workflow__tab--active' : 'stock-import-workflow__tab'} key={tab} onClick={() => setReviewTab(tab)} role="tab" type="button">{label}</button>)}</div>
        {reviewTab === 'resulting' || reviewTab === 'source' ? <div className="table-wrap"><table><thead><tr><th>Source</th><th>Source rows</th><th>Part ID</th><th>Quantity</th><th>Length</th><th>Width</th><th>Material</th><th>Status</th></tr></thead><tbody>{(reviewTab === 'resulting' ? plan.resultingEntries : plan.sourceRows).map((part) => <tr key={part.rowId}><td>{[...new Set((part.sourceReferences ?? []).map((reference) => reference.worksheetName))].join(', ') || '—'}</td><td>{part.sourceReferences?.length ?? 0}</td><td>{part.importedId}</td><td>{part.quantityText ?? part.quantity}</td><td>{part.lengthText ?? part.length}</td><td>{part.widthText ?? part.width}</td><td>{part.materialName}</td><td>{part.validationStatus}</td></tr>)}</tbody></table></div> : <div className="table-wrap"><table><thead><tr><th>Select</th><th>Worksheet</th><th>Row</th><th>Issue</th><th>Action</th></tr></thead><tbody>{visibleIssues.map(({ worksheetName, issue, kind }) => { const issueId = issue.rowId ?? `${worksheetName}:${issue.code}:${issue.message}`; const part = drafts.find((draft) => draft.worksheet.worksheetName === worksheetName)?.preview.parts.find((row) => row.rowId === issue.rowId); return <tr key={issueId}><td>{kind === 'error' && issue.rowId ? <input aria-label={`Select ${issueId} for exclusion`} checked={selectedIssueIds.has(issueId)} onChange={() => setSelectedIssueIds((current) => { const next = new Set(current); if (next.has(issueId)) next.delete(issueId); else next.add(issueId); return next; })} type="checkbox" /> : null}</td><td>{worksheetName}</td><td>{issue.location?.physicalRow ?? part?.sourceReferences?.[0]?.physicalRow ?? '—'}</td><td>{issue.message}</td><td><div className="table-actions"><button className="secondary-button" onClick={() => { activate(worksheetName); setStep('mapping'); }} type="button">View source</button>{part && kind === 'error' ? <button className="secondary-button" onClick={() => setCorrection(correctionFor(part))} type="button">Correct</button> : null}{issue.rowId && kind === 'error' ? <button className="danger-button" onClick={() => excludeIssues([issue.rowId!])} type="button">Exclude</button> : null}</div></td></tr>; })}</tbody></table></div>}
        {reviewTab === 'errors' && selectedIssueIds.size > 0 ? <button className="danger-button" onClick={() => excludeIssues([...selectedIssueIds])} type="button">Exclude selected ({selectedIssueIds.size})</button> : null}
        <div className="stock-import-workflow__behavior"><h3>Import behavior</h3><p>Valid Sheet Parts will be imported. Source rows with errors must be corrected or explicitly excluded.</p></div>
      </main>
    </section> : null}

    {correction ? <div className="stock-length-import__drawer-layer"><button aria-label="Dismiss correction" className="stock-length-import__drawer-backdrop" onClick={() => setCorrection(null)} type="button" /><aside aria-label="Correct source row" aria-modal="true" className="stock-length-import__piece-drawer" role="dialog"><div className="project-card__header"><h2>Correct source row {correction.row.sourceReferences?.[0]?.physicalRow}</h2><button className="secondary-button" onClick={() => setCorrection(null)} type="button">Close</button></div><div className="project-form-grid stock-length-import__piece-form">{(['importedId', 'length', 'width', 'quantity', 'materialName', 'group', 'sheetNumber', 'rowNumber', 'columnNumber'] as const).map((field) => { const label = { importedId: 'Part ID', length: 'Length', width: 'Width', quantity: 'Quantity', materialName: 'Material', group: 'Part Group', sheetNumber: 'Sheet Number', rowNumber: 'Row Number', columnNumber: 'Column Number' }[field]; return <label className="project-field" key={field}><span>{label}</span><input aria-label={`Corrected ${label}`} onChange={(event) => setCorrection((current) => current ? { ...current, [field]: event.target.value } : current)} value={correction[field]} /></label>; })}</div>{!correctedRow(correction) ? <p className="mapping-warning">Part ID, positive dimensions and quantity, and Material are required. Row and Column Numbers must be positive integers when supplied.</p> : null}<div className="form-actions"><button className="primary-button" disabled={!correctedRow(correction)} onClick={saveCorrection} type="button">Save correction</button><button className="secondary-button" onClick={() => setCorrection(null)} type="button">Cancel</button></div></aside></div> : null}

    <footer className="stock-import-workflow__footer"><button className="secondary-button" disabled={busy} onClick={() => run(onCancel)} type="button">Cancel Import</button><div className="stock-import-workflow__footer-status">{step === 'worksheets' ? `${selectedDrafts.length} selected` : step === 'mapping' ? `${layouts.filter((layout) => layout.drafts.every((draft) => draftStatus(draft, drafts) === 'Ready')).length} of ${layouts.length} layouts ready` : `${plan.outputEntryCount} entries · ${plan.unresolvedErrors.length} errors`}</div><div className="form-actions">{step !== 'worksheets' ? <button className="secondary-button" disabled={busy} onClick={() => setStep(step === 'review' ? 'mapping' : 'worksheets')} type="button">Back</button> : null}{step === 'worksheets' ? <button className="primary-button" disabled={busy || selectedDrafts.length === 0 || selectedDrafts.some((draft) => !draft.optimizationGroupId)} onClick={() => setStep('mapping')} type="button">Continue to Map Fields →</button> : step === 'mapping' ? <button className="primary-button" disabled={busy || !canReview} onClick={() => setStep('review')} type="button">Review {plan.outputEntryCount} Entries →</button> : <button className="primary-button" disabled={busy || !canFinalize} onClick={() => run(onFinalize)} type="button">Import {plan.outputEntryCount} Sheet Part Entries</button>}</div></footer>
  </div>;
}
