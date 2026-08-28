import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ProjectImportWorkflow } from '../src/pages/ProjectImportWorkflow';
import {
  buildSheetImportPlan,
  canReviewSheetImport,
} from '../src/pages/sheetImportPresentation';
import type {
  ImportMappingSession,
  ImportWorksheetDraft,
  Material,
  OptimizationGroup,
  PartRow,
} from '../src/types/contracts';

const material: Material = {
  materialId: 'aluminum',
  name: 'Aluminum',
  colorFinish: '',
  notes: '',
  sheetLength: 120,
  sheetWidth: 60,
  allowRotation: true,
  defaultSpacing: 0.125,
  defaultEdgeMargin: 0.5,
  costPerSheet: null,
};

const group: OptimizationGroup = {
  optimizationGroupId: 'panels',
  name: 'Panels',
  order: 0,
  origin: 'project',
  requiredPieces: [],
  stockGroups: [],
  parts: [],
  lastNestingResult: null,
  lastBatchNestingResult: null,
  resultStatus: 'none',
};

function part(rowId: string, physicalRow: number, quantity = 1): PartRow {
  return {
    rowId,
    importedId: 'P-1',
    length: 20,
    lengthText: '20',
    width: 10,
    widthText: '10',
    quantity,
    quantityText: `${quantity}`,
    materialName: 'Aluminum',
    group: 'A',
    isManual: false,
    sheetNumber: null,
    rowNumber: null,
    columnNumber: null,
    validationStatus: 'valid',
    validationMessages: [],
    sourceReferences: [{
      worksheetName: 'Parts', worksheetPosition: 1, physicalRow,
      sourceFingerprint: `row-${physicalRow}`,
    }],
  };
}

function session(resolved = true): ImportMappingSession {
  const descriptor = {
    worksheetName: 'Parts',
    originalPosition: 1,
    usedRowCount: 3,
    headingRange: 'A1:E1',
    headingRangeDetectionStatus: 'unique-high-confidence' as const,
    headingRangeCandidates: [],
    previewRows: [{
      rowNumber: 1,
      cells: ['Part ID', 'Length', 'Width', 'Quantity', 'Material'].map((value, index) => ({
        address: `${String.fromCharCode(65 + index)}1`,
        columnNumber: index + 1,
        value,
        isHidden: false,
        isFormula: false,
      })),
    }],
  };
  const mappings = ['Id', 'Length', 'Width', 'Quantity', 'Material'].map((targetField, index) => ({
    targetField: targetField as 'Id' | 'Length' | 'Width' | 'Quantity' | 'Material',
    sourceColumn: String.fromCharCode(65 + index),
  }));
  const preview = {
    success: resolved,
    filePath: 'F:\\parts.xlsx',
    parts: [part('row-2', 2), part('row-3', 3, 2)],
    requiredPieces: [],
    errors: resolved ? [] : [{ code: 'material-not-found', message: 'Material was not found.', rowId: 'row-2' }],
    warnings: [],
    availableColumns: ['A', 'B', 'C', 'D', 'E'],
    sourceColumns: mappings.map((mapping) => ({ address: mapping.sourceColumn, heading: mapping.targetField })),
    columnMappings: mappings,
    materialResolutions: [{
      sourceMaterialName: 'Aluminum',
      status: resolved ? 'resolved' as const : 'unresolved' as const,
      resolvedMaterialId: resolved ? material.materialId : null,
      resolvedMaterialName: resolved ? material.name : null,
    }],
    worksheet: descriptor,
  };
  const draft: ImportWorksheetDraft = {
    worksheet: descriptor,
    selected: true,
    optimizationGroupId: group.optimizationGroupId,
    optimizationGroupName: group.name,
    preview,
    options: { projectKind: 'sheet', columnMappings: mappings, materialMappings: [] },
    newMaterials: [],
    hasPendingChanges: false,
    headingRange: descriptor.headingRange,
    headingRangeConfirmed: true,
    excludedSourceRows: [],
    ignoredMaterialNames: [],
    partOverrides: [],
  };
  return {
    sessionId: 'sheet-session',
    filePath: preview.filePath,
    preview,
    options: draft.options,
    newMaterials: [],
    hasPendingChanges: false,
    activeWorksheetName: descriptor.worksheetName,
    workbook: { initialWorksheetName: descriptor.worksheetName, worksheets: [descriptor], macrosPresent: false },
    worksheets: [draft],
  };
}

describe('Sheet Project Import Workflow', () => {
  function Harness({ initial = session() }: { initial?: ImportMappingSession }) {
    const [current, setCurrent] = useState(initial);
    return <ProjectImportWorkflow
      busy={false}
      groups={[group]}
      materials={[material]}
      onCancel={vi.fn()}
      onFinalize={vi.fn()}
      onPreview={vi.fn()}
      onReplaceFile={vi.fn()}
      onUpdateSession={setCurrent}
      projectKind="sheet"
      session={current}
    />;
  }

  it('uses the stepped workflow without a Stock Length or separate sheet-size input', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    expect(screen.getByRole('heading', { name: 'Select Worksheets' })).toBeInTheDocument();
    expect(screen.queryByText('Stock Length (in)')).not.toBeInTheDocument();
    expect(screen.queryByText('Sheet length (in)')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    expect(screen.getByRole('heading', { name: 'Map Fields for Layout 1' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Resolve Materials' })).toBeInTheDocument();
    expect(screen.getByText('1 of 1 resolved')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Review 1 Entries/ })).toBeEnabled();
  });

  it('blocks Review until every exact material label is resolved', async () => {
    const user = userEvent.setup();
    render(<Harness initial={session(false)} />);
    await user.click(screen.getByRole('button', { name: /Continue to Map Fields/ }));
    expect(screen.getByText('0 of 1 resolved')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Review 1 Entries/ })).toBeDisabled();
  });
});

describe('Sheet import presentation', () => {
  it('preserves the Sheet Project kind and combines only finalization-compatible rows', () => {
    const current = session();
    expect(current.worksheets?.[0].options.projectKind).toBe('sheet');
    expect(canReviewSheetImport(current.worksheets ?? [])).toBe(true);
    const plan = buildSheetImportPlan(current.worksheets ?? []);
    expect(plan.sourceRowCount).toBe(2);
    expect(plan.outputEntryCount).toBe(1);
    expect(plan.resultingEntries[0].quantity).toBe(3);
    expect(plan.resultingEntries[0].sourceReferences).toHaveLength(2);
  });
});
