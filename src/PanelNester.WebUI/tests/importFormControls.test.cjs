const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const stylesPath = path.join(__dirname, '..', 'src', 'styles.css');
const styles = fs.readFileSync(stylesPath, 'utf8').replace(/\s+/g, ' ');

test('both Project Kind import workspaces share the standard form-control treatment', () => {
  const sharedControlRule = styles.match(
    /:where\(\.import-workspace, \.project-import-workflow, \.stock-import-workflow, \.stock-length-workspace\) :where\(input:not\(\[type=['"]checkbox['"]\]\):not\(\[type=['"]radio['"]\]\):not\(\[type=['"]file['"]\]\), select, textarea\) \{([^}]*)\}/,
  );

  assert.ok(sharedControlRule, 'expected a shared import form-control rule');
  assert.match(sharedControlRule[1], /border: 1px solid var\(--panel-border-strong\)/);
  assert.match(sharedControlRule[1], /border-radius: 10px/);
  assert.match(sharedControlRule[1], /background: var\(--panel-surface-3\)/);
  assert.match(sharedControlRule[1], /color: var\(--panel-text\)/);
  assert.match(styles, /\.module-search input \{[^}]*min-height: 0;[^}]*border: none;[^}]*padding: 0;/);
});
