import type {
  ImportColumnMapping,
  ImportConfiguration,
  ImportFileResponse,
  ImportFieldName,
  ImportMaterialMapping,
  ImportMappingSession,
  ImportNewMaterialRequest,
  ImportOptions,
  ImportPreviewSummary,
  ImportSourceColumn,
  ImportWorksheetDraft,
  ImportWorksheetDescriptor,
  OptimizationGroup,
  PartRow,
  WorkbookDiscovery,
} from '../types/contracts';

const stockLengthHeadingAliases: ReadonlyArray<readonly [ImportFieldName, readonly string[]]> = [
  ['Quantity', ['quantity', 'qty', 'count', 'pieces', 'piececount']],
  ['Length', ['length', 'len', 'partlength', 'panellength']],
  ['Profile Number', ['profile', 'profilenumber', 'profileno', 'die', 'dienumber', 'dieno', 'extrusion', 'extrusionnumber', 'extrusionno']],
  ['Part Name', ['partname', 'piecename', 'description']],
  ['Finish', ['finish', 'color', 'colour']],
  ['Part Number', ['partnumber', 'partno', 'itemnumber', 'itemno']],
];

export interface WorksheetLayout {
  layoutId: string;
  normalizedHeaderSchema: string[];
  worksheetNames: string[];
  drafts: ImportWorksheetDraft[];
}

export function buildWorksheetLayouts(drafts: ImportWorksheetDraft[]): WorksheetLayout[] {
  const layouts = new Map<string, ImportWorksheetDraft[]>();
  for (const draft of drafts.filter((candidate) => candidate.selected)) {
    const schema = worksheetSourceColumns(draft).map((column) => normalizeHeading(column.heading));
    const layoutId = schema.length > 0
      ? `layout:${JSON.stringify(schema)}`
      : `worksheet:${draft.worksheet.originalPosition}`;
    layouts.set(layoutId, [...(layouts.get(layoutId) ?? []), draft]);
  }
  return Array.from(layouts, ([layoutId, layoutDrafts]) => ({
    layoutId,
    normalizedHeaderSchema: worksheetSourceColumns(layoutDrafts[0]).map(
      (column) => normalizeHeading(column.heading)),
    worksheetNames: layoutDrafts.map((draft) => draft.worksheet.worksheetName),
    drafts: layoutDrafts,
  }));
}

export function applyWorksheetLayoutMappings(
  drafts: ImportWorksheetDraft[],
  layoutId: string,
  mappings: ImportColumnMapping[],
): ImportWorksheetDraft[] {
  const layout = buildWorksheetLayouts(drafts).find((candidate) => candidate.layoutId === layoutId);
  if (!layout) return drafts;
  const sourceDraft = layout.drafts.find((draft) => {
    const addresses = new Set(worksheetSourceColumns(draft).map((column) => column.address));
    return mappings.every((mapping) => addresses.has(mapping.sourceColumn));
  }) ?? layout.drafts[0];
  const sourceColumns = worksheetSourceColumns(sourceDraft);
  const fieldsByColumnIndex = mappings.flatMap((mapping) => {
    const columnIndex = sourceColumns.findIndex((column) => column.address === mapping.sourceColumn);
    return columnIndex >= 0 ? [[columnIndex, mapping.targetField] as const] : [];
  });
  const memberNames = new Set(layout.worksheetNames);
  return drafts.map((draft) => {
    if (!memberNames.has(draft.worksheet.worksheetName)) return draft;
    const targetColumns = worksheetSourceColumns(draft);
    return {
      ...draft,
      options: {
        ...draft.options,
        columnMappings: fieldsByColumnIndex.flatMap(([columnIndex, targetField]) => {
          const sourceColumn = targetColumns[columnIndex]?.address;
          return sourceColumn ? [{ sourceColumn, targetField }] : [];
        }),
      },
    };
  });
}

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

export function suggestStockLengthColumnMappings(
  worksheet: ImportWorksheetDescriptor,
  headingRange: string,
): ImportColumnMapping[] {
  const columns = sourceColumnsForWorksheetRange(worksheet, headingRange);
  const recognized = new Map<ImportFieldName, string[]>();
  for (const column of columns) {
    const normalized = normalizeHeading(column.heading);
    const target = stockLengthHeadingAliases.find(([, aliases]) => aliases.includes(normalized))?.[0];
    if (target) recognized.set(target, [...(recognized.get(target) ?? []), column.address]);
  }

  return stockLengthHeadingAliases.flatMap(([targetField]) => {
    const sourceColumns = recognized.get(targetField) ?? [];
    return sourceColumns.length === 1 ? [{ sourceColumn: sourceColumns[0], targetField }] : [];
  });
}

export function createWorkbookWorksheetDrafts(
  sessionId: string,
  workbook: WorkbookDiscovery,
  firstPreview: ImportFileResponse,
  firstOptions: ImportOptions,
  savedConfiguration?: ImportConfiguration,
  optimizationGroups: Array<Pick<OptimizationGroup, 'optimizationGroupId' | 'name' | 'stockLength'>> = [],
): ImportWorksheetDraft[] {
  const initialWorksheetName =
    workbook.initialWorksheetName || workbook.worksheets[0]?.worksheetName;
  const previewWorksheetName = firstPreview.worksheet?.worksheetName || initialWorksheetName;
  const savedWorksheets = savedConfiguration?.worksheets ?? [];
  const hasRestoredWorksheet = workbook.worksheets.some((worksheet) =>
    savedWorksheets.some((saved) =>
      saved.originalPosition === worksheet.originalPosition &&
      saved.worksheetName === worksheet.worksheetName));
  const groupNames = new Map(
    optimizationGroups.map((group) => [group.optimizationGroupId, group.name]),
  );
  const groupStockLengths = new Map(
    optimizationGroups.map((group) => [group.optimizationGroupId, group.stockLength]),
  );

  return workbook.worksheets.map((worksheet) => {
    const saved = savedWorksheets.find((candidate) =>
      candidate.originalPosition === worksheet.originalPosition &&
      candidate.worksheetName === worksheet.worksheetName);
    const isInitial = worksheet.worksheetName === previewWorksheetName;
    const optimizationGroupId = saved?.optimizationGroupId ??
      `import-${sessionId}-${worksheet.originalPosition}`;
    const restoredOrInitialMappings = saved?.columnMappings ??
      (isInitial ? firstOptions.columnMappings : []);
    const materialMappings = savedConfiguration?.options.materialMappings ??
      (isInitial ? firstOptions.materialMappings : []);
    const headingRange = saved?.headingRange || worksheet.headingRange;
    const assumedDetectedRange = firstOptions.projectKind === 'stockLength' && !saved &&
      worksheet.headingRangeDetectionStatus === 'unique-high-confidence' &&
      headingRange.length > 0;
    const detectedMappings = firstOptions.projectKind === 'stockLength' && assumedDetectedRange
      ? suggestStockLengthColumnMappings(worksheet, headingRange)
      : [];
    const mappedTargets = new Set(restoredOrInitialMappings.map((mapping) => mapping.targetField));
    const mappedSources = new Set(restoredOrInitialMappings.map((mapping) => mapping.sourceColumn));
    const columnMappings = [
      ...restoredOrInitialMappings,
      ...detectedMappings.filter((mapping) =>
        !mappedTargets.has(mapping.targetField) && !mappedSources.has(mapping.sourceColumn)),
    ];

    return {
      worksheet,
      selected: saved ? true : !hasRestoredWorksheet && isInitial,
      optimizationGroupId,
      optimizationGroupName: worksheet.worksheetName,
      stockLength: groupStockLengths.get(optimizationGroupId),
      ...(saved ? {
        optimizationGroupName: groupNames.get(optimizationGroupId) ?? worksheet.worksheetName,
      } : {}),
      preview: isInitial ? firstPreview : emptyPreview(),
      options: {
        ...(savedConfiguration?.options ?? (isInitial ? firstOptions : {})),
        projectKind: savedConfiguration?.options.projectKind ?? firstOptions.projectKind,
        columnMappings,
        materialMappings,
      },
      newMaterials: [],
      hasPendingChanges: !isInitial || Boolean(saved && firstPreview.worksheet?.worksheetName !== worksheet.worksheetName),
      headingRange,
      headingRangeConfirmed: Boolean(saved?.headingRange) || assumedDetectedRange,
      excludedSourceRows: saved?.excludedSourceRows ?? [],
      ignoredMaterialNames: [],
      partOverrides: (savedConfiguration?.partOverrides ?? []).filter((partOverride) =>
        partOverride.sourceReferences.some((reference) =>
          reference.worksheetPosition === worksheet.originalPosition &&
          reference.worksheetName === worksheet.worksheetName)),
    };
  });
}

export function assignSelectedWorksheetsToOptimizationGroup(
  drafts: ImportWorksheetDraft[],
  group: Pick<OptimizationGroup, 'optimizationGroupId' | 'name' | 'stockLength'>,
): ImportWorksheetDraft[] {
  return drafts.map((draft) => draft.selected
    ? {
        ...draft,
        optimizationGroupId: group.optimizationGroupId,
        optimizationGroupName: group.name,
        stockLength: group.stockLength,
      }
    : draft);
}

export function canFinalizeStockLengthWorkbook(
  drafts: ImportWorksheetDraft[],
  optimizationGroups: Array<Pick<OptimizationGroup, 'optimizationGroupId' | 'stockLength'>>,
): boolean {
  const selected = drafts.filter((draft) => draft.selected);
  const groups = new Map(
    optimizationGroups.map((group) => [group.optimizationGroupId, group]),
  );
  const requiredFields = ['Quantity', 'Length', 'Profile Number'];

  return selected.length > 0 && selected.every((draft) => {
    const group = groups.get(draft.optimizationGroupId);
    const resolvedRowIds = new Set([
      ...draft.excludedSourceRows.map((row) => row.rowId),
      ...draft.partOverrides
        .filter((partOverride) => partOverride.currentRequiredPiece
          ? partOverride.currentRequiredPiece.validationStatus !== 'error'
          : partOverride.currentValues?.validationStatus !== 'error')
        .map((partOverride) => partOverride.rowId),
    ]);
    const mappings = draft.options.columnMappings.length > 0
      ? draft.options.columnMappings
      : draft.preview.columnMappings.flatMap((mapping) => mapping.sourceColumn
        ? [{ sourceColumn: mapping.sourceColumn, targetField: mapping.targetField }]
        : []);
    const headingRange = /^([A-Z]+)([1-9]\d*):([A-Z]+)([1-9]\d*)$/i.exec(
      draft.headingRange.trim(),
    );
    const hasUsableHeadingRange = Boolean(
      draft.headingRangeConfirmed || (headingRange && headingRange[2] === headingRange[4]),
    );
    const stockLength = draft.stockLength ?? group?.stockLength;
    return Boolean(stockLength && stockLength > 0) &&
      hasUsableHeadingRange &&
      draft.preview.errors.every((error) =>
        ['material-not-found', 'material-name-required', 'missing-material'].includes(error.code) ||
        Boolean(error.rowId && resolvedRowIds.has(error.rowId))) &&
      requiredFields.every((field) => mappings.some(
        (mapping) => mapping.targetField === field && mapping.sourceColumn.trim().length > 0,
      ));
  });
}

export function validateRequiredPieceCorrection(
  quantityText: string,
  lengthText: string,
  profileNumber: string,
) {
  const validationMessages: string[] = [];
  const trimmedQuantity = quantityText.trim();
  const quantity = /^[-+]?\d+$/.test(trimmedQuantity)
    ? Number.parseInt(trimmedQuantity, 10)
    : 0;
  if (!Number.isSafeInteger(quantity) || quantity <= 0) {
    validationMessages.push('Quantity must be an integer greater than zero.');
  }

  const length = parseInchMeasurement(lengthText);
  if (length == null) {
    validationMessages.push('Length must be a decimal, fraction, or mixed-number inch value.');
  } else if (length <= 0) {
    validationMessages.push('Length must be greater than zero.');
  }

  if (profileNumber.trim().length === 0) {
    validationMessages.push('Profile Number is required.');
  }

  return {
    quantity,
    length: length ?? 0,
    validationStatus: validationMessages.length === 0 ? 'valid' as const : 'error' as const,
    validationMessages,
  };
}

function parseInchMeasurement(rawValue: string): number | undefined {
  const parts = rawValue.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 2 && /^[-+]?\d+$/.test(parts[0])) {
    const whole = Number.parseInt(parts[0], 10);
    const fraction = parseFraction(parts[1]);
    return fraction == null ? undefined : whole < 0 ? whole - fraction : whole + fraction;
  }
  if (parts.length !== 1) return undefined;
  const decimalText = parts[0];
  const decimal = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(decimalText)
    ? Number(decimalText)
    : Number.NaN;
  return Number.isFinite(decimal) ? decimal : parseFraction(decimalText);
}

function parseFraction(value: string): number | undefined {
  const match = /^([-+]?\d+)\/([-+]?\d+)$/.exec(value);
  if (!match) return undefined;
  const denominator = Number.parseInt(match[2], 10);
  return denominator === 0 ? undefined : Number.parseInt(match[1], 10) / denominator;
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

export function excludeInvalidSourceRows(
  draft: ImportWorksheetDraft,
  rowIds: string[],
): ImportWorksheetDraft {
  return rowIds.reduce(excludeInvalidSourceRow, draft);
}

export function excludeSourceRows(
  draft: ImportWorksheetDraft,
  rowIds: string[],
): ImportWorksheetDraft {
  const requestedRowIds = new Set(rowIds);
  const rows = draft.preview.parts.filter((part) => requestedRowIds.has(part.rowId));
  if (rows.length === 0) {
    return draft;
  }

  const excludedRows = rows.flatMap((sourceRow) => {
    const sourceReference = sourceRow.sourceReferences?.[0];
    if (!sourceReference) {
      return [];
    }
    const originalValidationError = draft.preview.errors.find(
      (error) => error.rowId === sourceRow.rowId,
    ) ?? {
      code: 'user-excluded',
      message: 'Source row was excluded during import review.',
      rowId: sourceRow.rowId,
      location: sourceReference,
    };
    return [{ rowId: sourceRow.rowId, sourceReference, originalValidationError, sourceRow }];
  });
  const excludedRowIds = new Set(excludedRows.map((row) => row.rowId));
  const errors = draft.preview.errors.filter((error) => !excludedRowIds.has(error.rowId ?? ''));
  return {
    ...draft,
    preview: {
      ...draft.preview,
      success: errors.length === 0,
      parts: draft.preview.parts.filter((part) => !excludedRowIds.has(part.rowId)),
      errors,
      warnings: draft.preview.warnings.filter(
        (warning) => !excludedRowIds.has(warning.rowId ?? ''),
      ),
    },
    excludedSourceRows: [
      ...draft.excludedSourceRows.filter((row) => !excludedRowIds.has(row.rowId)),
      ...excludedRows,
    ],
    partOverrides: draft.partOverrides.filter((item) => !excludedRowIds.has(item.rowId)),
  };
}

export interface SourceRowSelectionResult {
  selectedRowIds: Set<string>;
  anchorRowId: string;
}

export function selectSourceRowRange(
  selectedRowIds: Set<string>,
  orderedRowIds: string[],
  clickedRowId: string,
  checked: boolean,
  shiftKey: boolean,
  anchorRowId?: string,
): SourceRowSelectionResult {
  const next = new Set(selectedRowIds);
  const clickedIndex = orderedRowIds.indexOf(clickedRowId);
  const anchorIndex = anchorRowId ? orderedRowIds.indexOf(anchorRowId) : -1;
  const affectedRowIds = shiftKey && clickedIndex >= 0 && anchorIndex >= 0
    ? orderedRowIds.slice(
        Math.min(clickedIndex, anchorIndex),
        Math.max(clickedIndex, anchorIndex) + 1,
      )
    : [clickedRowId];
  for (const rowId of affectedRowIds) {
    if (checked) next.add(rowId);
    else next.delete(rowId);
  }
  return { selectedRowIds: next, anchorRowId: clickedRowId };
}

export function restoreExcludedSourceRow(
  draft: ImportWorksheetDraft,
  rowId: string,
): ImportWorksheetDraft {
  const excluded = draft.excludedSourceRows.find((item) => item.rowId === rowId);
  if (!excluded?.sourceRow) {
    return draft;
  }

  const restoresValidationError = excluded.originalValidationError.code !== 'user-excluded' &&
    excluded.originalValidationError.code !== 'ignored-material';
  const errors = restoresValidationError
    ? [...draft.preview.errors, excluded.originalValidationError]
    : draft.preview.errors;

  return {
    ...draft,
    preview: {
      ...draft.preview,
      success: errors.length === 0,
      parts: [...draft.preview.parts, excluded.sourceRow],
      errors,
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
    worksheetSourceColumns(previous),
    worksheetSourceColumns(target),
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

    const restoredDraft = restoreIgnoredMaterialRows(draft, sourceMaterialName);
    const materialMappings = restoredDraft.options.materialMappings.filter(
      (mapping) => mapping.sourceMaterialName !== sourceMaterialName,
    );
    const newMaterials = restoredDraft.newMaterials.filter(
      (material) => material.sourceMaterialName !== sourceMaterialName,
    );
    if (materialMapping?.targetMaterialId?.trim()) {
      materialMappings.push(materialMapping);
    }
    if (newMaterial) {
      newMaterials.push(newMaterial);
    }

    return {
      ...restoredDraft,
      options: { ...restoredDraft.options, materialMappings },
      newMaterials,
      hasPendingChanges: true,
    };
  });
}

export function ignoreWorkbookMaterial(
  drafts: ImportWorksheetDraft[],
  sourceMaterialName: string,
): ImportWorksheetDraft[] {
  return drafts.map((draft) => {
    if (!draft.selected) {
      return draft;
    }

    const matchingRows = draft.preview.parts.filter(
      (part) => part.materialName === sourceMaterialName,
    );
    const ignoredRows = matchingRows.flatMap((sourceRow) => {
      const sourceReference = sourceRow.sourceReferences?.[0];
      if (!sourceReference) {
        return [];
      }
      return [{
        rowId: sourceRow.rowId,
        sourceReference,
        originalValidationError: {
          code: 'ignored-material',
          message: `Material "${sourceMaterialName}" was ignored.`,
          rowId: sourceRow.rowId,
          location: sourceReference,
        },
        sourceRow,
      }];
    });
    const rowIds = new Set(ignoredRows.map((row) => row.rowId));
    const errors = draft.preview.errors.filter((error) => !rowIds.has(error.rowId ?? ''));

    return {
      ...draft,
      preview: {
        ...draft.preview,
        success: errors.length === 0,
        parts: draft.preview.parts.filter((part) => !rowIds.has(part.rowId)),
        errors,
        warnings: draft.preview.warnings.filter((warning) => !rowIds.has(warning.rowId ?? '')),
      },
      options: {
        ...draft.options,
        materialMappings: draft.options.materialMappings.filter(
          (mapping) => mapping.sourceMaterialName !== sourceMaterialName,
        ),
      },
      newMaterials: draft.newMaterials.filter(
        (material) => material.sourceMaterialName !== sourceMaterialName,
      ),
      ignoredMaterialNames: Array.from(new Set([
        ...draft.ignoredMaterialNames,
        sourceMaterialName,
      ])),
      excludedSourceRows: [
        ...draft.excludedSourceRows.filter((row) => !rowIds.has(row.rowId)),
        ...ignoredRows,
      ],
      partOverrides: draft.partOverrides.filter((item) => !rowIds.has(item.rowId)),
    };
  });
}

export function ignoreMaterialInSession(
  session: ImportMappingSession,
  sourceMaterialName: string,
): ImportMappingSession {
  if (!session.worksheets) {
    return session;
  }

  const worksheets = ignoreWorkbookMaterial(session.worksheets, sourceMaterialName);
  const activeDraft = worksheets.find(
    (draft) => draft.worksheet.worksheetName === session.activeWorksheetName,
  ) ?? worksheets.find((draft) => draft.selected);
  return {
    ...session,
    worksheets,
    activeWorksheetName: activeDraft?.worksheet.worksheetName ?? session.activeWorksheetName,
    preview: activeDraft?.preview ?? session.preview,
    options: activeDraft?.options ?? session.options,
    newMaterials: activeDraft?.newMaterials ?? session.newMaterials,
    hasPendingChanges: activeDraft?.hasPendingChanges ?? session.hasPendingChanges,
  };
}

function restoreIgnoredMaterialRows(
  draft: ImportWorksheetDraft,
  sourceMaterialName: string,
): ImportWorksheetDraft {
  const restored = draft.excludedSourceRows.filter(
    (row) => row.originalValidationError.code === 'ignored-material' &&
      row.sourceRow?.materialName === sourceMaterialName,
  );
  if (restored.length === 0 && !draft.ignoredMaterialNames.includes(sourceMaterialName)) {
    return draft;
  }

  return {
    ...draft,
    preview: {
      ...draft.preview,
      parts: [
        ...draft.preview.parts,
        ...restored.flatMap((row) => row.sourceRow ? [row.sourceRow] : []),
      ],
    },
    excludedSourceRows: draft.excludedSourceRows.filter((row) => !restored.includes(row)),
    ignoredMaterialNames: draft.ignoredMaterialNames.filter(
      (name) => name !== sourceMaterialName,
    ),
  };
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
  const workbookIgnoredMaterials = new Set(
    drafts
      .filter((draft) => draft.selected)
      .flatMap((draft) => draft.ignoredMaterialNames),
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
          ignoredMaterialNames: selected
            ? Array.from(new Set([...draft.ignoredMaterialNames, ...workbookIgnoredMaterials]))
            : draft.ignoredMaterialNames,
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

export function worksheetSourceColumns(
  draft: ImportWorksheetDraft,
): ImportSourceColumn[] {
  const fromRange = sourceColumnsForHeadingRange(draft, draft.headingRange);
  return fromRange.length > 0 ? fromRange : draft.preview.sourceColumns;
}

function sourceColumnsForHeadingRange(
  draft: ImportWorksheetDraft,
  address: string,
): ImportSourceColumn[] {
  return sourceColumnsForWorksheetRange(draft.worksheet, address);
}

function sourceColumnsForWorksheetRange(
  worksheet: ImportWorksheetDescriptor,
  address: string,
): ImportSourceColumn[] {
  const match = /^([A-Z]+)([1-9]\d*):([A-Z]+)([1-9]\d*)$/.exec(address);
  if (!match || match[2] !== match[4]) {
    return [];
  }

  const firstColumn = columnNumber(match[1]);
  const lastColumn = columnNumber(match[3]);
  const row = worksheet.previewRows?.find(
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
