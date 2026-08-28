const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

test('Optimization Groups fills its two-fraction summary grid track', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'src', 'styles.css'), 'utf8');
  const rule = css.match(/\.stock-length-import__group-create\s*\{([^}]*)\}/)?.[1] ?? '';

  assert.match(rule, /width:\s*100%\s*;/);
  assert.doesNotMatch(rule, /70rem/);
});
