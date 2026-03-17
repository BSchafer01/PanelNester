import { useEffect, useMemo, useState } from 'react';
import { StatusPill } from '../components/StatusPill';
import {
  requiredImportFieldNames,
  type HostBridgeSnapshot,
  type ImportFieldName,
  type ImportMappingSession,
  type ImportMaterialResolution,
  type Material,
  type MaterialDraft,
  type ImportResponse,
  type PartRow,
  type PartRowUpdate,
  type ValidationStatus,
} from '../types/contracts';

type SortKey =
  | 'row'
  | 'part'
  | 'material'
  | 'group'
  | 'status'
  | 'quantity'
  | 'length'
  | 'width';

type SortDirection = 'asc' | 'desc';
type StatusFilter = 'all' | ValidationStatus;

const requiredImportFieldSet = new Set<ImportFieldName>(requiredImportFieldNames);

interface ImportPageProps {
  bridge: HostBridgeSnapshot;
  materials: Material[];
  selectedFilePath?: string;
  importResponse: ImportResponse;
  mappingSession?: ImportMappingSession;
  importMessage: string;
  nestingMessage: string;
  importBusy: boolean;
  partMutationBusy: boolean;
  nestingBusy: boolean;
  canImportFiles: boolean;
  canAddRows: boolean;
  canEditRows: boolean;
  canDeleteRows: boolean;
  batchNestingEnabled: boolean;
  canRunNesting: boolean;
  readyPartCount: number;
  readyMaterialCount: number;
  onImportFile: () => Promise<void>;
  onUpdateImportMappingSession: (session: ImportMappingSession) => void;
  onPreviewImportMapping: () => Promise<void>;
  onFinalizeImportMapping: () => Promise<void>;
  onCancelImportMapping: () => void;
  onAddPartRow: (part: PartRowUpdate) => Promise<void>;
  onUpdatePartRow: (part: PartRowUpdate) => Promise<void>;
  onDeletePartRow: (rowId: string) => Promise<void>;
  onRunNesting: () => Promise<void>;
  onRetryHandshake: () => Promise<void>;
}

function getStatusTone(status: ValidationStatus): 'ok' | 'warn' | 'error' {
  switch (status) {
    case 'error':
      return 'error';
    case 'warning':
      return 'warn';
    case 'valid':
    default:
      return 'ok';
  }
}

function getStatusRank(status: ValidationStatus): number {
  switch (status) {
    case 'error':
      return 0;
    case 'warning':
      return 1;
    case 'valid':
    default:
      return 2;
  }
}

function getRowValue(
  part: PartRow,
  field: 'length' | 'width' | 'quantity',
): string {
  if (field === 'length') {
    return part.lengthText?.trim().length ? part.lengthText : `${part.length}`;
  }

  if (field === 'width') {
    return part.widthText?.trim().length ? part.widthText : `${part.width}`;
  }

  return part.quantityText?.trim().length ? part.quantityText : `${part.quantity}`;
}

function getGroupValue(part: Pick<PartRow, 'group'>): string {
  return part.group?.trim() ?? '';
}

function getDisplayGroup(part: Pick<PartRow, 'group'>): string {
  const group = getGroupValue(part);
  return group.length > 0 ? group : 'Ungrouped';
}

function createDraft(
  part?: PartRow,
  fallbackMaterialName = '',
): PartRowUpdate {
  if (part) {
    return {
      rowId: part.rowId,
      importedId: part.importedId,
      length: getRowValue(part, 'length'),
      width: getRowValue(part, 'width'),
      quantity: getRowValue(part, 'quantity'),
      materialName: part.materialName,
      group: part.group ?? '',
    };
  }

  return {
    importedId: '',
    length: '',
    width: '',
    quantity: '1',
    materialName: fallbackMaterialName,
    group: '',
  };
}

function compareStrings(left: string, right: string): number {
  return left.localeCompare(right, undefined, {
    numeric: true,
    sensitivity: 'base',
  });
}

function sortParts(
  parts: PartRow[],
  sortKey: SortKey,
  sortDirection: SortDirection,
): PartRow[] {
  const direction = sortDirection === 'asc' ? 1 : -1;
  return [...parts].sort((left, right) => {
    let result = 0;

    switch (sortKey) {
      case 'part':
        result = compareStrings(left.importedId, right.importedId);
        break;
      case 'material':
        result = compareStrings(left.materialName, right.materialName);
        break;
      case 'group':
        result =
          compareStrings(getGroupValue(left), getGroupValue(right)) ||
          compareStrings(left.rowId, right.rowId);
        break;
      case 'status':
        result =
          getStatusRank(left.validationStatus) - getStatusRank(right.validationStatus) ||
          compareStrings(left.importedId, right.importedId);
        break;
      case 'quantity':
        result = left.quantity - right.quantity || compareStrings(left.rowId, right.rowId);
        break;
      case 'length':
        result = left.length - right.length || compareStrings(left.rowId, right.rowId);
        break;
      case 'width':
        result = left.width - right.width || compareStrings(left.rowId, right.rowId);
        break;
      case 'row':
      default:
        result = compareStrings(left.rowId, right.rowId);
        break;
    }

    return result * direction;
  });
}

function createMaterialDraft(sourceMaterialName: string): MaterialDraft {
  return {
    name: sourceMaterialName,
    colorFinish: '',
    notes: '',
    sheetLength: 96,
    sheetWidth: 48,
    allowRotation: true,
    defaultSpacing: 0.125,
    defaultEdgeMargin: 0.5,
    costPerSheet: null,
  };
}

function validateMaterialDraft(draft: MaterialDraft): string | null {
  if (draft.name.trim().length === 0) {
    return 'Material name is required.';
  }

  if (draft.sheetLength <= 0) {
    return 'Sheet length must be greater than zero.';
  }

  if (draft.sheetWidth <= 0) {
    return 'Sheet width must be greater than zero.';
  }

  if (draft.defaultSpacing < 0) {
    return 'Default spacing cannot be negative.';
  }

  if (draft.defaultEdgeMargin < 0) {
    return 'Default edge margin cannot be negative.';
  }

  if (draft.costPerSheet != null && draft.costPerSheet < 0) {
    return 'Cost per sheet cannot be negative.';
  }

  return null;
}

function getFieldLabel(field: ImportFieldName): string {
  switch (field) {
    case 'Id':
      return 'Part ID';
    case 'Length':
      return 'Length';
    case 'Width':
      return 'Width';
    case 'Quantity':
      return 'Quantity';
    case 'Material':
      return 'Material';
    case 'Group':
      return 'Group';
    default:
      return field;
  }
}

function updateColumnMapping(
  session: ImportMappingSession,
  targetField: ImportFieldName,
  sourceColumn: string,
): ImportMappingSession {
  const trimmedSourceColumn = sourceColumn.trim();
  const previousMaterialSource =
    session.options.columnMappings.find((mapping) => mapping.targetField === 'Material')
      ?.sourceColumn ?? null;
  const nextColumnMappings = session.options.columnMappings.filter(
    (mapping) =>
      mapping.targetField !== targetField &&
      mapping.sourceColumn !== trimmedSourceColumn,
  );

  if (trimmedSourceColumn.length > 0) {
    nextColumnMappings.push({
      sourceColumn: trimmedSourceColumn,
      targetField,
    });
  }

  const shouldResetMaterials =
    targetField === 'Material' && previousMaterialSource !== trimmedSourceColumn;

  return {
    ...session,
    options: {
      ...session.options,
      columnMappings: nextColumnMappings,
      materialMappings: shouldResetMaterials ? [] : session.options.materialMappings,
    },
    newMaterials: shouldResetMaterials ? [] : session.newMaterials,
    hasPendingChanges: true,
  };
}

function updateExistingMaterialMapping(
  session: ImportMappingSession,
  sourceMaterialName: string,
  materialId: string,
): ImportMappingSession {
  const nextMaterialMappings = session.options.materialMappings.filter(
    (mapping) => mapping.sourceMaterialName !== sourceMaterialName,
  );

  if (materialId.trim().length > 0) {
    nextMaterialMappings.push({
      sourceMaterialName,
      targetMaterialId: materialId,
    });
  }

  return {
    ...session,
    options: {
      ...session.options,
      materialMappings: nextMaterialMappings,
    },
    newMaterials: session.newMaterials.filter(
      (material) => material.sourceMaterialName !== sourceMaterialName,
    ),
    hasPendingChanges: true,
  };
}

function startNewMaterialMapping(
  session: ImportMappingSession,
  sourceMaterialName: string,
): ImportMappingSession {
  const existingDraft = session.newMaterials.find(
    (material) => material.sourceMaterialName === sourceMaterialName,
  );

  return {
    ...session,
    options: {
      ...session.options,
      materialMappings: session.options.materialMappings.filter(
        (mapping) => mapping.sourceMaterialName !== sourceMaterialName,
      ),
    },
    newMaterials: existingDraft
      ? session.newMaterials
      : [
          ...session.newMaterials,
          {
            sourceMaterialName,
            material: createMaterialDraft(sourceMaterialName),
          },
        ],
    hasPendingChanges: true,
  };
}

function updateNewMaterialDraft(
  session: ImportMappingSession,
  sourceMaterialName: string,
  material: MaterialDraft,
): ImportMappingSession {
  return {
    ...session,
    newMaterials: session.newMaterials.map((entry) =>
      entry.sourceMaterialName === sourceMaterialName
        ? { ...entry, material }
        : entry,
    ),
    hasPendingChanges: true,
  };
}

function cancelNewMaterialMapping(
  session: ImportMappingSession,
  sourceMaterialName: string,
): ImportMappingSession {
  return {
    ...session,
    newMaterials: session.newMaterials.filter(
      (material) => material.sourceMaterialName !== sourceMaterialName,
    ),
    hasPendingChanges: true,
  };
}

function getResolutionTone(
  resolution: ImportMaterialResolution,
  hasPlannedCreate: boolean,
  hasSelectedExisting: boolean,
): 'ok' | 'warn' | 'error' {
  if (hasPlannedCreate) {
    return 'warn';
  }

  if (hasSelectedExisting || resolution.resolvedMaterialId) {
    return 'ok';
  }

  return 'error';
}

export function ImportPage({
  bridge,
  materials,
  selectedFilePath,
  importResponse,
  mappingSession,
  importMessage,
  nestingMessage,
  importBusy,
  partMutationBusy,
  nestingBusy,
  canImportFiles,
  canAddRows,
  canEditRows,
  canDeleteRows,
  batchNestingEnabled,
  canRunNesting,
  readyPartCount,
  readyMaterialCount,
  onImportFile,
  onUpdateImportMappingSession,
  onPreviewImportMapping,
  onFinalizeImportMapping,
  onCancelImportMapping,
  onAddPartRow,
  onUpdatePartRow,
  onDeletePartRow,
  onRunNesting,
  onRetryHandshake,
}: ImportPageProps) {
  const [editingRowId, setEditingRowId] = useState<string>();
  const [editingDraft, setEditingDraft] = useState<PartRowUpdate>();
  const [showAddRow, setShowAddRow] = useState(false);
  const [addDraft, setAddDraft] = useState<PartRowUpdate>({} as PartRowUpdate);
  const [materialFilter, setMaterialFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [sortKey, setSortKey] = useState<SortKey>('row');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');

  const activeImportResponse = mappingSession?.preview ?? importResponse;
  const displayFilePath = mappingSession?.filePath ?? selectedFilePath;
  const hasPendingImportReview = Boolean(mappingSession);
  const showRowActions = !hasPendingImportReview && (canEditRows || canDeleteRows);
  const hasParts = activeImportResponse.parts.length > 0;
  const busy = importBusy || partMutationBusy;
  const distinctMaterials = useMemo(
    () =>
      Array.from(
        new Set(
          activeImportResponse.parts
            .map((part) => part.materialName.trim())
            .filter((name) => name.length > 0),
        ),
      ).sort((left, right) => compareStrings(left, right)),
    [activeImportResponse.parts],
  );
  const counts = useMemo(
    () => ({
      valid: activeImportResponse.parts.filter((part) => part.validationStatus === 'valid')
        .length,
      warning: activeImportResponse.parts.filter(
        (part) => part.validationStatus === 'warning',
      ).length,
      error: activeImportResponse.parts.filter((part) => part.validationStatus === 'error')
        .length,
    }),
    [activeImportResponse.parts],
  );
  const defaultMaterialName = useMemo(() => {
    if (
      materialFilter !== 'all' &&
      distinctMaterials.includes(materialFilter)
    ) {
      return materialFilter;
    }

    return distinctMaterials[0] ?? '';
  }, [distinctMaterials, materialFilter]);
  const filteredParts = useMemo(() => {
    const filtered = activeImportResponse.parts.filter((part) => {
      const matchesMaterial =
        materialFilter === 'all' || part.materialName === materialFilter;
      const matchesStatus =
        statusFilter === 'all' || part.validationStatus === statusFilter;

      return matchesMaterial && matchesStatus;
    });

    return sortParts(filtered, sortKey, sortDirection);
  }, [activeImportResponse.parts, materialFilter, sortDirection, sortKey, statusFilter]);

  const mappedColumns = useMemo(
    () =>
      new Map(
        mappingSession?.options.columnMappings.map((mapping) => [
          mapping.targetField,
          mapping.sourceColumn,
        ]) ?? [],
      ),
    [mappingSession],
  );
  const plannedNewMaterials = useMemo(
    () =>
      new Map(
        mappingSession?.newMaterials.map((material) => [
          material.sourceMaterialName,
          material.material,
        ]) ?? [],
      ),
    [mappingSession],
  );
  const explicitMaterialMappings = useMemo(
    () =>
      new Map(
        mappingSession?.options.materialMappings.map((mapping) => [
          mapping.sourceMaterialName,
          mapping.targetMaterialId ?? '',
        ]) ?? [],
      ),
    [mappingSession],
  );
  const previewMaterialResolutions = mappingSession?.preview.materialResolutions ?? [];
  const pendingNewMaterials = mappingSession?.newMaterials ?? [];
  const allRequiredFieldsMapped = hasPendingImportReview
    ? requiredImportFieldNames.every(
        (field) => (mappedColumns.get(field) ?? '').trim().length > 0,
      )
    : true;
  const unresolvedImportMaterials = hasPendingImportReview
    ? previewMaterialResolutions.filter((resolution) => {
        const hasPlannedCreate = plannedNewMaterials.has(resolution.sourceMaterialName);
        const selectedExistingMaterialId =
          explicitMaterialMappings.get(resolution.sourceMaterialName) ??
          resolution.resolvedMaterialId ??
          '';
        return !hasPlannedCreate && selectedExistingMaterialId.trim().length === 0;
      }).length
    : 0;
  const hasInvalidNewMaterialDraft = hasPendingImportReview
    ? pendingNewMaterials.some(
        (material) => validateMaterialDraft(material.material) !== null,
      )
    : false;
  const canPreviewMapping =
    hasPendingImportReview && allRequiredFieldsMapped && !busy;
  const canFinalizeMapping =
    hasPendingImportReview &&
    !(mappingSession?.hasPendingChanges ?? true) &&
    allRequiredFieldsMapped &&
    unresolvedImportMaterials === 0 &&
    !hasInvalidNewMaterialDraft &&
    !busy;

  useEffect(() => {
    if (showAddRow && !hasPendingImportReview) {
      setAddDraft((current) =>
        current.materialName?.trim().length > 0
          ? current
          : createDraft(undefined, defaultMaterialName),
      );
    }

    if (editingRowId) {
      const nextRow = activeImportResponse.parts.find((part) => part.rowId === editingRowId);
      if (!nextRow) {
        setEditingRowId(undefined);
        setEditingDraft(undefined);
      }
    }

    if (materialFilter !== 'all' && !distinctMaterials.includes(materialFilter)) {
      setMaterialFilter('all');
    }

    if (hasPendingImportReview) {
      setShowAddRow(false);
      setEditingRowId(undefined);
      setEditingDraft(undefined);
    }
  }, [
    activeImportResponse.parts,
    defaultMaterialName,
    distinctMaterials,
    editingRowId,
    hasPendingImportReview,
    materialFilter,
    showAddRow,
  ]);

  const beginEdit = (part: PartRow) => {
    setEditingRowId(part.rowId);
    setEditingDraft(createDraft(part));
  };

  const cancelEdit = () => {
    setEditingRowId(undefined);
    setEditingDraft(undefined);
  };

  const saveEdit = async () => {
    if (!editingDraft) {
      return;
    }

    await onUpdatePartRow(editingDraft);
    cancelEdit();
  };

  const startAddRow = () => {
    setShowAddRow(true);
    setAddDraft(createDraft(undefined, defaultMaterialName));
  };

  const cancelAddRow = () => {
    setShowAddRow(false);
    setAddDraft(createDraft(undefined, defaultMaterialName));
  };

  const saveAddRow = async () => {
    await onAddPartRow(addDraft);
    cancelAddRow();
  };

  const requestDelete = async (rowId: string) => {
    if (
      !window.confirm(
        `Delete ${rowId}? The service will revalidate the remaining import rows.`,
      )
    ) {
      return;
    }

    await onDeletePartRow(rowId);
    if (editingRowId === rowId) {
      cancelEdit();
    }
  };

  const applySession = (nextSession: ImportMappingSession) => {
    onUpdateImportMappingSession(nextSession);
  };

  const handleColumnMappingChange = (
    targetField: ImportFieldName,
    sourceColumn: string,
  ) => {
    if (!mappingSession) {
      return;
    }

    applySession(updateColumnMapping(mappingSession, targetField, sourceColumn));
  };

  const handleExistingMaterialChange = (
    sourceMaterialName: string,
    materialId: string,
  ) => {
    if (!mappingSession) {
      return;
    }

    applySession(
      updateExistingMaterialMapping(mappingSession, sourceMaterialName, materialId),
    );
  };

  const handleCreateMaterialPlan = (sourceMaterialName: string) => {
    if (!mappingSession) {
      return;
    }

    applySession(startNewMaterialMapping(mappingSession, sourceMaterialName));
  };

  const handleCancelMaterialPlan = (sourceMaterialName: string) => {
    if (!mappingSession) {
      return;
    }

    applySession(cancelNewMaterialMapping(mappingSession, sourceMaterialName));
  };

  const handleMaterialDraftChange = <T extends keyof MaterialDraft>(
    sourceMaterialName: string,
    field: T,
    value: MaterialDraft[T],
  ) => {
    if (!mappingSession) {
      return;
    }

    const currentDraft = plannedNewMaterials.get(sourceMaterialName);
    if (!currentDraft) {
      return;
    }

    applySession(
      updateNewMaterialDraft(mappingSession, sourceMaterialName, {
        ...currentDraft,
        [field]: value,
      }),
    );
  };

  return (
    <div className="page-grid">
      <section className="panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Import</p>
            <h2>Import rows and prepare them for nesting</h2>
          </div>
          <div className="button-row">
            <button
              className="secondary-button"
              onClick={() => void onRetryHandshake()}
              type="button"
            >
              Retry
            </button>
            <button
              className="primary-button"
              disabled={!bridge.connected || busy || !canImportFiles}
              onClick={() => void onImportFile()}
              type="button"
            >
              {importBusy
                ? 'Working…'
                : hasPendingImportReview
                  ? 'Choose another file'
                  : 'Choose file'}
            </button>
            <button
              className="secondary-button"
              disabled={!bridge.connected || !canRunNesting || nestingBusy || busy}
              onClick={() => void onRunNesting()}
              type="button"
            >
              {nestingBusy
                ? 'Nesting…'
                : batchNestingEnabled
                  ? 'Run batch nesting'
                  : 'Run nesting'}
            </button>
          </div>
        </div>

        <div className="import-status-stack">
          <p className="section-note">{importMessage}</p>
          {displayFilePath ? (
            <p className="section-note import-path">Source file: {displayFilePath}</p>
          ) : null}
          <p className="section-note">
            {hasPendingImportReview
              ? 'Current imported rows remain unchanged until you finalize this review.'
              : batchNestingEnabled
                ? `Imported rows carry their material names. Batch nesting will group ${readyPartCount} ready row(s) across ${readyMaterialCount} ready material group(s).`
                : nestingMessage}
          </p>
        </div>
      </section>

      {mappingSession ? (
        <section className="panel">
          <div className="section-header">
            <div>
              <p className="eyebrow">Review</p>
              <h3>Map columns and resolve incoming materials</h3>
            </div>
            <div className="button-row">
              <button
                className="secondary-button"
                disabled={busy}
                onClick={onCancelImportMapping}
                type="button"
              >
                Cancel review
              </button>
              <button
                className="secondary-button"
                disabled={!bridge.connected || !canPreviewMapping}
                onClick={() => void onPreviewImportMapping()}
                type="button"
              >
                {importBusy ? 'Updating…' : 'Preview mapping'}
              </button>
              <button
                className="primary-button"
                disabled={!bridge.connected || !canFinalizeMapping}
                onClick={() => void onFinalizeImportMapping()}
                type="button"
              >
                {importBusy ? 'Finalizing…' : 'Finalize import'}
              </button>
            </div>
          </div>

          <div className="stats-grid">
            <article className="stat-card">
              <span>Columns</span>
              <strong>{mappingSession.preview.availableColumns.length}</strong>
            </article>
            <article className="stat-card">
              <span>Preview rows</span>
              <strong>{mappingSession.preview.parts.length}</strong>
            </article>
            <article className="stat-card">
              <span>Incoming materials</span>
              <strong>{mappingSession.preview.materialResolutions.length}</strong>
            </article>
            <article className="stat-card">
              <span>Create on finalize</span>
              <strong>{mappingSession.newMaterials.length}</strong>
            </article>
          </div>

          {mappingSession.hasPendingChanges ? (
            <p className="mapping-warning">
              Preview is out of date. Refresh preview before you finalize the import.
            </p>
          ) : null}

          <div className="import-review-grid">
            <article className="editor-card">
              <div className="section-header">
                <div>
                  <p className="eyebrow">Columns</p>
                  <h3>Expected import fields</h3>
                </div>
                <StatusPill
                  label={
                    allRequiredFieldsMapped ? 'Ready to preview' : 'Mapping required'
                  }
                  tone={allRequiredFieldsMapped ? 'ok' : 'warn'}
                />
              </div>

              <p className="section-note">
                Map each required field to one source column from the file header. Group
                is optional and can stay blank to keep imported rows ungrouped.
              </p>

              <div className="mapping-cards-grid">
                {mappingSession.preview.columnMappings.map((mapping) => {
                  const selectedSource = mappedColumns.get(mapping.targetField) ?? '';
                  const hasSelection = selectedSource.trim().length > 0;
                  const isRequiredField = requiredImportFieldSet.has(mapping.targetField);
                  const statusLabel = hasSelection
                    ? selectedSource
                    : mapping.suggestedSourceColumn
                      ? `Suggested: ${mapping.suggestedSourceColumn}`
                      : mapping.targetField === 'Group'
                        ? 'Leave blank to keep rows ungrouped.'
                        : 'Choose a column';

                  return (
                    <div className="mapping-card" key={mapping.targetField}>
                      <div className="mapping-card__header">
                        <strong>{getFieldLabel(mapping.targetField)}</strong>
                        <StatusPill
                          label={
                            hasSelection
                              ? 'Mapped'
                              : isRequiredField
                                ? 'Required'
                                : 'Optional'
                          }
                          tone={hasSelection ? 'ok' : isRequiredField ? 'warn' : 'muted'}
                        />
                      </div>
                      <label className="field">
                        <span>Source column</span>
                        <select
                          disabled={busy}
                          onChange={(event) =>
                            handleColumnMappingChange(
                              mapping.targetField,
                              event.target.value,
                            )
                          }
                          value={selectedSource}
                        >
                          <option value="">Choose a column</option>
                          {mappingSession.preview.availableColumns.map((column) => (
                            <option key={column} value={column}>
                              {column}
                            </option>
                          ))}
                        </select>
                      </label>
                      <p className="mapping-card__note">{statusLabel}</p>
                    </div>
                  );
                })}
              </div>
            </article>

            <article className="editor-card">
              <div className="section-header">
                <div>
                  <p className="eyebrow">Materials</p>
                  <h3>Resolve import material names</h3>
                </div>
                <StatusPill
                  label={
                    unresolvedImportMaterials === 0 ? 'Ready to finalize' : 'Resolution required'
                  }
                  tone={unresolvedImportMaterials === 0 ? 'ok' : 'warn'}
                />
              </div>

              <p className="section-note">
                Choose an existing library material or stage a new one to create during final import.
              </p>

              {!allRequiredFieldsMapped ? (
                <div className="empty-state">
                  <strong>Column mapping comes first</strong>
                  <span>Map every required field and refresh preview to review materials.</span>
                </div>
              ) : mappingSession.preview.materialResolutions.length > 0 ? (
                <div className="mapping-resolution-list">
                  {mappingSession.preview.materialResolutions.map((resolution) => {
                    const plannedDraft = plannedNewMaterials.get(
                      resolution.sourceMaterialName,
                    );
                    const selectedExistingMaterialId =
                      explicitMaterialMappings.get(resolution.sourceMaterialName) ??
                      (plannedDraft ? '' : resolution.resolvedMaterialId ?? '');
                    const selectedExistingMaterial = materials.find(
                      (material) => material.materialId === selectedExistingMaterialId,
                    );
                    const draftMessage = plannedDraft
                      ? validateMaterialDraft(plannedDraft)
                      : null;
                    const tone = getResolutionTone(
                      resolution,
                      Boolean(plannedDraft),
                      Boolean(selectedExistingMaterialId),
                    );
                    const label = plannedDraft
                      ? 'Create on finalize'
                      : selectedExistingMaterial?.name ??
                        resolution.resolvedMaterialName ??
                        'Resolution required';

                    return (
                      <div className="mapping-resolution-card" key={resolution.sourceMaterialName}>
                        <div className="mapping-resolution-card__header">
                          <div>
                            <strong>{resolution.sourceMaterialName}</strong>
                            <p>
                              {plannedDraft
                                ? 'New library material will be created when you finalize the import.'
                                : selectedExistingMaterialId
                                  ? 'This import material will resolve to the selected library entry.'
                                  : 'Choose a library match or create a new material for this import name.'}
                            </p>
                          </div>
                          <StatusPill label={label} tone={tone} />
                        </div>

                        {!plannedDraft ? (
                          <div className="mapping-resolution-card__body">
                            <label className="field">
                              <span>Use existing material</span>
                              <select
                                disabled={busy}
                                onChange={(event) =>
                                  handleExistingMaterialChange(
                                    resolution.sourceMaterialName,
                                    event.target.value,
                                  )
                                }
                                value={selectedExistingMaterialId}
                              >
                                <option value="">Choose a library material</option>
                                {materials.map((material) => (
                                  <option key={material.materialId} value={material.materialId}>
                                    {material.name}
                                  </option>
                                ))}
                              </select>
                            </label>
                            <button
                              className="secondary-button"
                              disabled={busy}
                              onClick={() => handleCreateMaterialPlan(resolution.sourceMaterialName)}
                              type="button"
                            >
                              Create new material
                            </button>
                          </div>
                        ) : (
                          <>
                            <div className="row-editor-grid">
                              <label className="field field--wide">
                                <span>Material name</span>
                                <input
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'name',
                                      event.target.value,
                                    )
                                  }
                                  type="text"
                                  value={plannedDraft.name}
                                />
                              </label>
                              <label className="field">
                                <span>Sheet length (in)</span>
                                <input
                                  min="0"
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'sheetLength',
                                      Number(event.target.value) || 0,
                                    )
                                  }
                                  step="0.125"
                                  type="number"
                                  value={plannedDraft.sheetLength}
                                />
                              </label>
                              <label className="field">
                                <span>Sheet width (in)</span>
                                <input
                                  min="0"
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'sheetWidth',
                                      Number(event.target.value) || 0,
                                    )
                                  }
                                  step="0.125"
                                  type="number"
                                  value={plannedDraft.sheetWidth}
                                />
                              </label>
                              <label className="field">
                                <span>Default spacing (in)</span>
                                <input
                                  min="0"
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'defaultSpacing',
                                      Number(event.target.value) || 0,
                                    )
                                  }
                                  step="0.0625"
                                  type="number"
                                  value={plannedDraft.defaultSpacing}
                                />
                              </label>
                              <label className="field">
                                <span>Default edge margin (in)</span>
                                <input
                                  min="0"
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'defaultEdgeMargin',
                                      Number(event.target.value) || 0,
                                    )
                                  }
                                  step="0.0625"
                                  type="number"
                                  value={plannedDraft.defaultEdgeMargin}
                                />
                              </label>
                              <label className="field">
                                <span>Color / finish</span>
                                <input
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'colorFinish',
                                      event.target.value,
                                    )
                                  }
                                  type="text"
                                  value={plannedDraft.colorFinish}
                                />
                              </label>
                              <label className="field">
                                <span>Cost per sheet</span>
                                <input
                                  min="0"
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'costPerSheet',
                                      event.target.value === ''
                                        ? null
                                        : Number(event.target.value),
                                    )
                                  }
                                  step="0.01"
                                  type="number"
                                  value={plannedDraft.costPerSheet ?? ''}
                                />
                              </label>
                              <label className="checkbox-field">
                                <input
                                  checked={plannedDraft.allowRotation}
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'allowRotation',
                                      event.target.checked,
                                    )
                                  }
                                  type="checkbox"
                                />
                                <span>Allow 90° rotation</span>
                              </label>
                              <label className="field field--wide">
                                <span>Notes</span>
                                <textarea
                                  onChange={(event) =>
                                    handleMaterialDraftChange(
                                      resolution.sourceMaterialName,
                                      'notes',
                                      event.target.value,
                                    )
                                  }
                                  value={plannedDraft.notes}
                                />
                              </label>
                            </div>
                            {draftMessage ? (
                              <p className="mapping-warning">{draftMessage}</p>
                            ) : null}
                            <div className="form-actions">
                              <button
                                className="secondary-button"
                                disabled={busy}
                                onClick={() =>
                                  handleCancelMaterialPlan(resolution.sourceMaterialName)
                                }
                                type="button"
                              >
                                Use existing material instead
                              </button>
                            </div>
                          </>
                        )}
                      </div>
                    );
                  })}
                </div>
              ) : (
                <div className="empty-state">
                  <strong>No material names detected yet</strong>
                  <span>
                    Refresh preview after the material column is mapped to inspect incoming material names.
                  </span>
                </div>
              )}
            </article>
          </div>
        </section>
      ) : null}

      <section className="panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Payload</p>
            <h3>{mappingSession ? 'Preview rows' : 'Imported rows'}</h3>
          </div>
          {canAddRows ? (
            <button
              className="secondary-button"
              disabled={!bridge.connected || busy}
              onClick={showAddRow ? cancelAddRow : startAddRow}
              type="button"
            >
              {showAddRow ? 'Cancel add' : 'Add row'}
            </button>
          ) : null}
        </div>

        <p className="section-note">
          {mappingSession
            ? 'Preview rows will replace the current import payload once you finalize this review.'
            : 'Use filters, inline edits, and add/delete actions here after the import is finalized.'}
        </p>

        <div className="stats-grid">
          <article className="stat-card">
            <span>Rows</span>
            <strong>{activeImportResponse.parts.length}</strong>
          </article>
          <article className="stat-card">
            <span>Valid</span>
            <strong>{counts.valid}</strong>
          </article>
          <article className="stat-card">
            <span>Warnings</span>
            <strong>{counts.warning}</strong>
          </article>
          <article className="stat-card">
            <span>Errors</span>
            <strong>{counts.error}</strong>
          </article>
        </div>

        {hasParts ? (
          <>
            <div className="toolbar-grid">
              <label className="field">
                <span>Filter by material</span>
                <select
                  disabled={busy}
                  onChange={(event) => setMaterialFilter(event.target.value)}
                  value={materialFilter}
                >
                  <option value="all">All materials</option>
                  {distinctMaterials.map((materialName) => (
                    <option key={materialName} value={materialName}>
                      {materialName}
                    </option>
                  ))}
                </select>
              </label>

              <label className="field">
                <span>Filter by status</span>
                <select
                  disabled={busy}
                  onChange={(event) =>
                    setStatusFilter(event.target.value as StatusFilter)
                  }
                  value={statusFilter}
                >
                  <option value="all">All statuses</option>
                  <option value="valid">Valid</option>
                  <option value="warning">Warning</option>
                  <option value="error">Error</option>
                </select>
              </label>

              <label className="field">
                <span>Sort rows</span>
                <select
                  disabled={busy}
                  onChange={(event) => setSortKey(event.target.value as SortKey)}
                  value={sortKey}
                >
                  <option value="row">Row order</option>
                  <option value="part">Part ID</option>
                  <option value="material">Material</option>
                  <option value="group">Group</option>
                  <option value="status">Validation status</option>
                  <option value="quantity">Quantity</option>
                  <option value="length">Length</option>
                  <option value="width">Width</option>
                </select>
              </label>

              <button
                className="secondary-button toolbar-button"
                disabled={busy}
                onClick={() =>
                  setSortDirection((current) =>
                    current === 'asc' ? 'desc' : 'asc',
                  )
                }
                type="button"
              >
                {sortDirection === 'asc' ? 'Ascending' : 'Descending'}
              </button>
            </div>

            <p className="section-note">
              Showing {filteredParts.length} of {activeImportResponse.parts.length} row(s).
            </p>
          </>
        ) : null}

        {showAddRow ? (
          <div className="editor-card">
            <div className="section-header">
              <div>
                <p className="eyebrow">New row</p>
                <h3>Add a row and validate it immediately</h3>
              </div>
            </div>
            <div className="row-editor-grid">
              <label className="field">
                <span>Part ID</span>
                <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      importedId: event.target.value,
                    }))
                  }
                  type="text"
                  value={addDraft.importedId ?? ''}
                />
              </label>
              <label className="field">
                <span>Length</span>
                <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      length: event.target.value,
                    }))
                  }
                  type="text"
                  value={addDraft.length ?? ''}
                />
              </label>
              <label className="field">
                <span>Width</span>
                <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      width: event.target.value,
                    }))
                  }
                  type="text"
                  value={addDraft.width ?? ''}
                />
              </label>
              <label className="field">
                <span>Quantity</span>
                <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      quantity: event.target.value,
                    }))
                  }
                  type="text"
                  value={addDraft.quantity ?? ''}
                />
              </label>
               <label className="field field--wide">
                 <span>Material</span>
                 <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      materialName: event.target.value,
                    }))
                  }
                  type="text"
                   value={addDraft.materialName ?? ''}
                 />
               </label>
               <label className="field field--wide">
                 <span>Group (optional)</span>
                 <input
                   onChange={(event) =>
                     setAddDraft((current) => ({
                       ...current,
                       group: event.target.value,
                     }))
                   }
                   type="text"
                   value={addDraft.group ?? ''}
                 />
               </label>
             </div>
            <div className="form-actions">
              <button
                className="secondary-button"
                disabled={busy}
                onClick={cancelAddRow}
                type="button"
              >
                Cancel
              </button>
              <button
                className="primary-button"
                disabled={!bridge.connected || busy}
                onClick={() => void saveAddRow()}
                type="button"
              >
                {partMutationBusy ? 'Validating…' : 'Save row'}
              </button>
            </div>
          </div>
        ) : null}

        {hasParts ? (
          filteredParts.length > 0 ? (
            <div className="table-shell">
              <table>
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Part</th>
                    <th>Length</th>
                    <th>Width</th>
                    <th>Qty</th>
                    <th>Material</th>
                    <th>Group</th>
                    <th>Status</th>
                    <th>Messages</th>
                    {showRowActions ? <th>Actions</th> : null}
                  </tr>
                </thead>
                <tbody>
                  {filteredParts.map((part) => {
                    const isEditing = editingRowId === part.rowId;
                    const draft = isEditing ? editingDraft ?? createDraft(part) : undefined;

                    return (
                      <tr key={part.rowId}>
                        <td>
                          <div className="row-meta">
                            <strong>{part.rowId}</strong>
                            <span>
                              {part.validationMessages.length > 0
                                ? `${part.validationMessages.length} issue(s)`
                                : 'Ready'}
                            </span>
                          </div>
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  importedId: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.importedId ?? ''}
                            />
                          ) : (
                            part.importedId || '—'
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  length: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.length ?? ''}
                            />
                          ) : (
                            getRowValue(part, 'length')
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  width: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.width ?? ''}
                            />
                          ) : (
                            getRowValue(part, 'width')
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  quantity: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.quantity ?? ''}
                            />
                          ) : (
                            getRowValue(part, 'quantity')
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  materialName: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.materialName ?? ''}
                            />
                          ) : (
                            part.materialName || '—'
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <input
                              className="table-input"
                              onChange={(event) =>
                                setEditingDraft((current) => ({
                                  ...(current ?? createDraft(part)),
                                  rowId: part.rowId,
                                  group: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.group ?? ''}
                            />
                          ) : (
                            getDisplayGroup(part)
                          )}
                        </td>
                        <td>
                          <div className="row-status">
                            <StatusPill
                              label={`${part.validationStatus} · ${part.validationMessages.length}`}
                              tone={getStatusTone(part.validationStatus)}
                            />
                          </div>
                        </td>
                        <td>
                          {part.validationMessages.length > 0 ? (
                            <ul className="row-message-list">
                              {part.validationMessages.map((message, index) => (
                                <li key={`${part.rowId}-${index}`}>{message}</li>
                              ))}
                            </ul>
                          ) : (
                            '—'
                          )}
                        </td>
                        {showRowActions ? (
                          <td>
                            <div className="table-actions">
                              {isEditing ? (
                                <>
                                  <button
                                    className="primary-button"
                                    disabled={!bridge.connected || busy}
                                    onClick={() => void saveEdit()}
                                    type="button"
                                  >
                                    {partMutationBusy ? 'Saving…' : 'Save'}
                                  </button>
                                  <button
                                    className="secondary-button"
                                    disabled={busy}
                                    onClick={cancelEdit}
                                    type="button"
                                  >
                                    Cancel
                                  </button>
                                </>
                              ) : (
                                <>
                                  {canEditRows ? (
                                    <button
                                      className="secondary-button"
                                      disabled={!bridge.connected || busy}
                                      onClick={() => beginEdit(part)}
                                      type="button"
                                    >
                                      Edit
                                    </button>
                                  ) : null}
                                  {canDeleteRows ? (
                                    <button
                                      className="secondary-button"
                                      disabled={!bridge.connected || busy}
                                      onClick={() => void requestDelete(part.rowId)}
                                      type="button"
                                    >
                                      Delete
                                    </button>
                                  ) : null}
                                </>
                              )}
                            </div>
                          </td>
                        ) : null}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="empty-state">
              <strong>No rows match the current review filters</strong>
              <span>Adjust material or validation filters to widen the table.</span>
            </div>
          )
        ) : (
          <div className="empty-state">
            <strong>{mappingSession ? 'No preview rows yet' : 'No parts imported'}</strong>
            <span>
              {mappingSession
                ? 'Complete the field mapping and refresh preview to inspect incoming rows.'
                : 'Choose a CSV or XLSX file to start an import review.'}
            </span>
          </div>
        )}
      </section>
    </div>
  );
}
