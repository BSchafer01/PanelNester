import type {
  ImportColumnMapping,
  ImportFileResponse,
  ImportMaterialMapping,
  ImportNewMaterialRequest,
  ImportOptions,
  ImportPreviewSummary,
  ImportSourceColumn,
  ImportWorksheetDraft,
  PartRow,
  WorkbookDiscovery,
} from '../types/contracts';

export function summarizeWorkbookPreview(
  drafts: ImportWorksheetDraft[],
): ImportPreviewSummary {
  const selected = drafts.filter((draft) => draft.selected);
  const worksheets = selected.map((draft) => ({
    worksheetName: draft.worksheet.worksheetName,
    originalPosition: draft.worksheet.originalPosition,
    sourceRowCount: draft.preview.parts.reduce(
      (count, part) => count + Math.max(1, part.sourceReferences?.length ?? 0),
      0,
    ) + draft.excludedSourceRows.length,
    importedPartCount: draft.preview.parts.length,
    excludedRowCount: draft.excludedSourceRows.length,
    issueCount: draft.preview.errors.length + draft.preview.warnings.length,
  }));
  const groupedDrafts = new Map<string, ImportWorksheetDraft[]>();
  for (const draft of selected) {
    groupedDrafts.set(draft.optimizationGroupId, [
      ...(groupedDrafts.get(draft.optimizationGroupId) ?? []),
      draft,
    ]);
  }

  const optimizationGroups = Array.from(groupedDrafts, ([optimizationGroupId, groupDrafts]) => {
    const parts = groupDrafts.flatMap((draft) => draft.preview.parts);
    const sourceRowCount = parts.reduce(
      (count, part) => count + Math.max(1, part.sourceReferences?.length ?? 0),
      0,
    ) + groupDrafts.reduce(
      (count, draft) => count + draft.excludedSourceRows.length,
      0,
    );
    const compatibleKeys = new Set<string>();
    let unmergeablePartCount = 0;
    for (const part of parts) {
      if (part.validationStatus === 'error' || part.isManual) {
        unmergeablePartCount += 1;
        continue;
      }

      compatibleKeys.add(JSON.stringify([
        part.importedId,
        part.materialName,
        part.length,
        part.width,
        part.group ?? null,
        part.sheetNumber ?? null,
        part.rowNumber ?? null,
        part.columnNumber ?? null,
      ]));
    }
    const combinedPartCount = compatibleKeys.size + unmergeablePartCount;
    return {
      optimizationGroupId,
      name: groupDrafts[0]?.optimizationGroupName ?? optimizationGroupId,
      sourceRowCount,
      combinedPartCount,
      mergedRowCount: Math.max(0, sourceRowCount - combinedPartCount),
    };
  });

  return { worksheets, optimizationGroups };
}

export function createWorkbookWorksheetDrafts(
  sessionId: string,
  workbook: WorkbookDiscovery,
  firstPreview: ImportFileResponse,
  firstOptions: ImportOptions,
): ImportWorksheetDraft[] {
  const initialWorksheetName =
    workbook.initialWorksheetName || workbook.worksheets[0]?.worksheetName;
  return workbook.worksheets.map((worksheet) => ({
    worksheet,
    selected: worksheet.worksheetName === initialWorksheetName,
    optimizationGroupId: `import-${sessionId}-${worksheet.originalPosition}`,
    optimizationGroupName: worksheet.worksheetName,
    preview:
      worksheet.worksheetName === initialWorksheetName
        ? firstPreview
        : emptyPreview(),
    options:
      worksheet.worksheetName === initialWorksheetName
        ? firstOptions
        : { columnMappings: [], materialMappings: [] },
    newMaterials: [],
    hasPendingChanges: worksheet.worksheetName !== initialWorksheetName,
    headingRange: worksheet.headingRange,
    headingRangeConfirmed: false,
    excludedSourceRows: [],
    partOverrides: [],
  }));
}

export function editInvalidSourceRow(
  draft: ImportWorksheetDraft,
  rowId: string,
  currentValues: PartRow,
): ImportWorksheetDraft {
  const importedValues = draft.preview.parts.find((part) => part.rowId === rowId);
  if (!importedValues) {
    return draft;
  }

  const sourceReferences = importedValues.sourceReferences ?? [];
  const existing = draft.partOverrides.find((item) => item.rowId === rowId);
  const partOverride = {
    rowId,
    importedValues: existing?.importedValues ?? importedValues,
    currentValues: { ...currentValues, sourceReferences },
    sourceReferences,
  };
  const errors = draft.preview.errors.filter((error) => error.rowId !== rowId);
  const warnings = draft.preview.warnings.filter((warning) => warning.rowId !== rowId);
  return {
    ...draft,
    preview: {
      ...draft.preview,
      success: errors.length === 0,
      parts: draft.preview.parts.map((part) =>
        part.rowId === rowId ? partOverride.currentValues : part,
      ),
      errors,
      warnings,
    },
    partOverrides: [
      ...draft.partOverrides.filter((item) => item.rowId !== rowId),
      partOverride,
    ],
  };
}

export function excludeInvalidSourceRow(
  draft: ImportWorksheetDraft,
  rowId: string,
): ImportWorksheetDraft {
  const sourceRow = draft.preview.parts.find((part) => part.rowId === rowId);
  const originalValidationError = draft.preview.errors.find((error) => error.rowId === rowId);
  const sourceReference = sourceRow?.sourceReferences?.[0];
  if (!sourceRow || !originalValidationError || !sourceReference) {
    return draft;
  }

  const errors = draft.preview.errors.filter((error) => error.rowId !== rowId);
  return {
    ...draft,
    preview: {
      ...draft.preview,
      success: errors.length === 0,
      parts: draft.preview.parts.filter((part) => part.rowId !== rowId),
      errors,
      warnings: draft.preview.warnings.filter((warning) => warning.rowId !== rowId),
    },
    excludedSourceRows: [
      ...draft.excludedSourceRows.filter((item) => item.rowId !== rowId),
      { rowId, sourceReference, originalValidationError, sourceRow },
    ],
    partOverrides: draft.partOverrides.filter((item) => item.rowId !== rowId),
  };
}

export function restoreExcludedSourceRow(
  draft: ImportWorksheetDraft,
  rowId: string,
): ImportWorksheetDraft {
  const excluded = draft.excludedSourceRows.find((item) => item.rowId === rowId);
  if (!excluded) {
    return draft;
  }

  return {
    ...draft,
    preview: {
      ...draft.preview,
      success: false,
      parts: [...draft.preview.parts, excluded.sourceRow],
      errors: [...draft.preview.errors, excluded.originalValidationError],
    },
    excludedSourceRows: draft.excludedSourceRows.filter((item) => item.rowId !== rowId),
  };
}

export interface WorksheetDraftOperationResult {
  drafts: ImportWorksheetDraft[];
  error?: string;
}

export interface BulkHeadingRangeConfirmationSummary {
  worksheetNames: string[];
}

export function confirmWorksheetHeadingRange(
  drafts: ImportWorksheetDraft[],
  worksheetName: string,
  address: string,
): WorksheetDraftOperationResult {
  const normalized = normalizeHeadingRangeAddress(address);
  if (!normalized) {
    return {
      drafts: drafts.map((draft) =>
        draft.worksheet.worksheetName === worksheetName
          ? {
              ...draft,
              headingRangeConfirmed: false,
              hasPendingChanges: true,
            }
          : draft,
      ),
      error: 'Enter one contiguous, single-row Heading Range such as B4:H4.',
    };
  }

  return {
    drafts: drafts.map((draft) =>
      draft.worksheet.worksheetName === worksheetName
        ? confirmHeadingRange(draft, normalized)
        : draft,
    ),
  };
}

export function copyColumnMappingsFromPreviousSelectedWorksheet(
  drafts: ImportWorksheetDraft[],
  worksheetName: string,
): WorksheetDraftOperationResult {
  const targetIndex = drafts.findIndex(
    (draft) => draft.worksheet.worksheetName === worksheetName,
  );
  const previous = drafts
    .slice(0, targetIndex)
    .reverse()
    .find((draft) => draft.selected && draft.headingRangeConfirmed);
  const target = drafts[targetIndex];
  if (targetIndex < 0 || !previous || !target?.headingRangeConfirmed) {
    return {
      drafts,
      error: 'Confirm both Worksheet Heading Ranges before copying Column Mappings.',
    };
  }

  const reconciliation = reconcileMappingsByUniqueHeading(
    previous.options.columnMappings,
    sourceColumnsForDraft(previous),
    sourceColumnsForDraft(target),
  );
  const nextDraft = {
    ...target,
    options: {
      ...target.options,
      columnMappings: reconciliation.mappings,
    },
    clearedColumnMappingFields: reconciliation.clearedFields,
    hasPendingChanges: true,
  };

  return {
    drafts: drafts.map((draft, index) => index === targetIndex ? nextDraft : draft),
  };
}

export function mergeRecognizedColumnMappings(
  options: ImportOptions,
  response: ImportFileResponse,
): ImportOptions {
  const retainedTargets = new Set(
    options.columnMappings.map((mapping) => mapping.targetField),
  );
  const usedSources = new Set(
    options.columnMappings.map((mapping) => mapping.sourceColumn.trim()),
  );
  const recognized = response.columnMappings.flatMap((mapping) => {
    const sourceColumn = (
      mapping.sourceColumn ?? mapping.suggestedSourceColumn ?? ''
    ).trim();
    if (
      sourceColumn.length === 0 ||
      retainedTargets.has(mapping.targetField) ||
      usedSources.has(sourceColumn)
    ) {
      return [];
    }

    retainedTargets.add(mapping.targetField);
    usedSources.add(sourceColumn);
    return [{ sourceColumn, targetField: mapping.targetField }];
  });

  return {
    ...options,
    columnMappings: [...options.columnMappings, ...recognized],
  };
}

export function synchronizeWorkbookMaterialResolution(
  drafts: ImportWorksheetDraft[],
  sourceMaterialName: string,
  materialMapping?: ImportMaterialMapping,
  newMaterial?: ImportNewMaterialRequest,
): ImportWorksheetDraft[] {
  return drafts.map((draft) => {
    if (!draft.selected) {
      return draft;
    }

    const materialMappings = draft.options.materialMappings.filter(
      (mapping) => mapping.sourceMaterialName !== sourceMaterialName,
    );
    const newMaterials = draft.newMaterials.filter(
      (material) => material.sourceMaterialName !== sourceMaterialName,
    );
    if (materialMapping?.targetMaterialId?.trim()) {
      materialMappings.push(materialMapping);
    }
    if (newMaterial) {
      newMaterials.push(newMaterial);
    }

    return {
      ...draft,
      options: { ...draft.options, materialMappings },
      newMaterials,
      hasPendingChanges: true,
    };
  });
}

export function collectWorkbookNewMaterials(
  drafts: ImportWorksheetDraft[],
): ImportNewMaterialRequest[] {
  const grouped = new Map<string, ImportNewMaterialRequest[]>();
  for (const material of drafts
    .filter((draft) => draft.selected)
    .flatMap((draft) => draft.newMaterials)) {
    const label = material.sourceMaterialName.trim();
    grouped.set(label, [...(grouped.get(label) ?? []), material]);
  }

  return Array.from(grouped.values()).flatMap((materials) => {
    const distinctDefinitions = new Set(
      materials.map((material) => JSON.stringify(material.material)),
    );
    return distinctDefinitions.size === 1 ? [materials[0]] : materials;
  });
}

export function copyHeadingRangeFromPreviousSelectedWorksheet(
  drafts: ImportWorksheetDraft[],
  worksheetName: string,
): WorksheetDraftOperationResult {
  const targetIndex = drafts.findIndex(
    (draft) => draft.worksheet.worksheetName === worksheetName,
  );
  const previous = drafts
    .slice(0, targetIndex)
    .reverse()
    .find((draft) => draft.selected && draft.headingRangeConfirmed);
  if (targetIndex < 0 || !previous) {
    return {
      drafts,
      error: 'Confirm the preceding selected Worksheet Heading Range first.',
    };
  }

  return confirmWorksheetHeadingRange(
    drafts,
    worksheetName,
    previous.headingRange,
  );
}

export function summarizeHighConfidenceHeadingRanges(
  drafts: ImportWorksheetDraft[],
): BulkHeadingRangeConfirmationSummary {
  return {
    worksheetNames: drafts
      .filter(
        (draft) =>
          draft.selected &&
          !draft.headingRangeConfirmed &&
          draft.worksheet.headingRangeDetectionStatus === 'unique-high-confidence' &&
          draft.headingRange.length > 0,
      )
      .map((draft) => draft.worksheet.worksheetName),
  };
}

export function applyHighConfidenceHeadingRanges(
  drafts: ImportWorksheetDraft[],
  summary: BulkHeadingRangeConfirmationSummary,
): ImportWorksheetDraft[] {
  const names = new Set(summary.worksheetNames);
  return drafts.map((draft) =>
    names.has(draft.worksheet.worksheetName)
      ? { ...draft, headingRangeConfirmed: true, hasPendingChanges: true }
      : draft,
  );
}

export type WorksheetNavigationStatus =
  | 'Ready'
  | 'Needs heading'
  | 'Needs mapping'
  | 'Has errors';

export function getWorksheetNavigationStatus(
  draft: ImportWorksheetDraft,
): WorksheetNavigationStatus {
  if (!draft.headingRangeConfirmed) {
    return 'Needs heading';
  }

  if (draft.preview.errors.length > 0) {
    return 'Has errors';
  }

  const requiredFields = ['Id', 'Length', 'Width', 'Quantity', 'Material'];
  const mappedTargets = new Set<string>(
    draft.preview.columnMappings
      .filter((mapping) => (mapping.sourceColumn ?? '').trim().length > 0)
      .map((mapping) => mapping.targetField),
  );
  return requiredFields.every((field) => mappedTargets.has(field)) &&
    !draft.hasPendingChanges
    ? 'Ready'
    : 'Needs mapping';
}

export function headingRangeFromPreviewCells(
  firstAddress: string,
  secondAddress: string,
): string | undefined {
  const first = /^([a-z]+)([1-9]\d*)$/i.exec(firstAddress.trim());
  const second = /^([a-z]+)([1-9]\d*)$/i.exec(secondAddress.trim());
  if (!first || !second || first[2] !== second[2]) {
    return undefined;
  }

  const [start, end] = columnNumber(first[1]) <= columnNumber(second[1])
    ? [first, second]
    : [second, first];
  return `${start[1].toUpperCase()}${start[2]}:${end[1].toUpperCase()}${end[2]}`;
}

export function setWorkbookWorksheetSelected(
  drafts: ImportWorksheetDraft[],
  worksheetName: string,
  selected: boolean,
): ImportWorksheetDraft[] {
  const workbookMaterialMappings = new Map(
    drafts
      .filter((draft) => draft.selected)
      .flatMap((draft) => draft.options.materialMappings)
      .map((mapping) => [mapping.sourceMaterialName, mapping]),
  );
  const workbookNewMaterials = new Map(
    drafts
      .filter((draft) => draft.selected)
      .flatMap((draft) => draft.newMaterials)
      .map((material) => [material.sourceMaterialName, material]),
  );

  return drafts.map((draft) =>
    draft.worksheet.worksheetName === worksheetName
      ? {
          ...draft,
          selected,
          options: selected
            ? {
                ...draft.options,
                materialMappings: Array.from(
                  new Map([
                    ...draft.options.materialMappings.map(
                      (mapping) => [mapping.sourceMaterialName, mapping] as const,
                    ),
                    ...workbookMaterialMappings,
                  ]).values(),
                ).filter(
                  (mapping) => !workbookNewMaterials.has(mapping.sourceMaterialName),
                ),
              }
            : draft.options,
          newMaterials: selected
            ? Array.from(
                new Map([
                  ...draft.newMaterials.map(
                    (material) => [material.sourceMaterialName, material] as const,
                  ),
                  ...workbookNewMaterials,
                ]).values(),
              ).filter(
                (material) => !workbookMaterialMappings.has(material.sourceMaterialName),
              )
            : draft.newMaterials,
        }
      : draft,
  );
}

function emptyPreview(): ImportFileResponse {
  return {
    success: false,
    filePath: null,
    parts: [],
    errors: [],
    warnings: [],
    availableColumns: [],
    sourceColumns: [],
    columnMappings: [],
    materialResolutions: [],
  };
}

function normalizeHeadingRangeAddress(address: string): string | undefined {
  const match = /^\s*([a-z]+)([1-9]\d*)\s*:\s*([a-z]+)([1-9]\d*)\s*$/i.exec(address);
  if (!match || match[2] !== match[4]) {
    return undefined;
  }

  const firstColumn = columnNumber(match[1]);
  const lastColumn = columnNumber(match[3]);
  if (firstColumn > lastColumn) {
    return undefined;
  }

  return `${match[1].toUpperCase()}${match[2]}:${match[3].toUpperCase()}${match[4]}`;
}

function confirmHeadingRange(
  draft: ImportWorksheetDraft,
  normalizedAddress: string,
): ImportWorksheetDraft {
  if (!draft.headingRangeConfirmed || draft.headingRange === normalizedAddress) {
    return {
      ...draft,
      headingRange: normalizedAddress,
      headingRangeConfirmed: true,
      hasPendingChanges: true,
    };
  }

  const reconciliation = reconcileMappingsByUniqueHeading(
    draft.options.columnMappings,
    draft.preview.sourceColumns,
    sourceColumnsForHeadingRange(draft, normalizedAddress),
  );
  return {
    ...draft,
    headingRange: normalizedAddress,
    headingRangeConfirmed: true,
    options: {
      ...draft.options,
      columnMappings: reconciliation.mappings,
    },
    clearedColumnMappingFields: reconciliation.clearedFields,
    hasPendingChanges: true,
  };
}

function reconcileMappingsByUniqueHeading(
  mappings: ImportColumnMapping[],
  sourceColumns: ImportSourceColumn[],
  targetColumns: ImportSourceColumn[],
): { mappings: ImportColumnMapping[]; clearedFields: string[] } {
  const uniqueSourceHeadings = uniqueHeadingAddresses(sourceColumns);
  const uniqueTargetHeadings = uniqueHeadingAddresses(targetColumns);
  const sourceHeadingByAddress = new Map(
    sourceColumns.map((column) => [column.address.trim(), normalizeHeading(column.heading)]),
  );
  const retained: ImportColumnMapping[] = [];
  const clearedFields: string[] = [];

  for (const mapping of mappings) {
    const normalizedHeading = sourceHeadingByAddress.get(mapping.sourceColumn.trim()) ?? '';
    const sourceIsUnique = uniqueSourceHeadings.get(normalizedHeading) === mapping.sourceColumn.trim();
    const targetAddress = sourceIsUnique
      ? uniqueTargetHeadings.get(normalizedHeading)
      : undefined;
    if (normalizedHeading.length > 0 && targetAddress) {
      retained.push({ sourceColumn: targetAddress, targetField: mapping.targetField });
    } else {
      clearedFields.push(mapping.targetField);
    }
  }

  return { mappings: retained, clearedFields };
}

function uniqueHeadingAddresses(
  columns: ImportSourceColumn[],
): Map<string, string> {
  const grouped = new Map<string, string[]>();
  for (const column of columns) {
    const normalized = normalizeHeading(column.heading);
    if (normalized.length === 0) {
      continue;
    }
    grouped.set(normalized, [...(grouped.get(normalized) ?? []), column.address.trim()]);
  }

  return new Map(
    Array.from(grouped.entries())
      .filter(([, addresses]) => addresses.length === 1)
      .map(([heading, addresses]) => [heading, addresses[0]]),
  );
}

function sourceColumnsForDraft(
  draft: ImportWorksheetDraft,
): ImportSourceColumn[] {
  const fromRange = sourceColumnsForHeadingRange(draft, draft.headingRange);
  return fromRange.length > 0 ? fromRange : draft.preview.sourceColumns;
}

function sourceColumnsForHeadingRange(
  draft: ImportWorksheetDraft,
  address: string,
): ImportSourceColumn[] {
  const match = /^([A-Z]+)([1-9]\d*):([A-Z]+)([1-9]\d*)$/.exec(address);
  if (!match || match[2] !== match[4]) {
    return [];
  }

  const firstColumn = columnNumber(match[1]);
  const lastColumn = columnNumber(match[3]);
  const row = draft.worksheet.previewRows?.find(
    (previewRow) => previewRow.rowNumber === Number(match[2]),
  );
  return (row?.cells ?? [])
    .filter(
      (cell) => cell.columnNumber >= firstColumn && cell.columnNumber <= lastColumn,
    )
    .map((cell) => ({
      address: cell.address.replace(/[0-9]+$/, ''),
      heading: cell.value,
    }));
}

function normalizeHeading(value: string): string {
  return Array.from(value.toLowerCase())
    .filter((character) => /[\p{L}\p{N}]/u.test(character))
    .join('');
}

function columnNumber(letters: string): number {
  return Array.from(letters.toUpperCase()).reduce(
    (value, letter) => value * 26 + letter.charCodeAt(0) - 64,
    0,
  );
}
