# Skill: Search Precision Testing with False-Positive Examples

**Date Created:** 2026-03-25  
**Context:** Panel search bug review for batch-sheets tab (PanelNester)

## Problem

Search precision gates commonly fail to catch false positives because test suites verify **code structure** (function signatures, normalized strings) but not **actual behavior** on edge cases. A panel search test can pass all assertions yet still return unrelated results in production.

## The Gap

**What the test suite checked:**
```csharp
Assert.Contains("function normalizePanelSearchValue(value: string): string {", resultsPage);
Assert.Contains("return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '');", resultsPage);
Assert.Contains("function buildNormalizedPanelSearchValues(value: string): string[] {", resultsPage);
Assert.Contains("entry.normalizedPanelSearchValues.includes(normalizedQuery)", resultsPage);
```

**What the test suite did NOT check:**
- What `buildNormalizedPanelSearchValues("PANEL-00004#2")` **actually returns**
- Whether the `.includes()` call correctly **rejects** `"04013"` as a match
- Whether partial digit sequences like `"04"` are being accepted as matches
- Real user searches with false-positive examples

## Solution Pattern

### 1. Lock Gate to User's False-Positive Examples

When a user reports false positives, **extract the exact panel IDs and search term**, then encode them as a regression gate:

```markdown
Search query: "04013"

Must NOT match:
- PANEL-00004#2
- PANEL-00040#1
- PANEL-00040#2
- PANEL-00045#1
- PANEL-00045#2
- PANEL-00045#3

Must match:
- Any panel ID containing full substring "04013"
```

### 2. Verify Return Values, Not Just Signatures

For fragment-based search, explicitly test the pre-computed values:

```typescript
// BAD: only checks function exists
Assert.Contains("function buildNormalizedPanelSearchValues(value: string): string[] {", code);

// GOOD: verifies actual behavior
const testPanel = buildNormalizedPanelSearchValues("PANEL-00004#2");
assert(!testPanel.includes("04013"), "Should not include partial digit match");
```

### 3. Create a Test Case Per False-Positive Type

- **Partial digit prefix match:** `"04013"` should not match `"00004"` (both start with `"00"`)
- **Non-contiguous fragments:** `"04013"` should not match panels where `"04"` and `"013"` are separated
- **Case/separator edge cases:** Ensure normalization strips separators without creating false matches

### 4. Regression Surface Checklist

For search-related fixes, always verify:
- ✓ False positives on exact examples from user report are fixed
- ✓ Deferred rendering still works (no UI hang on large batches)
- ✓ Click-to-view wiring still functions (search hits load sheets)
- ✓ Batch-sheet highlighting state still threads correctly

## Reusable Gate Template

```markdown
# Search Precision Acceptance Gate

## False-Positive Examples (LOCKED TO USER INPUT)

Search: "USER_QUERY"
False-positive hits from production: [LIST EXACT PANEL IDS]

Must NOT match these panels in the retry fix.

## True-Positive Cases

Panels that MUST match "USER_QUERY":
- EXAMPLE_PANEL_WITH_FULL_QUERY_SUBSTRING

## Regression Surface

- Deferred rendering: [verify useDeferredValue is present]
- Click-to-view: [verify reviewPanelMatch wiring]
- Batch-sheet highlighting: [verify panelSearchState threading]
```

## When to Use

- **After a user reports search false positives** with specific examples
- **Before re-attempting a search precision fix** to prevent regression to the same bug
- **In code review** to verify the fix actually addresses the reported false positives, not just nearby issues

## Render-Layer Validation (Critical)

**New (2026-03-25):** A matching logic fix is necessary but not sufficient. Always validate the **render layer independently**:

- ✓ Matching function returns correct array (verify via console.log or debugger)
- ✓ Render component uses ONLY that array for `map()` — no secondary sources
- ✓ Rendered row count = array length (count visible rows vs summary count)
- ✓ State cleanup on search clear (search input reset should instantly empty table)
- ✓ No stale renders (previous search results should not leak into new search)
- ✓ Scrollbar visual state matches expected row count

**Why this matters:** A false positive can come from two places:
1. **Matching logic:** Function returns wrong set of matches (bug in `.includes()`, normalization, etc.)
2. **Render logic:** Table renders more rows than the matched array contains (state leaks, concat errors, stale renders)

If tests pass but the user sees false positives in the UI, suspect #2.

## Anti-Pattern

Do NOT assume:
- "The code looks right" without testing actual behavior
- A passing unit test means no false positives exist
- Substring matching is correct if tokenization is involved
- Matching logic is sufficient; render layer automatically handles state correctly
