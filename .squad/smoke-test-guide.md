# Phase 0/1 Smoke-Test Checklist

> A repeatable, hands-on verification that the import → nesting → results path works end-to-end.

## Preflight: Build and Run Tests

Before launching the app, ensure the foundation is solid:

```powershell
# From repo root
dotnet restore
dotnet build
dotnet test
Set-Location .\src\PanelNester.WebUI
npm run build
Set-Location ..\..
```

**Expected outcome:** `Build succeeded`, `dotnet test` completes with zero failures, and `npm run build` completes without TypeScript/Vite errors. Skips should be limited to the documented WebView2/material-library placeholders that are still intentionally blocked.

**Runtime prerequisites for validation:**
- Local build/test commands require a `.NET 8.0.x` SDK.
- The per-user MSI is framework-dependent, so installed-app validation requires the x64 **.NET 8 Desktop Runtime** and **Microsoft Edge WebView2 Runtime** on the target machine.

---

## Happy-Path Smoke Test

**Setup:** You have a CSV file with valid part data. All imports parse, nesting runs, results display.

### CSV: `good-parts.csv`

Copy and save this exact file or paste into a new CSV:

```csv
Id,Length,Width,Quantity,Material
Panel-A,24,48,2,Demo Material
Panel-B,12,36,1,Demo Material
Corner-Block,6,6,4,Demo Material
```

**Column order does not matter.** (Verify: reorder to `Material,Width,Id,Length,Quantity` and it should still work.)

### Steps

1. **Import Step**
   - Trigger: File → Open CSV or equivalent dialog
   - Input: `good-parts.csv`
   - **Pass:** Service returns 3 parts, all marked `valid`, zero errors, zero warnings
   - **Fail:** If service reports `empty-file` or missing column names, check CSV header row

2. **Nesting Step**
   - Trigger: Click "Run Nesting"
   - Material: Demo Material (96" × 48", hardcoded)
   - Parts: Panel-A (24×48, qty 2), Panel-B (12×36, qty 1), Corner-Block (6×6, qty 4)
   - **Pass:** 
     - All 7 parts placed on 1–2 sheets
     - Summary shows sheet count, total utilization, zero unplaced items
     - Each placement has a sheet ID, coordinates, and dimensions

3. **Results Display**
   - **Pass:** UI renders:
     - Sheet list with utilization %
     - Placement list with part IDs and positions
     - Summary: "7 parts, N sheets, X% utilization"
     - No unplaced items reported

---

## Failure-Path Smoke Test

**Goal:** Prove that invalid input is rejected gracefully with actionable feedback. Not a crash. Not silent ignore.

### Test Case 1: Oversized Part

**CSV: `oversized-part.csv`**

```csv
Id,Length,Width,Quantity,Material
Normal-Panel,24,48,1,Demo Material
Giant-Panel,100,50,1,Demo Material
```

**Expected outcome:**
- Import succeeds (no file-level error)
- Normal-Panel marked `valid`
- Giant-Panel marked `error` with code `outside-usable-sheet` (exceeds 96"×48" usable area even with margins)
- Nesting run either:
  - Excludes Giant-Panel, runs on Normal-Panel only, or
  - Returns `empty-run` error if importer stops at first error row

**Fail condition:** App crashes, hangs, or silently ignores the error.

### Test Case 2: Invalid Material

**CSV: `bad-material.csv`**

```csv
Id,Length,Width,Quantity,Material
Panel-A,24,48,1,Demo Material
Panel-B,12,36,1,Unknown Material
```

**Expected outcome:**
- Import succeeds (no file-level error)
- Panel-A marked `valid`
- Panel-B marked `error` with code `material-not-found`
- Nesting run either:
  - Excludes Panel-B, runs on Panel-A only, or
  - Returns `invalid-input` error if no valid parts remain

**Fail condition:** App crashes, hangs, or silently accepts "Unknown Material".

### Test Case 3: Invalid Numeric

**CSV: `bad-numbers.csv`**

```csv
Id,Length,Width,Quantity,Material
Panel-A,24,48,1,Demo Material
Panel-B,abc,36,1,Demo Material
```

**Expected outcome:**
- Import succeeds (no file-level error)
- Panel-A marked `valid`
- Panel-B marked `error` with code `invalid-numeric`
- Nesting runs on Panel-A only

**Fail condition:** App crashes or reports a cryptic parse error.

### Test Case 4: Zero Quantity

**CSV: `zero-qty.csv`**

```csv
Id,Length,Width,Quantity,Material
Panel-A,24,48,1,Demo Material
Panel-B,12,36,0,Demo Material
```

**Expected outcome:**
- Import succeeds
- Panel-A marked `valid`
- Panel-B marked `error` with code `non-positive-quantity`
- Nesting runs on Panel-A only

**Fail condition:** App crashes or treats qty=0 as "use anyway".

---

## Test Matrix: Pass/Fail Summary

| Step | Happy Path | Oversized | Bad Material | Invalid Numeric | Zero Qty |
|------|-----------|-----------|--------------|-----------------|----------|
| **Import** | 3 valid rows | 1 valid, 1 `outside-usable-sheet` | 1 valid, 1 `material-not-found` | 1 valid, 1 `invalid-numeric` | 1 valid, 1 `non-positive-quantity` |
| **Nesting** | 1–2 sheets, 7 placed | 1 sheet, 1 placed (or `empty-run`) | 1 sheet, 1 placed (or `invalid-input`) | 1 sheet, 1 placed | 1 sheet, 1 placed |
| **Results** | Sheets + placements + utilization | Partial result or error | Partial result or error | Partial result or error | Partial result or error |
| **Risk** | ✓ Pass | Bounds-check enforcement | Material catalog lookup | Parse robustness | Validation rules |

---

## What Passes vs. What Fails

### ✓ This Passes the Smoke Test

- Import accepts any header order (as long as column names are exact)
- Valid parts import with `valid` status
- Nesting places parts on sheets and reports utilization
- UI displays sheet count, placement list, and summary
- Invalid rows are marked with a specific error code (not a generic "error")
- Invalid rows do not crash the app or hang the UI

### ✗ This Fails the Smoke Test

- Import fails with a cryptic error instead of row-level feedback
- Oversized or invalid parts cause a crash or hang
- Nesting runs but ignores validation errors
- UI does not render results or shows a blank screen
- Error messages are vague (e.g., "invalid input" instead of "material-not-found")
- Duplicate part IDs crash the importer instead of producing a warning

---

## Quick Reference: Demo Material

| Property | Value |
|----------|-------|
| Name | Demo Material |
| Sheet Size | 96" × 48" |
| Usable Area | 95" × 47" (0.5" edge margin each side) |
| Default Spacing | 0.125" between parts |
| Kerf | 0.0625" (demo value in UI) |
| Rotation | Allowed |

---

## Next Steps If Smoke Test Fails

1. **Build fails:** Check the installed dotnet SDK version (requires `.NET 8.0.x` for local build/test) and NuGet package restore.
2. **Tests fail:** Rerun `dotnet test` with `--verbosity detailed` to see which test breaks.
3. **Import crashes:** Check CSV headers match `Id`, `Length`, `Width`, `Quantity`, `Material` (exact case, any order).
4. **Nesting crashes:** Check that all imported parts have valid numeric dimensions and a known material name.
5. **UI blank:** Ensure desktop host spawned WebView2 successfully and Web UI loaded (check browser dev tools F12 in WebView context).

---

## Phase 2 Material Library Preview

> Activate this section once the material library slice lands. Until then, the skipped Hicks tests are the source of truth for what is still blocked.

### Test Case 5: Duplicate Material Name

1. Create a material named `Baltic Birch`.
2. Attempt to create or rename another material to `Baltic Birch`.
3. **Pass:** Operation is rejected with `material-name-exists`; the original material remains unchanged.
4. **Fail:** UI silently overwrites, trims into a collision, or returns a generic error.

### Test Case 6: Delete Material In Use

1. Select `Baltic Birch` into the current project and import at least one row that references it.
2. Attempt to delete `Baltic Birch`.
3. **Pass:** Operation is blocked with `material-in-use`; import state remains intact.
4. **Fail:** Material disappears, imported rows orphan silently, or the app falls back to `Demo Material`.

### Test Case 7: Import Selection Exact Match

1. Select only `Baltic Birch` into the current project.
2. Import rows for `Baltic Birch`, `baltic birch`, and `Unknown Material`.
3. **Pass:** exact match row succeeds; the other two rows report `material-not-found`.
4. **Fail:** case-insensitive matching sneaks through, or missing materials are auto-created without telling the user.

---

## Phase 3 Project Persistence Preview

> Hicks will not approve Phase 3 on green builds alone. Save/open, metadata, and snapshot drift each need a repeatable human check.

### Test Case 8: Metadata Save/Open Round-Trip

1. Create a new project.
2. Fill every PRD field: Project Name, Project Number, Customer Name, Estimator, Drafter, PM, Date, Revision, Notes, and kerf width.
3. Save the project as `phase3-metadata.pnest`, close it, then reopen that exact file.
4. **Pass:** every field rehydrates exactly, including date, revision, multiline notes, and kerf.
5. **Fail:** reopened metadata falls back to defaults, drops a field, trims unexpectedly, or silently clears notes/date.

### Test Case 9: Saved Project Restores Its Own Material Snapshot

1. Create or open a project that uses `Baltic Birch`.
2. Save the project to `phase3-snapshot.pnest`.
3. Edit the live material library entry for `Baltic Birch` (or delete it entirely).
4. Reopen `phase3-snapshot.pnest`.
5. **Pass:** the reopened project shows the snapshotted material values from the file and keeps its import/nesting context intact.
6. **Fail:** the project silently adopts the new live-library values, loses the material, or substitutes a fallback without telling the user.

### Test Case 10: Corrupt or Unsupported `.pnest` File

1. Copy a known-good `.pnest` file.
2. Break the copy by either corrupting the JSON or changing `version` to an unsupported number (for example `99`).
3. Attempt to open the broken file.
4. **Pass:** the app surfaces `project-corrupt` or `project-unsupported-version` and keeps the current session stable.
5. **Fail:** crash, blank project state, silent reset, or a generic error with no actionable code.

---

## Phase 4 Import Pipeline Preview

> Activate this section once Ripley locks the Phase 4 scope. Current code is still CSV-only and the import table is read-only, so these checks are scaffolding, not a release verdict.

### Test Case 11: XLSX Happy Path Matches CSV Expectations

1. Create an `.xlsx` workbook with the required columns `Id`, `Length`, `Width`, `Quantity`, and `Material`.
2. Include the same rows used in `good-parts.csv`, but shuffle the column order and add one ignored extra column.
3. Import the workbook through the desktop host.
4. **Pass:** all rows appear in the import table with the same values/statuses the CSV path would produce; extra columns are ignored; no `unsupported-file-type` error appears.
5. **Fail:** `.xlsx` cannot be selected, the import silently drops rows, or CSV/XLSX produce different row-level validation outcomes for the same data.

### Test Case 12: Inline Fix Clears a Row Error Without Reimport

1. Import a file that includes one bad row (for example `Length = abc` or `Material = Unknown Material`).
2. Edit that row inline instead of reimporting the file.
3. Correct the invalid field and commit the change.
4. **Pass:** the row revalidates immediately, the validation status changes from `error` to `valid`/`warning`, and the warnings/errors summary updates without a full reimport.
5. **Fail:** edits require a restart/reimport, validation messages do not refresh, or the row stays stale until nesting runs.

### Test Case 13: Add/Delete/Edit Rows Persist Through Save/Open

1. Import a mixed-validity file.
2. Fix one row inline, delete one unwanted row, and—if Ripley confirms manual row creation is in scope—add one new valid row manually.
3. Save the project, close it, and reopen the same `.pnest` file.
4. **Pass:** every import-table change rehydrates exactly, including validation statuses/messages and the disabled/enabled state for nesting.
5. **Fail:** reopened projects restore the pre-edit import state, resurrect deleted rows, drop manual rows, or clear validation messages.

### Test Case 14: Multi-Issue Rows Stay Actionable

1. Import a row with more than one problem (for example blank `Id`, invalid `Length`, and missing `Material`).
2. Inspect the import table and validation summary.
3. **Pass:** every issue remains visible with an actionable code/message, the row keeps a stable `rowId`, and the app stays responsive.
4. **Fail:** only the first error is shown, later errors are silently discarded, or the UI becomes unstable when one row carries multiple issues.

### Phase 4 Assumptions to Reconcile With Ripley

- Whether Phase 4 includes full row CRUD (`add`, `edit`, `delete`) or only editing/deleting imported rows.
- Whether the bridge keeps `import-csv` as the live message and broadens it to `.xlsx`, or introduces a new generic import contract.
- Whether richer validation adds new warning/error rules beyond the current CSV set, especially for unusually small parts, very large imports, or field-level validation timing.
- Whether missing/deleted library materials after project reopen stay as inline import errors or get a dedicated remap flow.

---

## Phase 5 Results Viewer & PDF Reporting Preview

> Activate this section once Ripley locks the Phase 5 scope. Current code still renders a `SheetViewerPlaceholder`, the Web UI has no PDF export controls, and the host handshake exposes no report/export capability yet, so these checks are scaffolding, not a release verdict.

### Test Case 15: Viewer Matches the Latest Nesting Run

1. Import a valid file, run nesting, and open the Results page.
2. Compare the visible result cards/tables with the interactive sheet viewer.
3. If more than one sheet exists, switch between sheets in the viewer and the surrounding detail UI.
4. **Pass:** the viewer shows the same generated sheets, part placements, dimensions, rotation flags, and utilization story already present in the latest `NestResponse`; sheet outline and unused area are visually clear.
5. **Fail:** the viewer stays a placeholder, omits placements that exist in the tables, invents geometry not backed by the nesting result, or becomes the only place where critical result data is visible.

### Test Case 16: Viewer Interactions Stay Informational, Not Destructive

1. With a nested result open, use the Phase 5 viewer interactions Ripley keeps in scope (expected: zoom, pan, hover tooltip, click-to-inspect).
2. Hover a placed part, click it, and navigate away and back if the page keeps selection state.
3. **Pass:** interactions reveal metadata without mutating the underlying result, counts, or placement coordinates; selection details stay consistent with the placement table and unplaced list.
4. **Fail:** zoom/pan/selection corrupts the displayed result, changes counts, loses the active result silently, or disagrees with the placement table about which part is selected.

### Test Case 17: Report Edits Drive PDF Output From Current Project Data

1. Produce a nesting result, then open the Phase 5 report-edit UI.
2. Edit all PRD report fields in one pass: Company Name, Report Title, Project / Job Name, Project / Job Number, Date, and Notes.
3. Export the PDF to a known path.
4. **Pass:** the PDF reflects the edited report fields plus the latest nesting result, including summary content, sheet count/utilization context, sheet visuals, and unplaced/invalid items.
5. **Fail:** export ignores edited fields, uses stale project data, drops unplaced/invalid items, or requires rerunning nesting to see report-field changes.

### Test Case 18: Export Failures Stay Actionable and Non-Destructive

1. Attempt a PDF export under at least one failure condition Ripley keeps in scope (for example cancel the save dialog, choose a locked path, or export without a current nesting result).
2. Observe the desktop host and Web UI behavior.
3. **Pass:** the app surfaces a specific, user-visible outcome, does not crash, and does not leave the current project/result state corrupted.
4. **Fail:** export failure is silent, leaves behind a partial/blank file without warning, resets the active result, or falls back to a generic error with no actionable path forward.

### Test Case 19: Save/Open Restores Report Context Exactly If Phase 5 Persists It

1. Produce a nesting result and edit the report fields.
2. Save the project, close it, and reopen the same `.pnest` file.
3. **Pass:** if Ripley decides report fields persist with the project, the reopened project restores those exact report edits alongside the same `lastNestingResult`; if Ripley keeps report edits transient, the reopen behavior is explicit and documented instead of ambiguous.
4. **Fail:** reopened projects silently mix saved and live report data, restore stale report edits from another run, or mutate the saved nesting result while trying to reconstruct the report state.

### Phase 5 Assumptions to Reconcile With Ripley

- The current domain/UI seam is still single-material (`NestRequest` takes one material and `NestResponse` exposes one `MaterialSummary`), while the PRD expects a summary by material; Ripley needs to decide whether Phase 5 expands that contract or intentionally stays scoped to the current single-material result flow.
- Whether editable report fields persist inside `.pnest`, stay transient until export, or reuse existing project metadata fields without mutating the saved project record unexpectedly.
- Whether the PDF must include invalid import rows in addition to `unplacedItems`, since current nesting results persist unplaced output but not a dedicated report-ready invalid-item section.
- Whether the desktop bridge adds a new `export-pdf` capability/message with native save-dialog ownership or the Web UI generates the file directly.
- Whether viewer state (selected sheet, zoom/pan, clicked part) must survive reruns and save/open, or reset whenever a new nesting result becomes current.

---

## Phase 5 Follow-Up Correction Batch

> Use this section for Brandon's follow-up notes after the original Phase 5 slice. These checks are corrective: Hicks is defining the gate, not issuing a new approval here. Pair this with `tests\Phase5-Followup-Correction-Test-Matrix.md`.

### Test Case 20: Viewer Is the Live Three.js Path and Stays Locked to 2D

1. Import a valid file, run nesting, and open the Results page.
2. Interact with the live viewer using the navigation controls kept in scope for this batch.
3. Attempt the full range of likely viewer gestures: wheel zoom, drag pan, double-click/reset if present, and any rotate/orbit gesture the control library would normally allow.
4. **Pass:** the live results viewer is the Three.js-backed path rather than a placeholder/static fallback; zoom and pan work smoothly; the camera stays in plan view/top-down mode; rotate/orbit/tilt is disabled, ignored, or reset immediately; the viewer still matches the active sheet's placements, labels, and utilization story.
5. **Fail:** the UI falls back to a placeholder/static renderer, allows accidental 3D tilt/orbit, or drifts from the current result tables/cards.

### Test Case 21: Viewer Size and Mouse Ownership Stay Bounded

1. Open the same result on a normal desktop window, then enlarge the app to a much larger window.
2. Observe how much vertical space the viewer consumes relative to the surrounding summary, sheet list, and report-edit controls.
3. With the pointer outside the viewer, use the mouse wheel and normal page scrolling.
4. Move the pointer into the viewer and use wheel zoom and drag pan.
5. Drag inside the viewer, then release and move the pointer back to the page outside the viewer.
6. **Pass:** the viewer grows enough to stay readable but does not monopolize the Results page; summary/report UI remains comfortably reachable; while the pointer is over the viewer, wheel/drag input affects the viewer only; once the pointer leaves or the drag ends, page scrolling and other mouse input behave normally again.
7. **Fail:** the viewer expands so much that it crowds out the rest of the page, page scrolling still fires while zooming/panning inside the viewer, or pointer capture gets "stuck" after leaving the viewer.

### Test Case 22: Report Graphic Labels Every Panel Unambiguously

1. Produce a nesting result with more than one placed panel and export the PDF report.
2. Inspect each sheet graphic in the exported report, not just the placement summary text.
3. **Pass:** every placed panel is identifiable from the graphic itself with a visible label using the part/panel ID; if a panel is too small to carry an in-shape label, the report provides an adjacent legend/callout that removes ambiguity.
4. **Fail:** the report graphic shows anonymous rectangles, labels are missing for some panels, or the reviewer has to guess which shape corresponds to which part.

### Test Case 23: Utilization Percentages Use Decimal Fractions Exactly Once

1. Run or load a result with a known utilization near 60% so the incorrect `6000%` class of bug would be obvious.
2. Compare the utilization shown in the Results page summary cards, per-sheet details, and exported PDF.
3. **Pass:** utilization values are treated as decimal fractions and rendered exactly once as percentages (`0.6` → `60.0%`, or the agreed precision); overall and per-sheet values stay numerically consistent across UI and PDF.
4. **Fail:** any surface shows `6000%`, `0.6%`, raw decimal values, or a rounding rule that disagrees with the other user-visible surfaces.

---

## Phase 5 Bugfix Batch — Plan View Camera + PDF Save Dialog

> Use this section for Brandon's current bugfix batch. These are Hicks's acceptance checks for two user-visible regressions: the viewer opening side-on instead of face-on, and the desktop PDF save dialog becoming unusable/crashing before the user can finish the export.

### Test Case 24: Viewer Opens in Plan View Instead of Edge-On

1. Import a valid file, run nesting, and open the Results page.
2. Observe the viewer before touching any controls, then switch sheets or use any reset-to-fit behavior the page exposes.
3. **Pass:** the sheet is immediately visible face-on in plan view/top-down orientation; the user can see the full sheet rectangle, placements, and labels without first recovering from a side-on or tilted camera; switching sheets or resetting the view returns to the same plan-view orientation.
4. **Fail:** the first frame shows the sheet edge-on/as a thin sliver, any sheet change reintroduces the side view, or the user has to orbit/guess to understand the layout.

### Test Case 25: Viewer Navigation Stays Strictly 2D After the Camera Fix

1. With the same result open, use the full interaction set the viewer allows: wheel zoom, drag pan, touch gestures if available, and any right-click/alternate-drag gesture OrbitControls would normally interpret as rotate.
2. Zoom in, pan away from center, then trigger any reset/fit behavior and repeat on another sheet if the result has more than one sheet.
3. **Pass:** zoom and pan remain available, but orbit/tilt/roll never move the camera out of plan view; reset/fit returns to the same top-down orientation; interactions stay smooth and informational rather than changing the underlying result data.
4. **Fail:** the camera can still tilt, rotate, or roll after the fix, reset returns to a side view, or sheet changes produce inconsistent camera orientation.

### Test Case 26: PDF Save Dialog Stays Interactive Through Rename and Save

1. Produce a nesting result and click the PDF export action without pre-supplying a path.
2. When the native save dialog opens, click into the file-name field, rename the file, and if practical change folders before pressing **Save**.
3. **Pass:** the dialog stays responsive; the file-name field accepts edits; folder navigation and the Save button work normally; the app does not crash, freeze, or dismiss the dialog prematurely; a PDF is written to the exact folder/name the user chose.
4. **Fail:** the app crashes as soon as the dialog opens, the user cannot type a new filename, Save is blocked without explanation, or export writes to some other path/name than the one chosen in the dialog.

### Test Case 27: Cancel or Failed Export Does Not Poison the Next Save Attempt

1. Start a PDF export and cancel the save dialog, or intentionally choose a target that should fail cleanly.
2. Confirm the app remains open and the current result/report state is still intact.
3. Immediately start export again, choose a valid folder and filename, and save.
4. **Pass:** the first attempt ends with a clear, user-visible non-crash outcome; the second attempt opens a usable save dialog and can complete successfully; the final PDF is valid and non-empty.
5. **Fail:** cancelling or failing once leaves the next dialog broken, crashes the app, clears the active result, or traps the user in a stale/bad export state.

---

## Phase 6 Save Batch — FlatBuffers `.pnest` Migration + Save-Crash Hardening

> Hicks will not clear this batch on implementation claims alone. The app has to prove three things together: new `.pnest` saves are truly FlatBuffers on disk, legacy JSON `.pnest` projects stay readable during the transition, and the current project-save crash/failure path becomes a stable, user-visible non-crash outcome.

### Test Case 28: New `.pnest` Saves Write FlatBuffers and Reopen Without Drift

1. Create or open a project with non-default metadata, imported rows, a selected material, and a current nesting result.
2. Save As to `phase6-flatbuffers-save.pnest`.
3. Inspect the saved file in a plain text editor, then reopen that exact file in PanelNester.
4. **Pass:** the file is no longer readable JSON text, the app reopens it successfully, and metadata, import rows, material snapshots, selected material, and the latest nesting result all match the pre-save state.
5. **Fail:** the file is still JSON, reopen loses or mutates persisted data, or the save/open sequence crashes or silently resets the session.

### Test Case 29: Legacy JSON `.pnest` Files Still Open and Can Be Re-Saved

1. Open a known-good legacy JSON `.pnest` fixture (for example a pre-migration project copy or `sample-project.pnest`).
2. Verify the project loads with its metadata, imported parts, material snapshots, and last nesting result intact.
3. Save that project to a new `.pnest` path, close it, and reopen the newly saved file.
4. **Pass:** the legacy JSON file opens without manual conversion, the re-saved file uses the new FlatBuffers encoding, and the reopened project still matches the legacy source data.
5. **Fail:** old `.pnest` files now break without an explicit migration outcome, or the re-save drops snapshots, row validation state, or nesting/report context.

### Test Case 30: Project Save Dialog Stays Interactive Through Rename and Save

1. Start from an unsaved or dirty project and invoke **Save** or **Save As** so the native project save dialog opens.
2. Rename the file, edit the folder if practical, and complete the save to a known path.
3. **Pass:** the dialog stays responsive, accepts filename edits, respects the chosen folder/name, writes a `.pnest` file to that exact path, and the app does not crash or freeze.
4. **Fail:** the save dialog itself crashes the host, becomes uneditable, dismisses prematurely, or writes somewhere other than the user-selected path.

### Test Case 31: Corrupt or Unsupported `.pnest` Files Fail Cleanly Across Both Formats

1. Prepare one broken legacy JSON `.pnest` file and one broken FlatBuffers `.pnest` file (for example truncated bytes, random byte edits, or an unsupported version marker).
2. Attempt to open both files while another valid project is already open.
3. **Pass:** each failure surfaces a specific, user-visible outcome (`project-corrupt`, `project-unsupported-version`, or an explicitly documented legacy-compatibility message), and the currently open project remains intact.
4. **Fail:** either format crashes the app, clears the active session, or falls back to a generic error that gives the user no actionable clue.

### Test Case 32: Cancelled or Failed Project Save Does Not Leave Partial State or Poison the Next Attempt

1. Start a project save, then either cancel the dialog or intentionally choose a target that should fail cleanly.
2. Confirm the current project remains open, dirty state is still accurate, and no partial `.pnest` artifact is left behind at the failed target.
3. Immediately run Save/Save As again to a valid path and reopen the resulting file.
4. **Pass:** the first attempt ends with a clear non-crash outcome, the second attempt succeeds without restarting the app, and the reopened file contains the latest project state rather than stale or partially saved data.
5. **Fail:** one failed/cancelled attempt breaks the next save, clears unsaved work, leaves behind a corrupt partial file, or requires an app restart before saving works again.

---

## Acceptance Criteria

- [ ] Preflight: `dotnet test` reports zero failures and `npm run build` succeeds, with only documented placeholder skips remaining
- [ ] Happy path: All 3 parts import as `valid`
- [ ] Happy path: Nesting places all 7 instances on 1–2 sheets
- [ ] Happy path: Results show sheet count, utilization, and zero unplaced items
- [ ] Oversized part: Marked `error` with `outside-usable-sheet` code
- [ ] Bad material: Marked `error` with `material-not-found` code
- [ ] Invalid numeric: Marked `error` with `invalid-numeric` code
- [ ] Zero quantity: Marked `error` with `non-positive-quantity` code
- [ ] No crashes, hangs, or silent failures on any error path
- [ ] Phase 2 duplicate create/update rejects with `material-name-exists`
- [ ] Phase 2 in-use delete rejects with `material-in-use`
- [ ] Phase 2 selected-material import mismatch rejects with `material-not-found`
- [ ] Phase 3 save/open preserves all PRD metadata fields and kerf
- [ ] Phase 3 reopened projects show snapshotted material values instead of silently rereading the live library
- [ ] Phase 3 corrupt or unsupported `.pnest` files report `project-corrupt` or `project-unsupported-version`
- [ ] Phase 5 viewer renders the latest nesting result without drifting from the summary, sheet, placement, or unplaced tables
- [ ] Phase 5 viewer interactions stay informational and do not mutate the active nesting result
- [ ] Phase 5 PDF export includes the agreed editable fields plus current summary, sheet visuals, and unplaced/invalid output
- [ ] Phase 5 export failures stay user-visible and non-destructive
- [ ] Phase 5 report persistence behavior matches Ripley's final scope and is explicit on save/open
- [ ] Phase 5 follow-up: the live viewer uses the Three.js interaction path and stays locked to 2D/top-down navigation
- [ ] Phase 5 follow-up: viewer sizing stays bounded as the window grows and does not crowd out summary/report UI
- [ ] Phase 5 follow-up: mouse wheel/drag input is owned by the viewer only while the pointer is inside it, then returns cleanly to the page
- [ ] Phase 5 follow-up: exported report graphics label each placed panel unambiguously
- [ ] Phase 5 follow-up: utilization values treat decimal fractions correctly and never render as `6000%`-style double-multiplied percentages
- [ ] Phase 5 bugfix batch: the viewer opens face-on in plan view on first render, sheet changes, and reset/fit actions
- [ ] Phase 5 bugfix batch: viewer interaction remains 2D-only after the camera fix (zoom/pan allowed, tilt/orbit/roll blocked)
- [ ] Phase 5 bugfix batch: the native PDF save dialog remains interactive long enough to rename the file, change location, and press Save without crashing
- [ ] Phase 5 bugfix batch: the exported PDF lands at the exact filename/path chosen in the dialog, and cancel/failure does not break the next export attempt
- [ ] Phase 6 FlatBuffers batch: new `.pnest` saves are binary FlatBuffers on disk and reopen with no metadata, import-row, snapshot, selected-material, or nesting-result drift
- [ ] Phase 6 FlatBuffers batch: existing JSON `.pnest` fixtures remain openable during the transition and can be re-saved into the new format without data loss
- [ ] Phase 6 FlatBuffers batch: corrupt or unsupported `.pnest` files fail with specific, user-visible outcomes while the current session stays intact
- [ ] Phase 6 FlatBuffers batch: the native project save dialog stays interactive through rename/folder changes and writes to the exact path chosen by the user
- [ ] Phase 6 FlatBuffers batch: cancelled or failed project saves leave no partial project artifact, preserve dirty state, and do not block the next valid save attempt

---

## Phase 6 Hardening Smoke Tests

> Focus areas: empty-result export, dense-layout readability, save/open/export stability, viewer/pointer polish.

### Test Case 33: Empty-Result Export Behavior

1. Launch the app and create a new project without importing or running nesting.
2. Attempt to export a PDF report.
3. **Pass:** Export is disabled with a clear indication, shows a "no results" message, or produces a valid empty-state report—never a crash or blank/corrupt file.
4. **Fail:** The app crashes, freezes, or produces a corrupt/empty PDF without explanation.

### Test Case 34: Unplaced-Only Export

1. Import a file where every part exceeds sheet dimensions (e.g., 100×100 on a 96×48 sheet).
2. Run nesting (all parts will be unplaced).
3. Export PDF.
4. **Pass:** PDF includes the unplaced-items section with reason codes; no sheet visuals since no sheets were used; report is coherent.
5. **Fail:** Crash, blank report, or missing unplaced-items section.

### Test Case 35: Dense Layout Viewer Stress

1. Import or generate 50+ small panels (e.g., 4×4 inch qty 50) on a single material.
2. Run nesting to produce a dense single-sheet or few-sheet result.
3. Inspect the viewer: zoom in, hover over individual panels, click to inspect.
4. **Pass:** Viewer responds within 2 seconds, hover/click identifies the correct panel, labels remain readable at reasonable zoom.
5. **Fail:** Viewer freezes, hover selects wrong panel, labels overlap illegibly.

### Test Case 36: Dense Layout PDF Export

1. With the 50+ panel result from Test Case 35, export the PDF.
2. Open the PDF and inspect sheet graphics.
3. **Pass:** Each panel is labeled or has a legend/callout; reviewer can identify every shape without guessing.
4. **Fail:** Anonymous rectangles, missing labels, or labels that overlap so badly they're unreadable.

### Test Case 37: Focus-Loss During Save Dialog

1. Open a project, make edits to put it in dirty state.
2. Invoke Save or Save As so the native dialog opens.
3. Alt-Tab away from the app (or click another window) for 5 seconds, then Alt-Tab back.
4. Complete the save (rename file if desired), close and reopen the project.
5. **Pass:** Dialog survives focus loss, save completes, reopened file is correct.
6. **Fail:** Crash during focus switch, dialog dismissed, or partial/corrupt file written.

### Test Case 38: Pointer Capture Release

1. Open a nesting result with at least one sheet in the viewer.
2. Click inside the viewer and start a drag (pan gesture).
3. While still holding the mouse button, move the pointer rapidly outside the viewer bounds, then release.
4. Move pointer back outside viewer and attempt normal page scrolling with mouse wheel.
5. **Pass:** Page scroll works normally; viewer does not continue panning; no stuck input state.
6. **Fail:** Scroll is hijacked by viewer, pan continues after release, or pointer capture remains stuck.

### Test Case 39: Zoom Limits

1. With a result open, zoom in as far as the viewer allows.
2. Then zoom out as far as the viewer allows.
3. **Pass:** Both extremes remain usable—zoomed-in shows detail without breaking the canvas, zoomed-out keeps the sheet visible rather than shrinking to a dot.
4. **Fail:** Canvas artifacts, sheet disappears, or viewer becomes unresponsive at extremes.

### Test Case 40: Precision After Save/Open

1. Create a project with a kerf value like 0.0625 and parts with dimensions like 24.125 × 48.375.
2. Run nesting, note utilization percentages.
3. Save, close, and reopen the project.
4. **Pass:** Kerf, dimensions, and utilization display identically before and after save/open—no visible floating-point drift.
5. **Fail:** Values show rounding noise (e.g., 48.1250001), utilization changes by more than display precision.

---

## Phase 6 Acceptance Criteria

- [ ] Phase 6 hardening: empty-result export is graceful (disabled, message, or valid empty report)
- [ ] Phase 6 hardening: unplaced-only results export with coherent unplaced section and no crash
- [ ] Phase 6 hardening: 50+ panel viewer remains responsive and readable
- [ ] Phase 6 hardening: 50+ panel PDF has identifiable labels for every placement
- [ ] Phase 6 hardening: focus loss during save dialog does not crash or corrupt state
- [ ] Phase 6 hardening: pointer capture releases cleanly when leaving viewer bounds
- [ ] Phase 6 hardening: zoom limits prevent canvas breakage at extremes
- [ ] Phase 6 hardening: save/open round-trip shows no visible precision drift

---

## Notes for Reviewers

- This smoke test covers SC1–SC5 from the Phase 0/1 test matrix (bridge handshake, CSV validation, nesting boundaries, results display).
- Desktop integration test (WebView2 runtime detection) is deferred; marked `[Skip]` in the xUnit suite.
- Material library is hardcoded to "Demo Material" until Phase 2.
- Unicode part IDs are supported; test with `棚板-ä` if needed.
- Kerf is additive to material spacing, not subtractive from part dimensions.
- Floating-point tolerance is 0.0001" for fit decisions near the boundary.
