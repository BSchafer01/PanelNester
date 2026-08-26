import type { NestSheet, OptimizationGroup } from '../types/contracts';

export function getSheetDisplayId(
  sheet: Pick<NestSheet, 'sheetId' | 'sheetNumber'>,
): string {
  return `Sheet ${sheet.sheetNumber}`;
}

export function getResultsOptimizationGroups(
  optimizationGroups: OptimizationGroup[],
): OptimizationGroup[] {
  return optimizationGroups
    .filter((group) => group.parts.length > 0)
    .sort((left, right) => left.order - right.order);
}
