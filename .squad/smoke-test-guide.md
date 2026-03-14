# Phase 0/1 Smoke-Test Checklist

> A repeatable, hands-on verification that the import → nesting → results path works end-to-end.

## Preflight: Build and Run Tests

Before launching the app, ensure the foundation is solid:

```powershell
# From repo root
dotnet restore
dotnet build
dotnet test
```

**Expected outcome:** `Build succeeded` + `dotnet test` completes with zero failures. Skips should be limited to the documented WebView2/material-library placeholders that are still intentionally blocked.

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

1. **Build fails:** Check dotnet version (requires .NET 10.0.x) and NuGet package restore.
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

## Acceptance Criteria

- [ ] Preflight: `dotnet test` reports zero failures, with only documented placeholder skips remaining
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

---

## Notes for Reviewers

- This smoke test covers SC1–SC5 from the Phase 0/1 test matrix (bridge handshake, CSV validation, nesting boundaries, results display).
- Desktop integration test (WebView2 runtime detection) is deferred; marked `[Skip]` in the xUnit suite.
- Material library is hardcoded to "Demo Material" until Phase 2.
- Unicode part IDs are supported; test with `棚板-ä` if needed.
- Kerf is additive to material spacing, not subtractive from part dimensions.
- Floating-point tolerance is 0.0001" for fit decisions near the boundary.
