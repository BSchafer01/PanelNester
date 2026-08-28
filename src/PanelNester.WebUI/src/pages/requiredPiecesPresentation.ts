import type {
  ImportWorksheetDraft,
  OptimizationGroup,
  RequiredPiece,
  ValidationError,
  ValidationWarning,
} from '../types/contracts';

export const requiredPiecePageSizes = [25, 50, 100] as const;
export const requiredStockLengthFields = ['Quantity', 'Length', 'Profile Number'] as const;

export type RequiredPieceSourceFilter = 'all' | 'manual' | 'worksheet';
export type RequiredPieceStatusFilter = 'all' | 'valid' | 'warning' | 'error';

export interface RequiredPieceRow {
  group: OptimizationGroup;
  piece: RequiredPiece;
  sourceLabel: string;
  status: Exclude<RequiredPieceStatusFilter, 'all'>;
}

export interface RequiredPieceFilters {
  query: string;
  optimizationGroupId: string;
  source: RequiredPieceSourceFilter;
  status: RequiredPieceStatusFilter;
}

export interface ImportReviewIssue {
  kind: 'error' | 'warning';
  worksheetName: string;
  issue: ValidationError | ValidationWarning;
}

export interface ImportReviewSummary {
  selectedWorksheets: ImportWorksheetDraft[];
  rows: RequiredPiece[];
  validCount: number;
  excludedCount: number;
  issues: ImportReviewIssue[];
  unresolvedErrors: ImportReviewIssue[];
  warnings: ImportReviewIssue[];
  optimizationGroupCount: number;
}

export interface StockLengthImportPlan {
  sourceRowCount: number;
  validSourceRowCount: number;
  outputEntryCount: number;
  totalPieceQuantity: number;
  createdEntryCount: number;
  updatedEntryCount: number;
  skippedSourceRowCount: number;
  resultingEntries: RequiredPiece[];
  sourceRows: RequiredPiece[];
  aggregationRule: string;
}

export const stockLengthAggregationRule =
  'Source rows are combined when Optimization Group, Length, Profile Number, Part Name, Finish, and Part Number match after text normalization; quantities are summed and Source References are retained.';

export function buildStockLengthImportPlan(drafts: ImportWorksheetDraft[]): StockLengthImportPlan {
  const selected = drafts.filter((draft) => draft.selected);
  const excludedIds = new Set(selected.flatMap((draft) =>
    draft.excludedSourceRows.map((row) => row.rowId)));
  const sourceRows = selected.flatMap((draft) => {
    const overrides = new Map(draft.partOverrides.flatMap((override) => {
      const piece = override.currentRequiredPiece;
      return piece ? [[override.rowId, piece] as const] : [];
    }));
    return (draft.preview.requiredPieces ?? []).map((piece) => ({
      ...(overrides.get(piece.requiredPieceId) ?? piece),
      optimizationGroupId: draft.optimizationGroupId,
    }));
  });
  const validRows = sourceRows.filter((piece) =>
    !excludedIds.has(piece.requiredPieceId) && piece.validationStatus !== 'error');
  const resultingEntries: RequiredPiece[] = [];
  const entryIndex = new Map<string, number>();
  for (const piece of validRows) {
    const groupId = (piece as RequiredPiece & { optimizationGroupId?: string }).optimizationGroupId ?? '';
    const normalize = (value?: string | null) => value?.trim().toLocaleUpperCase() ?? '';
    const key = JSON.stringify([
      groupId,
      piece.length,
      normalize(piece.profileNumber),
      normalize(piece.partName),
      normalize(piece.finish),
      normalize(piece.partNumber),
    ]);
    const existingIndex = entryIndex.get(key);
    if (existingIndex == null) {
      entryIndex.set(key, resultingEntries.length);
      resultingEntries.push({
        ...piece,
        profileNumber: piece.profileNumber.trim(),
        partName: piece.partName?.trim() || null,
        finish: piece.finish?.trim() || null,
        partNumber: piece.partNumber?.trim() || null,
      });
    } else {
      const existing = resultingEntries[existingIndex];
      const quantity = existing.quantity + piece.quantity;
      resultingEntries[existingIndex] = {
        ...existing,
        quantity,
        quantityText: `${quantity}`,
        sourceReferences: [...existing.sourceReferences, ...piece.sourceReferences],
      };
    }
  }
  const sourceReferenceCount = sourceRows.reduce(
    (count, piece) => count + Math.max(1, piece.sourceReferences.length), 0);
  const sourceRowIds = new Set(sourceRows.map((piece) => piece.requiredPieceId));
  const excludedRowsMissingFromPreview = [...excludedIds].filter((rowId) => !sourceRowIds.has(rowId)).length;
  const skippedSourceRowCount = excludedIds.size + sourceRows.filter(
    (piece) => !excludedIds.has(piece.requiredPieceId) && piece.validationStatus === 'error').length;
  return {
    sourceRowCount: sourceReferenceCount + excludedRowsMissingFromPreview,
    validSourceRowCount: validRows.reduce(
      (count, piece) => count + Math.max(1, piece.sourceReferences.length), 0),
    outputEntryCount: resultingEntries.length,
    totalPieceQuantity: resultingEntries.reduce((count, piece) => count + piece.quantity, 0),
    createdEntryCount: 0,
    updatedEntryCount: 0,
    skippedSourceRowCount,
    resultingEntries,
    sourceRows,
    aggregationRule: stockLengthAggregationRule,
  };
}

export function fileNameFromImportPath(path: string): string {
  const segments = path.split(/[\\/]/).filter(Boolean);
  return segments[segments.length - 1] ?? path;
}

export function flattenRequiredPieces(groups: OptimizationGroup[]): RequiredPieceRow[] {
  return groups.flatMap((group) => group.requiredPieces.map((piece) => ({
    group,
    piece,
    sourceLabel: piece.isManual
      ? 'Manual'
      : formatRequiredPieceSourceReferences(piece),
    status: piece.validationStatus ?? 'valid',
  })));
}

export function formatRequiredPieceSourceReferences(piece: RequiredPiece): string {
  const counts = new Map<string, number>();
  for (const reference of piece.sourceReferences) {
    if (!reference.worksheetName) continue;
    counts.set(reference.worksheetName, (counts.get(reference.worksheetName) ?? 0) + 1);
  }
  return Array.from(counts, ([worksheetName, count]) =>
    `${worksheetName} · ${count} source row${count === 1 ? '' : 's'}`).join(', ') || 'Imported';
}

export function filterRequiredPieces(
  rows: RequiredPieceRow[],
  filters: RequiredPieceFilters,
): RequiredPieceRow[] {
  const query = filters.query.trim().toLocaleLowerCase();
  return rows.filter(({ group, piece, sourceLabel, status }) => {
    if (filters.optimizationGroupId && group.optimizationGroupId !== filters.optimizationGroupId) {
      return false;
    }
    if (filters.source === 'manual' && !piece.isManual) return false;
    if (filters.source === 'worksheet' && piece.isManual) return false;
    if (filters.status !== 'all' && status !== filters.status) return false;
    if (!query) return true;
    const searchable = [
      group.name,
      piece.profileNumber,
      piece.finish,
      piece.partName,
      piece.partNumber,
      sourceLabel,
      ...piece.sourceReferences.flatMap((reference) => [
        reference.worksheetName,
        `${reference.worksheetName}!${reference.physicalRow}`,
      ]),
    ].filter(Boolean).join(' ').toLocaleLowerCase();
    return searchable.includes(query);
  });
}

export function paginateRequiredPieces<T>(
  rows: T[],
  page: number,
  pageSize: number,
): { rows: T[]; page: number; pageCount: number; first: number; last: number } {
  const pageCount = Math.max(1, Math.ceil(rows.length / pageSize));
  const normalizedPage = Math.min(Math.max(1, page), pageCount);
  const start = (normalizedPage - 1) * pageSize;
  return {
    rows: rows.slice(start, start + pageSize),
    page: normalizedPage,
    pageCount,
    first: rows.length === 0 ? 0 : start + 1,
    last: Math.min(rows.length, start + pageSize),
  };
}

export function hasRequiredStockLengthMappings(draft: ImportWorksheetDraft): boolean {
  const mapped = new Set(
    (draft.options.columnMappings.length > 0
      ? draft.options.columnMappings
      : draft.preview.columnMappings.flatMap((mapping) => mapping.sourceColumn
        ? [{ targetField: mapping.targetField, sourceColumn: mapping.sourceColumn }]
        : []))
      .filter((mapping) => mapping.sourceColumn.trim().length > 0)
      .map((mapping) => mapping.targetField),
  );
  return requiredStockLengthFields.every((field) => mapped.has(field));
}

export function canReviewStockLengthImport(drafts: ImportWorksheetDraft[]): boolean {
  const selected = drafts.filter((draft) => draft.selected);
  return selected.length > 0 && selected.every((draft) =>
    draft.optimizationGroupId.trim().length > 0 &&
    draft.optimizationGroupName.trim().length > 0 &&
    Boolean(draft.stockLength && draft.stockLength > 0) &&
    draft.headingRangeConfirmed &&
    hasRequiredStockLengthMappings(draft));
}

export function summarizeStockLengthImport(drafts: ImportWorksheetDraft[]): ImportReviewSummary {
  const selectedWorksheets = drafts.filter((draft) => draft.selected);
  const excludedIds = new Set(selectedWorksheets.flatMap((draft) =>
    draft.excludedSourceRows.map((row) => row.rowId)));
  const correctedIds = new Set(selectedWorksheets.flatMap((draft) =>
    draft.partOverrides.filter((override) =>
      override.currentRequiredPiece?.validationStatus !== 'error').map((override) => override.rowId)));
  const issues = selectedWorksheets.flatMap((draft) => [
    ...draft.preview.errors.map((issue) => ({
      kind: 'error' as const,
      worksheetName: draft.worksheet.worksheetName,
      issue,
    })),
    ...draft.preview.warnings.map((issue) => ({
      kind: 'warning' as const,
      worksheetName: draft.worksheet.worksheetName,
      issue,
    })),
  ]);
  const unresolvedErrors = issues.filter(({ kind, issue }) =>
    kind === 'error' &&
    !['material-not-found', 'material-name-required', 'missing-material'].includes(issue.code) &&
    !(issue.rowId && (excludedIds.has(issue.rowId) || correctedIds.has(issue.rowId))));
  const rows = selectedWorksheets.flatMap((draft) => draft.preview.requiredPieces ?? []);
  return {
    selectedWorksheets,
    rows,
    validCount: rows.filter((piece) =>
      !excludedIds.has(piece.requiredPieceId) &&
      (piece.validationStatus !== 'error' || correctedIds.has(piece.requiredPieceId))).length,
    excludedCount: excludedIds.size,
    issues,
    unresolvedErrors,
    warnings: issues.filter((issue) => issue.kind === 'warning'),
    optimizationGroupCount: new Set(selectedWorksheets.map((draft) => draft.optimizationGroupId)).size,
  };
}
