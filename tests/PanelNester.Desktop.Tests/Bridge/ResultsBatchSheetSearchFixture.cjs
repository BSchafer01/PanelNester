const fs = require('fs');
const path = require('path');
const Module = require('module');

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const webUiNodeModules = path.join(repoRoot, 'src', 'PanelNester.WebUI', 'node_modules');
const ts = require(path.join(webUiNodeModules, 'typescript'));

function loadTsModule(filePath) {
  const source = fs.readFileSync(filePath, 'utf8');
  const transpiled = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2020,
    },
    fileName: filePath,
  });

  const loadedModule = new Module(filePath, module);
  loadedModule.filename = filePath;
  loadedModule.paths = Module._nodeModulePaths(path.dirname(filePath));
  loadedModule._compile(transpiled.outputText, filePath);
  return loadedModule.exports;
}

const helperPath = path.join(
  repoRoot,
  'src',
  'PanelNester.WebUI',
  'src',
  'pages',
  'resultsBatchSheetSearch.ts',
);

const {
  buildPanelSearchIndex,
  buildPanelSearchResults,
  panelIdMatchesQuery,
} = loadTsModule(helperPath);

const batchSheets = [
  {
    materialKey: 'mat-acm',
    materialName: 'Mat-ACM-62x196',
    sheet: {
      sheetId: 'sheet-008',
      sheetNumber: 8,
      sheetLength: 196,
      sheetWidth: 62,
      utilizationPercent: 94.8,
    },
    placements: [
      {
        placementId: 'placement-00004-2',
        partId: 'PANEL-00004#2',
        sheetId: 'sheet-008',
        group: null,
        displayGroup: 'Ungrouped',
        width: 54,
        height: 30,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-aluminum',
    materialName: 'Mat-Aluminum-48x120',
    sheet: {
      sheetId: 'sheet-086',
      sheetNumber: 86,
      sheetLength: 120,
      sheetWidth: 48,
      utilizationPercent: 94.2,
    },
    placements: [
      {
        placementId: 'placement-00040-1',
        partId: 'PANEL-00040#1',
        sheetId: 'sheet-086',
        group: null,
        displayGroup: 'Ungrouped',
        width: 54,
        height: 4,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-aluminum',
    materialName: 'Mat-Aluminum-48x120',
    sheet: {
      sheetId: 'sheet-087',
      sheetNumber: 87,
      sheetLength: 120,
      sheetWidth: 48,
      utilizationPercent: 94.2,
    },
    placements: [
      {
        placementId: 'placement-00040-2',
        partId: 'PANEL-00040#2',
        sheetId: 'sheet-087',
        group: null,
        displayGroup: 'Ungrouped',
        width: 54,
        height: 4,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-copper',
    materialName: 'Mat-Copper-36x120',
    sheet: {
      sheetId: 'sheet-145',
      sheetNumber: 145,
      sheetLength: 120,
      sheetWidth: 36,
      utilizationPercent: 92.9,
    },
    placements: [
      {
        placementId: 'placement-00045-1',
        partId: 'PANEL-00045#1',
        sheetId: 'sheet-145',
        group: null,
        displayGroup: 'Ungrouped',
        width: 36,
        height: 30,
      },
      {
        placementId: 'placement-00045-2',
        partId: 'PANEL-00045#2',
        sheetId: 'sheet-145',
        group: null,
        displayGroup: 'Ungrouped',
        width: 36,
        height: 30,
      },
      {
        placementId: 'placement-00045-3',
        partId: 'PANEL-00045#3',
        sheetId: 'sheet-145',
        group: null,
        displayGroup: 'Ungrouped',
        width: 36,
        height: 30,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-screenshot',
    materialName: 'Mat-ACM-62x196',
    sheet: {
      sheetId: 'sheet-017',
      sheetNumber: 17,
      sheetLength: 196,
      sheetWidth: 62,
      utilizationPercent: 94.8,
    },
    placements: [
      {
        placementId: 'placement-0408-2',
        partId: 'PANEL-0408#2',
        sheetId: 'sheet-017',
        group: null,
        displayGroup: 'Ungrouped',
        width: 48,
        height: 30,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-screenshot',
    materialName: 'Mat-Steel-60x120',
    sheet: {
      sheetId: 'sheet-076',
      sheetNumber: 76,
      sheetLength: 120,
      sheetWidth: 60,
      utilizationPercent: 94.8,
    },
    placements: [
      {
        placementId: 'placement-0407-3',
        partId: 'PANEL-0407#3',
        sheetId: 'sheet-076',
        group: null,
        displayGroup: 'Ungrouped',
        width: 60,
        height: 20,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-steel',
    materialName: 'Mat-Steel-48x120',
    sheet: {
      sheetId: 'sheet-014',
      sheetNumber: 14,
      sheetLength: 120,
      sheetWidth: 48,
      utilizationPercent: 95.4,
    },
    placements: [
      {
        placementId: 'placement-04013-1',
        partId: 'PANEL-04013#1',
        sheetId: 'sheet-014',
        group: null,
        displayGroup: 'Ungrouped',
        width: 22,
        height: 12,
      },
      {
        placementId: 'placement-04013-2',
        partId: 'PANEL-04013#2',
        sheetId: 'sheet-014',
        group: null,
        displayGroup: 'Ungrouped',
        width: 22,
        height: 12,
      },
      {
        placementId: 'placement-04013-3',
        partId: 'PANEL-04013#3',
        sheetId: 'sheet-014',
        group: null,
        displayGroup: 'Ungrouped',
        width: 22,
        height: 12,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-acm-hit',
    materialName: 'Mat-ACM-62x196',
    sheet: {
      sheetId: 'sheet-021',
      sheetNumber: 21,
      sheetLength: 196,
      sheetWidth: 62,
      utilizationPercent: 93.1,
    },
    placements: [
      {
        placementId: 'placement-04-013',
        partId: 'panel-04-013',
        sheetId: 'sheet-021',
        group: null,
        displayGroup: 'Ungrouped',
        width: 20,
        height: 12,
      },
      {
        placementId: 'placement-xx-04013',
        partId: 'XX-04013-ZZ',
        sheetId: 'sheet-021',
        group: null,
        displayGroup: 'Ungrouped',
        width: 18,
        height: 10,
      },
    ],
    groupSummaries: [],
  },
  {
    materialKey: 'mat-birch',
    materialName: 'Mat-Birch-48x96',
    sheet: {
      sheetId: 'sheet-034',
      sheetNumber: 34,
      sheetLength: 96,
      sheetWidth: 48,
      utilizationPercent: 91.7,
    },
    placements: [
      {
        placementId: 'placement-04013-left',
        partId: 'PANEL-04013-LEFT',
        sheetId: 'sheet-034',
        group: null,
        displayGroup: 'Ungrouped',
        width: 16,
        height: 12,
      },
      {
        placementId: 'placement-04013-right',
        partId: 'PANEL-04013-RIGHT',
        sheetId: 'sheet-034',
        group: null,
        displayGroup: 'Ungrouped',
        width: 16,
        height: 12,
      },
    ],
    groupSummaries: [],
  },
];

const results = buildPanelSearchResults(buildPanelSearchIndex(batchSheets), '04013');

process.stdout.write(
  JSON.stringify({
    matches: results.matches.map((match) => ({
      partId: match.partId,
      materialName: match.materialName,
      sheetNumber: match.sheetNumber,
    })),
    totalMatchCount: results.totalMatchCount,
    matchedSheetCount: results.matchedSheetCount,
    sheetCounts: Array.from(results.sheetCounts.entries())
      .map(([sheetKey, count]) => ({ sheetKey, count }))
      .sort((left, right) => left.sheetKey.localeCompare(right.sheetKey)),
    firstMatchesBySheet: Array.from(results.firstMatchBySheet.entries())
      .map(([sheetKey, match]) => ({ sheetKey, partId: match.partId }))
      .sort((left, right) => left.sheetKey.localeCompare(right.sheetKey)),
    directChecks: {
      exactHit: panelIdMatchesQuery('PANEL-04013#1', '04013'),
      separatorHit: panelIdMatchesQuery('panel-04-013', '04013'),
      falsePositive0408: panelIdMatchesQuery('PANEL-0408#2', '04013'),
      falsePositive0407: panelIdMatchesQuery('PANEL-0407#3', '04013'),
      falsePositive00004: panelIdMatchesQuery('PANEL-00004#2', '04013'),
      falsePositive00040: panelIdMatchesQuery('PANEL-00040#1', '04013'),
      falsePositive00045: panelIdMatchesQuery('PANEL-00045#1', '04013'),
    },
  }),
);
