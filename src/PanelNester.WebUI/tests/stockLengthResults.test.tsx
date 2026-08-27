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

  it('exports the visible semantic scope even when the Optimization Group needs generation', async () => {
    const user = userEvent.setup();
    const exportExcel = vi.fn(async () => undefined);
    const needsGeneration: OptimizationGroup = {
      optimizationGroupId: 'frames', name: 'Frames', order: 0, parts: [],
      requiredPieces: [{ requiredPieceId: 'piece-1', quantity: 1, length: 20, profileNumber: 'P-100', finish: 'Clear', isManual: true, sourceReferences: [] }],
      stockGroups: [], resultStatus: 'stale', lastNestingResult: null, lastBatchNestingResult: null,
    };

    render(<StockLengthResults activeOptimizationGroupId="frames" canExportExcelReport onExportExcelReport={exportExcel} onSelectOptimizationGroup={vi.fn()} optimizationGroups={[needsGeneration]} />);

    await user.click(screen.getByRole('button', { name: 'Export Excel' }));
    expect(exportExcel).toHaveBeenCalledWith({
      stockLengthScope: {
        optimizationGroupId: 'frames',
        hasStockGroupFilter: false,
        stockGroupProfileNumber: null,
        stockGroupFinish: null,
      },
    });
  });

  it('defaults to All Optimization Groups and applies a view-only Stock Group filter', async () => {
    const user = userEvent.setup();
    const frames = stockGroup();
    const doorPiece = { ...pieces[0], pieceInstanceId: 'door:instance-1', requiredPieceId: 'door', profileNumber: 'A-100', finish: null };
    const doors: OptimizationGroup = {
      ...stockGroup([stockItem({ stockItemId: 'door-stock', cutSequence: [doorPiece] })]),
      optimizationGroupId: 'doors', name: 'Doors', order: 1,
      lastStockLengthOptimizationResult: {
        optimizationGroupId: 'doors', status: 'complete', description: 'Door Cut Plan',
        cutPlans: [{
          cutPlanId: 'doors:a-100', status: 'complete',
          stockGroup: { profileNumber: 'A-100', finish: null, requiredPieceIds: ['door'] },
          stockItems: [stockItem({ stockItemId: 'door-stock', cutSequence: [doorPiece] })],
          unplacedPieceInstances: [],
        }],
      },
    };

    const firstRender = render(<StockLengthResults projectId="project-a" activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[doors, frames]} />);

    expect(screen.getByRole('combobox', { name: 'Optimization Group scope' })).toHaveValue('all');
    expect(screen.getByRole('option', { name: 'All Optimization Groups' })).toBeInTheDocument();
    const initialRows = screen.getAllByRole('row').filter((row) => row.hasAttribute('aria-selected'));
    expect(within(initialRows[0]).getByText('Frames')).toBeInTheDocument();
    expect(within(initialRows[1]).getByText('Doors')).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: 'Stock Group filter' }), 'a-100\u0000');
    const filteredRow = screen.getAllByRole('row').find((row) => row.hasAttribute('aria-selected'))!;
    expect(within(filteredRow).getByText('Doors')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Stock Item 1 viewer' })).toHaveTextContent('A-100');

    firstRender.unmount();
    const restoredRender = render(<StockLengthResults projectId="project-a" activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[doors, frames]} />);
    expect(screen.getByRole('combobox', { name: 'Stock Group filter' })).toHaveValue('a-100\u0000');

    restoredRender.unmount();
    render(<StockLengthResults projectId="project-b" activeOptimizationGroupId="frames" onSelectOptimizationGroup={vi.fn()} optimizationGroups={[doors, frames]} />);
    expect(screen.getByRole('combobox', { name: 'Stock Group filter' })).toHaveValue('all');
  });

  it('shows all freshness states and activates the matching Stock Item and Piece Instance from search', async () => {
    const user = userEvent.setup();
    const searchablePiece = {
      ...pieces[0], partNumber: 'PN-42', partName: 'Header',
      sourceReferences: [{ worksheetName: 'Cuts', worksheetPosition: 0, physicalRow: 7, sourceFingerprint: 'abc' }],
    };
    const current = stockGroup([stockItem({ cutSequence: [searchablePiece] })]);
    const empty: OptimizationGroup = {
      optimizationGroupId: 'empty', name: 'Empty', order: 1, parts: [], requiredPieces: [],
      stockGroups: [], resultStatus: 'none', lastNestingResult: null, lastBatchNestingResult: null,
    };
    const stale: OptimizationGroup = {
      ...empty, optimizationGroupId: 'stale', name: 'Changed', order: 2,
      requiredPieces: [{ requiredPieceId: 'stale-piece', quantity: 1, length: 20, profileNumber: 'S', isManual: true, sourceReferences: [] }],
      resultStatus: 'stale',
    };
    const failed: OptimizationGroup = {
      ...stale, optimizationGroupId: 'failed', name: 'Broken', order: 3,
      lastStockLengthGenerationError: { code: 'adapter-invariant', message: 'Unexpected placement geometry.' },
    };
    const allUnplaced: OptimizationGroup = {
      ...stale, optimizationGroupId: 'unplaced', name: 'Overlength', order: 4, resultStatus: 'valid',
      lastStockLengthOptimizationResult: {
        optimizationGroupId: 'unplaced', status: 'failed', description: 'No Piece Instances placed',
        cutPlans: [{
          cutPlanId: 'unplaced:plan', status: 'failed',
          stockGroup: { profileNumber: 'S', finish: null, requiredPieceIds: ['stale-piece'] },
          stockItems: [],
          unplacedPieceInstances: [{
            pieceInstance: { pieceInstanceId: 'stale-piece:instance-1', requiredPieceId: 'stale-piece', instanceNumber: 1, length: 130, profileNumber: 'S', sourceReferences: [] },
            reasonCode: 'exceeds-stock-length', reasonDescription: 'Piece Instance exceeds Stock Length.',
          }],
        }],
      },
    };

    render(<StockLengthResults onSelectOptimizationGroup={vi.fn()} optimizationGroups={[current, empty, stale, failed, allUnplaced]} />);

    expect(screen.getAllByText('Empty', { selector: '[data-result-state]' })).toHaveLength(1);
    expect(screen.getByText('Needs Generation', { selector: '[data-result-state]' })).toBeInTheDocument();
    expect(screen.getByText('Application Error', { selector: '[data-result-state]' })).toBeInTheDocument();
    expect(screen.getByText('Failed', { selector: '[data-result-state]' })).toBeInTheDocument();
    expect(screen.getByText('Unexpected placement geometry.')).toBeInTheDocument();
    expect(screen.getByText('Piece Instance exceeds Stock Length.')).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Search Results' }), 'Cuts!7');
    await user.click(screen.getByRole('button', { name: /PN-42.*Cuts!7/ }));
    expect(screen.getByRole('row', { name: /Stock Item 1/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('button', { name: /Cut 1.*PN-42/ })).toHaveAttribute('aria-pressed', 'true');
  });

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

    rerender(
      <StockItemViewer
        finish="Clear"
        onSelectPieceInstance={onSelectPieceInstance}
        pieceInstances={pieces}
        profileNumber="P-100"
        selectedPieceInstanceId={undefined}
        stockItem={stockItem()}
      />,
    );
    expect(screen.getByRole('button', { name: /Cut 2.*B-1.*30 in/ })).toHaveAttribute('aria-pressed', 'false');

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
    const originalWidth = window.innerWidth;
    const manyPieces = Array.from({ length: 12 }, (_, index): PieceInstance => ({
      ...pieces[index % pieces.length],
      pieceInstanceId: `piece-${index + 1}:instance-1`,
      partNumber: `P-${index + 1}`,
      length: 5,
    }));
    render(<StockItemViewer finish="Clear" pieceInstances={manyPieces} profileNumber="P-100" stockItem={stockItem({ cutSequence: manyPieces, pieceLength: 60, remainder: 58.625, sawLoss: 1.375 })} />);
    const card = screen.getByLabelText('Cut Sequence');
    const surface = card.parentElement!;
    expect(surface).toHaveAttribute('data-cut-sequence-placement', 'overlay');
    const rows = card.querySelector<HTMLElement>('.cut-sequence-card__rows')!;
    rows.scrollTop = 72;
    await user.click(screen.getByRole('button', { name: /Cut 6.*P-6.*5 in/ }));

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 700 });
    fireEvent(window, new Event('resize'));

    expect(screen.getByLabelText('Cut Sequence')).toBe(card);
    expect(surface).toHaveAttribute('data-cut-sequence-placement', 'below');
    expect(rows.scrollTop).toBe(72);
    expect(screen.getByRole('button', { name: /Cut 6.*P-6.*5 in/ })).toHaveAttribute('aria-pressed', 'true');
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalWidth });
    fireEvent(window, new Event('resize'));
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
    expect(screen.getAllByText('Partial').length).toBeGreaterThanOrEqual(3);
    expect(screen.getAllByText(/240 in Stock Length/).length).toBeGreaterThanOrEqual(2);
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
    const onReviewOptimizationGroup = vi.fn();
    const empty: OptimizationGroup = {
      optimizationGroupId: 'empty', name: 'Empty', order: 0, parts: [], requiredPieces: [], stockGroups: [],
      lastNestingResult: null, lastBatchNestingResult: null, lastStockLengthOptimizationResult: null, resultStatus: 'none',
    };
    const populated: OptimizationGroup = {
      ...empty, optimizationGroupId: 'populated', name: 'Populated', order: 1,
      requiredPieces: [{ requiredPieceId: 'piece-1', quantity: 1, length: 20, profileNumber: 'P-100', sourceReferences: [] }],
    };

    render(<StockLengthResults activeOptimizationGroupId="empty" onReviewOptimizationGroup={onReviewOptimizationGroup} onSelectOptimizationGroup={vi.fn()} optimizationGroups={[empty, populated]} />);

    expect(screen.getByRole('heading', { name: 'All Optimization Groups' })).toBeInTheDocument();
    expect(screen.getByText('Empty', { selector: '[data-result-state]' })).toBeInTheDocument();
    expect(screen.getByText('Needs Generation', { selector: '[data-result-state]' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add Piece Instances' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review Required Pieces' }));
    expect(onReviewOptimizationGroup).toHaveBeenNthCalledWith(1, 'empty');
    expect(onReviewOptimizationGroup).toHaveBeenNthCalledWith(2, 'populated');
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
