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
  assignSelectedWorksheetsToOptimizationGroup,
  canFinalizeStockLengthWorkbook,
  collectWorkbookNewMaterials,
  confirmWorksheetHeadingRange,
  copyColumnMappingsFromPreviousSelectedWorksheet,
  copyHeadingRangeFromPreviousSelectedWorksheet,
  createWorkbookWorksheetDrafts,
  editInvalidSourceRow,
  excludeSourceRows,
  excludeInvalidSourceRow,
  excludeInvalidSourceRows,
  getWorksheetNavigationStatus,
  headingRangeFromPreviewCells,
  mergeRecognizedColumnMappings,
  setWorkbookWorksheetSelected,
  restoreExcludedSourceRow,
  ignoreWorkbookMaterial,
  ignoreMaterialInSession,
  selectSourceRowRange,
  summarizeWorkbookPreview,
  summarizeHighConfidenceHeadingRanges,
  synchronizeWorkbookMaterialResolution,
  validateRequiredPieceCorrection,
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

test('re-import restores each matched Worksheet configuration independently', () => {
  const sourceReference = {
    worksheetName: 'Second', worksheetPosition: 2, physicalRow: 7, sourceFingerprint: 'ROW-7',
  };
  const configuration = {
    options: { projectKind: 'stockLength', columnMappings: [], materialMappings: [] },
    worksheets: [{
      worksheetName: 'Second', originalPosition: 2, headingRange: 'B4:G4',
      columnMappings: [
        { sourceColumn: 'B', targetField: 'Quantity' },
        { sourceColumn: 'C', targetField: 'Length' },
        { sourceColumn: 'D', targetField: 'Profile Number' },
      ],
      optimizationGroupId: 'group-b',
      excludedSourceRows: [{
        rowId: 'row-7', sourceReference,
        originalValidationError: { code: 'invalid-length', message: 'Bad length' },
      }],
    }],
    partOverrides: [{ rowId: 'row-8', sourceReferences: [{ ...sourceReference, physicalRow: 8 }] }],
  };
  const groups = [{ optimizationGroupId: 'group-b', name: 'Frames', stockLength: 240 }];

  const drafts = createWorkbookWorksheetDrafts(
    'fixture', workbook, preview, options, configuration, groups,
  );

  assert.deepEqual(drafts.map((draft) => draft.selected), [false, true, false]);
  assert.equal(drafts[1].headingRange, 'B4:G4');
  assert.equal(drafts[1].headingRangeConfirmed, true);
  assert.equal(drafts[1].optimizationGroupId, 'group-b');
  assert.equal(drafts[1].optimizationGroupName, 'Frames');
  assert.deepEqual(drafts[1].options.columnMappings, configuration.worksheets[0].columnMappings);
  assert.deepEqual(drafts[1].excludedSourceRows, configuration.worksheets[0].excludedSourceRows);
  assert.equal(drafts[1].partOverrides.length, 1);
});

test('bulk Stock-Length assignment adopts one group without changing independent mappings', () => {
  const drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft, index) => ({ ...draft, selected: index < 2 }));
  const mappings = drafts.map((draft) => draft.options.columnMappings);

  const assigned = assignSelectedWorksheetsToOptimizationGroup(
    drafts,
    { optimizationGroupId: 'shared', name: 'Shared', stockLength: 240 },
  );

  assert.deepEqual(assigned.slice(0, 2).map((draft) => draft.optimizationGroupId), ['shared', 'shared']);
  assert.deepEqual(assigned.map((draft) => draft.options.columnMappings), mappings);
});

test('Stock-Length Workbook finalization accepts ready per-Worksheet groups with positive Stock Length', () => {
  const requiredMappings = [
    { sourceColumn: 'A', targetField: 'Quantity' },
    { sourceColumn: 'B', targetField: 'Length' },
    { sourceColumn: 'C', targetField: 'Profile Number' },
  ];
  const readyDrafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .slice(0, 2)
    .map((draft, index) => ({
      ...draft,
      selected: true,
      stockLength: index === 0 ? 240 : 120,
      headingRangeConfirmed: true,
      hasPendingChanges: false,
      options: { projectKind: 'stockLength', columnMappings: requiredMappings, materialMappings: [] },
    }));

  assert.equal(canFinalizeStockLengthWorkbook(readyDrafts, []), true);
  assert.equal(canFinalizeStockLengthWorkbook([
    { ...readyDrafts[0], headingRangeConfirmed: false, headingRange: 'B10:L10' },
  ], []), true);
  assert.equal(canFinalizeStockLengthWorkbook([
    { ...readyDrafts[0], hasPendingChanges: true },
  ], []), true);
  assert.equal(canFinalizeStockLengthWorkbook([
    {
      ...readyDrafts[0],
      preview: {
        ...readyDrafts[0].preview,
        materialResolutions: [{ sourceMaterialName: 'Unmapped Material', status: 'unresolved' }],
        errors: [{ code: 'material-not-found', message: 'Material mapping is not required here.' }],
      },
    },
  ], []), true);
  assert.equal(canFinalizeStockLengthWorkbook([
    readyDrafts[0], { ...readyDrafts[1], stockLength: 0 },
  ], []), false);
  assert.equal(canFinalizeStockLengthWorkbook([
    {
      ...readyDrafts[0],
      preview: { ...readyDrafts[0].preview, errors: [{ code: 'bad-row', message: 'Bad row', rowId: 'row-1' }] },
      partOverrides: [{
        rowId: 'row-1', sourceReferences: [],
        currentRequiredPiece: { validationStatus: 'error' },
      }],
    },
  ], [{ optimizationGroupId: 'shared', stockLength: 240 }]), false);
});

test('invalid Required Piece corrections remain blockers until all required values validate', () => {
  const invalid = validateRequiredPieceCorrection('abc', '0', '');
  const exponent = validateRequiredPieceCorrection('1', '1e2', 'P-100');
  const valid = validateRequiredPieceCorrection('3', '12 3/8', 'P-100');

  assert.equal(invalid.validationStatus, 'error');
  assert.deepEqual(invalid.validationMessages, [
    'Quantity must be an integer greater than zero.',
    'Length must be greater than zero.',
    'Profile Number is required.',
  ]);
  assert.equal(exponent.validationStatus, 'error');
  assert.deepEqual(exponent.validationMessages, [
    'Length must be a decimal, fraction, or mixed-number inch value.',
  ]);
  assert.equal(valid.validationStatus, 'valid');
  assert.equal(valid.quantity, 3);
  assert.equal(valid.length, 12.375);
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

test('multiple invalid source rows can be excluded in one operation', () => {
  const makeInvalid = (rowId, physicalRow) => ({
    rowId, importedId: '', lengthText: '48', length: 48,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName: 'ACM', isManual: false, validationStatus: 'error',
    validationMessages: ['Id is required.'], sourceReferences: [{
      worksheetName: 'First', worksheetPosition: 1, physicalRow,
      sourceFingerprint: `ROW-${physicalRow}`,
    }],
  });
  const rows = [makeInvalid('row-9', 9), makeInvalid('row-10', 10), makeInvalid('row-11', 11)];
  const base = {
    ...createWorkbookWorksheetDrafts('fixture', workbook, preview, options)[0],
    preview: {
      ...preview,
      success: false,
      parts: rows,
      errors: rows.map((row) => ({
        code: 'missing-id', message: 'Id is required.', rowId: row.rowId,
        location: row.sourceReferences[0],
      })),
    },
  };

  const excluded = excludeInvalidSourceRows(base, ['row-9', 'row-11']);

  assert.deepEqual(excluded.preview.parts.map((row) => row.rowId), ['row-10']);
  assert.deepEqual(excluded.excludedSourceRows.map((row) => row.rowId), ['row-9', 'row-11']);
  assert.deepEqual(excluded.preview.errors.map((error) => error.rowId), ['row-10']);
});

test('ready and invalid source rows can be excluded together during review', () => {
  const sourceRow = (rowId, validationStatus, physicalRow) => ({
    rowId, importedId: rowId, lengthText: '48', length: 48,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName: 'ACM', isManual: false, validationStatus,
    validationMessages: validationStatus === 'error' ? ['Id is required.'] : [],
    sourceReferences: [{
      worksheetName: 'First', worksheetPosition: 1, physicalRow,
      sourceFingerprint: `ROW-${physicalRow}`,
    }],
  });
  const rows = [sourceRow('ready-1', 'ready', 4), sourceRow('error-1', 'error', 5)];
  const base = {
    ...createWorkbookWorksheetDrafts('fixture', workbook, preview, options)[0],
    preview: {
      ...preview,
      success: false,
      parts: rows,
      errors: [{
        code: 'missing-id', message: 'Id is required.', rowId: 'error-1',
        location: rows[1].sourceReferences[0],
      }],
    },
  };

  const excluded = excludeSourceRows(base, ['ready-1', 'error-1']);

  assert.deepEqual(excluded.preview.parts, []);
  assert.deepEqual(excluded.excludedSourceRows.map((row) => row.rowId), ['ready-1', 'error-1']);
  assert.deepEqual(excluded.preview.errors, []);

  const restoredReady = restoreExcludedSourceRow(excluded, 'ready-1');
  assert.deepEqual(restoredReady.preview.parts.map((row) => row.rowId), ['ready-1']);
  assert.deepEqual(restoredReady.preview.errors, []);
});

test('shift selection includes every visible row between the anchor and clicked row', () => {
  const orderedRowIds = ['row-1', 'row-2', 'row-3', 'row-4', 'row-5'];
  const first = selectSourceRowRange(new Set(), orderedRowIds, 'row-2', true, false);
  const ranged = selectSourceRowRange(first.selectedRowIds, orderedRowIds, 'row-5', true, true, first.anchorRowId);

  assert.deepEqual([...ranged.selectedRowIds], ['row-2', 'row-3', 'row-4', 'row-5']);
  assert.equal(ranged.anchorRowId, 'row-5');
});

test('ignoring one import material excludes every matching source row across selected Worksheets', () => {
  const sourceRow = (rowId, materialName, worksheetName, worksheetPosition, physicalRow) => ({
    rowId, importedId: rowId, lengthText: '48', length: 48,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName, isManual: false, validationStatus: 'ready', validationMessages: [],
    sourceReferences: [{ worksheetName, worksheetPosition, physicalRow, sourceFingerprint: rowId }],
  });
  const drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options)
    .map((draft, index) => ({
      ...draft,
      selected: index < 2,
      preview: {
        ...preview,
        parts: [
          sourceRow(`ignored-${index}`, 'Ignore Me', draft.worksheet.worksheetName, index + 1, 4),
          sourceRow(`kept-${index}`, 'Keep Me', draft.worksheet.worksheetName, index + 1, 5),
        ],
      },
    }));

  const ignored = ignoreWorkbookMaterial(drafts, 'Ignore Me');

  assert.deepEqual(ignored.slice(0, 2).map((draft) => draft.preview.parts.map((row) => row.materialName)), [
    ['Keep Me'], ['Keep Me'],
  ]);
  assert.deepEqual(ignored.slice(0, 2).map((draft) => draft.excludedSourceRows.length), [1, 1]);
  assert.equal(ignored[2], drafts[2]);

  const resolved = synchronizeWorkbookMaterialResolution(
    ignored,
    'Ignore Me',
    { sourceMaterialName: 'Ignore Me', targetMaterialId: 'material-a' },
  );
  assert.deepEqual(resolved.slice(0, 2).map((draft) => draft.preview.parts.length), [2, 2]);
  assert.deepEqual(resolved.slice(0, 2).map((draft) => draft.ignoredMaterialNames), [[], []]);
  assert.deepEqual(resolved.slice(0, 2).map((draft) => draft.excludedSourceRows.length), [0, 0]);
});

test('ignoring a material synchronizes the visible session preview with the active Worksheet draft', () => {
  const sourceRow = (rowId, materialName) => ({
    rowId, importedId: rowId, lengthText: '48', length: 48,
    widthText: '24', width: 24, quantityText: '1', quantity: 1,
    materialName, isManual: false, validationStatus: 'ready', validationMessages: [],
    sourceReferences: [{
      worksheetName: 'First', worksheetPosition: 1, physicalRow: 4,
      sourceFingerprint: rowId,
    }],
  });
  const rows = [sourceRow('ignored-1', 'Ignore Me'), sourceRow('kept-1', 'Keep Me')];
  const drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options);
  drafts[0] = { ...drafts[0], preview: { ...preview, parts: rows } };
  const session = {
    sessionId: 'fixture', filePath: 'fixture.xlsx', preview: { ...preview, parts: rows },
    options, newMaterials: [], hasPendingChanges: false, workbook,
    worksheets: drafts,
    // A restored session can lack an active Worksheet name; the selected draft is authoritative.
    activeWorksheetName: undefined,
  };

  const ignored = ignoreMaterialInSession(session, 'Ignore Me');

  assert.deepEqual(ignored.preview.parts.map((row) => row.rowId), ['kept-1']);
  assert.equal(ignored.preview, ignored.worksheets[0].preview);
  assert.equal(ignored.worksheets[0].excludedSourceRows.length, 1);
});
