import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { StockLengthImportWorkflow } from '../src/pages/StockLengthImportWorkflow';
import type { ImportMappingSession, RequiredPiece } from '../src/types/contracts';

function piece(id: string, finish: string | null, row: number): RequiredPiece {
  return {
    requiredPieceId: id, quantity: 1, quantityText: '1', length: 20, lengthText: '20',
    profileNumber: 'P-100', finish, validationStatus: 'valid', validationMessages: [],
    sourceReferences: [{ worksheetName: 'Parts', worksheetPosition: 1, physicalRow: row, sourceFingerprint: id }],
  };
}

function importSession(): ImportMappingSession {
  const mappings = [
    { targetField: 'Quantity' as const, sourceColumn: 'A' },
    { targetField: 'Length' as const, sourceColumn: 'B' },
    { targetField: 'Profile Number' as const, sourceColumn: 'C' },
    { targetField: 'Finish' as const, sourceColumn: 'D' },
  ];
  const worksheet = {
    worksheetName: 'Parts', originalPosition: 1, usedRowCount: 4, headingRange: 'A1:D1',
    headingRangeDetectionStatus: 'unique-high-confidence' as const, headingRangeCandidates: [],
    previewRows: [{ rowNumber: 1, cells: ['Quantity', 'Length', 'Profile', 'Finish'].map((value, index) => ({ address: `${String.fromCharCode(65 + index)}1`, columnNumber: index + 1, value, isHidden: false, isFormula: false })) }],
  };
  const requiredPieces = [piece('one', ' Clear ', 2), piece('two', 'clear', 3), piece('blank', null, 4)];
  const options = { projectKind: 'stockLength' as const, columnMappings: mappings, materialMappings: [] };
  const preview = { success: true, filePath: 'F:\\stock.csv', parts: [], requiredPieces, errors: [], warnings: [], availableColumns: ['A', 'B', 'C', 'D'], sourceColumns: mappings.map((mapping) => ({ address: mapping.sourceColumn, heading: mapping.targetField })), columnMappings: mappings, materialResolutions: [], worksheet };
  return {
    sessionId: 'field-groups', filePath: preview.filePath, preview, options, newMaterials: [], hasPendingChanges: false,
    activeWorksheetName: 'Parts', workbook: { initialWorksheetName: 'Parts', worksheets: [worksheet], macrosPresent: false },
    worksheets: [{ worksheet, selected: true, optimizationGroupId: '', optimizationGroupName: '', stockLength: null, preview, options, newMaterials: [], hasPendingChanges: false, headingRange: 'A1:D1', headingRangeConfirmed: true, excludedSourceRows: [], ignoredMaterialNames: [], partOverrides: [] }],
  };
}

describe('Stock-Length field grouping', () => {
  it('merges normalized field values, keeps blanks last, and requires each Stock Length', async () => {
    const user = userEvent.setup();
    function Harness() {
      const [session, setSession] = useState(importSession());
      return <StockLengthImportWorkflow busy={false} groups={[]} onCancel={vi.fn()} onFinalize={vi.fn()} onPreview={vi.fn()} onReplaceFile={vi.fn()} onUpdateSession={setSession} session={session} />;
    }
    render(<Harness />);

    await user.selectOptions(screen.getByLabelText('Create Optimization Groups by'), 'mappedField');
    await user.selectOptions(screen.getByLabelText('Grouping Field'), 'Finish');
    await user.click(screen.getByRole('button', { name: 'Continue to Map Fields →' }));
    await user.click(screen.getByRole('button', { name: /Review 2 Entries/ }));

    expect(screen.getByRole('heading', { name: 'Optimization Groups from Finish' })).toBeInTheDocument();
    expect(screen.getAllByText('Unspecified Finish')).toHaveLength(2);
    expect(screen.getByLabelText('Stock Length for Clear')).toBeRequired();
    expect(screen.getByLabelText('Stock Length for Unspecified Finish')).toBeRequired();
    expect(screen.getByRole('button', { name: /Import 2 Required Piece Entries/ })).toBeDisabled();
  });
});
