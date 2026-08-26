import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { RequiredPiecesPage } from '../src/pages/RequiredPiecesPage';
import type { OptimizationGroup } from '../src/types/contracts';

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
});
