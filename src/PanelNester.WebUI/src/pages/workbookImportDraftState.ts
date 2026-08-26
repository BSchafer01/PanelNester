import type {
  ImportFileResponse,
  ImportOptions,
  ImportWorksheetDraft,
  WorkbookDiscovery,
} from '../types/contracts';

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
  }));
}

export interface ConfirmHeadingRangeResult {
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
): ConfirmHeadingRangeResult {
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
        ? {
            ...draft,
            headingRange: normalized,
            headingRangeConfirmed: true,
            hasPendingChanges: true,
          }
        : draft,
    ),
  };
}

export function copyHeadingRangeFromPreviousSelectedWorksheet(
  drafts: ImportWorksheetDraft[],
  worksheetName: string,
): ConfirmHeadingRangeResult {
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
  return drafts.map((draft) =>
    draft.worksheet.worksheetName === worksheetName
      ? { ...draft, selected }
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

function columnNumber(letters: string): number {
  return Array.from(letters.toUpperCase()).reduce(
    (value, letter) => value * 26 + letter.charCodeAt(0) - 64,
    0,
  );
}
