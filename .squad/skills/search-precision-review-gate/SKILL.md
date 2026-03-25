---
title: Search Precision Review Gate
owner: Hicks
date: 2026-03-25
tags: [tester, acceptance-criteria, regression-strategy, edge-cases, search-behavior]
---

# Search Precision Review Gate

## Pattern
When tightening search behavior from loose (any substring match) to strict (contiguous-only match), use a three-layer acceptance & regression strategy:
1. **Acceptance criteria matrix** (8+ gates covering exact match, fragment match, empty state, performance, real data)
2. **Regression risk table** (7+ critical areas: index integrity, payload accuracy, state management, navigation, edge cases)
3. **Edge-case catalog** (8+ scenarios: valid partials, non-contiguous patterns, whitespace, large batch, empty batch)

## Why This Matters
Search precision fixes are deceptively tricky because:
- A "fix" that solves false positives may break valid partial-ID matches
- Changes to search logic touch data-flow seams (index build → state memoization → UI navigation)
- Performance regression in large batches (7500+ rows) can destroy workflow usability
- Edge cases (empty batch, whitespace) are often overlooked but cause silent failures

## How To Apply

### 1. Define Acceptance Criteria (AC table)
Each AC answers: "How do I know the fix worked?"
- **AC-001 (Exact Match):** Search term "X" returns only panels with exact sequence "X"
- **AC-002 (Contiguous Fragment):** Search "013" matches "PANEL-04013", not "PANEL-00145" with scattered "0", "1", "3"
- **AC-003 (No Loose Partials):** Confirm false positives like "04013" → "PANEL-00004" are gone
- **AC-004 (Empty Result State):** Zero results display gracefully; no crash or error state
- **AC-005 (Case Invariance):** Both "04013" and "04013" return same results (normalization preserved)
- **AC-006 (Real Data):** Actual dataset (7500-row import) remains searchable
- **AC-007 (UX Clarity):** Result counts and sheet hit counts are accurate
- **AC-008 (Performance):** Large batch search completes in <100ms

### 2. Identify Regression Risks (RR table)
For each critical seam touched by the change, list:
- **Risk:** What could break?
- **Why:** What does it affect?
- **Test:** How do we verify it didn't break?

Examples:
- **RR-001 (Index Integrity):** If search index build is corrupted, component render fails
- **RR-002 (Deferred State):** If useDeferredValue assumptions break, search debouncing fails
- **RR-003 (Sheet Counts):** If hit counts are wrong, UI displays false batch coverage
- **RR-004 (Payload Accuracy):** If partIds are modified during nesting, search finds nothing
- **RR-005 (Navigation):** If sheet selection is broken, clicking search result loses workflow
- **RR-006 (Import Chain):** If end-to-end import→nest→search→view breaks, core feature fails
- **RR-007 (Edge Cases):** If empty batch crashes, edge case handling is incomplete

### 3. Catalog Edge Cases (EC table)
List scenarios that are easy to overlook:
- Valid partial at ID start (must remain working after fix)
- Valid fragment mid-sequence (must remain working)
- Query with whitespace (should trim before matching)
- Query with no matches (should display empty state, not error)
- Large batch performance (should not degrade)
- Empty batch with zero placements (should not crash)
- Case variants (should return identical results)
- Non-contiguous patterns scattered in ID (should NOT match; these are the false positives)

## Outputs

**For Tester Gate:**
- AC table (8+ criteria with acceptance checks)
- RR table (7+ risks with validation methods)
- EC table (8+ edge cases with expected behavior)
- Must-pass test gates (contract validation, regression, must-fail checks)
- Implementation notes (code paths under review, potential root cause areas)

**For Team Decision:**
- Clear go/no-go verdict
- Before/after trace of bug with actual data
- Root-cause confirmation (not speculation)

## Example from Panel Search
User reports: "Search '04013' returns PANEL-00004, PANEL-00040, PANEL-00045 (false positives)"

**Gate output:**
1. **AC-001:** Search "04013" returns only PANEL-04013#1; PANEL-00004 is not returned
2. **RR-001:** Verify `buildPanelSearchIndex()` on 7500 rows does not corrupt normalization
3. **EC-001:** Confirm "04013" vs "00004" are not matching due to JavaScript `.includes()` edge case
4. **Must-pass:** ImportResultsRevisionGateSpecs + Phase05BridgeSpecs green before merge
5. **Must-fail:** If false positives still exist, fix is incomplete

## Anti-Patterns

❌ **"The fix looks right, so let's ship it"** — No trace of actual bug reproduction; root cause unclear.

❌ **"We tested the happy path"** — Missing edge cases: empty batch, whitespace, large datasets.

❌ **"Regression test baseline is unchanged"** — Baseline may be green but miss the specific bug (e.g., search works, but falsely matches).

❌ **"Performance is probably fine"** — Measure before/after on real 7500-row batch; don't assume.

## References
- `ResultsPage.tsx:252–264` (buildPanelSearchMatches function)
- `ImportResultsRevisionGateSpecs.cs` (contract validation baseline)
- `Phase05BridgeSpecs.cs` (batch nesting round-trip validation)
