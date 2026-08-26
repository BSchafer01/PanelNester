const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');
const test = require('node:test');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const ts = require('typescript');

function loadTsxModule(filePath) {
  const source = fs.readFileSync(filePath, 'utf8');
  const transpiled = ts.transpileModule(source, {
    compilerOptions: {
      jsx: ts.JsxEmit.ReactJSX,
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

const componentPath = path.join(__dirname, '..', 'src', 'components', 'ImportDetails.tsx');
const { ImportDetails } = loadTsxModule(componentPath);

test('Import Details renders the complete saved import audit trail without actions', () => {
  const sourceReference = {
    worksheetName: 'Panels',
    worksheetPosition: 1,
    physicalRow: 7,
    sourceFingerprint: 'ROW-FINGERPRINT',
  };
  const importedValues = {
    rowId: 'row-7',
    importedId: 'OLD-ID',
    length: 24,
    width: 12,
    quantity: 1,
    materialName: 'Source Aluminum',
    group: 'Exterior',
    isManual: false,
    validationStatus: 'valid',
    validationMessages: [],
    sourceReferences: [sourceReference],
  };
  const currentValues = {
    ...importedValues,
    importedId: 'NEW-ID',
    length: 48,
    sheetNumber: 'S-2',
    rowNumber: 3,
    columnNumber: 4,
  };
  const html = renderToStaticMarkup(
    React.createElement(ImportDetails, {
      importSource: {
        importSourcePath: 'C:\\imports\\panels.xlsx',
        contentFingerprint: 'WORKBOOK-FINGERPRINT',
        contentLength: 4096,
        snapshotCapturedAtUtc: '2026-08-25T12:30:00Z',
      },
      importConfiguration: {
        options: {
          columnMappings: [],
          materialMappings: [
            { sourceMaterialName: 'Source Aluminum', targetMaterialId: 'material-1' },
          ],
        },
        worksheets: [
          {
            worksheetName: 'Panels',
            originalPosition: 1,
            headingRange: 'A4:F4',
            columnMappings: [{ sourceColumn: 'B', targetField: 'Length' }],
            optimizationGroupId: 'group-1',
            excludedSourceRows: [
              {
                rowId: 'row-8',
                sourceReference: { ...sourceReference, physicalRow: 8 },
                originalValidationError: { code: 'invalid-length', message: 'Length is invalid.' },
              },
            ],
          },
        ],
        partOverrides: [
          {
            rowId: 'row-7',
            importedValues,
            currentValues,
            sourceReferences: [sourceReference],
          },
        ],
      },
      importedParts: [currentValues],
      materials: [{ materialId: 'material-1', name: 'Library Aluminum' }],
      optimizationGroups: [{ optimizationGroupId: 'group-1', name: 'Facade Panels' }],
    }),
  );

  for (const expected of [
    'Import Details',
    'WORKBOOK-FINGERPRINT',
    'Panels',
    'A4:F4',
    'B → Length',
    'Source Aluminum → Library Aluminum',
    'Panels!8',
    'Length is invalid.',
    'ROW-FINGERPRINT',
    'Part ID: OLD-ID → NEW-ID',
    'Length: 24 → 48',
    'Sheet Number: Blank → S-2',
    'Row Number: Blank → 3',
    'Column Number: Blank → 4',
    'Facade Panels',
  ]) {
    assert.ok(html.includes(expected), `Expected rendered audit details to include: ${expected}`);
  }
  assert.doesNotMatch(html, /re-import|refresh/i);
  assert.doesNotMatch(html, /<button/i);
});
