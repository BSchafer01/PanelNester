import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { StockLengthResults } from '../src/pages/ResultsPage';
import type { OptimizationGroup } from '../src/types/contracts';

describe('Stock-Length Results', () => {
  it('shows ordered Stock Items with core metrics status and Unplaced details', () => {
    const group: OptimizationGroup = {
      optimizationGroupId: 'frames', name: 'Frames', order: 0, parts: [], stockLength: 120,
      requiredPieces: [], stockGroups: [], lastNestingResult: null, lastBatchNestingResult: null,
      resultStatus: 'valid',
      lastStockLengthOptimizationResult: {
        optimizationGroupId: 'frames', status: 'partial',
        description: 'Deterministic heuristic Cut Plan',
        cutPlans: [{
          cutPlanId: 'frames:stock-group-1', status: 'partial',
          stockGroup: { profileNumber: 'P-100', finish: 'Clear', requiredPieceIds: ['piece-1'] },
          stockItems: [
            { stockItemId: 'item-2', stockItemNumber: 2, stockLength: 120, pieceLength: 40, sawLoss: 0, remainder: 80, utilizationPercent: 33.33, cutSequence: [] },
            { stockItemId: 'item-1', stockItemNumber: 1, stockLength: 120, pieceLength: 96, sawLoss: 0.125, remainder: 23.875, utilizationPercent: 80, cutSequence: [] },
          ],
          unplacedPieceInstances: [{
            pieceInstance: { pieceInstanceId: 'piece-2:instance-1', requiredPieceId: 'piece-2', instanceNumber: 1, length: 130, profileNumber: 'P-100', sourceReferences: [] },
            reasonCode: 'exceeds-stock-length', reasonDescription: 'Piece Instance exceeds Stock Length.',
          }],
        }],
      },
    };

    render(<StockLengthResults activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[group]} />);

    expect(screen.getByRole('heading', { name: 'Frames Cut Plan' })).toBeInTheDocument();
    expect(screen.getByText('Deterministic heuristic Cut Plan')).toBeInTheDocument();
    expect(screen.getAllByText('Partial')).toHaveLength(3);
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]).getByText('1')).toBeInTheDocument();
    expect(within(rows[0]).getByText('96 in')).toBeInTheDocument();
    expect(within(rows[0]).getByText('0.125 in')).toBeInTheDocument();
    expect(within(rows[0]).getByText('23.875 in')).toBeInTheDocument();
    expect(within(rows[0]).getByText('80.0%')).toBeInTheDocument();
    expect(within(rows[1]).getByText('2')).toBeInTheDocument();
    expect(screen.getByText('piece-2:instance-1')).toBeInTheDocument();
    expect(screen.getByText('Piece Instance exceeds Stock Length.')).toBeInTheDocument();
  });

  it('keeps the selected empty group visible instead of falling back to another group', () => {
    const empty: OptimizationGroup = {
      optimizationGroupId: 'empty', name: 'Empty', order: 0, parts: [], requiredPieces: [], stockGroups: [],
      lastNestingResult: null, lastBatchNestingResult: null, lastStockLengthOptimizationResult: null, resultStatus: 'none',
    };
    const populated: OptimizationGroup = {
      ...empty, optimizationGroupId: 'populated', name: 'Populated', order: 1,
      requiredPieces: [{ requiredPieceId: 'piece-1', quantity: 1, length: 20, profileNumber: 'P-100', sourceReferences: [] }],
    };

    render(<StockLengthResults activeOptimizationGroupId="empty" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[empty, populated]} />);

    expect(screen.getByRole('heading', { name: 'Empty Cut Plan' })).toBeInTheDocument();
    expect(screen.getByText('Empty Optimization Group', { selector: 'strong' })).toBeInTheDocument();
  });

  it('preserves Cut Plan group order beyond nine groups', () => {
    const cutPlans = Array.from({ length: 10 }, (_, index) => ({
      cutPlanId: `frames:stock-group-${index + 1}`, status: 'complete' as const,
      stockGroup: { profileNumber: `P-${index + 1}`, finish: null, requiredPieceIds: [`piece-${index + 1}`] },
      stockItems: [{ stockItemId: `item-${index + 1}`, stockItemNumber: 1, stockLength: 120, pieceLength: 20, sawLoss: 0, remainder: 100, utilizationPercent: 16.67, cutSequence: [] }],
      unplacedPieceInstances: [],
    }));
    const group: OptimizationGroup = {
      optimizationGroupId: 'frames', name: 'Frames', order: 0, parts: [], requiredPieces: [], stockGroups: [],
      lastNestingResult: null, lastBatchNestingResult: null, resultStatus: 'valid',
      lastStockLengthOptimizationResult: { optimizationGroupId: 'frames', status: 'complete', description: 'Deterministic heuristic Cut Plan', cutPlans },
    };

    render(<StockLengthResults activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[group]} />);

    const stockRows = screen.getAllByRole('row').slice(1);
    expect(within(stockRows[1]).getByText('P-2')).toBeInTheDocument();
    expect(within(stockRows[9]).getByText('P-10')).toBeInTheDocument();
  });
});
