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
