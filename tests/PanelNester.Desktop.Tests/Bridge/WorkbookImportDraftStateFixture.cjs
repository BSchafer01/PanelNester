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

const statePath = path.join(
  repoRoot,
  'src',
  'PanelNester.WebUI',
  'src',
  'pages',
  'workbookImportDraftState.ts',
);
const {
  createWorkbookWorksheetDrafts,
  setWorkbookWorksheetSelected,
} = loadTsModule(statePath);

const workbook = {
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
  columnMappings: [],
  materialResolutions: [],
};
const options = {
  columnMappings: [{ sourceColumn: 'Id', targetField: 'Id' }],
  materialMappings: [],
};

let drafts = createWorkbookWorksheetDrafts('fixture', workbook, preview, options);
drafts[0] = {
  ...drafts[0],
  optimizationGroupId: 'combined',
  options: {
    ...drafts[0].options,
    columnMappings: [
      ...drafts[0].options.columnMappings,
      { sourceColumn: 'Length', targetField: 'Length' },
    ],
  },
};
drafts = setWorkbookWorksheetSelected(drafts, 'First', false);
drafts = setWorkbookWorksheetSelected(drafts, 'First', true);

process.stdout.write(JSON.stringify({
  initialSelection: createWorkbookWorksheetDrafts('initial', workbook, preview, options)
    .map((draft) => draft.selected),
  defaultGroups: createWorkbookWorksheetDrafts('groups', workbook, preview, options)
    .map((draft) => ({
      name: draft.optimizationGroupName,
      position: draft.worksheet.originalPosition,
    })),
  restoredDraft: {
    selected: drafts[0].selected,
    optimizationGroupId: drafts[0].optimizationGroupId,
    mappedFields: drafts[0].options.columnMappings.map((mapping) => mapping.targetField),
  },
}));
