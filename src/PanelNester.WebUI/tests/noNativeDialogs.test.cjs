const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

function sourceFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return sourceFiles(fullPath);
    return /\.(ts|tsx)$/.test(entry.name) ? [fullPath] : [];
  });
}

test('application code does not use native browser alert dialogs', () => {
  const sourceRoot = path.join(__dirname, '..', 'src');
  const violations = sourceFiles(sourceRoot).filter((file) =>
    /(?:window\.)?(?:alert|confirm|prompt)\s*\(/.test(fs.readFileSync(file, 'utf8')));

  assert.deepEqual(violations, [], 'Use the OptiFab modal dialog instead of alert, confirm, or prompt.');
});
