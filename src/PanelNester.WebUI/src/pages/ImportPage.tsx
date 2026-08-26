import { useEffect, useMemo, useState } from 'react';
import { MaterialCombobox } from '../components/MaterialCombobox';
import { StatusPill } from '../components/StatusPill';
import { ThemedSelect, type ThemedSelectOption } from '../components/ThemedSelect';
import { setWorkbookWorksheetSelected } from './workbookImportDraftState';
import {
  requiredImportFieldNames,
  type HostBridgeSnapshot,
  type ImportFieldName,
  type ImportMappingSession,
  type ImportSessionPhase,
  type ImportMaterialResolution,
  type Material,
  type MaterialDraft,
  type OptimizationGroup,
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
const pageSizeOptions = [100, 250, 500] as const;
const defaultPageSize = 250;
const manualAddMaterialComboboxId = 'import-manual-add-material';
const statusFilterOptions: ThemedSelectOption[] = [
  { value: 'all', label: 'All statuses' },
  { value: 'valid', label: 'Valid' },
  { value: 'warning', label: 'Warnings' },
  { value: 'error', label: 'Errors' },
];
const sortOptions: ThemedSelectOption[] = [
  { value: 'row', label: 'Row order' },
  { value: 'part', label: 'Part ID' },
  { value: 'material', label: 'Material' },
  { value: 'group', label: 'Part Group' },
  { value: 'status', label: 'Status' },
  { value: 'quantity', label: 'Quantity' },
  { value: 'length', label: 'Length' },
  { value: 'width', label: 'Width' },
];
const pageSizeSelectOptions: ThemedSelectOption[] = pageSizeOptions.map((option) => ({
  value: `${option}`,
  label: `${option}`,
}));

interface ImportPageProps {
  bridge: HostBridgeSnapshot;
  materials: Material[];
  selectedFilePath?: string;
  importResponse: ImportResponse;
  mappingSession?: ImportMappingSession;
  importMessage: string;
  importPhase?: ImportSessionPhase;
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
  canRunAllNesting: boolean;
  readyPartCount: number;
  readyMaterialCount: number;
  onImportFile: () => Promise<void>;
  onUpdateImportMappingSession: (session: ImportMappingSession) => void;
  onPreviewImportMapping: () => Promise<void>;
  onFinalizeImportMapping: () => Promise<void>;
  onCancelImportMapping: () => void | Promise<void>;
  onAddPartRow: (part: PartRowUpdate) => Promise<void>;
  onUpdatePartRow: (part: PartRowUpdate) => Promise<void>;
  onDeletePartRow: (rowId: string) => Promise<void>;
  onRunNesting: () => Promise<void>;
  onRunAllNesting: () => Promise<void>;
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  onActivateOptimizationGroup: (optimizationGroupId: string) => void;
  onMovePartToOptimizationGroup: (
    partRowId: string,
    targetOptimizationGroupId: string,
  ) => Promise<void>;
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
      isManual: part.isManual,
      sheetNumber: part.sheetNumber ?? '',
      rowNumber: part.rowNumber?.toString() ?? '',
      columnNumber: part.columnNumber?.toString() ?? '',
    };
  }

  return {
    importedId: '',
    length: '',
    width: '',
    quantity: '1',
    materialName: fallbackMaterialName,
    group: '',
    isManual: true,
    sheetNumber: '',
    rowNumber: '',
    columnNumber: '',
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
      return 'Part Group';
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

function ImportGlyph({ icon }: { icon: 'file' | 'batch' | 'filter' | 'sort' | 'row' }) {
  switch (icon) {
    case 'file':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M7 4.5h7l4.5 4.5v10a1.5 1.5 0 0 1-1.5 1.5h-10A1.5 1.5 0 0 1 5.5 19V6A1.5 1.5 0 0 1 7 4.5Z" />
          <path d="M14 4.5V9h4.5" />
          <path d="M9 13h6" />
          <path d="M9 16h6" />
        </svg>
      );
    case 'batch':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="m8 7 8 5-8 5z" />
        </svg>
      );
    case 'filter':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M4.5 7h15" />
          <path d="M7.5 12h9" />
          <path d="M10.5 17h3" />
        </svg>
      );
    case 'sort':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M5.5 7h13" />
          <path d="M5.5 12h9" />
          <path d="M5.5 17h5" />
        </svg>
      );
    case 'row':
    default:
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M7 7h10v10H7z" />
        </svg>
      );
  }
}

function SearchGlyph() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <circle cx="11" cy="11" r="5.5" />
      <path d="m15.5 15.5 3 3" />
    </svg>
  );
}

function RowMarker({ status }: { status: ValidationStatus }) {
  if (status === 'warning') {
    return (
      <svg aria-hidden="true" viewBox="0 0 24 24">
        <path d="M12 5.5 18.5 18H5.5z" />
      </svg>
    );
  }

  if (status === 'error') {
    return (
      <svg aria-hidden="true" viewBox="0 0 24 24">
        <circle cx="12" cy="12" r="6.5" />
      </svg>
    );
  }

  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M7 7h10v10H7z" />
    </svg>
  );
}

function matchesPartSearch(part: PartRow, query: string): boolean {
  const normalized = query.trim().toLowerCase();
  if (normalized.length === 0) {
    return true;
  }

  return [
    part.rowId,
    part.importedId,
    part.materialName,
    part.group ?? '',
    ...part.validationMessages,
  ].some((value) => value.toLowerCase().includes(normalized));
}

function formatDimensionValue(value: string): string {
  if (value.trim().length === 0) {
    return '—';
  }

  const parsed = Number.parseFloat(value);
  if (Number.isNaN(parsed)) {
    return value;
  }

  return parsed.toLocaleString(undefined, {
    minimumFractionDigits: parsed % 1 === 0 ? 0 : 2,
    maximumFractionDigits: 2,
  });
}

function getImportStatusLabel(status: ValidationStatus): string {
  switch (status) {
    case 'error':
      return 'Error';
    case 'warning':
      return 'Warning';
    case 'valid':
    default:
      return 'Valid';
  }
}

function getImportStatusNote(part: PartRow): string {
  if (part.validationMessages.length === 0) {
    return 'Ready for nesting';
  }

  const primaryMessage = part.validationMessages[0];
  return part.validationMessages.length > 1
    ? `${primaryMessage} (+${part.validationMessages.length - 1} more)`
    : primaryMessage;
}

export function ImportPage({
  bridge,
  materials,
  selectedFilePath,
  importResponse,
  mappingSession,
  importMessage,
  importPhase,
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
  canRunAllNesting,
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
  onRunAllNesting,
  optimizationGroups,
  activeOptimizationGroupId,
  onActivateOptimizationGroup,
  onMovePartToOptimizationGroup,
}: ImportPageProps) {
  const [editingRowId, setEditingRowId] = useState<string>();
  const [editingDraft, setEditingDraft] = useState<PartRowUpdate>();
  const [showAddRow, setShowAddRow] = useState(false);
  const [addDraft, setAddDraft] = useState<PartRowUpdate>({} as PartRowUpdate);
  const [searchQuery, setSearchQuery] = useState('');
  const [materialFilter, setMaterialFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [sortKey, setSortKey] = useState<SortKey>('row');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');
  const [pageSize, setPageSize] = useState<number>(defaultPageSize);
  const [currentPage, setCurrentPage] = useState(1);
  const [bulkWorksheetGroupId, setBulkWorksheetGroupId] = useState('');

  const activeImportResponse = mappingSession?.preview ?? importResponse;
  const displayFilePath = mappingSession?.filePath ?? selectedFilePath;
  const hasPendingImportReview = Boolean(mappingSession);
  const worksheetDrafts = mappingSession?.worksheets ?? [];
  const selectedWorksheetDrafts = worksheetDrafts.filter((draft) => draft.selected);
  const showRowActions = !hasPendingImportReview && (canEditRows || canDeleteRows);
  const hasParts = activeImportResponse.parts.length > 0;
  const busy = importBusy || partMutationBusy;
  const optimizationGroupOptions = useMemo<ThemedSelectOption[]>(
    () =>
      optimizationGroups.map((group) => ({
        value: group.optimizationGroupId,
        label: group.name,
      })),
    [optimizationGroups],
  );
  const worksheetOptimizationGroupOptions = useMemo(() => {
    const options = new Map(
      optimizationGroups.map((group) => [group.optimizationGroupId, group.name]),
    );
    for (const draft of worksheetDrafts) {
      if (draft.selected) {
        options.set(draft.optimizationGroupId, draft.optimizationGroupName);
      }
    }
    return Array.from(options, ([value, label]) => ({ value, label }));
  }, [optimizationGroups, worksheetDrafts]);
  const optimizationGroupByPartRowId = useMemo(() => {
    const ownership = new Map<string, string>();
    for (const group of optimizationGroups) {
      for (const part of group.parts) {
        ownership.set(part.rowId, group.optimizationGroupId);
      }
    }
    return ownership;
  }, [optimizationGroups]);
  const { distinctMaterials, counts } = useMemo(() => {
    const materialNames = new Set<string>();
    let valid = 0;
    let warning = 0;
    let error = 0;

    for (const part of activeImportResponse.parts) {
      const materialName = part.materialName.trim();
      if (materialName.length > 0) {
        materialNames.add(materialName);
      }

      if (part.validationStatus === 'valid') {
        valid += 1;
      } else if (part.validationStatus === 'warning') {
        warning += 1;
      } else {
        error += 1;
      }
    }

    return {
      distinctMaterials: Array.from(materialNames).sort((left, right) =>
        compareStrings(left, right),
      ),
      counts: {
        valid,
        warning,
        error,
      },
    };
  }, [activeImportResponse.parts]);
  const materialLibraryNames = useMemo(
    () =>
      Array.from(
        new Set(
          materials
            .map((material) => material.name.trim())
            .filter((name) => name.length > 0),
        ),
      ).sort((left, right) => compareStrings(left, right)),
    [materials],
  );
  const materialFilterOptions = useMemo<ThemedSelectOption[]>(
    () => [
      { value: 'all', label: 'All materials' },
      ...distinctMaterials.map((materialName) => ({
        value: materialName,
        label: materialName,
      })),
    ],
    [distinctMaterials],
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
      const matchesQuery = matchesPartSearch(part, searchQuery);

      return matchesMaterial && matchesStatus && matchesQuery;
    });

    return sortParts(filtered, sortKey, sortDirection);
  }, [
    activeImportResponse.parts,
    materialFilter,
    searchQuery,
    sortDirection,
    sortKey,
    statusFilter,
  ]);
  const shouldPaginate = filteredParts.length > pageSize;
  const totalPages = shouldPaginate
    ? Math.max(1, Math.ceil(filteredParts.length / pageSize))
    : 1;
  const pagedParts = useMemo(() => {
    if (!shouldPaginate) {
      return filteredParts;
    }

    const pageStart = (currentPage - 1) * pageSize;
    return filteredParts.slice(pageStart, pageStart + pageSize);
  }, [currentPage, filteredParts, pageSize, shouldPaginate]);
  const renderedRangeStart =
    filteredParts.length === 0
      ? 0
      : shouldPaginate
        ? (currentPage - 1) * pageSize + 1
        : 1;
  const renderedRangeEnd =
    filteredParts.length === 0
      ? 0
      : shouldPaginate
        ? renderedRangeStart + pagedParts.length - 1
        : filteredParts.length;

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
    hasPendingImportReview &&
    (mappingSession?.preview.columnMappings.length === 0 || allRequiredFieldsMapped) &&
    (!mappingSession?.workbook || selectedWorksheetDrafts.length > 0) &&
    !busy;
  const canFinalizeMapping =
    hasPendingImportReview &&
    !(mappingSession?.hasPendingChanges ?? true) &&
    allRequiredFieldsMapped &&
    unresolvedImportMaterials === 0 &&
    !hasInvalidNewMaterialDraft &&
    (worksheetDrafts.length === 0 ||
      (selectedWorksheetDrafts.length > 0 &&
        selectedWorksheetDrafts.every((draft) => {
          const materialMappings = new Map(
            draft.options.materialMappings.map((mapping) => [
              mapping.sourceMaterialName,
              mapping.targetMaterialId ?? '',
            ]),
          );
          const plannedMaterials = new Set(
            draft.newMaterials.map((material) => material.sourceMaterialName),
          );
          return !draft.hasPendingChanges &&
            requiredImportFieldNames.every((field) =>
              draft.options.columnMappings.some(
                (mapping) =>
                  mapping.targetField === field && mapping.sourceColumn.trim().length > 0,
              ),
            ) &&
            draft.preview.materialResolutions.every(
              (resolution) =>
                plannedMaterials.has(resolution.sourceMaterialName) ||
                (materialMappings.get(resolution.sourceMaterialName) ??
                  resolution.resolvedMaterialId ??
                  '').trim().length > 0,
            ) &&
            draft.newMaterials.every(
              (material) => validateMaterialDraft(material.material) === null,
            );
        }))) &&
    !busy;

  const activateWorksheet = (worksheetName: string) => {
    if (!mappingSession?.worksheets) {
      return;
    }

    const draft = mappingSession.worksheets.find(
      (item) => item.worksheet.worksheetName === worksheetName,
    );
    if (!draft) {
      return;
    }

    onUpdateImportMappingSession({
      ...mappingSession,
      activeWorksheetName: worksheetName,
      preview: draft.preview,
      options: draft.options,
      newMaterials: draft.newMaterials,
      hasPendingChanges: draft.hasPendingChanges,
    });
  };

  const setWorksheetSelected = (worksheetName: string, selected: boolean) => {
    if (!mappingSession?.worksheets) {
      return;
    }

    const worksheets = setWorkbookWorksheetSelected(
      mappingSession.worksheets,
      worksheetName,
      selected,
    );
    const nextActive = selected
      ? worksheetName
      : mappingSession.activeWorksheetName === worksheetName
        ? worksheets.find((draft) => draft.selected)?.worksheet.worksheetName
        : mappingSession.activeWorksheetName;
    const activeDraft = worksheets.find(
      (draft) => draft.worksheet.worksheetName === nextActive,
    );
    onUpdateImportMappingSession({
      ...mappingSession,
      worksheets,
      activeWorksheetName: nextActive,
      preview: activeDraft?.preview ?? mappingSession.preview,
      options: activeDraft?.options ?? mappingSession.options,
      newMaterials: activeDraft?.newMaterials ?? mappingSession.newMaterials,
      hasPendingChanges: activeDraft?.hasPendingChanges ?? true,
    });
  };

  const setAllWorksheetsSelected = (selected: boolean) => {
    if (!mappingSession?.worksheets) {
      return;
    }

    const worksheets = mappingSession.worksheets.map((draft) => ({ ...draft, selected }));
    const activeDraft = selected ? worksheets[0] : undefined;
    onUpdateImportMappingSession({
      ...mappingSession,
      worksheets,
      activeWorksheetName: activeDraft?.worksheet.worksheetName,
      preview: activeDraft?.preview ?? mappingSession.preview,
      options: activeDraft?.options ?? mappingSession.options,
      newMaterials: activeDraft?.newMaterials ?? mappingSession.newMaterials,
      hasPendingChanges: activeDraft?.hasPendingChanges ?? true,
    });
  };

  const assignWorksheetGroup = (worksheetName: string, optimizationGroupId: string) => {
    if (!mappingSession?.worksheets) {
      return;
    }

    const groupName = worksheetOptimizationGroupOptions.find(
      (option) => option.value === optimizationGroupId,
    )?.label ?? worksheetName;
    onUpdateImportMappingSession({
      ...mappingSession,
      worksheets: mappingSession.worksheets.map((draft) =>
        draft.worksheet.worksheetName === worksheetName
          ? { ...draft, optimizationGroupId, optimizationGroupName: groupName }
          : draft,
      ),
    });
  };

  const assignSelectedWorksheetsToGroup = () => {
    if (!mappingSession?.worksheets || bulkWorksheetGroupId.length === 0) {
      return;
    }

    const groupName = worksheetOptimizationGroupOptions.find(
      (option) => option.value === bulkWorksheetGroupId,
    )?.label;
    if (!groupName) {
      return;
    }

    onUpdateImportMappingSession({
      ...mappingSession,
      worksheets: mappingSession.worksheets.map((draft) =>
        draft.selected
          ? {
              ...draft,
              optimizationGroupId: bulkWorksheetGroupId,
              optimizationGroupName: groupName,
            }
          : draft,
      ),
    });
  };

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

  useEffect(() => {
    setCurrentPage(1);
  }, [
    displayFilePath,
    hasPendingImportReview,
    materialFilter,
    pageSize,
    searchQuery,
    sortDirection,
    sortKey,
    statusFilter,
  ]);

  useEffect(() => {
    setCurrentPage((current) => Math.min(current, totalPages));
  }, [totalPages]);

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

  const recordCountLabel =
    filteredParts.length === activeImportResponse.parts.length
      ? `Showing ${filteredParts.length} records`
      : `Showing ${filteredParts.length} of ${activeImportResponse.parts.length} records`;

  return (
    <div className="page-grid import-workspace">
      <section className="module-hero module-hero--import">
        <div className="module-hero__copy">
          <p className="module-hero__breadcrumb">Workspace / Module</p>
          <h1>Import &amp; Panel Management</h1>
          <p className="module-hero__intro">{importMessage}</p>
          {importBusy && importPhase ? (
            <div className="import-session-progress" role="status">
              <span>{`${importPhase[0].toUpperCase()}${importPhase.slice(1)}…`}</span>
              <progress aria-label={`Import ${importPhase}`} />
            </div>
          ) : null}
          {displayFilePath ? (
            <p className="module-hero__meta import-path">Source file: {displayFilePath}</p>
          ) : null}
          <p className="module-hero__meta">
            {hasPendingImportReview
              ? 'Current imported rows remain unchanged until you finalize this review.'
              : batchNestingEnabled
                ? `Imported rows carry their material names. Batch nesting will group ${readyPartCount} ready row(s) across ${readyMaterialCount} ready material group(s).`
                : nestingMessage}
          </p>
        </div>
        <div className="module-hero__actions">
          <button
            className="secondary-button module-action-button"
            disabled={!bridge.connected || busy || !canImportFiles}
            onClick={() => void onImportFile()}
            type="button"
          >
            <ImportGlyph icon="file" />
            <span>
              {importBusy
                ? 'Working…'
                : hasPendingImportReview
                  ? 'Choose another file'
                  : 'Choose file'}
            </span>
          </button>
          {importBusy ? (
            <button
              className="secondary-button module-action-button"
              disabled={!bridge.connected}
              onClick={() => void onCancelImportMapping()}
              type="button"
            >
              <span>Cancel import</span>
            </button>
          ) : null}
          <button
            className="primary-button module-action-button module-action-button--primary"
            disabled={!bridge.connected || !canRunNesting || nestingBusy || busy}
            onClick={() => void onRunNesting()}
            type="button"
          >
            <ImportGlyph icon="batch" />
            <span>
              {nestingBusy
                ? 'Nesting…'
                : batchNestingEnabled
                  ? 'Run active group'
                  : 'Run nesting'}
            </span>
          </button>
          {batchNestingEnabled ? (
            <button
              className="secondary-button module-action-button"
              disabled={!bridge.connected || !canRunAllNesting || nestingBusy || busy}
              onClick={() => void onRunAllNesting()}
              type="button"
            >
              <ImportGlyph icon="batch" />
              <span>{nestingBusy ? 'Nesting…' : 'Run All'}</span>
            </button>
          ) : null}
        </div>
      </section>

      {mappingSession ? (
        <section className="module-panel">
          {mappingSession.workbook ? (
            <article className="editor-card workbook-discovery">
              <div className="section-header">
                <div>
                  <p className="eyebrow">Workbook discovery</p>
                  <h3>Select Worksheets and assign Optimization Groups</h3>
                </div>
                <StatusPill
                  label={`${selectedWorksheetDrafts.length} selected`}
                  tone={selectedWorksheetDrafts.length > 0 ? 'ok' : 'warn'}
                />
              </div>
              <p className="section-note">
                Visible, nonempty Worksheets stay in Workbook order. Select only the Worksheets
                to finalize; draft mappings are retained when a Worksheet is deselected.
              </p>
              {mappingSession.workbook.macrosPresent ? (
                <p className="mapping-warning">
                  Macros are not run. OptiFab reads worksheet values only.
                </p>
              ) : null}
              <div className="button-row">
                <button
                  className="secondary-button"
                  disabled={busy}
                  onClick={() => setAllWorksheetsSelected(true)}
                  type="button"
                >
                  Select all Worksheets
                </button>
                <button
                  className="secondary-button"
                  disabled={busy || selectedWorksheetDrafts.length === 0}
                  onClick={() => setAllWorksheetsSelected(false)}
                  type="button"
                >
                  Clear selection
                </button>
              </div>
              <div className="button-row">
                <label className="field">
                  <span>Move selected Worksheets to</span>
                  <select
                    aria-label="Optimization Group for selected Worksheets"
                    disabled={busy || selectedWorksheetDrafts.length === 0}
                    onChange={(event) => setBulkWorksheetGroupId(event.target.value)}
                    value={bulkWorksheetGroupId}
                  >
                    <option value="">Choose an Optimization Group</option>
                    {worksheetOptimizationGroupOptions.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  className="secondary-button"
                  disabled={
                    busy ||
                    selectedWorksheetDrafts.length === 0 ||
                    bulkWorksheetGroupId.length === 0
                  }
                  onClick={assignSelectedWorksheetsToGroup}
                  type="button"
                >
                  Assign selected Worksheets
                </button>
              </div>
              <div className="mapping-resolution-list">
                {worksheetDrafts.map((draft) => (
                  <div
                    className="mapping-resolution-card"
                    key={`${draft.worksheet.originalPosition}-${draft.worksheet.worksheetName}`}
                  >
                    <div className="mapping-resolution-card__header">
                      <label className="checkbox-field">
                        <input
                          checked={draft.selected}
                          disabled={busy}
                          onChange={(event) =>
                            setWorksheetSelected(
                              draft.worksheet.worksheetName,
                              event.target.checked,
                            )
                          }
                          type="checkbox"
                        />
                        <span>
                          {draft.worksheet.originalPosition}. {draft.worksheet.worksheetName}
                        </span>
                      </label>
                      <button
                        className="secondary-button"
                        disabled={busy || !draft.selected}
                        onClick={() => activateWorksheet(draft.worksheet.worksheetName)}
                        type="button"
                      >
                        {mappingSession.activeWorksheetName === draft.worksheet.worksheetName
                          ? 'Configuring'
                          : 'Configure'}
                      </button>
                    </div>
                    {draft.selected ? (
                      <label className="field">
                        <span>Optimization Group</span>
                        <select
                          disabled={busy}
                          onChange={(event) =>
                            assignWorksheetGroup(
                              draft.worksheet.worksheetName,
                              event.target.value,
                            )
                          }
                          value={draft.optimizationGroupId}
                        >
                          {worksheetOptimizationGroupOptions.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </label>
                    ) : null}
                  </div>
                ))}
              </div>
            </article>
          ) : null}
          <div className="module-panel__header">
            <div>
              <p className="eyebrow">Column Mapping</p>
              <h3>Column mapping &amp; expected fields</h3>
            </div>
            <div className="button-row">
              <button
                className="secondary-button"
                disabled={!bridge.connected}
                onClick={() => void onCancelImportMapping()}
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

          <div className="stats-grid module-stats-grid">
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
                Map each required field to one source column from the file header. Part Group
                is optional and can stay blank to keep imported rows ungrouped.
              </p>

              <div className="module-mapping-grid">
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
                    <div
                      className={
                        hasSelection
                          ? 'module-mapping-tile module-mapping-tile--active'
                          : 'module-mapping-tile'
                      }
                      key={mapping.targetField}
                    >
                      <span>{getFieldLabel(mapping.targetField)}</span>
                      <label className="field">
                        <select
                          className="module-mapping-tile__select"
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
                          {(mappingSession.preview.sourceColumns.length > 0
                            ? mappingSession.preview.sourceColumns
                            : mappingSession.preview.availableColumns.map((column) => ({
                                address: column,
                                heading: column,
                              }))).map((column) => (
                            <option key={column.address} value={column.address}>
                              {column.heading
                                ? `${column.address} — ${column.heading}`
                                : column.address}
                            </option>
                          ))}
                        </select>
                      </label>
                      <strong>{statusLabel}</strong>
                      <small>
                        {hasSelection ? 'Mapped' : isRequiredField ? 'Required' : 'Optional'}
                      </small>
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

      <section className="module-panel module-panel--table">
        <div className="module-panel__header">
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

        <div className="stats-grid module-stats-grid">
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
            <div className="module-table-toolbar">
              <label className="module-search">
                <SearchGlyph />
                <input
                  disabled={busy}
                  onChange={(event) => setSearchQuery(event.target.value)}
                  placeholder="Filter parts by ID, material, or Part Group..."
                  type="search"
                  value={searchQuery}
                />
              </label>

              <div className="module-toolbar-group">
                <ThemedSelect
                  ariaLabel="Filter imported rows by material"
                  className="module-filter-chip"
                  disabled={busy}
                  icon={<ImportGlyph icon="filter" />}
                  onChange={setMaterialFilter}
                  options={materialFilterOptions}
                  value={materialFilter}
                />

                <ThemedSelect
                  ariaLabel="Filter imported rows by validation status"
                  className="module-filter-chip"
                  disabled={busy}
                  icon={<ImportGlyph icon="filter" />}
                  onChange={(value) => setStatusFilter(value as StatusFilter)}
                  options={statusFilterOptions}
                  value={statusFilter}
                />

                <ThemedSelect
                  ariaLabel="Sort imported rows"
                  className="module-filter-chip"
                  disabled={busy}
                  icon={<ImportGlyph icon="sort" />}
                  onChange={(value) => setSortKey(value as SortKey)}
                  options={sortOptions}
                  value={sortKey}
                />

                <button
                  className="secondary-button module-icon-button"
                  disabled={busy}
                  onClick={() =>
                    setSortDirection((current) =>
                      current === 'asc' ? 'desc' : 'asc',
                    )
                  }
                  type="button"
                  title={sortDirection === 'asc' ? 'Ascending sort' : 'Descending sort'}
                >
                  <ImportGlyph icon="sort" />
                </button>
              </div>

              <div className="module-table-toolbar__summary">
                <span>{recordCountLabel}</span>
              </div>
            </div>

            <p className="section-note">
              {shouldPaginate
                ? `Showing rows ${renderedRangeStart}-${renderedRangeEnd} of ${filteredParts.length} filtered row(s) (${activeImportResponse.parts.length} total).`
                : `Showing ${filteredParts.length} of ${activeImportResponse.parts.length} row(s).`}
            </p>

            {shouldPaginate ? (
              <div className="pagination-bar">
                <div className="pagination-summary">
                  <strong>
                    Page {currentPage} of {totalPages}
                  </strong>
                  <span>
                    Rendering {pagedParts.length} row(s) at a time keeps large imports responsive.
                  </span>
                </div>

                <div className="pagination-controls">
                  <div className="field pagination-field">
                    <span>Rows per page</span>
                    <ThemedSelect
                      ariaLabel="Rows per page"
                      disabled={busy}
                      onChange={(value) => setPageSize(Number(value) || defaultPageSize)}
                      options={pageSizeSelectOptions}
                      value={`${pageSize}`}
                    />
                  </div>

                  <div className="pagination-buttons">
                    <button
                      className="secondary-button"
                      disabled={currentPage === 1}
                      onClick={() => setCurrentPage(1)}
                      type="button"
                    >
                      First
                    </button>
                    <button
                      className="secondary-button"
                      disabled={currentPage === 1}
                      onClick={() => setCurrentPage((page) => Math.max(1, page - 1))}
                      type="button"
                    >
                      Previous
                    </button>
                    <button
                      className="secondary-button"
                      disabled={currentPage === totalPages}
                      onClick={() =>
                        setCurrentPage((page) => Math.min(totalPages, page + 1))
                      }
                      type="button"
                    >
                      Next
                    </button>
                    <button
                      className="secondary-button"
                      disabled={currentPage === totalPages}
                      onClick={() => setCurrentPage(totalPages)}
                      type="button"
                    >
                      Last
                    </button>
                  </div>
                </div>
              </div>
            ) : null}
          </>
        ) : null}

        {showAddRow ? (
          <div className="editor-card module-add-row-card">
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
                <MaterialCombobox
                  disabled={busy}
                  inputId={manualAddMaterialComboboxId}
                  materials={materialLibraryNames}
                  onChange={(value) =>
                    setAddDraft((current) => ({
                      ...current,
                      materialName: value,
                    }))
                  }
                  value={addDraft.materialName ?? ''}
                />
              </label>
              <label className="field field--wide">
                <span>Optimization Group</span>
                <ThemedSelect
                  ariaLabel="Active Optimization Group for new manual parts"
                  disabled={busy || optimizationGroups.length === 0}
                  onChange={onActivateOptimizationGroup}
                  options={optimizationGroupOptions}
                  value={
                    activeOptimizationGroupId ??
                    optimizationGroups[0]?.optimizationGroupId ??
                    ''
                  }
                />
              </label>
              <label className="field field--wide">
                <span>Part Group (optional)</span>
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
              <label className="field">
                <span>Sheet Number</span>
                <input
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      sheetNumber: event.target.value,
                    }))
                  }
                  type="text"
                  value={addDraft.sheetNumber ?? ''}
                />
              </label>
              <label className="field">
                <span>Row Number</span>
                <input
                  min={1}
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      rowNumber: event.target.value,
                    }))
                  }
                  type="number"
                  value={addDraft.rowNumber ?? ''}
                />
              </label>
              <label className="field">
                <span>Column Number</span>
                <input
                  min={1}
                  onChange={(event) =>
                    setAddDraft((current) => ({
                      ...current,
                      columnNumber: event.target.value,
                    }))
                  }
                  type="number"
                  value={addDraft.columnNumber ?? ''}
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
            <div className="table-shell module-table-shell">
              <table className="module-table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Part reference</th>
                    <th>Length</th>
                    <th>Width</th>
                    <th>Qty</th>
                    <th>Material spec</th>
                    <th>Part Group</th>
                    <th>Optimization Group</th>
                    <th>Sheet</th>
                    <th>Cell</th>
                    <th>Status</th>
                    {showRowActions ? <th>Actions</th> : null}
                  </tr>
                </thead>
                <tbody>
                  {pagedParts.map((part) => {
                    const isEditing = editingRowId === part.rowId;
                    const draft = isEditing ? editingDraft ?? createDraft(part) : undefined;

                    return (
                      <tr key={part.rowId}>
                        <td>
                          <div className="module-row-id">
                            <span className="module-row-id__marker">
                              <RowMarker status={part.validationStatus} />
                            </span>
                            <div className="row-meta">
                              <strong>{part.rowId}</strong>
                              <span>
                                {part.validationMessages.length > 0
                                  ? `${part.validationMessages.length} issue(s)`
                                  : 'Ready'}
                              </span>
                            </div>
                          </div>
                        </td>
                        <td className="module-table__part">
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
                            <strong>{part.importedId || '—'}</strong>
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
                            formatDimensionValue(getRowValue(part, 'length'))
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
                            formatDimensionValue(getRowValue(part, 'width'))
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
                            <span className="module-table__tag">{part.materialName || '—'}</span>
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
                          {part.isManual ? (
                            <ThemedSelect
                              ariaLabel={`Optimization Group for ${part.importedId || part.rowId}`}
                              disabled={
                                busy ||
                                hasPendingImportReview ||
                                optimizationGroups.length < 2
                              }
                              onChange={(optimizationGroupId) =>
                                void onMovePartToOptimizationGroup(
                                  part.rowId,
                                  optimizationGroupId,
                                )
                              }
                              options={optimizationGroupOptions}
                              value={
                                optimizationGroupByPartRowId.get(part.rowId) ??
                                activeOptimizationGroupId ??
                                optimizationGroups[0]?.optimizationGroupId ??
                                ''
                              }
                            />
                          ) : (
                            optimizationGroups.find(
                              (group) =>
                                group.optimizationGroupId ===
                                optimizationGroupByPartRowId.get(part.rowId),
                            )?.name ?? 'Unassigned'
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
                                  sheetNumber: event.target.value,
                                }))
                              }
                              type="text"
                              value={draft?.sheetNumber ?? ''}
                            />
                          ) : (
                            part.sheetNumber ?? '—'
                          )}
                        </td>
                        <td>
                          {isEditing ? (
                            <div className="table-actions">
                              <input
                                className="table-input"
                                min={1}
                                onChange={(event) =>
                                  setEditingDraft((current) => ({
                                    ...(current ?? createDraft(part)),
                                    rowId: part.rowId,
                                    rowNumber: event.target.value,
                                  }))
                                }
                                type="number"
                                value={draft?.rowNumber ?? ''}
                              />
                              <input
                                className="table-input"
                                min={1}
                                onChange={(event) =>
                                  setEditingDraft((current) => ({
                                    ...(current ?? createDraft(part)),
                                    rowId: part.rowId,
                                    columnNumber: event.target.value,
                                  }))
                                }
                                type="number"
                                value={draft?.columnNumber ?? ''}
                              />
                            </div>
                          ) : part.rowNumber != null && part.columnNumber != null ? (
                            `${part.rowNumber}, ${part.columnNumber}`
                          ) : (
                            '—'
                          )}
                        </td>
                        <td>
                          <div className="module-status-stack">
                            <span
                              className={`module-status-chip module-status-chip--${part.validationStatus}`}
                              title={
                                part.validationMessages.length > 0
                                  ? part.validationMessages.join(' | ')
                                  : 'Ready'
                              }
                            >
                              {getImportStatusLabel(part.validationStatus)}
                            </span>
                            <span className="module-status-note">
                              {getImportStatusNote(part)}
                            </span>
                          </div>
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
                                      className="module-table-action"
                                      disabled={!bridge.connected || busy}
                                      onClick={() => beginEdit(part)}
                                      type="button"
                                    >
                                      Edit
                                    </button>
                                  ) : null}
                                  {canDeleteRows ? (
                                    <button
                                      className="module-table-action module-table-action--danger"
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
                : 'Choose a CSV file or Excel Workbook to start an import review.'}
            </span>
          </div>
        )}

        <div className="module-table-footer">
          <div className="module-legend">
            <span className="module-legend__item">
              <i className="module-legend__dot module-legend__dot--valid" />
              {counts.valid} Valid
            </span>
            <span className="module-legend__item">
              <i className="module-legend__dot module-legend__dot--error" />
              {counts.error} Errors
            </span>
            <span className="module-legend__item">
              <i className="module-legend__dot module-legend__dot--warning" />
              {counts.warning} Warnings
            </span>
          </div>

          {shouldPaginate ? (
            <span className="module-table-footer__page">
              Page {currentPage} of {totalPages}
            </span>
          ) : null}
        </div>
      </section>
    </div>
  );
}
