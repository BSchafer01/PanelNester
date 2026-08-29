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

test('long Stock-Length Optimization Group lists scroll independently while generation actions stay visible', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'src', 'styles.css'), 'utf8');
  const source = fs.readFileSync(path.join(__dirname, '..', 'src', 'pages', 'RequiredPiecesWorkspace.tsx'), 'utf8');
  const summaryRule = css.match(/\.stock-length-workspace__summary-row\s*\{([^}]*)\}/)?.[1] ?? '';
  const groupRule = css.match(/\.stock-length-workspace__groups\s*\{([^}]*)\}/)?.[1] ?? '';
  const scrollRule = css.match(/\.stock-length-workspace__groups-scroll\s*\{([^}]*)\}/)?.[1] ?? '';
  const actionsRule = css.match(/\.stock-length-workspace__generate-actions\s*\{([^}]*)\}/)?.[1] ?? '';

  assert.match(source, /className="table-wrap stock-length-workspace__groups-scroll"/);
  assert.match(summaryRule, /height:\s*clamp\(/);
  assert.match(groupRule, /overflow:\s*hidden/);
  assert.match(scrollRule, /overflow:\s*auto/);
  assert.match(scrollRule, /flex:\s*1/);
  assert.match(actionsRule, /flex-shrink:\s*0/);
});
