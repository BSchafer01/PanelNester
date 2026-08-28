const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const webUiRoot = path.join(__dirname, '..');
const read = (...segments) => fs.readFileSync(path.join(webUiRoot, ...segments), 'utf8');

test('both Project Kind mapping panels give the field table an owned scroll region', () => {
  for (const fileName of ['SheetProjectImportWorkflow.tsx', 'StockLengthImportWorkflow.tsx']) {
    const source = read('src', 'pages', fileName);
    assert.match(source, /className="stock-import-workflow__mapping-controls"/);
    assert.match(source, /className="stock-import-workflow__field-table"/);
  }

  const styles = read('src', 'styles.css').replace(/\s+/g, ' ');
  const tableRule = styles.match(/\.stock-import-workflow__field-table \{([^}]*)\}/);
  assert.ok(tableRule, 'expected mapping table styles');
  assert.match(tableRule[1], /min-height: 0/);
  assert.match(tableRule[1], /overflow: auto/);
  assert.match(tableRule[1], /scrollbar-gutter: stable/);
  assert.match(styles, /\.stock-import-workflow__field-head \{[^}]*position: sticky;[^}]*top: 0;/);
});
