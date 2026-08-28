const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const read = (fileName) => fs.readFileSync(path.join(__dirname, '..', 'src', 'pages', fileName), 'utf8');

test('finalized Sheet Projects use the Stock-Length workspace hierarchy', () => {
  const source = read('ImportPage.tsx');

  assert.match(source, /sheet-parts-workspace stock-length-workspace stock-length-workspace--completed/);
  assert.match(source, /Sheet Part Entries\{hasParts \? ` \(\$\{activeImportResponse\.parts\.length\}\)`/);
  assert.match(source, /stock-length-workspace__summary-row/);
  assert.match(source, /stock-length-workspace__last-import/);
  assert.match(source, /stock-length-workspace__pieces/);
});

test('Required Piece rows use Sheet-style status and action treatments', () => {
  const source = read('RequiredPiecesWorkspace.tsx');

  assert.match(source, /<th>Status<\/th><th>Actions<\/th>/);
  assert.match(source, /module-status-chip module-status-chip--\$\{status\}/);
  assert.match(source, /className="module-table-action"[\s\S]{0,300}>Edit<\/button>/);
  assert.match(source, /className="module-table-action module-table-action--danger"[\s\S]{0,300}>Delete<\/button>/);
});
