import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { StockLengthResults } from '../src/pages/ResultsPage';
import type { OptimizationGroup } from '../src/types/contracts';

describe('Stock-Length PDF export', () => {
  it('exports the visible Stock Group scope even when it contains only unplaced work', async () => {
    const user = userEvent.setup();
    const exportPdf = vi.fn(async () => undefined);
    const group: OptimizationGroup = {
      optimizationGroupId: 'frames',
      name: 'Frames',
      order: 0,
      parts: [],
      requiredPieces: [{ requiredPieceId: 'piece-1', quantity: 1, length: 130, profileNumber: 'P-100', finish: 'Clear', isManual: true, sourceReferences: [] }],
      stockGroups: [],
      resultStatus: 'valid',
      lastStockLengthOptimizationResult: {
        optimizationGroupId: 'frames',
        status: 'failed',
        description: 'No pieces placed',
        cutPlans: [{
          cutPlanId: 'frames:p-100-clear',
          status: 'failed',
          stockGroup: { profileNumber: 'P-100', finish: 'Clear', requiredPieceIds: ['piece-1'] },
          stockItems: [],
          unplacedPieceInstances: [{
            pieceInstance: { pieceInstanceId: 'piece-1:1', requiredPieceId: 'piece-1', instanceNumber: 1, length: 130, profileNumber: 'P-100', finish: 'Clear', sourceReferences: [] },
            reasonCode: 'too-long',
            reasonDescription: 'Piece exceeds Stock Length.',
          }],
        }],
      },
    };

    render(
      <StockLengthResults
        activeOptimizationGroupId="frames"
        canExportReport
        onExportReport={exportPdf}
        onSelectOptimizationGroup={vi.fn()}
        optimizationGroups={[group]}
      />,
    );

    await user.selectOptions(screen.getByRole('combobox', { name: 'Stock Group filter' }), 'p-100\u0000clear');
    await user.click(screen.getByRole('button', { name: 'Export PDF' }));

    expect(exportPdf).toHaveBeenCalledWith({
      stockLengthScope: {
        optimizationGroupId: 'frames',
        hasStockGroupFilter: true,
        stockGroupProfileNumber: 'P-100',
        stockGroupFinish: 'Clear',
      },
    });
  });
});
