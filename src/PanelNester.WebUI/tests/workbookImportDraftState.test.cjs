const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const test = require('node:test');
const ts = require('typescript');

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

const statePath = path.join(
  __dirname,
  '..',
  'src',
  'pages',
  'workbookImportDraftState.ts',
);
const {
  applyHighConfidenceHeadingRanges,
  confirmWorksheetHeadingRange,
  copyHeadingRangeFromPreviousSelectedWorksheet,
  createWorkbookWorksheetDrafts,
  getWorksheetNavigationStatus,
  headingRangeFromPreviewCells,
  setWorkbookWorksheetSelected,
  summarizeHighConfidenceHeadingRanges,
} = loadTsModule(statePath);

const workbook = {
  initialWorksheetName: 'First',
  macrosPresent: false,
  worksheets: [
    { worksheetName: 'First', originalPosition: 1, headingRange: '' },
    { worksheetName: 'Second', originalPosition: 2, headingRange: '' },
    { worksheetName: 'Third', originalPosition: 3, headingRange: '' },
  ],
};
const preview = {
  success: true,
  filePath: 'fixture.xlsx',
  parts: [],
  errors: [],
  warnings: [],
  availableColumns: ['Id'],
  sourceColumns: [],
  columnMappings: [],
  materialResolutions: [],
};
const options = {
  columnMappings: [{ sourceColumn: 'Id', targetField: 'Id' }],
  materialMappings: [],
};

test('selects only the discovered initial Worksheet and restores its complete draft', () => {
  let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options);
  assert.deepEqual(drafts.map((draft) => draft.selected), [true, false, false]);
  assert.deepEqual(
    drafts.map((draft) => [draft.optimizationGroupName, draft.worksheet.originalPosition]),
    [['First', 1], ['Second', 2], ['Third', 3]],
  );

  drafts[0] = {
    ...drafts[0],
    optimizationGroupId: 'combined',
    options: {
      columnMappings: [
        ...drafts[0].options.columnMappings,
        { sourceColumn: 'Length', targetField: 'Length' },
      ],
      materialMappings: [
        { sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' },
      ],
    },
    newMaterials: [
      {
        sourceMaterialName: 'New Label',
        material: { name: 'New Material' },
      },
    ],
  };
  const savedDraft = drafts[0];
  drafts = setWorkbookWorksheetSelected(drafts, 'First', false);
  drafts = setWorkbookWorksheetSelected(drafts, 'First', true);

  assert.deepEqual(drafts[0], { ...savedDraft, selected: true });
});

test('manual A1 entry confirms one contiguous single-row Heading Range', () => {
  const drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options);

  const result = confirmWorksheetHeadingRange(drafts, 'First', 'b4:h4');

  assert.equal(result.error, undefined);
  assert.equal(result.drafts[0].headingRange, 'B4:H4');
  assert.equal(result.drafts[0].headingRangeConfirmed, true);
});

test('preview cell endpoints produce a normalized single-row Heading Range', () => {
  assert.equal(headingRangeFromPreviewCells('H4', 'B4'), 'B4:H4');
  assert.equal(headingRangeFromPreviewCells('B4', 'H5'), undefined);
});

test('manual entry rejects multi-row and noncontiguous Heading Ranges clearly', () => {
  let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options);
  drafts = confirmWorksheetHeadingRange(drafts, 'First', 'B4:H4').drafts;

  const multiRow = confirmWorksheetHeadingRange(drafts, 'First', 'B4:H5');
  const noncontiguous = confirmWorksheetHeadingRange(drafts, 'First', 'B4:D4,F4:H4');

  assert.match(multiRow.error, /one contiguous, single-row/i);
  assert.match(noncontiguous.error, /one contiguous, single-row/i);
  assert.equal(multiRow.drafts[0].headingRangeConfirmed, false);
  assert.equal(multiRow.drafts[0].hasPendingChanges, true);
});

test('Same Heading Range as Previous copies an independently editable snapshot across Optimization Groups', () => {
  let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft) => ({ ...draft, selected: true }));
  drafts = confirmWorksheetHeadingRange(drafts, 'First', 'B4:H4').drafts;
  drafts[1] = { ...drafts[1], optimizationGroupId: 'another-group' };

  drafts = copyHeadingRangeFromPreviousSelectedWorksheet(drafts, 'Second').drafts;
  drafts = confirmWorksheetHeadingRange(drafts, 'First', 'C5:I5').drafts;

  assert.equal(drafts[1].headingRange, 'B4:H4');
  assert.equal(drafts[1].headingRangeConfirmed, true);
});

test('bulk confirmation summarizes and applies only unique high-confidence detections', () => {
  const detectedWorkbook = {
    ...workbook,
    worksheets: [
      { ...workbook.worksheets[0], headingRange: 'A3:E3', headingRangeDetectionStatus: 'unique-high-confidence' },
      { ...workbook.worksheets[1], headingRange: '', headingRangeDetectionStatus: 'tied' },
      { ...workbook.worksheets[2], headingRange: '', headingRangeDetectionStatus: 'low-confidence' },
    ],
  };
  const drafts = createWorkbookWorksheetDrafts('fixture', detectedWorkbook, preview, options)
    .map((draft) => ({ ...draft, selected: true }));

  const summary = summarizeHighConfidenceHeadingRanges(drafts);
  const applied = applyHighConfidenceHeadingRanges(drafts, summary);

  assert.deepEqual(summary.worksheetNames, ['First']);
  assert.deepEqual(applied.map((draft) => draft.headingRangeConfirmed), [true, false, false]);
});

test('Worksheet navigation exposes heading, mapping, error, and ready states without blocking activation', () => {
  const base = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)[0];
  const mappedPreview = { ...preview, columnMappings: [
    { targetField: 'Id', sourceColumn: 'A' },
    { targetField: 'Length', sourceColumn: 'B' },
    { targetField: 'Width', sourceColumn: 'C' },
    { targetField: 'Quantity', sourceColumn: 'D' },
    { targetField: 'Material', sourceColumn: 'E' },
  ] };

  assert.deepEqual([
    getWorksheetNavigationStatus(base),
    getWorksheetNavigationStatus({ ...base, headingRangeConfirmed: true }),
    getWorksheetNavigationStatus({ ...base, headingRangeConfirmed: true, preview: { ...mappedPreview, errors: [{ code: 'bad-row', message: 'Bad row' }] } }),
    getWorksheetNavigationStatus({ ...base, headingRangeConfirmed: true, preview: mappedPreview, hasPendingChanges: false }),
  ], ['Needs heading', 'Needs mapping', 'Has errors', 'Ready']);
});
