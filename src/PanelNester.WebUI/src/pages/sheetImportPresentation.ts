import type {
  ImportFieldName,
  ImportWorksheetDraft,
  MaterialDraft,
  PartRow,
  ValidationError,
  ValidationWarning,
} from '../types/contracts';

export const sheetImportFields: ImportFieldName[] = [
  'Id', 'Length', 'Width', 'Quantity', 'Material',
  'Group', 'Sheet Number', 'Row Number', 'Column Number',
];
export const requiredSheetImportFields: ImportFieldName[] = [
  'Id', 'Length', 'Width', 'Quantity', 'Material',
];

export interface SheetImportIssue {
  kind: 'error' | 'warning';
  worksheetName: string;
  issue: ValidationError | ValidationWarning;
}

export interface SheetImportPlan {
  sourceRowCount: number;
  validSourceRowCount: number;
  outputEntryCount: number;
  totalPartQuantity: number;
  skippedSourceRowCount: number;
  sourceRows: PartRow[];
  resultingEntries: PartRow[];
  issues: SheetImportIssue[];
  unresolvedErrors: SheetImportIssue[];
  warnings: SheetImportIssue[];
}

function effectiveMappings(draft: ImportWorksheetDraft) {
  return draft.options.columnMappings.length > 0
    ? draft.options.columnMappings
    : draft.preview.columnMappings.flatMap((mapping) => mapping.sourceColumn
      ? [{ sourceColumn: mapping.sourceColumn, targetField: mapping.targetField }]
      : []);
}

export function hasRequiredSheetMappings(draft: ImportWorksheetDraft): boolean {
  const mapped = new Set(effectiveMappings(draft)
    .filter((mapping) => mapping.sourceColumn.trim().length > 0)
    .map((mapping) => mapping.targetField));
  return requiredSheetImportFields.every((field) => mapped.has(field));
}

export function validateMaterialDraft(draft: MaterialDraft): string | null {
  if (!draft.name.trim()) return 'Material name is required.';
  if (draft.sheetLength <= 0) return 'Sheet length must be greater than zero.';
  if (draft.sheetWidth <= 0) return 'Sheet width must be greater than zero.';
  if (draft.defaultSpacing < 0) return 'Default spacing cannot be negative.';
  if (draft.defaultEdgeMargin < 0) return 'Default edge margin cannot be negative.';
  if (draft.costPerSheet != null && draft.costPerSheet < 0) return 'Cost per sheet cannot be negative.';
  return null;
}

export function createMaterialDraft(sourceMaterialName: string): MaterialDraft {
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

export function sheetMaterialLabels(drafts: ImportWorksheetDraft[]): string[] {
  return [...new Set(drafts.filter((draft) => draft.selected).flatMap((draft) =>
    draft.preview.materialResolutions.map((resolution) => resolution.sourceMaterialName.trim())
      .filter(Boolean)))];
}

export function isSheetMaterialResolved(
  drafts: ImportWorksheetDraft[],
  sourceMaterialName: string,
): boolean {
  const selected = drafts.filter((draft) => draft.selected);
  return selected.every((draft) => {
    const containsLabel = draft.preview.materialResolutions.some((resolution) =>
      resolution.sourceMaterialName === sourceMaterialName) ||
      draft.preview.parts.some((part) => part.materialName === sourceMaterialName) ||
      draft.ignoredMaterialNames.includes(sourceMaterialName);
    if (!containsLabel) return true;
    if (draft.ignoredMaterialNames.includes(sourceMaterialName)) return true;
    const mapped = draft.options.materialMappings.some((mapping) =>
      mapping.sourceMaterialName === sourceMaterialName && Boolean(mapping.targetMaterialId?.trim()));
    const staged = draft.newMaterials.find((material) =>
      material.sourceMaterialName === sourceMaterialName);
    const autoResolved = draft.preview.materialResolutions.some((resolution) =>
      resolution.sourceMaterialName === sourceMaterialName && Boolean(resolution.resolvedMaterialId));
    return mapped || autoResolved || Boolean(staged && !validateMaterialDraft(staged.material));
  });
}

export function canReviewSheetImport(drafts: ImportWorksheetDraft[]): boolean {
  const selected = drafts.filter((draft) => draft.selected);
  const labels = sheetMaterialLabels(drafts);
  return selected.length > 0 && selected.every((draft) =>
    Boolean(draft.optimizationGroupId.trim()) &&
    Boolean(draft.optimizationGroupName.trim()) &&
    draft.headingRangeConfirmed &&
    hasRequiredSheetMappings(draft) &&
    !draft.hasPendingChanges &&
    draft.newMaterials.every((material) => !validateMaterialDraft(material.material))) &&
    labels.every((label) => isSheetMaterialResolved(drafts, label));
}

export function buildSheetImportPlan(drafts: ImportWorksheetDraft[]): SheetImportPlan {
  const selected = drafts.filter((draft) => draft.selected);
  const excludedIds = new Set(selected.flatMap((draft) =>
    draft.excludedSourceRows.map((row) => row.rowId)));
  const correctedIds = new Set(selected.flatMap((draft) => draft.partOverrides
    .filter((override) => override.currentValues?.validationStatus !== 'error')
    .map((override) => override.rowId)));
  const sourceRows = selected.flatMap((draft) => {
    const overrides = new Map(draft.partOverrides.flatMap((override) =>
      override.currentValues ? [[override.rowId, override.currentValues] as const] : []));
    return draft.preview.parts.map((part) => ({
      ...(overrides.get(part.rowId) ?? part),
      optimizationGroupId: draft.optimizationGroupId,
    }));
  });
  const validRows = sourceRows.filter((row) =>
    !excludedIds.has(row.rowId) && row.validationStatus !== 'error');
  const resultingEntries: PartRow[] = [];
  const entryIndex = new Map<string, number>();
  for (const row of validRows) {
    const groupId = (row as PartRow & { optimizationGroupId?: string }).optimizationGroupId ?? '';
    const key = JSON.stringify([
      groupId, row.importedId, row.length, row.width, row.materialName,
      row.group ?? null, row.sheetNumber ?? null, row.rowNumber ?? null, row.columnNumber ?? null,
    ]);
    const existingIndex = entryIndex.get(key);
    if (existingIndex == null) {
      entryIndex.set(key, resultingEntries.length);
      resultingEntries.push(row);
    } else {
      const existing = resultingEntries[existingIndex];
      const quantity = existing.quantity + row.quantity;
      resultingEntries[existingIndex] = {
        ...existing,
        quantity,
        quantityText: `${quantity}`,
        sourceReferences: [...(existing.sourceReferences ?? []), ...(row.sourceReferences ?? [])],
      };
    }
  }
  const issues: SheetImportIssue[] = selected.flatMap((draft) => [
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
  const sourceRowIds = new Set(sourceRows.map((row) => row.rowId));
  const missingExcluded = [...excludedIds].filter((rowId) => !sourceRowIds.has(rowId)).length;
  return {
    sourceRowCount: sourceRows.reduce((count, row) =>
      count + Math.max(1, row.sourceReferences?.length ?? 0), 0) + missingExcluded,
    validSourceRowCount: validRows.reduce((count, row) =>
      count + Math.max(1, row.sourceReferences?.length ?? 0), 0),
    outputEntryCount: resultingEntries.length,
    totalPartQuantity: resultingEntries.reduce((count, row) => count + row.quantity, 0),
    skippedSourceRowCount: excludedIds.size + sourceRows.filter((row) =>
      !excludedIds.has(row.rowId) && row.validationStatus === 'error').length,
    sourceRows,
    resultingEntries,
    issues,
    unresolvedErrors,
    warnings: issues.filter((issue) => issue.kind === 'warning'),
  };
}
