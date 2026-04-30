import type {
  NestPlacement,
  NestResponse,
  NestSheet,
} from '../types/contracts';

export interface ResultsPlacement extends NestPlacement {
  displayGroup: string;
}

export interface SheetGroupSummary {
  key: string;
  label: string;
  placementCount: number;
}

export interface BatchSheetView {
  materialKey: string;
  materialName: string;
  materialId?: string;
  sheet: NestSheet;
  placements: ResultsPlacement[];
  groupSummaries: SheetGroupSummary[];
}

export interface PanelSearchMatch {
  materialKey: string;
  materialName: string;
  sheetId: string;
  sheetNumber: number;
  sheetLength: number;
  sheetWidth: number;
  utilizationPercent: number;
  placementId: string;
  partId: string;
  groupKey: string;
  displayGroup: string;
  width: number;
  height: number;
}

export interface PanelSearchResults {
  matches: PanelSearchMatch[];
  totalMatchCount: number;
  matchedSheetCount: number;
  sheetCounts: Map<string, number>;
  firstMatchBySheet: Map<string, PanelSearchMatch>;
}

export interface BatchSheetMaterialSource {
  key: string;
  materialName: string;
  materialId?: string;
  response: NestResponse;
}

interface IndexedPanelSearchMatch extends PanelSearchMatch {
  normalizedPanelSearchValue: string;
}

export function normalizeGroup(value?: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length > 0 ? trimmed : null;
}

export function getGroupKey(value?: string | null): string {
  return normalizeGroup(value) ?? '';
}

export function getDisplayGroup(value?: string | null): string {
  return normalizeGroup(value) ?? 'Ungrouped';
}

export function compareLabels(left: string, right: string): number {
  return left.localeCompare(right, undefined, {
    numeric: true,
    sensitivity: 'base',
  });
}

export function normalizePanelSearchValue(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '');
}

export function panelIdMatchesNormalizedQuery(
  normalizedPanelId: string,
  normalizedQuery: string,
): boolean {
  return normalizedQuery.length > 0 && normalizedPanelId.includes(normalizedQuery);
}

export function panelIdMatchesQuery(panelId: string, query: string): boolean {
  return panelIdMatchesNormalizedQuery(
    normalizePanelSearchValue(panelId),
    normalizePanelSearchValue(query),
  );
}

export function sheetLookupKey(materialKey: string, sheetId: string): string {
  return `${materialKey}:${sheetId}`;
}

export function decoratePlacements(placements: NestPlacement[]): ResultsPlacement[] {
  return placements.map((placement) => ({
    ...placement,
    displayGroup: getDisplayGroup(placement.group),
  }));
}

function buildSheetGroupSummaries(
  placements: ResultsPlacement[],
): SheetGroupSummary[] {
  const groups = new Map<string, SheetGroupSummary>();

  for (const placement of placements) {
    const key = getGroupKey(placement.group);
    const existing = groups.get(key);
    if (existing) {
      existing.placementCount += 1;
      continue;
    }

    groups.set(key, {
      key,
      label: placement.displayGroup,
      placementCount: 1,
    });
  }

  return Array.from(groups.values()).sort((left, right) => {
    const leftUngrouped = left.key === '';
    const rightUngrouped = right.key === '';
    if (leftUngrouped !== rightUngrouped) {
      return leftUngrouped ? 1 : -1;
    }

    return compareLabels(left.label, right.label);
  });
}

export function buildBatchSheets(
  materialResults: BatchSheetMaterialSource[],
): BatchSheetView[] {
  return materialResults.flatMap((result) => {
    const placements = decoratePlacements(result.response.placements);
    const placementsBySheet = new Map<string, ResultsPlacement[]>();

    for (const placement of placements) {
      const groupedPlacements = placementsBySheet.get(placement.sheetId) ?? [];
      groupedPlacements.push(placement);
      placementsBySheet.set(placement.sheetId, groupedPlacements);
    }

    return result.response.sheets.map((sheet) => {
      const sheetPlacements = placementsBySheet.get(sheet.sheetId) ?? [];

      return {
        materialKey: result.key,
        materialName: result.materialName,
        materialId: result.materialId,
        sheet,
        placements: sheetPlacements,
        groupSummaries: buildSheetGroupSummaries(sheetPlacements),
      } satisfies BatchSheetView;
    });
  });
}

export function buildPanelSearchIndex(
  batchSheets: BatchSheetView[],
): IndexedPanelSearchMatch[] {
  return batchSheets
    .flatMap((batchSheet) =>
      batchSheet.placements.map(
        (placement) =>
          ({
            materialKey: batchSheet.materialKey,
            materialName: batchSheet.materialName,
            sheetId: batchSheet.sheet.sheetId,
            sheetNumber: batchSheet.sheet.sheetNumber,
            sheetLength: batchSheet.sheet.sheetLength,
            sheetWidth: batchSheet.sheet.sheetWidth,
            utilizationPercent: batchSheet.sheet.utilizationPercent,
            placementId: placement.placementId,
            partId: placement.partId,
            normalizedPanelSearchValue: normalizePanelSearchValue(placement.partId),
            groupKey: getGroupKey(placement.group),
            displayGroup: placement.displayGroup,
            width: placement.width,
            height: placement.height,
          }) satisfies IndexedPanelSearchMatch,
      ),
    )
    .sort(
      (left, right) =>
        compareLabels(left.partId, right.partId) ||
        compareLabels(left.materialName, right.materialName) ||
        left.sheetNumber - right.sheetNumber,
    );
}

export function buildPanelSearchResults(
  panelSearchIndex: IndexedPanelSearchMatch[],
  query: string,
): PanelSearchResults {
  const normalizedQuery = normalizePanelSearchValue(query);
  const emptyResults = {
    matches: [],
    totalMatchCount: 0,
    matchedSheetCount: 0,
    sheetCounts: new Map<string, number>(),
    firstMatchBySheet: new Map<string, PanelSearchMatch>(),
  } satisfies PanelSearchResults;

  if (normalizedQuery.length === 0) {
    return emptyResults;
  }

  const matches: PanelSearchMatch[] = [];
  const sheetCounts = new Map<string, number>();
  const firstMatchBySheet = new Map<string, PanelSearchMatch>();

  for (const entry of panelSearchIndex) {
    if (!panelIdMatchesNormalizedQuery(entry.normalizedPanelSearchValue, normalizedQuery)) {
      continue;
    }

    matches.push(entry);

    const key = sheetLookupKey(entry.materialKey, entry.sheetId);
    sheetCounts.set(key, (sheetCounts.get(key) ?? 0) + 1);
    if (!firstMatchBySheet.has(key)) {
      firstMatchBySheet.set(key, entry);
    }
  }

  return {
    matches,
    totalMatchCount: matches.length,
    matchedSheetCount: firstMatchBySheet.size,
    sheetCounts,
    firstMatchBySheet,
  };
}
