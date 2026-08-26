import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { StockItemViewer } from '../src/components/StockItemViewer';
import { StockLengthResults } from '../src/pages/ResultsPage';
import type { OptimizationGroup, PieceInstance, StockItem } from '../src/types/contracts';

const pieces: PieceInstance[] = [
  { pieceInstanceId: 'piece-a:instance-1', requiredPieceId: 'piece-a', instanceNumber: 1, length: 40, profileNumber: 'P-100', finish: 'Clear', partNumber: 'A-1', partName: 'Left jamb', sourceReferences: [] },
  { pieceInstanceId: 'piece-b:instance-1', requiredPieceId: 'piece-b', instanceNumber: 1, length: 30, profileNumber: 'P-100', finish: 'Clear', partNumber: 'B-1', partName: 'Head', sourceReferences: [] },
];

function stockItem(overrides: Partial<StockItem> = {}): StockItem {
  return {
    stockItemId: 'item-1', stockItemNumber: 1, stockLength: 120, pieceLength: 70,
    sawLoss: 0.125, remainder: 49.875, utilizationPercent: 58.33, cutSequence: pieces,
    ...overrides,
  };
}

function stockGroup(items: StockItem[] = [stockItem()]): OptimizationGroup {
  return {
    optimizationGroupId: 'frames', name: 'Frames', order: 0, parts: [], stockLength: 120,
    requiredPieces: [], stockGroups: [], lastNestingResult: null, lastBatchNestingResult: null,
    resultStatus: 'valid',
    lastStockLengthOptimizationResult: {
      optimizationGroupId: 'frames', status: 'complete', description: 'Deterministic heuristic Cut Plan',
      cutPlans: [{
        cutPlanId: 'frames:p-100-clear', status: 'complete',
        stockGroup: { profileNumber: 'P-100', finish: 'Clear', requiredPieceIds: ['piece-a', 'piece-b'] },
        stockItems: items, unplacedPieceInstances: [],
      }],
    },
  };
}

describe('Stock-Length Results', () => {
  beforeEach(() => sessionStorage.clear());

  it('selects the first Stock Item, updates the viewer from a row, and retains only in-scope composite identity', async () => {
    const user = userEvent.setup();
    const second = stockItem({ stockItemId: 'item-2', stockItemNumber: 2, cutSequence: [pieces[1]], pieceLength: 30, sawLoss: 0, remainder: 90 });
    const group = stockGroup([stockItem(), second]);
    const { rerender } = render(<StockLengthResults activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[group]} />);

    expect(screen.getByRole('region', { name: 'Stock Item 1 viewer' })).toBeInTheDocument();
    expect(screen.getByRole('row', { name: /Stock Item 1/ })).toHaveAttribute('aria-selected', 'true');
    await user.click(screen.getByRole('button', { name: /Cut 1.*A-1.*40 in/ }));
    expect(screen.getByRole('button', { name: /Cut 1.*A-1.*40 in/ })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('row', { name: /Stock Item 2/ }));
    expect(screen.getByRole('region', { name: 'Stock Item 2 viewer' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cut 1.*B-1.*30 in/ })).toHaveAttribute('aria-pressed', 'false');

    rerender(<StockLengthResults activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[stockGroup([second])]} />);
    expect(screen.getByRole('region', { name: 'Stock Item 2 viewer' })).toBeInTheDocument();

    rerender(<StockLengthResults activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[stockGroup([stockItem()])]} />);
    expect(screen.getByRole('region', { name: 'Stock Item 1 viewer' })).toBeInTheDocument();
  });

  it('synchronizes Piece Instance selection between canvas and keyboard Cut Sequence rows', async () => {
    const user = userEvent.setup();
    const onSelectPieceInstance = vi.fn();
    const { rerender } = render(
      <StockItemViewer
        finish="Clear"
        onSelectPieceInstance={onSelectPieceInstance}
        pieceInstances={pieces}
        profileNumber="P-100"
        stockItem={stockItem()}
      />,
    );

    const secondRow = screen.getByRole('button', { name: /Cut 2.*B-1.*30 in/ });
    secondRow.focus();
    await user.keyboard('{Enter}');
    expect(onSelectPieceInstance).toHaveBeenLastCalledWith('piece-b:instance-1');

    rerender(
      <StockItemViewer
        finish="Clear"
        onSelectPieceInstance={onSelectPieceInstance}
        pieceInstances={pieces}
        profileNumber="P-100"
        selectedPieceInstanceId="piece-b:instance-1"
        stockItem={stockItem()}
      />,
    );
    expect(screen.getByRole('button', { name: /Cut 2.*B-1.*30 in/ })).toHaveAttribute('aria-pressed', 'true');

    const canvas = screen.getByRole('img', { name: /schematic for Stock Item 1/ });
    vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({ x: 0, y: 0, left: 0, top: 0, right: 600, bottom: 300, width: 600, height: 300, toJSON: () => ({}) });
    fireEvent.click(canvas, { clientX: 100, clientY: 150 });
    expect(onSelectPieceInstance).toHaveBeenLastCalledWith('piece-a:instance-1');
    fireEvent.click(canvas, { clientX: 590, clientY: 20 });
    expect(onSelectPieceInstance).toHaveBeenLastCalledWith(undefined);
  });

  it('provides non-color cues, explanatory waste hover, camera controls, and remembered collapse state', async () => {
    const user = userEvent.setup();
    const { unmount } = render(
      <StockItemViewer finish="Clear" pieceInstances={pieces} profileNumber="P-100" stockItem={stockItem()} />,
    );

    expect(screen.getByText('Solid numbered blocks')).toBeInTheDocument();
    expect(screen.getByText('Striped gaps')).toBeInTheDocument();
    expect(screen.getByText('Dotted tail')).toBeInTheDocument();
    await user.hover(screen.getByText('Saw Loss'));
    expect(screen.getByRole('tooltip')).toHaveTextContent('0.125 in consumed by 1 kerf');
    expect(screen.getByRole('button', { name: 'Zoom in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Zoom out' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reset View' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Collapse Cut Sequence' }));
    expect(screen.queryByRole('button', { name: /Cut 1/ })).not.toBeInTheDocument();
    unmount();

    render(<StockItemViewer finish="Clear" pieceInstances={pieces} profileNumber="P-100" stockItem={stockItem()} />);
    expect(screen.getByRole('button', { name: 'Expand Cut Sequence' })).toBeInTheDocument();
  });

  it('keeps Cut Sequence selection and scroll state when the responsive layout changes', async () => {
    const user = userEvent.setup();
    const manyPieces = Array.from({ length: 12 }, (_, index): PieceInstance => ({
      ...pieces[index % pieces.length],
      pieceInstanceId: `piece-${index + 1}:instance-1`,
      partNumber: `P-${index + 1}`,
      length: 5,
    }));
    render(<StockItemViewer finish="Clear" pieceInstances={manyPieces} profileNumber="P-100" stockItem={stockItem({ cutSequence: manyPieces, pieceLength: 60, remainder: 58.625, sawLoss: 1.375 })} />);
    const card = screen.getByLabelText('Cut Sequence');
    const rows = card.querySelector<HTMLElement>('.cut-sequence-card__rows')!;
    rows.scrollTop = 72;
    await user.click(screen.getByRole('button', { name: /Cut 6.*P-6.*5 in/ }));

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 700 });
    fireEvent(window, new Event('resize'));

    expect(screen.getByLabelText('Cut Sequence')).toBe(card);
    expect(rows.scrollTop).toBe(72);
    expect(screen.getByRole('button', { name: /Cut 6.*P-6.*5 in/ })).toHaveAttribute('aria-pressed', 'true');
  });

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
    expect(screen.getAllByText('Partial')).toHaveLength(4);
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

  it('shows selected empty and populated groups as placeholders in the default All scope', () => {
    const empty: OptimizationGroup = {
      optimizationGroupId: 'empty', name: 'Empty', order: 0, parts: [], requiredPieces: [], stockGroups: [],
      lastNestingResult: null, lastBatchNestingResult: null, lastStockLengthOptimizationResult: null, resultStatus: 'none',
    };
    const populated: OptimizationGroup = {
      ...empty, optimizationGroupId: 'populated', name: 'Populated', order: 1,
      requiredPieces: [{ requiredPieceId: 'piece-1', quantity: 1, length: 20, profileNumber: 'P-100', sourceReferences: [] }],
    };

    render(<StockLengthResults activeOptimizationGroupId="empty" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[empty, populated]} />);

    expect(screen.getByRole('heading', { name: 'All Optimization Groups' })).toBeInTheDocument();
    expect(screen.getByText('Empty', { selector: '[data-result-state]' })).toBeInTheDocument();
    expect(screen.getByText('Needs Generation', { selector: '[data-result-state]' })).toBeInTheDocument();
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
