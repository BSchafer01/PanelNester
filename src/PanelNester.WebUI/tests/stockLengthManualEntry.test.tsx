import { useState } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { RequiredPiecesPage } from '../src/pages/RequiredPiecesPage';
import type { ImportMappingSession, OptimizationGroup } from '../src/types/contracts';

const emptyGroup: OptimizationGroup = {
  optimizationGroupId: 'frames',
  name: 'Frames',
  order: 0,
  origin: 'project',
  stockLength: 240,
  requiredPieces: [],
  stockGroups: [],
  parts: [],
  lastNestingResult: null,
  lastBatchNestingResult: null,
  resultStatus: 'none',
};

const populatedGroup: OptimizationGroup = {
  ...emptyGroup,
  requiredPieces: [{
    requiredPieceId: 'piece-1', quantity: 3, length: 12.375, profileNumber: 'H-120',
    partName: 'Header', finish: '', partNumber: 'P-17', isManual: true, sourceReferences: [],
  }],
};

const baseProps = {
  busy: false,
  inchDisplayFormat: 'decimal' as const,
  onCreateOptimizationGroup: vi.fn(),
  onCreateRequiredPiece: vi.fn(),
  onDeleteRequiredPiece: vi.fn(),
  onInchDisplayFormatChange: vi.fn(),
  onUpdateRequiredPiece: vi.fn(),
  onUpdateStockLength: vi.fn(),
};

function makeWorkbookSession(options?: { csv?: boolean; error?: boolean }): ImportMappingSession {
  const csv = options?.csv ?? false;
  const worksheetName = csv ? 'stock.csv' : 'First';
  const descriptor = {
    worksheetName,
    originalPosition: csv ? 0 : 1,
    usedRowCount: 3,
    headingRange: csv ? 'R1C1:R1C3' : 'A1:C1',
    headingRangeDetectionStatus: 'unique-high-confidence' as const,
    headingRangeCandidates: [],
    previewRows: [{
      rowNumber: 1,
      cells: [
        { address: 'A1', columnNumber: 1, value: 'Quantity', isHidden: false, isFormula: false },
        { address: 'B1', columnNumber: 2, value: 'Length', isHidden: false, isFormula: false },
        { address: 'C1', columnNumber: 3, value: 'Profile', isHidden: false, isFormula: false },
      ],
    }, {
      rowNumber: 2,
      cells: [
        { address: 'A2', columnNumber: 1, value: '2', isHidden: false, isFormula: false },
        { address: 'B2', columnNumber: 2, value: '48', isHidden: false, isFormula: false },
        { address: 'C2', columnNumber: 3, value: 'EX-1', isHidden: false, isFormula: false },
      ],
    }],
  };
  const requiredPiece = {
    requiredPieceId: options?.error ? 'bad-row' : 'good-row',
    quantity: options?.error ? 0 : 2,
    quantityText: options?.error ? 'bad' : '2',
    length: 48,
    lengthText: '48',
    profileNumber: 'EX-1',
    partName: null,
    finish: null,
    partNumber: null,
    isManual: false,
    validationStatus: options?.error ? 'error' as const : 'valid' as const,
    validationMessages: options?.error ? ['Quantity must be an integer value.'] : [],
    sourceReferences: [{
      worksheetName,
      worksheetPosition: descriptor.originalPosition,
      physicalRow: 2,
      sourceFingerprint: 'ABC',
    }],
  };
  const mappings = [
    { targetField: 'Quantity' as const, sourceColumn: 'A' },
    { targetField: 'Length' as const, sourceColumn: 'B' },
    { targetField: 'Profile Number' as const, sourceColumn: 'C' },
  ];
  const preview = {
    success: !options?.error,
    filePath: csv ? 'F:\\stock.csv' : 'F:\\stock.xlsx',
    parts: [],
    requiredPieces: [requiredPiece],
    errors: options?.error ? [{ code: 'invalid-quantity', message: 'Quantity must be an integer value.', rowId: requiredPiece.requiredPieceId }] : [],
    warnings: [],
    availableColumns: ['A', 'B', 'C'],
    sourceColumns: [
      { address: 'A', heading: 'Quantity' },
      { address: 'B', heading: 'Length' },
      { address: 'C', heading: 'Profile' },
    ],
    columnMappings: mappings,
    materialResolutions: [],
    worksheet: descriptor,
  };
  return {
    sessionId: csv ? 'csv-session' : 'workbook-session',
    filePath: preview.filePath,
    preview,
    options: { projectKind: 'stockLength', columnMappings: mappings, materialMappings: [] },
    newMaterials: [],
    hasPendingChanges: false,
    activeWorksheetName: worksheetName,
    workbook: { initialWorksheetName: worksheetName, worksheets: [descriptor], macrosPresent: false },
    worksheets: [{
      worksheet: descriptor,
      selected: true,
      optimizationGroupId: 'frames',
      optimizationGroupName: 'Frames',
      stockLength: 240,
      preview,
      options: { projectKind: 'stockLength', columnMappings: mappings, materialMappings: [] },
      newMaterials: [],
      hasPendingChanges: false,
      headingRange: descriptor.headingRange,
      headingRangeConfirmed: true,
      excludedSourceRows: [],
      ignoredMaterialNames: [],
      partOverrides: [],
    }],
  };
}

describe('Stock-Length Required Pieces workspace', () => {
  it('renders the default empty state without duplicate actions or Optimization Groups and accepts a dropped Workbook', () => {
    const onImportFile = vi.fn();
    render(<RequiredPiecesPage {...baseProps} onImportFile={onImportFile} optimizationGroups={[]} />);
    expect(screen.getByRole('heading', { name: 'No Required Pieces have been added yet.' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Optimization Groups' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Import file' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add Required Piece' })).not.toBeInTheDocument();
    const dropZone = screen.getByRole('region', { name: 'Import Required Pieces' });
    const file = new File(['workbook'], 'parts.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
    Object.defineProperty(file, 'path', { value: 'C:\\Imports\\parts.xlsx' });
    fireEvent.drop(dropZone, { dataTransfer: { files: [file], getData: () => '' } });
    expect(onImportFile).toHaveBeenCalledWith('C:\\Imports\\parts.xlsx');
  });

  it('hands a pathless dropped Workbook to the desktop import boundary', () => {
    const onImportFile = vi.fn();
    const onImportDroppedFile = vi.fn();
    render(<RequiredPiecesPage
      {...baseProps}
      onImportDroppedFile={onImportDroppedFile}
      onImportFile={onImportFile}
      optimizationGroups={[]}
    />);

    const file = new File(
      ['workbook'],
      'parts.xlsx',
      { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
    );
    fireEvent.drop(screen.getByRole('region', { name: 'Import Required Pieces' }), {
      dataTransfer: { files: [file], getData: () => '' },
    });

    expect(onImportDroppedFile).toHaveBeenCalledWith(file);
    expect(onImportFile).not.toHaveBeenCalled();
    expect(screen.queryByText(/could not read the dropped file path/i)).not.toBeInTheDocument();
  });

  it('adds, edits, formats, filters, and deletes Required Pieces', async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn();
    const onUpdate = vi.fn();
    const onDelete = vi.fn();
    const { rerender } = render(<RequiredPiecesPage {...baseProps} onCreateRequiredPiece={onCreate} onDeleteRequiredPiece={onDelete} onUpdateRequiredPiece={onUpdate} optimizationGroups={[emptyGroup]} />);

    await user.click(screen.getByRole('button', { name: 'Add Required Piece' }));
    await user.type(screen.getByRole('textbox', { name: 'Quantity' }), '3');
    await user.type(screen.getByRole('textbox', { name: 'Length' }), '12 3/8');
    await user.type(screen.getByRole('textbox', { name: 'Profile Number' }), 'H-120');
    await user.click(screen.getByRole('button', { name: 'Save New Required Piece' }));
    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ type: 'create', optimizationGroupId: 'frames', quantity: '3' }));

    rerender(<RequiredPiecesPage {...baseProps} inchDisplayFormat="fractional16" onCreateRequiredPiece={onCreate} onDeleteRequiredPiece={onDelete} onUpdateRequiredPiece={onUpdate} optimizationGroups={[populatedGroup]} />);
    expect(screen.getByText('12 3/8 in')).toBeInTheDocument();
    expect(screen.getByText('No finish specified')).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Search Required Pieces' }), 'missing');
    expect(screen.getByText('No Required Pieces match the current filters.')).toBeInTheDocument();
    await user.clear(screen.getByRole('textbox', { name: 'Search Required Pieces' }));
    await user.click(screen.getByRole('button', { name: 'Edit Required Piece piece-1' }));
    const quantity = screen.getByRole('textbox', { name: 'Quantity' });
    await user.clear(quantity);
    await user.type(quantity, '5');
    await user.click(screen.getByRole('button', { name: 'Save Required Piece' }));
    expect(onUpdate).toHaveBeenCalledWith(expect.objectContaining({ type: 'update', quantity: '5' }));
    await user.click(screen.getByRole('button', { name: 'Delete Required Piece piece-1' }));
    expect(onDelete).toHaveBeenCalledWith('frames', 'piece-1');
  });

  it('generates an ordered selection and retains the large-quantity confirmation', async () => {
    const user = userEvent.setup();
    const onGenerate = vi.fn();
    const large = { ...populatedGroup, requiredPieces: [{ ...populatedGroup.requiredPieces[0], quantity: 10_001 }] };
    const doors = {
      ...populatedGroup,
      optimizationGroupId: 'doors',
      name: 'Doors',
      order: 1,
      requiredPieces: [{ ...populatedGroup.requiredPieces[0], requiredPieceId: 'door-1', quantity: 1 }],
    };
    render(<RequiredPiecesPage {...baseProps} activeOptimizationGroupId="frames" onGenerateSelectedGroups={onGenerate} optimizationGroups={[large, doors]} />);

    await user.click(screen.getByRole('checkbox', { name: 'Select Doors for generation' }));
    await user.click(screen.getByRole('button', { name: 'Generate Selected' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('10,002 Piece Instances');
    expect(onGenerate).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onGenerate).toHaveBeenCalledWith(['frames', 'doors']);
  });

  it('shows persisted import details and re-imports from the saved source', async () => {
    const user = userEvent.setup();
    const onReimportFile = vi.fn();
    const onUndoImport = vi.fn();
    render(<RequiredPiecesPage
      {...baseProps}
      importConfiguration={{ options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] }, worksheets: [], partOverrides: [] }}
      importSource={{ importSourcePath: 'F:\\Imports\\stock.xlsx', contentFingerprint: 'ABC', contentLength: 12, snapshotCapturedAtUtc: '2026-08-27T12:00:00Z' }}
      lastImportReceipt={{ sourceRowCount: 30, validSourceRowCount: 30, outputEntryCount: 11, totalPieceQuantity: 60, createdEntryCount: 11, updatedEntryCount: 0, skippedSourceRowCount: 0, worksheetCount: 2 }}
      onReimportFile={onReimportFile}
      onUndoImport={onUndoImport}
      optimizationGroups={[populatedGroup]}
    />);

    expect(screen.getByText('✓ stock.xlsx')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Last Import', level: 2 })).toBeInTheDocument();
    expect(screen.getByText('Imported 30 source rows as 11 required-piece entries from 2 worksheets.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'View details' }));
    expect(screen.getByText('F:\\Imports\\stock.xlsx')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '↻ Re-import' }));
    expect(onReimportFile).toHaveBeenCalledOnce();
    await user.click(screen.getByRole('button', { name: 'Undo Import' }));
    expect(onUndoImport).toHaveBeenCalledOnce();
  });

  it('places the completed summary and Last Import side-by-side while keeping Required Pieces full width', () => {
    render(<RequiredPiecesPage
      {...baseProps}
      importConfiguration={{ options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] }, worksheets: [], partOverrides: [] }}
      importSource={{ importSourcePath: 'F:\\Imports\\stock.xlsx', contentFingerprint: 'ABC', contentLength: 12, snapshotCapturedAtUtc: '2026-08-27T12:00:00Z' }}
      optimizationGroups={[populatedGroup]}
    />);

    const summaryRow = screen.getByRole('heading', { name: 'Optimization Groups' }).closest('.stock-length-workspace__summary-row');
    expect(summaryRow).not.toBeNull();
    expect(summaryRow).toContainElement(screen.getByRole('heading', { name: 'Last Import', level: 2 }));
    expect(screen.getByRole('heading', { name: 'Required Piece Entries (1)', level: 2 }).closest('.stock-length-workspace__summary-row')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Import another file' })).not.toBeInTheDocument();
  });

  it('shows domain progress and lets the user cancel generation', async () => {
    const user = userEvent.setup();
    const onCancel = vi.fn();
    render(<RequiredPiecesPage {...baseProps} busy generationBusy generationProgress={{
      phase: 'optimizationGroups', completedOptimizationGroups: 1, totalOptimizationGroups: 3,
      optimizationGroupId: 'frames', completedStockGroups: 0, totalStockGroups: 0,
      completedPieceInstanceSteps: 0, totalPieceInstanceSteps: 0, label: "Generating Cut Plan for 'Frames'",
    }} onCancelGeneration={onCancel} optimizationGroups={[emptyGroup]} />);
    expect(screen.getByRole('progressbar', { name: 'Cut Plan generation progress' })).toHaveValue(1);
    await user.click(screen.getByRole('button', { name: 'Cancel Generation' }));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});

describe('Stock-Length Import Workflow', () => {
  function Harness({ initial, onFinalize = vi.fn(), onPreview = vi.fn() }: { initial: ImportMappingSession; onFinalize?: () => void; onPreview?: (session?: ImportMappingSession) => void }) {
    const [session, setSession] = useState(initial);
    return <RequiredPiecesPage
      {...baseProps}
      mappingSession={session}
      onCancelImportMapping={vi.fn()}
      onFinalizeImportMapping={onFinalize}
      onImportFile={vi.fn()}
      onPreviewImportMapping={onPreview}
      onUpdateImportMappingSession={setSession}
      optimizationGroups={[emptyGroup]}
    />;
  }

  it('moves through Worksheet selection, mapping, and review while preserving Back state', async () => {
    const user = userEvent.setup();
    render(<Harness initial={makeWorkbookSession()} />);
    expect(screen.getByRole('heading', { name: 'Select Worksheets' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '3' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    expect(screen.getByRole('heading', { name: 'Map Fields for Layout 1' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'First cell preview' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Review 1 Entries/ }));
    expect(screen.getByRole('heading', { name: 'Review & Validate' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('heading', { name: 'Map Fields for Layout 1' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'First column for Quantity' })).toHaveValue('A');
  });

  it('shows the complete application schema before Auto-map and keeps readiness messaging consistent', async () => {
    const user = userEvent.setup();
    render(<RequiredPiecesPage
      {...baseProps}
      mappingSession={makeWorkbookSession()}
      message="3 field mapping(s) still need attention before finalizing the import."
      onCancelImportMapping={vi.fn()}
      onFinalizeImportMapping={vi.fn()}
      onImportFile={vi.fn()}
      onPreviewImportMapping={vi.fn()}
      onUpdateImportMappingSession={vi.fn()}
      optimizationGroups={[emptyGroup]}
    />);
    expect(screen.queryByText(/field mapping\(s\) still need attention/)).not.toBeInTheDocument();
    expect(screen.queryByText('Needs mapping')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    for (const field of ['Quantity', 'Length', 'Profile Number', 'Part Name', 'Finish', 'Part Number']) {
      expect(screen.getByRole('combobox', { name: `First column for ${field}` })).toBeInTheDocument();
    }
  });

  it('enables Review immediately when the final required mapping is selected for every Worksheet in a layout', async () => {
    const user = userEvent.setup();
    const initial = makeWorkbookSession();
    const incompleteMappings = initial.options.columnMappings.filter((mapping) => mapping.targetField !== 'Profile Number');
    const firstDraft = initial.worksheets![0];
    const secondDescriptor = { ...firstDraft.worksheet, worksheetName: 'Second', originalPosition: 2 };
    const secondDraft = {
      ...firstDraft,
      worksheet: secondDescriptor,
      preview: { ...firstDraft.preview, worksheet: secondDescriptor },
      options: { ...firstDraft.options, columnMappings: incompleteMappings },
      hasPendingChanges: true,
    };
    const incomplete = {
      ...initial,
      options: { ...initial.options, columnMappings: incompleteMappings },
      worksheets: initial.worksheets?.map((draft) => ({
        ...draft,
        hasPendingChanges: true,
        options: { ...draft.options, columnMappings: incompleteMappings },
      })).concat(secondDraft),
      workbook: { ...initial.workbook!, worksheets: [...initial.workbook!.worksheets, secondDescriptor] },
    };
    const onPreview = vi.fn();
    render(<Harness initial={incomplete} onPreview={onPreview} />);

    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    const reviewButton = screen.getByRole('button', { name: /Review \d+ Entries/ });
    expect(reviewButton).toBeDisabled();
    await user.selectOptions(screen.getByRole('combobox', { name: 'First column for Profile Number' }), 'C');

    expect(reviewButton).toBeEnabled();
    expect(screen.getByText('1 of 1 layouts ready')).toBeInTheDocument();
    expect(onPreview).toHaveBeenCalledOnce();
    expect(onPreview).toHaveBeenCalledWith(
      expect.objectContaining({ activeWorksheetName: 'First' }),
      ['First', 'Second'],
    );
  });

  it('refreshes Imported Preview and entry counts live when required mappings become complete', async () => {
    const user = userEvent.setup();
    const initial = makeWorkbookSession();
    const resultingPiece = initial.preview.requiredPieces![0];
    const incompleteMappings = initial.options.columnMappings.filter((mapping) => mapping.targetField !== 'Profile Number');
    const emptyPreview = { ...initial.preview, requiredPieces: [] };
    const liveSession = {
      ...initial,
      preview: emptyPreview,
      options: { ...initial.options, columnMappings: incompleteMappings },
      worksheets: initial.worksheets?.map((draft) => ({
        ...draft,
        preview: emptyPreview,
        options: { ...draft.options, columnMappings: incompleteMappings },
      })),
    };
    const onPreview = vi.fn();

    function LiveHarness() {
      const [session, setSession] = useState(liveSession);
      return <RequiredPiecesPage
        {...baseProps}
        mappingSession={session}
        onCancelImportMapping={vi.fn()}
        onFinalizeImportMapping={vi.fn()}
        onImportFile={vi.fn()}
        onPreviewImportMapping={(next) => {
          onPreview(next);
          if (!next) return;
          const refreshedPreview = { ...next.preview, requiredPieces: [resultingPiece] };
          setSession({
            ...next,
            preview: refreshedPreview,
            worksheets: next.worksheets?.map((draft) => draft.worksheet.worksheetName === next.activeWorksheetName
              ? { ...draft, preview: refreshedPreview, hasPendingChanges: false }
              : draft),
          });
        }}
        onUpdateImportMappingSession={setSession}
        optimizationGroups={[emptyGroup]}
      />;
    }

    render(<LiveHarness />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    await user.click(screen.getByRole('tab', { name: 'Imported Preview' }));
    expect(screen.getByRole('button', { name: /Review 0 Entries/ })).toBeDisabled();
    await user.selectOptions(screen.getByRole('combobox', { name: 'First column for Profile Number' }), 'C');

    await waitFor(() => expect(onPreview).toHaveBeenCalledOnce());
    expect(screen.getByRole('button', { name: /Review 1 Entries/ })).toBeEnabled();
    expect(screen.getByRole('cell', { name: 'EX-1' })).toBeInTheDocument();
  });

  it('stages every cleared Worksheet in a shared layout and reviews their combined 11 entries', async () => {
    const user = userEvent.setup();
    const initial = makeWorkbookSession();
    const firstDraft = initial.worksheets![0];
    const secondDescriptor = { ...firstDraft.worksheet, worksheetName: 'Second', originalPosition: 2 };
    const incompleteMappings = firstDraft.options.columnMappings.filter(
      (mapping) => mapping.targetField !== 'Profile Number',
    );
    const emptyPreview = { ...firstDraft.preview, requiredPieces: [] };
    const piecesFor = (worksheetName: string, worksheetPosition: number, count: number, offset: number) =>
      Array.from({ length: count }, (_, index) => ({
        ...firstDraft.preview.requiredPieces![0],
        requiredPieceId: `${worksheetName}-${index + 1}`,
        length: 40 + offset + index,
        lengthText: `${40 + offset + index}`,
        partNumber: `${worksheetName}-${index + 1}`,
        sourceReferences: [{
          worksheetName,
          worksheetPosition,
          physicalRow: index + 2,
          sourceFingerprint: `${worksheetName}-${index + 1}`,
        }],
      }));
    const liveSession: ImportMappingSession = {
      ...initial,
      preview: emptyPreview,
      options: { ...initial.options, columnMappings: incompleteMappings },
      workbook: { ...initial.workbook!, worksheets: [firstDraft.worksheet, secondDescriptor] },
      worksheets: [
        {
          ...firstDraft,
          preview: emptyPreview,
          options: { ...firstDraft.options, columnMappings: incompleteMappings },
          hasPendingChanges: true,
        },
        {
          ...firstDraft,
          worksheet: secondDescriptor,
          preview: { ...emptyPreview, worksheet: secondDescriptor },
          options: { ...firstDraft.options, columnMappings: incompleteMappings },
          hasPendingChanges: true,
        },
      ],
    };

    function LayoutHarness() {
      const [session, setSession] = useState(liveSession);
      return <RequiredPiecesPage
        {...baseProps}
        mappingSession={session}
        onCancelImportMapping={vi.fn()}
        onFinalizeImportMapping={vi.fn()}
        onImportFile={vi.fn()}
        onPreviewImportMapping={(next, worksheetNames) => {
          if (!next) return;
          const requested = new Set(worksheetNames);
          const worksheets = next.worksheets?.map((draft) => {
            if (!requested.has(draft.worksheet.worksheetName)) return draft;
            const isFirst = draft.worksheet.worksheetName === 'First';
            return {
              ...draft,
              preview: {
                ...draft.preview,
                requiredPieces: piecesFor(
                  draft.worksheet.worksheetName,
                  draft.worksheet.originalPosition,
                  isFirst ? 3 : 8,
                  isFirst ? 0 : 10,
                ),
              },
              hasPendingChanges: false,
            };
          });
          const active = worksheets?.find(
            (draft) => draft.worksheet.worksheetName === next.activeWorksheetName,
          );
          setSession({ ...next, worksheets, preview: active?.preview ?? next.preview });
        }}
        onUpdateImportMappingSession={setSession}
        optimizationGroups={[emptyGroup]}
      />;
    }

    render(<LayoutHarness />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'First column for Profile Number' }), 'C');

    await waitFor(() => expect(screen.getByRole('button', { name: /Review 11 Entries/ })).toBeEnabled());
  });

  it('shows column letters in the source preview', async () => {
    const user = userEvent.setup();
    render(<Harness initial={makeWorkbookSession()} />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    expect(screen.getByRole('columnheader', { name: 'A' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Quantity' })).toHaveClass('worksheet-preview__cell--heading');
    expect(screen.getByRole('button', { name: '2' })).not.toHaveClass('worksheet-preview__cell--heading');
  });

  it('requires explicit exclusion before finalizing an invalid source row', async () => {
    const user = userEvent.setup();
    const onFinalize = vi.fn();
    render(<Harness initial={makeWorkbookSession({ error: true })} onFinalize={onFinalize} />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    await user.click(screen.getByRole('button', { name: /Review 0 Entries/ }));
    expect(screen.getByRole('button', { name: /Import 0 Required Piece Entries/ })).toBeDisabled();
    await user.click(screen.getByRole('tab', { name: 'Errors (1)' }));
    await user.click(screen.getByRole('button', { name: 'Exclude' }));
    await user.click(screen.getByRole('button', { name: /Import 0 Required Piece Entries/ }));
    expect(onFinalize).toHaveBeenCalledOnce();
  });

  it('uses the same workflow for a CSV synthetic Worksheet', () => {
    render(<Harness initial={makeWorkbookSession({ csv: true })} />);
    expect(screen.getByRole('columnheader', { name: 'Source' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Select Import Source' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select stock.csv' })).toBeDisabled();
  });

  it('updates a changed Heading Range without a separate Refresh action', async () => {
    const user = userEvent.setup();
    const onPreview = vi.fn();
    render(<Harness initial={makeWorkbookSession()} onPreview={onPreview} />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    const input = screen.getByRole('textbox', { name: 'Heading Range for First' });
    await user.clear(input);
    await user.type(input, 'A1:C1');
    await user.tab();

    expect(onPreview).toHaveBeenCalledOnce();
    expect(onPreview.mock.calls[0][0]?.worksheets[0].headingRangeConfirmed).toBe(true);
    expect(screen.queryByRole('button', { name: /Refresh preview/i })).not.toBeInTheDocument();
  });

  it('bounds a large Worksheet list inside its own scrolling table region', () => {
    const initial = makeWorkbookSession();
    const baseDraft = initial.worksheets[0];
    const worksheets = Array.from({ length: 45 }, (_, index) => ({
      ...baseDraft,
      selected: index === 0,
      worksheet: { ...baseDraft.worksheet, worksheetName: `Worksheet ${index + 1}`, originalPosition: index + 1 },
    }));
    render(<Harness initial={{ ...initial, activeWorksheetName: 'Worksheet 1', worksheets, workbook: { ...initial.workbook!, initialWorksheetName: 'Worksheet 1', worksheets: worksheets.map((draft) => draft.worksheet) } }} />);

    expect(screen.getByRole('region', { name: 'Worksheet selection table' })).toHaveClass('stock-import-workflow__worksheet-table');
  });
});
