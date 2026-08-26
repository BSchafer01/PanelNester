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
  const firstWorksheet = workbook.worksheets[0];
  return workbook.worksheets.map((worksheet) => ({
    worksheet,
    selected: worksheet.worksheetName === firstWorksheet.worksheetName,
    optimizationGroupId: `import-${sessionId}-${worksheet.originalPosition}`,
    optimizationGroupName: worksheet.worksheetName,
    preview:
      worksheet.worksheetName === firstWorksheet.worksheetName
        ? firstPreview
        : emptyPreview(),
    options:
      worksheet.worksheetName === firstWorksheet.worksheetName
        ? firstOptions
        : { columnMappings: [], materialMappings: [] },
    newMaterials: [],
    hasPendingChanges: worksheet.worksheetName !== firstWorksheet.worksheetName,
  }));
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
    columnMappings: [],
    materialResolutions: [],
  };
}
