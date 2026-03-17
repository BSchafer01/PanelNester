# Phase 6 Hardening Test Matrix

This matrix defines acceptance criteria for the Phase 6 hardening slice. Baseline: FlatBuffers persistence is live, 110 tests passing (2 skipped), Phase 5 results viewer and PDF reporting complete and approved.

## Legend

- **Now** — coverage exists and can run in current repo
- **Gate** — manual reviewer smoke required before approval
- **New** — test coverage needed this phase
- **Regression** — existing test must remain green

---

## 1. Empty-Result Export Behavior

| ID | Scenario | Concrete acceptance check | Coverage | Status |
|---|---|---|---|---|
| P6-ER-01 | PDF export with no nesting result | Export action when `lastNestingResult` is null or undefined either disables the export button, shows a clear message, or produces a valid "no results" PDF—never a crash or blank/corrupt file | New + Gate | Pending |
| P6-ER-02 | PDF export after clearing imported rows | Clearing all imports after a successful nest, then exporting, produces a coherent report showing "no parts to nest" or explicitly states results are stale | New + Gate | Pending |
| P6-ER-03 | PDF export with unplaced-only result | A nesting run where every part is unplaced (all `outside-usable-sheet` or similar) exports a valid PDF with the unplaced items table and zero sheet visuals | Gate | Pending |
| P6-ER-04 | Empty project save/open round-trip | A brand-new unsaved project with no imports and no result can be saved, closed, and reopened without drift or crash | Regression | Now |

---

## 2. Dense Layout Readability

| ID | Scenario | Concrete acceptance check | Coverage | Status |
|---|---|---|---|---|
| P6-DL-01 | Viewer clarity with 20+ placements per sheet | A sheet with ≥20 small panels remains readable: zoom works, hover/click inspection still identifies the correct part, labels remain unambiguous | Gate | Pending |
| P6-DL-02 | PDF graphic with dense placements | Exported PDF sheet visuals with ≥20 panels have labels/callouts that let a reviewer identify each part without guessing | Gate | Pending |
| P6-DL-03 | Viewer performance under load | A 50-panel result loads and responds to zoom/pan within 2 seconds on a typical desktop; no janky repaints or lost frames | Gate | Pending |
| P6-DL-04 | Import table with 100+ rows | Import pipeline and inline editing remain usable at scale; no dropdown freezes, scroll jank, or phantom selection bugs | Gate | Pending |

---

## 3. Save / Open / Export Stability

| ID | Scenario | Concrete acceptance check | Coverage | Status |
|---|---|---|---|---|
| P6-SO-01 | FlatBuffers round-trip regression | Newly saved `.pnest` files remain FlatBuffers binary; metadata, imports, snapshots, and nesting result rehydrate exactly | Regression | Now |
| P6-SO-02 | Legacy JSON compatibility | Pre-migration JSON `.pnest` fixtures open without crash or silent data loss; re-save converts cleanly to FlatBuffers | Regression | Now |
| P6-SO-03 | Save dialog interrupted by focus loss | Losing app focus (Alt-Tab, window minimize) while save dialog is open does not crash the host or leave partial state | Gate | Pending |
| P6-SO-04 | PDF export dialog stability | Native PDF save dialog remains interactive through rename, folder change, and Save; no crash, freeze, or path mismatch | Regression | Now (P5 bugfix batch) |
| P6-SO-05 | Cancel/fail recovery | Cancelling or failing any save/export leaves the next attempt fully usable; no trapped state | Regression | Now |
| P6-SO-06 | Decimal precision across save/open | Coordinates, dimensions, utilization, and kerf survive save/open without visible precision drift (e.g., 48.125 does not become 48.1250001) | Regression | Now |
| P6-SO-07 | Autosave / dirty-state indicator | If autosave is in scope, dirty flag clears only after confirmed write; unsaved work is never silently discarded | Scope Hold | TBD |

---

## 4. Viewer / Pointer Polish

| ID | Scenario | Concrete acceptance check | Coverage | Status |
|---|---|---|---|---|
| P6-VP-01 | Plan view persistence | Initial load, reset, sheet change all return to face-on plan view; orbit/tilt stays blocked | Regression | Now (P5 bugfix batch) |
| P6-VP-02 | Zoom limits | User cannot zoom so far out the sheet becomes a dot, nor so far in the canvas breaks; limits are sensible | Gate | Pending |
| P6-VP-03 | Pointer capture release | Drag interactions inside the viewer release input capture immediately on mouse-up or pointer leave; no "stuck" pan/scroll | Gate | Pending |
| P6-VP-04 | Keyboard accessibility | Focus ring visible, arrow/tab navigation works if in scope, Esc closes any modal/tooltip | Gate | Pending |
| P6-VP-05 | Touch gesture support (if applicable) | Pinch-zoom and two-finger pan work on touch-enabled devices without triggering orbit | Scope Hold | TBD |

---

## 5. Regression Coverage

| ID | Scenario | Concrete acceptance check | Coverage | Status |
|---|---|---|---|---|
| P6-RC-01 | Import pipeline stability | CSV and XLSX happy paths still import cleanly; inline fix still clears row errors | Regression | Now |
| P6-RC-02 | Nesting determinism | Same input produces identical placement coordinates and sheet counts on repeated runs | Regression | Now |
| P6-RC-03 | Material library CRUD | Create, update, delete materials; in-use protection and name-collision rules unchanged | Regression | Now |
| P6-RC-04 | Metadata persistence | All PRD metadata fields survive save/open; kerf, date, notes, revision intact | Regression | Now |
| P6-RC-05 | Build gate | `dotnet test .\PanelNester.slnx` and `npm run build` pass with zero failures; skips documented | Regression | Now |

---

## Manual Reviewer Gates Hicks Will Apply

| ID | Gate description | Pass criteria | Fail criteria |
|---|---|---|---|
| P6-MG-01 | Empty-result export | Export button/action behaves gracefully when no result exists; no crash, no blank file | Crash, corrupt PDF, or silent hang |
| P6-MG-02 | Dense layout readability | 20+ panel viewer and PDF remain readable and identifiable | Anonymous shapes, unreadable labels, viewer freeze |
| P6-MG-03 | Focus-loss stability | Alt-Tab during save dialog does not crash or trap state | Crash or partial save artifact |
| P6-MG-04 | Pointer release | Leaving viewer mid-drag releases input cleanly | Stuck pan, scroll captured by viewer outside its bounds |
| P6-MG-05 | Zoom limits | Extreme zoom in/out stays usable | Canvas breaks, sheet disappears |
| P6-MG-06 | Precision display | Utilization, dimensions, kerf show correct precision (no drift or rounding errors) | Double-multiplied percentages, floating-point noise |

---

## Scope Holds Pending Ripley's Phase 6 Lock

- Whether autosave or background save is in scope and how dirty-state indicator behaves.
- Whether touch/gesture support is required this phase or deferred.
- Whether accessibility (keyboard nav, screen reader) is in scope.
- Whether there is a maximum placement count for guaranteed viewer performance.

---

## Hicks Ready / No-Go Rule

Phase 6 is **not review-ready** until all of the following are true:

1. **Build gate:** `dotnet test .\PanelNester.slnx` and `npm run build` pass with no regressions.
2. **Regression gate:** All P6-RC items remain green; no regressions in import, nesting, material library, persistence, or export.
3. **Empty-result gate:** Export with no result or unplaced-only result is graceful and never crashes.
4. **Dense layout gate:** 20+ panel viewer and PDF remain readable; labels identifiable.
5. **Save stability gate:** Focus loss, cancel, and failure paths leave the app recoverable.
6. **Pointer gate:** Viewer drag/zoom releases cleanly; no stuck capture.
7. **Precision gate:** No double-multiplied utilization or visible floating-point drift.

---

## Suggested Smoke Scenarios

### Scenario A: Empty-Result Path

1. Launch app, create new project.
2. Attempt PDF export before importing or nesting.
3. **Pass:** Export is disabled or shows clear "no results" message. **Fail:** Crash or blank file.

### Scenario B: Dense Layout Stress

1. Import or generate 50 small panels (e.g., 4×4 qty 50).
2. Run nesting on a single large sheet (96×48).
3. Inspect viewer: zoom, pan, hover labels.
4. Export PDF and verify all panels are labeled.
5. **Pass:** Viewer responsive, PDF readable. **Fail:** Freeze, anonymous shapes.

### Scenario C: Focus-Loss Recovery

1. Open project, make edits.
2. Invoke Save As, wait for dialog.
3. Alt-Tab away, wait 5 seconds, Alt-Tab back.
4. Complete save, reopen file.
5. **Pass:** Dialog survives, file saves correctly. **Fail:** Crash or partial file.

### Scenario D: Pointer Capture Edge

1. Start drag inside viewer.
2. While dragging, move pointer rapidly outside viewer bounds and release.
3. Scroll page outside viewer.
4. **Pass:** Scroll works normally. **Fail:** Page scroll hijacked or stuck.

---

## Evidence Required Before Approval

Hicks will not sign off Phase 6 without:

- [ ] Screenshot or recording of empty-result export attempt showing graceful behavior.
- [ ] Screenshot or PDF sample with ≥20 panels demonstrating readable labels.
- [ ] Recording of focus-loss recovery (Alt-Tab during save dialog).
- [ ] Recording of pointer capture release (drag inside viewer, release outside).
- [ ] `dotnet test` summary showing zero failures, skips documented.
- [ ] `npm run build` passing with no TypeScript/Vite errors.
