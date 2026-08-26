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
  collectWorkbookNewMaterials,
  confirmWorksheetHeadingRange,
  copyColumnMappingsFromPreviousSelectedWorksheet,
  copyHeadingRangeFromPreviousSelectedWorksheet,
  createWorkbookWorksheetDrafts,
  editInvalidSourceRow,
  excludeInvalidSourceRow,
  getWorksheetNavigationStatus,
  headingRangeFromPreviewCells,
  mergeRecognizedColumnMappings,
  setWorkbookWorksheetSelected,
  restoreExcludedSourceRow,
  summarizeWorkbookPreview,
  summarizeHighConfidenceHeadingRanges,
  synchronizeWorkbookMaterialResolution,
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

test('Copy Mappings from Previous matches only unique normalized heading labels and keeps Heading Ranges independent', () => {
  let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft) => ({ ...draft, selected: true, headingRangeConfirmed: true }));
  drafts[0] = {
    ...drafts[0],
    headingRange: 'A1:D1',
    preview: {
      ...preview,
      sourceColumns: [
        { address: 'A', heading: 'Part ID' },
        { address: 'B', heading: 'Width' },
        { address: 'C', heading: 'Length' },
        { address: 'D', heading: 'W-i-d-t-h' },
      ],
    },
    options: {
      ...options,
      columnMappings: [
        { sourceColumn: 'A', targetField: 'Id' },
        { sourceColumn: 'B', targetField: 'Width' },
        { sourceColumn: 'C', targetField: 'Length' },
      ],
    },
  };
  drafts[1] = {
    ...drafts[1],
    headingRange: 'D4:G4',
    preview: {
      ...preview,
      sourceColumns: [
        { address: 'D', heading: 'Length' },
        { address: 'E', heading: 'Part-ID' },
        { address: 'F', heading: 'Width' },
        { address: 'G', heading: 'Width' },
      ],
    },
    options: { columnMappings: [], materialMappings: [] },
  };

  const result = copyColumnMappingsFromPreviousSelectedWorksheet(drafts, 'Second');
  const copied = result.drafts[1];

  assert.equal(result.error, undefined);
  assert.equal(copied.headingRange, 'D4:G4');
  assert.deepEqual(copied.options.columnMappings, [
    { sourceColumn: 'E', targetField: 'Id' },
    { sourceColumn: 'D', targetField: 'Length' },
  ]);
  assert.deepEqual(copied.clearedColumnMappingFields, ['Width']);
});

test('changing a confirmed Heading Range keeps only mappings whose normalized labels remain unique', () => {
  const rangeWorkbook = {
    ...workbook,
    worksheets: [{
      ...workbook.worksheets[0],
      headingRange: 'A1:E1',
      previewRows: [{
        rowNumber: 3,
        cells: [
          { address: 'C3', columnNumber: 3, value: 'Width' },
          { address: 'D3', columnNumber: 4, value: 'Id' },
          { address: 'E3', columnNumber: 5, value: 'Material' },
          { address: 'F3', columnNumber: 6, value: 'Length' },
          { address: 'G3', columnNumber: 7, value: 'Quantity' },
          { address: 'H3', columnNumber: 8, value: 'W-i-d-t-h' },
        ],
      }],
    }],
  };
  let drafts = createWorkbookWorksheetDrafts('fixture', rangeWorkbook, {
    ...preview,
    sourceColumns: [
      { address: 'A', heading: 'Id' },
      { address: 'B', heading: 'Length' },
      { address: 'C', heading: 'Width' },
      { address: 'D', heading: 'Quantity' },
      { address: 'E', heading: 'Material' },
    ],
  }, {
    columnMappings: [
      { sourceColumn: 'A', targetField: 'Id' },
      { sourceColumn: 'B', targetField: 'Length' },
      { sourceColumn: 'C', targetField: 'Width' },
      { sourceColumn: 'D', targetField: 'Quantity' },
      { sourceColumn: 'E', targetField: 'Material' },
    ],
    materialMappings: [],
  });
  drafts = confirmWorksheetHeadingRange(drafts, 'First', 'A1:E1').drafts;

  const changed = confirmWorksheetHeadingRange(drafts, 'First', 'C3:H3').drafts[0];

  assert.deepEqual(changed.options.columnMappings, [
    { sourceColumn: 'D', targetField: 'Id' },
    { sourceColumn: 'F', targetField: 'Length' },
    { sourceColumn: 'G', targetField: 'Quantity' },
    { sourceColumn: 'E', targetField: 'Material' },
  ]);
  assert.deepEqual(changed.clearedColumnMappingFields, ['Width']);
  assert.equal(changed.hasPendingChanges, true);
});

test('refreshed recognition prefills newly unambiguous aliases without replacing retained mappings', () => {
  const existingOptions = {
    columnMappings: [
      { sourceColumn: 'D', targetField: 'Id' },
      { sourceColumn: 'F', targetField: 'Length' },
    ],
    materialMappings: [
      { sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' },
    ],
  };
  const refreshed = {
    ...preview,
    columnMappings: [
      { sourceColumn: 'D', targetField: 'Id' },
      { sourceColumn: 'F', targetField: 'Length' },
      { targetField: 'Width', suggestedSourceColumn: 'C' },
    ],
  };

  assert.deepEqual(mergeRecognizedColumnMappings(existingOptions, refreshed), {
    columnMappings: [
      { sourceColumn: 'D', targetField: 'Id' },
      { sourceColumn: 'F', targetField: 'Length' },
      { sourceColumn: 'C', targetField: 'Width' },
    ],
    materialMappings: existingOptions.materialMappings,
  });
});

test('one Material Resolution is synchronized across selected Worksheets and Optimization Groups', () => {
  let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft, index) => ({
      ...draft,
      selected: index < 2,
      optimizationGroupId: index === 0 ? 'group-a' : 'group-b',
      options: {
        ...draft.options,
        materialMappings: index === 0
          ? [{ sourceMaterialName: 'Shared Label', targetMaterialId: 'old-material' }]
          : [],
      },
    }));

  drafts = synchronizeWorkbookMaterialResolution(
    drafts,
    'Shared Label',
    { sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' },
  );

  assert.deepEqual(
    drafts.slice(0, 2).map((draft) => draft.options.materialMappings),
    [
      [{ sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' }],
      [{ sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' }],
    ],
  );
  assert.deepEqual(drafts[2].options.materialMappings, []);

  drafts = setWorkbookWorksheetSelected(drafts, 'Third', true);
  assert.deepEqual(drafts[2].options.materialMappings, [
    { sourceMaterialName: 'Shared Label', targetMaterialId: 'material-a' },
  ]);
});

test('Workbook finalization submits one shared new Material Resolution only once', () => {
  const sharedCreation = {
    sourceMaterialName: 'Shared Label',
    material: { name: 'Created Material' },
  };
  const drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft, index) => ({
      ...draft,
      selected: index < 2,
      newMaterials: index < 2 ? [sharedCreation] : [],
    }));

  assert.deepEqual(collectWorkbookNewMaterials(drafts), [sharedCreation]);
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

test('invalid source rows can be corrected with imported values and Source References retained', () => {
  const sourceReference = {
    worksheetName: 'First', worksheetPosition: 1, physicalRow: 7, sourceFingerprint: 'ABC123',
  };
  const invalid = {
    rowId: 'row-7', importedId: 'P-7', lengthText: 'bad', length: 0,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName: 'ACM', isManual: false, validationStatus: 'error',
    validationMessages: ['Length must be a decimal value.'], sourceReferences: [sourceReference],
  };
  const draft = {
    ...createWorkbookWorksheetDrafts('fixture', workbook, preview, options)[0],
    preview: { ...preview, success: false, parts: [invalid], errors: [{
      code: 'invalid-length', message: 'Length must be a decimal value.', rowId: 'row-7',
      location: sourceReference,
    }] },
  };

  const edited = editInvalidSourceRow(draft, 'row-7', { ...invalid, lengthText: '48', length: 48 });

  assert.equal(edited.preview.parts[0].length, 48);
  assert.equal(edited.partOverrides.length, 1);
  assert.equal(edited.partOverrides[0].importedValues.lengthText, 'bad');
  assert.equal(edited.partOverrides[0].currentValues.lengthText, '48');
  assert.deepEqual(edited.partOverrides[0].sourceReferences, [sourceReference]);
  assert.equal(edited.preview.errors.length, 0);
});

test('invalid source rows are excluded only explicitly and can be restored with the original error', () => {
  const sourceReference = {
    worksheetName: 'First', worksheetPosition: 1, physicalRow: 9, sourceFingerprint: 'DEF456',
  };
  const invalid = {
    rowId: 'row-9', importedId: '', lengthText: '48', length: 48,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName: 'ACM', isManual: false, validationStatus: 'error',
    validationMessages: ['Id is required.'], sourceReferences: [sourceReference],
  };
  const base = {
    ...createWorkbookWorksheetDrafts('fixture', workbook, preview, options)[0],
    preview: { ...preview, success: false, parts: [invalid], errors: [{
      code: 'missing-id', message: 'Id is required.', rowId: 'row-9', location: sourceReference,
    }] },
  };

  const excluded = excludeInvalidSourceRow(base, 'row-9');
  assert.equal(excluded.preview.parts.length, 0);
  assert.equal(excluded.excludedSourceRows.length, 1);
  assert.equal(excluded.excludedSourceRows[0].sourceReference.sourceFingerprint, 'DEF456');
  assert.equal(excluded.excludedSourceRows[0].originalValidationError.code, 'missing-id');
  assert.deepEqual(summarizeWorkbookPreview([excluded]).worksheets[0], {
    worksheetName: 'First', originalPosition: 1, sourceRowCount: 1,
    importedPartCount: 0, excludedRowCount: 1, issueCount: 0,
  });
  assert.equal(
    summarizeWorkbookPreview([excluded]).optimizationGroups[0].sourceRowCount,
    1,
  );

  const restored = restoreExcludedSourceRow(excluded, 'row-9');
  assert.equal(restored.preview.parts.length, 1);
  assert.equal(restored.preview.errors[0].code, 'missing-id');
  assert.equal(restored.excludedSourceRows.length, 0);
});
