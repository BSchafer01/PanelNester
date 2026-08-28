import { describe, expect, it } from 'vitest';
import type { OptimizationGroup, RequiredPiece } from '../src/types/contracts';
import {
  buildStockLengthImportPlan,
  canReviewStockLengthImport,
  filterRequiredPieces,
  flattenRequiredPieces,
  formatRequiredPieceSourceReferences,
  paginateRequiredPieces,
} from '../src/pages/requiredPiecesPresentation';

const importedPiece: RequiredPiece = {
  requiredPieceId: 'imported',
  quantity: 1,
  length: 96,
  profileNumber: 'EX-1',
  partName: 'Door jamb',
  finish: 'Clear anodized',
  partNumber: 'D-100',
  isManual: false,
  validationStatus: 'warning',
  validationMessages: ['Check finish.'],
  sourceReferences: [{ worksheetName: 'Doors', worksheetPosition: 2, physicalRow: 17, sourceFingerprint: 'ABC' }],
};

const manualPiece: RequiredPiece = {
  ...importedPiece,
  requiredPieceId: 'manual',
  profileNumber: 'EX-2',
  isManual: true,
  validationStatus: 'valid',
  validationMessages: [],
  sourceReferences: [],
};

function group(id: string, name: string, pieces: RequiredPiece[]): OptimizationGroup {
  return {
    optimizationGroupId: id,
    name,
    order: 0,
    origin: 'project',
    stockLength: 240,
    requiredPieces: pieces,
    stockGroups: [],
    parts: [],
    lastNestingResult: null,
    lastBatchNestingResult: null,
    resultStatus: 'none',
  };
}

describe('Required Pieces presentation', () => {
  const rows = flattenRequiredPieces([
    group('frames', 'Frames', [manualPiece]),
    group('doors', 'Doors', [importedPiece]),
  ]);

  it('filters by Optimization Group, source, status, and searchable provenance', () => {
    expect(filterRequiredPieces(rows, { query: '', optimizationGroupId: 'doors', source: 'all', status: 'all' })).toHaveLength(1);
    expect(filterRequiredPieces(rows, { query: '', optimizationGroupId: '', source: 'manual', status: 'all' })).toEqual([rows[0]]);
    expect(filterRequiredPieces(rows, { query: '', optimizationGroupId: '', source: 'worksheet', status: 'warning' })).toEqual([rows[1]]);
    expect(filterRequiredPieces(rows, { query: 'doors!17', optimizationGroupId: '', source: 'all', status: 'all' })).toEqual([rows[1]]);
    expect(filterRequiredPieces(rows, { query: 'clear anodized', optimizationGroupId: '', source: 'all', status: 'all' })).toHaveLength(2);
  });

  it('paginates deterministically and clamps out-of-range pages', () => {
    const result = paginateRequiredPieces(Array.from({ length: 63 }, (_, index) => index + 1), 9, 25);
    expect(result).toEqual({ rows: Array.from({ length: 13 }, (_, index) => index + 51), page: 3, pageCount: 3, first: 51, last: 63 });
  });

  it('deduplicates Worksheet names and reports Source Reference row counts', () => {
    expect(formatRequiredPieceSourceReferences({
      ...importedPiece,
      sourceReferences: [
        ...importedPiece.sourceReferences,
        { ...importedPiece.sourceReferences[0], physicalRow: 18 },
      ],
    })).toBe('Doors · 2 source rows');
  });

  it('keeps Review disabled while a required layout mapping is incomplete', () => {
    const draft = {
      selected: true,
      optimizationGroupId: 'group-a',
      optimizationGroupName: 'Group A',
      stockLength: 240,
      headingRangeConfirmed: true,
      hasPendingChanges: false,
      options: { columnMappings: [
        { sourceColumn: 'A', targetField: 'Quantity' },
        { sourceColumn: 'B', targetField: 'Length' },
      ] },
      preview: { columnMappings: [] },
    } as never;

    expect(canReviewStockLengthImport([draft])).toBe(false);
  });

  it('allows Review when required mappings are complete even if the draft has pending mapping changes', () => {
    const draft = {
      selected: true,
      optimizationGroupId: 'group-a',
      optimizationGroupName: 'Group A',
      stockLength: 240,
      headingRangeConfirmed: true,
      hasPendingChanges: true,
      options: { columnMappings: [
        { sourceColumn: 'A', targetField: 'Quantity' },
        { sourceColumn: 'B', targetField: 'Length' },
        { sourceColumn: 'C', targetField: 'Profile Number' },
      ] },
      preview: { columnMappings: [] },
    } as never;

    expect(canReviewStockLengthImport([draft])).toBe(true);
  });

  it('distinguishes 30 valid source rows from 11 consolidated output entries', () => {
    const pieces = Array.from({ length: 30 }, (_, index): RequiredPiece => ({
      ...importedPiece,
      requiredPieceId: `row-${index + 1}`,
      quantity: 2,
      length: 90 + (index % 11),
      profileNumber: ` profile-${index % 11} `,
      partName: `Part ${index % 11}`,
      finish: ' clear ',
      partNumber: `P-${index % 11}`,
      validationStatus: 'valid',
      validationMessages: [],
      sourceReferences: [{
        worksheetName: 'Sheet A',
        worksheetPosition: 1,
        physicalRow: index + 2,
        sourceFingerprint: `source-${index + 1}`,
      }],
    }));
    const draft = {
      worksheet: { worksheetName: 'Sheet A', originalPosition: 1, headingRange: 'A1:F1' },
      selected: true,
      optimizationGroupId: 'group-a',
      optimizationGroupName: 'Group A',
      stockLength: 240,
      preview: { requiredPieces: pieces, errors: [], warnings: [] },
      excludedSourceRows: [],
      partOverrides: [],
    } as never;

    const plan = buildStockLengthImportPlan([draft]);

    expect(plan.sourceRowCount).toBe(30);
    expect(plan.validSourceRowCount).toBe(30);
    expect(plan.outputEntryCount).toBe(11);
    expect(plan.totalPieceQuantity).toBe(60);
    expect(plan.skippedSourceRowCount).toBe(0);
    expect(plan.resultingEntries).toHaveLength(11);
    expect(plan.aggregationRule).toContain('Optimization Group, Length, Profile Number, Part Name, Finish, and Part Number');
  });
});
