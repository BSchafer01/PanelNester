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
  createWorkbookWorksheetDrafts,
  setWorkbookWorksheetSelected,
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
