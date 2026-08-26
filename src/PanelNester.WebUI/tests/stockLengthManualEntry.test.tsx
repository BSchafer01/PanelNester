import { render, screen } from '@testing-library/react';
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

describe('Stock-Length manual entry', () => {
  it('generates only the selected nonempty Optimization Group', async () => {
    const user = userEvent.setup();
    const onGenerateSelected = vi.fn();
    const populatedGroup: OptimizationGroup = {
      ...emptyGroup,
      requiredPieces: [{
        requiredPieceId: 'piece-1', quantity: 1, length: 48, profileNumber: 'P-100',
        isManual: true, sourceReferences: [],
      }],
    };
    const { rerender } = render(
      <RequiredPiecesPage
        activeOptimizationGroupId="frames"
        busy={false}
        inchDisplayFormat="decimal"
        onCreateOptimizationGroup={vi.fn()}
        onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()}
        onGenerateSelected={onGenerateSelected}
        onInchDisplayFormatChange={vi.fn()}
        onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[populatedGroup]}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Generate Selected' }));
    expect(onGenerateSelected).toHaveBeenCalledWith('frames');

    rerender(
      <RequiredPiecesPage
        activeOptimizationGroupId="frames"
        busy={false}
        inchDisplayFormat="decimal"
        onCreateOptimizationGroup={vi.fn()}
        onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()}
        onGenerateSelected={onGenerateSelected}
        onInchDisplayFormatChange={vi.fn()}
        onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[emptyGroup]}
      />,
    );
    expect(screen.getByRole('button', { name: 'Generate Selected' })).toBeDisabled();
    expect(screen.getByText('Empty Optimization Group')).toBeInTheDocument();
  });

  it('creates an Optimization Group with Stock Length', async () => {
    const user = userEvent.setup();
    const onCreateOptimizationGroup = vi.fn();
    render(
      <RequiredPiecesPage
        busy={false}
        inchDisplayFormat="decimal"
        onCreateOptimizationGroup={onCreateOptimizationGroup}
        onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()}
        onInchDisplayFormatChange={vi.fn()}
        onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[]}
      />,
    );

    await user.type(screen.getByRole('textbox', { name: 'Optimization Group name' }), 'Frames');
    await user.type(screen.getByRole('textbox', { name: 'Stock Length' }), '20 1/2');
    await user.click(screen.getByRole('button', { name: 'Add Optimization Group' }));

    expect(onCreateOptimizationGroup).toHaveBeenCalledWith('Frames', '20 1/2');
  });

  it('creates edits formats and deletes a manual Required Piece', async () => {
    const user = userEvent.setup();
    const onCreateRequiredPiece = vi.fn();
    const onUpdateRequiredPiece = vi.fn();
    const onDeleteRequiredPiece = vi.fn();
    const { rerender } = render(
      <RequiredPiecesPage
        busy={false}
        inchDisplayFormat="decimal"
        onCreateOptimizationGroup={vi.fn()}
        onCreateRequiredPiece={onCreateRequiredPiece}
        onDeleteRequiredPiece={onDeleteRequiredPiece}
        onInchDisplayFormatChange={vi.fn()}
        onUpdateRequiredPiece={onUpdateRequiredPiece}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[emptyGroup]}
      />,
    );

    await user.type(screen.getByRole('textbox', { name: 'Quantity' }), '3');
    await user.type(screen.getByRole('textbox', { name: 'Length' }), '12 3/8');
    await user.type(screen.getByRole('textbox', { name: 'Profile Number' }), ' H-120 ');
    await user.type(screen.getByRole('textbox', { name: 'Part Name' }), 'Header');
    await user.type(screen.getByRole('textbox', { name: 'Finish' }), 'Clear');
    await user.type(screen.getByRole('textbox', { name: 'Part Number' }), 'P-17');
    await user.click(screen.getByRole('button', { name: 'Add Required Piece' }));

    expect(onCreateRequiredPiece).toHaveBeenCalledWith({
      type: 'create',
      optimizationGroupId: 'frames',
      quantity: '3',
      length: '12 3/8',
      profileNumber: ' H-120 ',
      partName: 'Header',
      finish: 'Clear',
      partNumber: 'P-17',
    });

    const populatedGroup: OptimizationGroup = {
      ...emptyGroup,
      requiredPieces: [
        {
          requiredPieceId: 'piece-1',
          quantity: 3,
          length: 12.375,
          profileNumber: 'H-120',
          partName: 'Header',
          finish: '',
          partNumber: 'P-17',
          isManual: true,
          sourceReferences: [],
        },
      ],
      stockGroups: [
        {
          profileNumber: 'H-120',
          finish: null,
          requiredPieceIds: ['piece-1'],
        },
      ],
    };
    rerender(
      <RequiredPiecesPage
        busy={false}
        inchDisplayFormat="fractional16"
        onCreateOptimizationGroup={vi.fn()}
        onCreateRequiredPiece={onCreateRequiredPiece}
        onDeleteRequiredPiece={onDeleteRequiredPiece}
        onInchDisplayFormatChange={vi.fn()}
        onUpdateRequiredPiece={onUpdateRequiredPiece}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[populatedGroup]}
      />,
    );

    expect(screen.getByText('12 3/8 in')).toBeInTheDocument();
    expect(screen.getByText('No finish specified')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit Required Piece piece-1' }));
    const quantity = screen.getByRole('textbox', { name: 'Quantity' });
    await user.clear(quantity);
    await user.type(quantity, '5');
    await user.click(screen.getByRole('button', { name: 'Save Required Piece' }));
    expect(onUpdateRequiredPiece).toHaveBeenCalledWith(
      expect.objectContaining({
        type: 'update',
        requiredPieceId: 'piece-1',
        optimizationGroupId: 'frames',
        quantity: '5',
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Delete Required Piece piece-1' }));
    expect(onDeleteRequiredPiece).toHaveBeenCalledWith('frames', 'piece-1');
  });

  it('renders all supported fractional display precisions without changing geometry', () => {
    const precisionPiece = {
      ...emptyGroup,
      requiredPieces: [{
        requiredPieceId: 'piece-precision',
        quantity: 1,
        length: 1.015625,
        profileNumber: 'P',
        partName: null,
        finish: null,
        partNumber: null,
        isManual: true,
        sourceReferences: [],
      }],
      stockGroups: [{
        profileNumber: 'P',
        finish: null,
        requiredPieceIds: ['piece-precision'],
      }],
    };
    const props = {
      busy: false,
      onCreateOptimizationGroup: vi.fn(),
      onCreateRequiredPiece: vi.fn(),
      onDeleteRequiredPiece: vi.fn(),
      onInchDisplayFormatChange: vi.fn(),
      onUpdateRequiredPiece: vi.fn(),
      onUpdateStockLength: vi.fn(),
      optimizationGroups: [precisionPiece],
    };
    const { rerender } = render(
      <RequiredPiecesPage {...props} inchDisplayFormat="fractional32" />,
    );
    expect(screen.getByText('1 1/32 in')).toBeInTheDocument();

    rerender(<RequiredPiecesPage {...props} inchDisplayFormat="fractional64" />);
    expect(screen.getByText('1 1/64 in')).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /nearest 1\/16/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /nearest 1\/32/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /nearest 1\/64/ })).toBeInTheDocument();
  });

  it('reviews a synthetic CSV Worksheet and excludes invalid Required Pieces before finalization', async () => {
    const user = userEvent.setup();
    const onUpdateImportMappingSession = vi.fn();
    const onFinalizeImportMapping = vi.fn();
    const requiredPiece = {
      requiredPieceId: 'required-invalid',
      quantity: 0,
      quantityText: 'bad',
      length: 12,
      lengthText: '12',
      profileNumber: 'EX-1',
      partName: null,
      finish: null,
      partNumber: null,
      isManual: false,
      validationStatus: 'error' as const,
      validationMessages: ['Quantity must be an integer value.'],
      sourceReferences: [{
        worksheetName: 'stock.csv',
        worksheetPosition: 0,
        physicalRow: 2,
        sourceFingerprint: 'ABC',
      }],
    };
    const preview = {
      success: false,
      filePath: 'F:\\stock.csv',
      parts: [],
      requiredPieces: [requiredPiece],
      errors: [{ code: 'invalid-quantity', message: 'Quantity must be an integer value.', rowId: requiredPiece.requiredPieceId }],
      warnings: [],
      availableColumns: ['Qty', 'Length', 'Die'],
      sourceColumns: [],
      columnMappings: [
        { targetField: 'Quantity' as const, sourceColumn: 'Qty' },
        { targetField: 'Length' as const, sourceColumn: 'Length' },
        { targetField: 'Profile Number' as const, sourceColumn: 'Die' },
        { targetField: 'Part Name' as const, sourceColumn: null },
        { targetField: 'Finish' as const, sourceColumn: null },
        { targetField: 'Part Number' as const, sourceColumn: null },
      ],
      materialResolutions: [],
      worksheet: {
        worksheetName: 'stock.csv', originalPosition: 0, headingRange: 'R1C1:R1C3',
        headingRangeDetectionStatus: 'none' as const, headingRangeCandidates: [], previewRows: [],
      },
    };
    const mappingSession: ImportMappingSession = {
      sessionId: 'stock-session', filePath: 'F:\\stock.csv', preview,
      options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] },
      newMaterials: [], hasPendingChanges: false, activeWorksheetName: 'stock.csv',
      workbook: { initialWorksheetName: 'stock.csv', worksheets: [preview.worksheet!], macrosPresent: false },
      worksheets: [{
        worksheet: preview.worksheet!, selected: true, optimizationGroupId: '', optimizationGroupName: '',
        preview, options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] },
        newMaterials: [], hasPendingChanges: false, headingRange: 'R1C1:R1C3', headingRangeConfirmed: true,
        excludedSourceRows: [], ignoredMaterialNames: [], partOverrides: [],
      }],
    };

    const { rerender } = render(
      <RequiredPiecesPage
        busy={false}
        inchDisplayFormat="decimal"
        mappingSession={mappingSession}
        onCancelImportMapping={vi.fn()}
        onCreateOptimizationGroup={vi.fn()}
        onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()}
        onFinalizeImportMapping={onFinalizeImportMapping}
        onImportFile={vi.fn()}
        onInchDisplayFormatChange={vi.fn()}
        onPreviewImportMapping={vi.fn()}
        onUpdateImportMappingSession={onUpdateImportMappingSession}
        onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()}
        optimizationGroups={[emptyGroup]}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Import Stock-Length CSV' })).toBeInTheDocument();
    expect(screen.queryByText('Material Resolution')).not.toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Optimization Group for stock.csv' }), 'frames');
    expect(onUpdateImportMappingSession).toHaveBeenCalled();
    const assigned = onUpdateImportMappingSession.mock.calls.at(-1)![0] as ImportMappingSession;
    rerender(
      <RequiredPiecesPage
        busy={false} inchDisplayFormat="decimal" mappingSession={assigned}
        onCancelImportMapping={vi.fn()} onCreateOptimizationGroup={vi.fn()} onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()} onFinalizeImportMapping={onFinalizeImportMapping} onImportFile={vi.fn()}
        onInchDisplayFormatChange={vi.fn()} onPreviewImportMapping={vi.fn()}
        onUpdateImportMappingSession={onUpdateImportMappingSession} onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()} optimizationGroups={[emptyGroup]}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Exclude source row 2' }));
    const excluded = onUpdateImportMappingSession.mock.calls.at(-1)![0] as ImportMappingSession;
    rerender(
      <RequiredPiecesPage
        busy={false} inchDisplayFormat="decimal" mappingSession={excluded}
        onCancelImportMapping={vi.fn()} onCreateOptimizationGroup={vi.fn()} onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()} onFinalizeImportMapping={onFinalizeImportMapping} onImportFile={vi.fn()}
        onInchDisplayFormatChange={vi.fn()} onPreviewImportMapping={vi.fn()}
        onUpdateImportMappingSession={onUpdateImportMappingSession} onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()} optimizationGroups={[emptyGroup]}
      />,
    );
    await user.click(screen.getByRole('button', { name: 'Finalize CSV Import' }));
    expect(onFinalizeImportMapping).toHaveBeenCalledOnce();
  });

  it('corrects an invalid imported Required Piece with a provenance-bound Part Override', async () => {
    const user = userEvent.setup();
    const onUpdateImportMappingSession = vi.fn();
    const requiredPiece = {
      requiredPieceId: 'required-invalid', quantity: 0, quantityText: 'bad', length: 12,
      lengthText: '12', profileNumber: 'EX-1', partName: null, finish: null, partNumber: null,
      isManual: false, validationStatus: 'error' as const,
      validationMessages: ['Quantity must be an integer value.'],
      sourceReferences: [{ worksheetName: 'stock.csv', worksheetPosition: 0, physicalRow: 2, sourceFingerprint: 'ABC' }],
    };
    const preview = {
      success: false, filePath: 'F:\\stock.csv', parts: [], requiredPieces: [requiredPiece],
      errors: [{ code: 'invalid-quantity', message: 'Quantity must be an integer value.', rowId: requiredPiece.requiredPieceId }],
      warnings: [], availableColumns: ['Qty', 'Length', 'Die'], sourceColumns: [],
      columnMappings: [
        { targetField: 'Quantity' as const, sourceColumn: 'Qty' },
        { targetField: 'Length' as const, sourceColumn: 'Length' },
        { targetField: 'Profile Number' as const, sourceColumn: 'Die' },
      ],
      materialResolutions: [],
      worksheet: { worksheetName: 'stock.csv', originalPosition: 0, headingRange: 'R1C1:R1C3', headingRangeDetectionStatus: 'none' as const, headingRangeCandidates: [], previewRows: [] },
    };
    const mappingSession: ImportMappingSession = {
      sessionId: 'stock-session', filePath: 'F:\\stock.csv', preview,
      options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] },
      newMaterials: [], hasPendingChanges: false, activeWorksheetName: 'stock.csv',
      workbook: { initialWorksheetName: 'stock.csv', worksheets: [preview.worksheet!], macrosPresent: false },
      worksheets: [{
        worksheet: preview.worksheet!, selected: true, optimizationGroupId: 'frames', optimizationGroupName: 'Frames',
        preview, options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] },
        newMaterials: [], hasPendingChanges: false, headingRange: 'R1C1:R1C3', headingRangeConfirmed: true,
        excludedSourceRows: [], ignoredMaterialNames: [], partOverrides: [],
      }],
    };

    render(
      <RequiredPiecesPage
        busy={false} inchDisplayFormat="decimal" mappingSession={mappingSession}
        onCancelImportMapping={vi.fn()} onCreateOptimizationGroup={vi.fn()} onCreateRequiredPiece={vi.fn()}
        onDeleteRequiredPiece={vi.fn()} onFinalizeImportMapping={vi.fn()} onImportFile={vi.fn()}
        onInchDisplayFormatChange={vi.fn()} onPreviewImportMapping={vi.fn()}
        onUpdateImportMappingSession={onUpdateImportMappingSession} onUpdateRequiredPiece={vi.fn()}
        onUpdateStockLength={vi.fn()} optimizationGroups={[emptyGroup]}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Correct source row 2' }));
    await user.clear(screen.getByRole('textbox', { name: 'Corrected Quantity' }));
    await user.type(screen.getByRole('textbox', { name: 'Corrected Quantity' }), '3');
    await user.click(screen.getByRole('button', { name: 'Save Correction' }));

    const corrected = onUpdateImportMappingSession.mock.calls.at(-1)![0] as ImportMappingSession;
    const partOverride = corrected.worksheets![0].partOverrides[0];
    expect(partOverride.rowId).toBe(requiredPiece.requiredPieceId);
    expect(partOverride.currentRequiredPiece?.quantityText).toBe('3');
    expect(partOverride.sourceReferences).toEqual(requiredPiece.sourceReferences);
  });

  it('configures multiple Workbook Worksheets in bulk and confirms a shared Stock Length edit', async () => {
    const user = userEvent.setup();
    const onUpdateImportMappingSession = vi.fn();
    const onUpdateStockLength = vi.fn();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const requiredMappings = [
      { targetField: 'Quantity' as const, sourceColumn: 'A' },
      { targetField: 'Length' as const, sourceColumn: 'B' },
      { targetField: 'Profile Number' as const, sourceColumn: 'C' },
    ];
    const descriptors = ['First', 'Second'].map((worksheetName, index) => ({
      worksheetName,
      originalPosition: index + 1,
      headingRange: 'A1:C1',
      headingRangeDetectionStatus: 'unique-high-confidence' as const,
      headingRangeCandidates: [],
      previewRows: [],
    }));
    const makePreview = (index: number) => ({
      success: true, filePath: 'F:\\stock.xlsx', parts: [], requiredPieces: [], errors: [], warnings: [],
      availableColumns: ['A', 'B', 'C'], sourceColumns: [], columnMappings: requiredMappings,
      materialResolutions: [], worksheet: descriptors[index],
    });
    const mappingSession: ImportMappingSession = {
      sessionId: 'workbook', filePath: 'F:\\stock.xlsx', preview: makePreview(0),
      options: { projectKind: 'stockLength', columnMappings: requiredMappings, materialMappings: [] },
      newMaterials: [], hasPendingChanges: false, activeWorksheetName: 'First',
      workbook: { initialWorksheetName: 'First', worksheets: descriptors, macrosPresent: false },
      worksheets: descriptors.map((worksheet, index) => ({
        worksheet, selected: true, optimizationGroupId: index === 0 ? 'frames' : 'doors',
        optimizationGroupName: index === 0 ? 'Frames' : 'Doors', preview: makePreview(index),
        options: { projectKind: 'stockLength', columnMappings: requiredMappings, materialMappings: [] },
        newMaterials: [], hasPendingChanges: false, headingRange: 'A1:C1', headingRangeConfirmed: true,
        excludedSourceRows: [], ignoredMaterialNames: [], partOverrides: [],
      })),
    };
    const doors = { ...emptyGroup, optimizationGroupId: 'doors', name: 'Doors', order: 1, stockLength: 120 };

    const pageProps = {
      busy: false,
      inchDisplayFormat: 'decimal' as const,
      mappingSession,
      onCancelImportMapping: vi.fn(),
      onCreateOptimizationGroup: vi.fn(),
      onCreateRequiredPiece: vi.fn(),
      onDeleteRequiredPiece: vi.fn(),
      onFinalizeImportMapping: vi.fn(),
      onImportFile: vi.fn(),
      onInchDisplayFormatChange: vi.fn(),
      onPreviewImportMapping: vi.fn(),
      onUpdateImportMappingSession,
      onUpdateRequiredPiece: vi.fn(),
      onUpdateStockLength,
      optimizationGroups: [emptyGroup, doors],
    };
    const { rerender } = render(
      <RequiredPiecesPage {...pageProps} />,
    );

    expect(screen.getByRole('heading', { name: 'Import Stock-Length Workbook' })).toBeInTheDocument();
    expect(screen.getByText('1. First')).toBeInTheDocument();
    expect(screen.getByText('2. Second')).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Optimization Group for selected Worksheets' }), 'frames');
    await user.click(screen.getByRole('button', { name: 'Assign selected Worksheets' }));
    const assigned = onUpdateImportMappingSession.mock.calls.at(-1)![0] as ImportMappingSession;
    expect(assigned.worksheets?.map((draft) => draft.optimizationGroupId)).toEqual(['frames', 'frames']);
    rerender(<RequiredPiecesPage {...pageProps} mappingSession={assigned} />);

    const stockLength = screen.getByRole('textbox', { name: 'Shared Stock Length for Frames' });
    await user.clear(stockLength);
    await user.type(stockLength, '288');
    await user.click(screen.getByRole('button', { name: 'Save shared Stock Length' }));
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('First, Second'));
    expect(onUpdateStockLength).toHaveBeenCalledWith('frames', '288');
    confirm.mockRestore();
  });
});
