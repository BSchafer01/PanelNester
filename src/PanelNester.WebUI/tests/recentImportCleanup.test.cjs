const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const test = require('node:test');
const ts = require('typescript');

function loadTsModule(filePath) {
  const source = fs.readFileSync(filePath, 'utf8');
  const transpiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
    fileName: filePath,
  });
  const loadedModule = new Module(filePath, module);
  loadedModule.filename = filePath;
  loadedModule.paths = Module._nodeModulePaths(path.dirname(filePath));
  loadedModule._compile(transpiled.outputText, filePath);
  return loadedModule.exports;
}

const webRoot = path.join(__dirname, '..');
const importPageSource = fs.readFileSync(path.join(webRoot, 'src', 'pages', 'ImportPage.tsx'), 'utf8');
const overviewPageSource = fs.readFileSync(path.join(webRoot, 'src', 'pages', 'OverviewPage.tsx'), 'utf8');
const appSource = fs.readFileSync(path.join(webRoot, 'src', 'App.tsx'), 'utf8');
const stylesSource = fs.readFileSync(path.join(webRoot, 'src', 'styles.css'), 'utf8');
const resultsPageSource = fs.readFileSync(path.join(webRoot, 'src', 'pages', 'ResultsPage.tsx'), 'utf8');
const resultsPresentation = loadTsModule(path.join(webRoot, 'src', 'pages', 'resultsPresentation.ts'));

test('Worksheet setup offers a collapsible preview without a readiness chip', () => {
  const worksheetSetupStart = importPageSource.indexOf('{worksheetDrafts.map((draft)');
  const worksheetSetup = importPageSource.slice(
    worksheetSetupStart,
    importPageSource.indexOf("{selectedWorksheetDrafts.length > 0 ?", worksheetSetupStart),
  );

  assert.match(worksheetSetup, /aria-expanded=[\s\S]*Collapse preview/i);
  assert.match(worksheetSetup, /Collapse preview/i);
  assert.doesNotMatch(worksheetSetup, /getWorksheetNavigationStatus/);
});

test('row review exposes multi-select and one bulk exclusion action', () => {
  assert.match(importPageSource, /Select all rows on this page/);
  assert.match(importPageSource, /Select \$\{part\.rowId\} for exclusion/);
  assert.match(importPageSource, /Exclude selected \(\{selectedSourceRowIds\.size\}\)/);
  assert.doesNotMatch(importPageSource, /\{part\.validationStatus === 'error' \? \(\s*<input\s+aria-label=\{`Select \$\{part\.rowId\} for exclusion`\}/);
  assert.match(importPageSource, /event\.nativeEvent as MouseEvent\)\.shiftKey/);
});

test('Optimization Group management lives on Import and shows associated Worksheets', () => {
  assert.doesNotMatch(overviewPageSource, /<h2>Optimization Groups<\/h2>/);
  assert.match(importPageSource, /<h2>Optimization Groups<\/h2>/);
  assert.match(importPageSource, /Associated Worksheets/);
});

test('Optimization Group rows use the dark application theme', () => {
  const rowStylesStart = stylesSource.indexOf('.optimization-group-row {');
  const rowStyles = stylesSource.slice(
    rowStylesStart,
    stylesSource.indexOf('.optimization-group-row--active', rowStylesStart),
  );

  assert.match(rowStyles, /background: var\(--panel-surface/);
  assert.match(rowStyles, /border: 1px solid var\(--panel-border/);
  assert.doesNotMatch(rowStyles, /#f5f6f8|#d8dce2/);
});

test('Run All omits Optimization Groups that have no ready rows', () => {
  assert.match(appSource, /\.filter\(\(group\) =>[\s\S]*?group\.parts\.some\(\(part\) => part\.validationStatus !== 'error'\)/);
});

test('building a project request preserves an empty Optimization Group list before finalization', () => {
  const builderStart = appSource.indexOf('function buildOptimizationGroups(');
  const builder = appSource.slice(
    builderStart,
    appSource.indexOf('function syncPartsToOptimizationGroups(', builderStart),
  );

  assert.match(builder, /if \(state\.optimizationGroups\.length === 0\) \{\s*return \[\];\s*\}/);
});

test('Material Resolution offers ignore as a third choice', () => {
  assert.match(importPageSource, /'Ignore material'/);
  assert.match(importPageSource, /handleIgnoreMaterial\(resolution\.sourceMaterialName\)/);
});

test('result sheets use a short user-facing identifier instead of the internal id', () => {
  assert.equal(resultsPresentation.getSheetDisplayId({ sheetId: '14d40d02-2394-4aca-a455-14057900d495-sheet-1', sheetNumber: 1 }), 'Sheet 1');
  assert.equal(resultsPresentation.getSheetDisplayId({ sheetId: 'another-internal-id', sheetNumber: 2 }), 'Sheet 2');
  assert.doesNotMatch(resultsPageSource, /<strong>\{sheet\.sheet\.sheetId\}<\/strong>/);
  assert.match(resultsPageSource, /getSheetDisplayId\(sheet\.sheet\)/);
});

test('Results Optimization Group options omit an empty placeholder group', () => {
  const groups = [
    {
      optimizationGroupId: 'parts', name: 'Parts', order: 0, parts: [], resultStatus: 'none',
      lastBatchNestingResult: { optimizationGroupId: 'parts', success: false },
    },
    { optimizationGroupId: 'sheet-1', name: 'SHEET1', order: 1, parts: [{ rowId: 'row-1' }], resultStatus: 'valid' },
  ];

  assert.deepEqual(
    resultsPresentation.getResultsOptimizationGroups(groups).map((group) => group.name),
    ['SHEET1'],
  );
});
