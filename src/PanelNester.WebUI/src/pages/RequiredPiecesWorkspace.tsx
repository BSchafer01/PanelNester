import { useEffect, useMemo, useState } from 'react';
import { ImportDetails } from '../components/ImportDetails';
import { ConfirmationDialog } from '../components/ConfirmationDialog';
import type {
  ImportConfiguration,
  ImportResultCounts,
  ImportSourceMetadata,
  InchDisplayFormat,
  OptimizationGroup,
  RequiredPiece,
  RequiredPieceChange,
} from '../types/contracts';
import { RequiredPieceDrawer } from './RequiredPieceDrawer';
import { ProjectEntriesEmptyState } from './ProjectEntriesEmptyState';
import {
  filterRequiredPieces,
  flattenRequiredPieces,
  paginateRequiredPieces,
  requiredPiecePageSizes,
  type RequiredPieceSourceFilter,
  type RequiredPieceStatusFilter,
} from './requiredPiecesPresentation';

const largeQuantityWarningThreshold = 10_000;

const displayFormatOptions: Array<{ value: InchDisplayFormat; label: string }> = [
  { value: 'decimal', label: 'Decimal' },
  { value: 'fractional16', label: 'Fractional — nearest 1/16' },
  { value: 'fractional32', label: 'Fractional — nearest 1/32' },
  { value: 'fractional64', label: 'Fractional — nearest 1/64' },
];

function greatestCommonDivisor(left: number, right: number): number {
  let a = Math.abs(left);
  let b = Math.abs(right);
  while (b !== 0) [a, b] = [b, a % b];
  return a || 1;
}

export function formatInches(value: number, format: InchDisplayFormat): string {
  if (format === 'decimal') return `${Number(value.toFixed(6))} in`;
  const denominator = format === 'fractional16' ? 16 : format === 'fractional32' ? 32 : 64;
  const totalNumerator = Math.round(value * denominator);
  const whole = Math.floor(totalNumerator / denominator);
  const numerator = totalNumerator % denominator;
  if (numerator === 0) return `${whole} in`;
  const divisor = greatestCommonDivisor(numerator, denominator);
  const fraction = `${numerator / divisor}/${denominator / divisor}`;
  return `${whole > 0 ? `${whole} ` : ''}${fraction} in`;
}

interface RequiredPiecesWorkspaceProps {
  groups: OptimizationGroup[];
  activeGroupId?: string;
  inchDisplayFormat: InchDisplayFormat;
  busy: boolean;
  projectDirty?: boolean;
  message?: string;
  importSource?: ImportSourceMetadata;
  importConfiguration?: ImportConfiguration;
  lastImportReceipt?: ImportResultCounts;
  onImportFile?: (filePath?: string) => void | Promise<void>;
  onImportDroppedFile?: (file: File) => void | Promise<void>;
  onReimportFile?: () => void | Promise<void>;
  onUndoImport?: () => void | Promise<void>;
  onCreateGroup: (name: string, stockLength: string) => void | Promise<void>;
  onUpdateStockLength: (groupId: string, stockLength: string) => void | Promise<void>;
  onRequiredPieceChange: (change: RequiredPieceChange) => void | Promise<void>;
  onDeletePiece: (groupId: string, pieceId: string) => void | Promise<void>;
  onGenerateSelected?: (groupIds: string[]) => void | Promise<void>;
  onGenerateAll?: () => void | Promise<void>;
  onInchDisplayFormatChange: (format: InchDisplayFormat) => void;
}

export function RequiredPiecesWorkspace({
  groups,
  activeGroupId,
  inchDisplayFormat,
  busy,
  projectDirty = false,
  message,
  importSource,
  importConfiguration,
  lastImportReceipt,
  onImportFile,
  onImportDroppedFile,
  onReimportFile,
  onUndoImport,
  onCreateGroup,
  onUpdateStockLength,
  onRequiredPieceChange,
  onDeletePiece,
  onGenerateSelected,
  onGenerateAll,
  onInchDisplayFormatChange,
}: RequiredPiecesWorkspaceProps) {
  const [query, setQuery] = useState('');
  const [groupFilter, setGroupFilter] = useState('');
  const [sourceFilter, setSourceFilter] = useState<RequiredPieceSourceFilter>('all');
  const [statusFilter, setStatusFilter] = useState<RequiredPieceStatusFilter>('all');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<number>(25);
  const [selectedGroups, setSelectedGroups] = useState<Set<string>>(
    new Set(activeGroupId ? [activeGroupId] : []),
  );
  const [drawer, setDrawer] = useState<{ groupId?: string; piece?: RequiredPiece } | null>(null);
  const [showGroupForm, setShowGroupForm] = useState(false);
  const [newGroupName, setNewGroupName] = useState('');
  const [newStockLength, setNewStockLength] = useState('');
  const [stockLengthDrafts, setStockLengthDrafts] = useState<Record<string, string>>({});
  const [showImportDetails, setShowImportDetails] = useState(false);
  const [pendingGeneration, setPendingGeneration] = useState<{ groupIds: string[]; action: 'selected' | 'all'; quantity: number } | null>(null);

  const allRows = useMemo(() => flattenRequiredPieces(groups), [groups]);
  const filteredRows = useMemo(() => filterRequiredPieces(allRows, {
    query,
    optimizationGroupId: groupFilter,
    source: sourceFilter,
    status: statusFilter,
  }), [allRows, groupFilter, query, sourceFilter, statusFilter]);
  const pagination = useMemo(
    () => paginateRequiredPieces(filteredRows, page, pageSize),
    [filteredRows, page, pageSize],
  );
  useEffect(() => setPage(1), [query, groupFilter, sourceFilter, statusFilter, pageSize]);
  useEffect(() => {
    setSelectedGroups((current) => new Set([...current].filter((id) => groups.some((group) => group.optimizationGroupId === id))));
  }, [groups]);

  const isEmpty = groups.length === 0 && allRows.length === 0 && !importSource;
  const selectableGroups = groups.filter((group) => group.requiredPieces.length > 0 && Boolean(group.stockLength && group.stockLength > 0));
  const staleGroups = selectableGroups.filter((group) => group.resultStatus !== 'valid');
  const selectedReadyGroupIds = [...selectedGroups].filter((id) =>
    selectableGroups.some((group) => group.optimizationGroupId === id));
  const run = (action: () => void | Promise<void>) => void Promise.resolve(action()).catch(() => undefined);
  const toggleGroup = (groupId: string) => setSelectedGroups((current) => {
    const next = new Set(current);
    if (next.has(groupId)) next.delete(groupId); else next.add(groupId);
    return next;
  });
  const generationQuantity = (groupIds: string[]) => {
    return groups
      .filter((group) => groupIds.includes(group.optimizationGroupId))
      .flatMap((group) => group.requiredPieces)
      .reduce((total, piece) => total + piece.quantity, 0);
  };
  const requestGeneration = (groupIds: string[], action: 'selected' | 'all') => {
    const quantity = generationQuantity(groupIds);
    if (quantity > largeQuantityWarningThreshold) {
      setPendingGeneration({ groupIds, action, quantity });
      return;
    }
    if (action === 'selected' && onGenerateSelected) run(() => onGenerateSelected(groupIds));
    if (action === 'all' && onGenerateAll) run(onGenerateAll);
  };
  const confirmGeneration = () => {
    if (!pendingGeneration) return;
    const request = pendingGeneration;
    setPendingGeneration(null);
    if (request.action === 'selected' && onGenerateSelected) run(() => onGenerateSelected(request.groupIds));
    if (request.action === 'all' && onGenerateAll) run(onGenerateAll);
  };
  return <div className={allRows.length > 0 ? 'stock-length-workspace stock-length-workspace--completed' : 'stock-length-workspace'}>
    <header className="stock-length-workspace__header">
      <div>
        <div className="stock-length-workspace__title-line"><h1>Required Piece Entries{allRows.length > 0 ? ` (${allRows.length})` : ''}</h1>{projectDirty ? <span className="stock-length-workspace__unsaved">● Unsaved changes</span> : null}</div>
        <p>Configure Stock Length by Optimization Group, then enter the pieces to cut.</p>
        {message ? <p className="section-note" role="status">{message}</p> : null}
      </div>
      {!isEmpty ? <div className="form-actions">
        {!importSource ? <button className="secondary-button" disabled={busy || !onImportFile} onClick={() => onImportFile && run(() => onImportFile())} type="button">Import file</button> : null}
        <button aria-label="Add Required Piece" className="primary-button" disabled={busy || groups.length === 0} onClick={() => setDrawer({ groupId: activeGroupId ?? groups[0]?.optimizationGroupId })} type="button">＋ Add piece</button>
      </div> : null}
    </header>

    {isEmpty ? <ProjectEntriesEmptyState
      busy={busy}
      canAddManually={groups.length > 0}
      onAddManually={() => setDrawer({ groupId: groups[0]?.optimizationGroupId })}
      onImportDroppedFile={onImportDroppedFile}
      onImportFile={onImportFile}
      projectKind="stockLength"
    /> : null}

    {!isEmpty ? <div className={importSource ? 'stock-length-workspace__summary-row' : 'stock-length-workspace__summary-row stock-length-workspace__summary-row--single'}>
        <section className="project-card stock-length-workspace__groups stock-length-import__group-create">
          <div className="project-card__header"><div><h2>Optimization Groups</h2><p className="section-note">{groups.length} group{groups.length === 1 ? '' : 's'}</p></div><button className="secondary-button" onClick={() => setShowGroupForm((value) => !value)} type="button">＋ Add group</button></div>
          {showGroupForm || groups.length === 0 ? <div className="stock-length-workspace__group-form">
            <label className="project-field"><span>Optimization Group name</span><input aria-label="Optimization Group name" onChange={(event) => setNewGroupName(event.target.value)} value={newGroupName} /></label>
            <label className="project-field"><span>Stock Length (in)</span><input aria-label="Stock Length" onChange={(event) => setNewStockLength(event.target.value)} value={newStockLength} /></label>
            <button className="primary-button" disabled={busy || !newGroupName.trim() || !newStockLength} onClick={() => run(async () => { await onCreateGroup(newGroupName, newStockLength); setNewGroupName(''); setNewStockLength(''); setShowGroupForm(false); })} type="button">Add Optimization Group</button>
          </div> : null}
          {groups.length === 0 ? <div className="stock-length-workspace__table-empty"><strong>No Optimization Groups yet.</strong><span>Add a group to define Stock Length and start Cut Plan generation.</span></div> : <div aria-label="Optimization Groups table" className="table-wrap stock-length-workspace__groups-scroll"><table><thead><tr><th>Generate</th><th>Optimization Group</th><th>Stock Length</th><th>Required Piece Entries</th><th>Total Piece Quantity</th><th>Status</th><th>Actions</th></tr></thead><tbody>
            {groups.map((group) => {
              const stockValue = stockLengthDrafts[group.optimizationGroupId] ?? `${group.stockLength ?? ''}`;
              const ready = group.requiredPieces.length > 0 && Boolean(group.stockLength && group.stockLength > 0);
              return <tr key={group.optimizationGroupId}><td><input aria-label={`Select ${group.name} for generation`} checked={selectedGroups.has(group.optimizationGroupId)} disabled={!ready} onChange={() => toggleGroup(group.optimizationGroupId)} type="checkbox" /></td><td><strong>{group.name}</strong></td><td><input aria-label={`Stock Length for ${group.name}`} className="stock-length-workspace__inline-input" onChange={(event) => setStockLengthDrafts((current) => ({ ...current, [group.optimizationGroupId]: event.target.value }))} value={stockValue} /></td><td>{group.requiredPieces.length}</td><td>{group.requiredPieces.reduce((total, piece) => total + piece.quantity, 0)}</td><td><span className={ready ? 'status-pill status-pill--ready' : 'status-pill'}>{ready ? group.resultStatus === 'valid' ? 'Current Cut Plan' : 'Needs generation' : 'Needs setup'}</span></td><td><button className="secondary-button" disabled={busy} onClick={() => run(() => onUpdateStockLength(group.optimizationGroupId, stockValue))} type="button">Save</button></td></tr>;
            })}
          </tbody></table></div>}
          <div className="stock-length-workspace__generate-actions">
            <label className="checkbox-field"><input checked={selectableGroups.length > 0 && selectedReadyGroupIds.length === selectableGroups.length} onChange={(event) => setSelectedGroups(event.target.checked ? new Set(selectableGroups.map((group) => group.optimizationGroupId)) : new Set())} type="checkbox" /><span>Select all ({selectableGroups.length})</span></label>
            <button aria-label="Generate Selected" className="primary-button" disabled={busy || selectedReadyGroupIds.length === 0 || !onGenerateSelected} onClick={() => requestGeneration(selectedReadyGroupIds, 'selected')} type="button">Generate selected ({selectedReadyGroupIds.length})</button>
            <button aria-label="Generate All Needing Generation" className="secondary-button" disabled={busy || staleGroups.length === 0 || !onGenerateAll} onClick={() => requestGeneration(staleGroups.map((group) => group.optimizationGroupId), 'all')} type="button">Generate all ({staleGroups.length})</button>
            {groups.filter((group) => group.requiredPieces.length > 0 && group.resultStatus !== 'valid').length > 0 ? <span className="section-note">{groups.filter((group) => group.requiredPieces.length > 0 && group.resultStatus !== 'valid').length} Optimization Group{groups.filter((group) => group.requiredPieces.length > 0 && group.resultStatus !== 'valid').length === 1 ? '' : 's'} need generation</span> : null}
          </div>
        </section>

      {importSource && importConfiguration ? <aside className="project-card stock-length-workspace__last-import">
        <h2>Last Import</h2><p><strong>✓ {importSource.importSourcePath.split(/[\\/]/).filter(Boolean).slice(-1)[0] ?? importSource.importSourcePath}</strong></p>{lastImportReceipt ? <><p>Imported {lastImportReceipt.sourceRowCount} source rows as {lastImportReceipt.outputEntryCount} required-piece entries from {lastImportReceipt.worksheetCount} worksheets.</p><p>{lastImportReceipt.createdEntryCount} created · {lastImportReceipt.updatedEntryCount} updated · {lastImportReceipt.skippedSourceRowCount} skipped</p></> : <p>{importConfiguration.worksheets.length} Worksheet{importConfiguration.worksheets.length === 1 ? '' : 's'} · {allRows.filter((row) => !row.piece.isManual).length} Required Piece Entries</p>}<p className="section-note">{new Date(importSource.snapshotCapturedAtUtc).toLocaleString()}</p>
        <div className="form-actions"><button className="secondary-button" onClick={() => setShowImportDetails((value) => !value)} type="button">View details</button><button className="secondary-button" disabled={busy || !onReimportFile} onClick={() => onReimportFile && run(onReimportFile)} type="button">↻ Re-import</button>{onUndoImport ? <button className="danger-button" disabled={busy} onClick={() => run(onUndoImport)} type="button">Undo Import</button> : null}</div>
        <p className="stock-length-workspace__success">✓ Import completed successfully</p>
        {showImportDetails ? <ImportDetails importConfiguration={importConfiguration} importedParts={[]} importSource={importSource} materials={[]} optimizationGroups={groups} /> : null}
      </aside> : null}
    </div> : null}

        {allRows.length > 0 ? <section className="project-card stock-length-workspace__pieces">
          <div className="project-card__header"><div><h2>Required Piece Entries ({allRows.length})</h2></div><label className="project-field stock-length-workspace__display"><span>Length display</span><select aria-label="Length Display" onChange={(event) => onInchDisplayFormatChange(event.target.value as InchDisplayFormat)} value={inchDisplayFormat}>{displayFormatOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label></div>
          <div className="stock-length-workspace__filters">
            <input aria-label="Search Required Pieces" onChange={(event) => setQuery(event.target.value)} placeholder="Search Required Pieces…" value={query} />
            <select aria-label="Filter by Optimization Group" onChange={(event) => setGroupFilter(event.target.value)} value={groupFilter}><option value="">All Optimization Groups</option>{groups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}</select>
            <select aria-label="Filter by source" onChange={(event) => setSourceFilter(event.target.value as RequiredPieceSourceFilter)} value={sourceFilter}><option value="all">All sources</option><option value="worksheet">Imported Worksheets</option><option value="manual">Manual</option></select>
            <select aria-label="Filter by status" onChange={(event) => setStatusFilter(event.target.value as RequiredPieceStatusFilter)} value={statusFilter}><option value="all">All statuses</option><option value="valid">Valid</option><option value="warning">Warning</option><option value="error">Error</option></select>
          </div>
          <div aria-label="Saved Required Pieces table" className="table-wrap stock-length-workspace__pieces-scroll stock-length-import__saved-pieces-scroll"><table><thead><tr><th>Optimization Group</th><th>Qty</th><th>Length</th><th>Profile Number</th><th>Finish</th><th>Part Name</th><th>Part Number</th><th>Source</th><th>Status</th><th>Actions</th></tr></thead><tbody>
            {pagination.rows.map(({ group, piece, sourceLabel, status }) => <tr key={piece.requiredPieceId}><td>{group.name}</td><td>{piece.quantity}</td><td>{formatInches(piece.length, inchDisplayFormat)}</td><td>{piece.profileNumber}</td><td>{piece.finish || 'No finish specified'}</td><td>{piece.partName || '—'}</td><td>{piece.partNumber || '—'}</td><td title={piece.sourceReferences.map((reference) => `${reference.worksheetName}!${reference.physicalRow}`).join(', ')}>{sourceLabel}</td><td><div className="module-status-stack"><span className={`module-status-chip module-status-chip--${status}`} title={(piece.validationMessages ?? []).join(' | ') || 'Ready'}>{status}</span><span className="module-status-note">{piece.validationMessages?.[0] ?? 'Ready for cutting'}</span></div></td><td><div className="table-actions"><button aria-label={`Edit Required Piece ${piece.requiredPieceId}`} className="module-table-action" onClick={() => setDrawer({ groupId: group.optimizationGroupId, piece })} type="button">Edit</button><button aria-label={`Delete Required Piece ${piece.requiredPieceId}`} className="module-table-action module-table-action--danger" onClick={() => run(() => onDeletePiece(group.optimizationGroupId, piece.requiredPieceId))} type="button">Delete</button></div></td></tr>)}
          </tbody></table></div>
          {filteredRows.length === 0 ? <p className="section-note">No Required Pieces match the current filters.</p> : null}
          <div className="pagination-bar"><span>Showing {pagination.first}–{pagination.last} of {filteredRows.length} Required Piece Entries</span><div className="pagination-controls"><button className="secondary-button" disabled={pagination.page <= 1} onClick={() => setPage((value) => value - 1)} type="button">‹</button><span>Page {pagination.page} of {pagination.pageCount}</span><button className="secondary-button" disabled={pagination.page >= pagination.pageCount} onClick={() => setPage((value) => value + 1)} type="button">›</button><select aria-label="Required Piece Entries per page" onChange={(event) => setPageSize(Number(event.target.value))} value={pageSize}>{requiredPiecePageSizes.map((size) => <option key={size} value={size}>{size} / page</option>)}</select></div></div>
        </section> : null}

    {drawer ? <RequiredPieceDrawer busy={busy} groupId={drawer.groupId} groups={groups} onClose={() => setDrawer(null)} onSave={onRequiredPieceChange} piece={drawer.piece} /> : null}
    {pendingGeneration ? <ConfirmationDialog busy={busy} message={`${pendingGeneration.quantity.toLocaleString()} Piece Instances will be generated. This may take time and use significant memory.`} onCancel={() => setPendingGeneration(null)} onConfirm={confirmGeneration} title="Generate a large Cut Plan?" /> : null}
  </div>;
}
