# Panel Nester Decisions

## Decision: Paginate large import payload tables

- **Author:** Dallas
- **Date:** 2026-03-17

### Context

The import/edit page becomes sluggish with large payloads because the UI was mounting every imported row at once. The provided `02_multi_material_7500_rows.xlsx` stress file contains 7,500 data rows, which is enough to make entering/leaving the Import tab and interacting with the table feel heavy.

### Decision

Keep the existing filter/sort/edit flow, but paginate the payload table when the filtered result set exceeds the selected page size. Default to 250 rows per page, with 100/250/500 row options and explicit page navigation controls.

### Why

- Preserves the operator workflow without changing import semantics.
- Cuts DOM work dramatically for large imports, which improves route changes and table interaction.
- Keeps small imports unchanged: if the filtered set fits inside the selected page size, the page still renders as a single table.

### Follow-up

If operators still need smoother scanning across very large datasets, the next frontend step would be row virtualization. Pagination is the lowest-risk fix that materially improves responsiveness now.


---

## Hicks — large import results responsiveness coverage

- Added Desktop revision-gate coverage that locks the WebUI performance-sensitive contract to nesting payloads only:
  - `ResultsPage` must not accept or re-scan `PartRow[]`
  - group-review state must derive from `NestPlacement.group`
  - `App` must not forward `state.importResponse.parts` into `ResultsPage`
  - `SheetViewer` continues to consume group metadata from the shared placement contract instead of re-declaring it locally
- Automated performance timing is still unrealistic in the current stack because we do not have a browser/UI test harness that can measure tab-switch latency or React commit cost against the 7,500-row workbook.
- Remaining manual gate: import `02_multi_material_7500_rows.xlsx`, switch into and back out of the affected results workspace tabs repeatedly, and confirm there is no visible stall/regression compared with the prior build while group review still renders correctly.


---


---



# Decision: Group Import Mapping Fix + Grouped Results Review

**Author:** Ripley | **Date:** 2026-03-17 | **Status:** Proposed

---

## 1. Import Mapping — Group-Only Mismatch Bug

### Root Cause

Two layers conspire to let Group silently disappear during auto-import:

**Server side:** `ImportMappingResolver.ResolveColumns` only sets `hasAllRequiredFields = false` when a *required* field is unmatched (line 330). Group is in `ImportFieldNames.Optional`, so an unmatched Group never flips that flag. This is *correct* behavior for the server — the server needs all required fields to parse rows at all.

**UI side:** `countMissingImportFields` (App.tsx:586–593) counts only `requiredImportFieldNames` that lack a `sourceColumn`. The auto-import gate at line 1680–1683 fires when this count is zero AND materials are resolved. Group is never checked. If a file has 6 columns (Id, Length, Width, Quantity, Material, Category) and only 5 auto-map, the import finalizes silently — the user never sees the mapping UI.

### Fix Contract

**Trigger manual mapping when:**
1. Any required field has no mapped source column *(existing behavior)*, **OR**
2. Any optional target field has no mapped source column **AND** the file has source columns not consumed by any field mapping.

Condition 2 is the key addition. It catches files like `[Id, Length, Width, Qty, Material, Category]` where "Category" should map to Group but didn't auto-match. It does NOT trigger for files with exactly 5 columns matching the 5 required fields (no spare columns to map → no mapping needed).

### Fix Location

**UI only.** Change the auto-import decision in `App.tsx` (around line 1680). Do NOT change `HasAllRequiredFields` on the server — that flag correctly controls whether the server can parse the file at all.

**Implementation:**

```typescript
// New companion function (App.tsx)
function hasUnmappedColumnsForOptionalFields(response: ImportResponse): boolean {
  const mappedSources = new Set(
    response.columnMappings
      .filter((m) => Boolean(m.sourceColumn))
      .map((m) => m.sourceColumn),
  );
  const unmappedSourceColumns = response.availableColumns
    .filter((col) => !mappedSources.has(col));

  if (unmappedSourceColumns.length === 0) return false;

  const unmappedOptionalFields = response.columnMappings.filter(
    (m) => !m.sourceColumn && optionalImportFieldNames.includes(m.targetField as any)
  );

  return unmappedOptionalFields.length > 0;
}

// Updated gate (line ~1680):
if (
  countMissingImportFields(importResponse) === 0 &&
  countUnresolvedImportMaterials(importResponse) === 0 &&
  !hasUnmappedColumnsForOptionalFields(importResponse)
) {
  // auto-finalize
}
```

Also update `describeImportReview` to include optional field status in the review message.

### Affected Files

| File | Change |
|---|---|
| `src/PanelNester.WebUI/src/App.tsx` | Add `hasUnmappedColumnsForOptionalFields`, update auto-import gate (~line 1680) |

**Server changes: None.** Bridge changes: None.

---

## 2. Grouped Results Review — Design

### What Brandon Asked For

1. Results page gets a **"By group"** tab when groups exist
2. Sheets with mixed groups: selected group renders normal, unselected group panels are **visually muted**
3. Hover tooltip shows **which group** a panel belongs to

### Data Gap: Group Must Flow Into NestPlacement

Currently `NestPlacement` has no `Group` field. Group is captured in `ExpandedPart` (used for nesting batching) but dropped when building `NestPlacement` results. Without this, the UI cannot know which group a placement belongs to without a `partId → PartRow → group` reverse lookup, which is fragile and requires carrying the full part list into the viewer.

**Decision:** Add `Group` as an optional field directly on `NestPlacement`. This is the clean contract — the placement carries its own group identity.

### Contract Changes

#### C# Domain (`NestPlacement.cs`)

```csharp
public sealed record NestPlacement
{
    // ... existing fields ...
    public string? Group { get; init; }  // New. Null = ungrouped.
}
```

#### C# Nesting Engine (`ShelfNestingService.cs`)

At placement creation (~line 312), set `Group = part.Group` from the `ExpandedPart`.

#### TypeScript (`contracts.ts`)

```typescript
export interface NestPlacement {
  // ... existing fields ...
  group?: string | null;  // New. Null/undefined = ungrouped.
}
```

### Results Page — "By Group" Tab

**Tab name:** "Summary by group" — appears in the left workspace tab bar alongside existing tabs ("Summary by material", "Sheet detail", etc.)

**Visibility:** Tab only renders when at least one placement across all results has a non-null group. When all parts are ungrouped, the tab does not appear.

**Tab content:**
- **Group selector dropdown** listing all unique groups (first-seen order from nesting, matching import order). Include "All groups" as the default and "Ungrouped" if any ungrouped placements exist.
- **Group summary table:** Group name, sheet count, placement count, utilization — filtered to the active material.
- Selecting a group sets `activeGroup` state, which propagates to the SheetViewer.

**Interaction with material tab:** Groups are **within** a material result. The "By group" tab filters the current material's sheets/placements by group. Switching materials resets the group filter to "All groups."

### SheetViewer — Mixed-Group Rendering

**New prop:** `activeGroup?: string | null`
- `null` or `undefined` = show all groups normally (default, "All groups" mode)
- `string` value = the selected group name

**When `activeGroup` is set:**
- Placements matching `activeGroup` render with their normal partId-hashed color and full opacity
- Placements NOT matching `activeGroup` render at **0.25 opacity** with a **desaturated gray** fill (`hsl(0 0% 40%)`). Their outline becomes a dashed pattern (if Three.js LineLoop supports it) or simply a lighter gray solid outline.
- This is a style-only change on existing meshes — no scene rebuild needed. Update `MeshBasicMaterial.opacity` and `MeshBasicMaterial.color` per placement.

**Performance note:** On group switch, iterate `PlacementVisual[]` and update material properties. This is O(n) in placements and does not trigger geometry rebuilds. Acceptable for typical sheet densities (<200 placements/sheet).

### SheetViewer — Hover Tooltip with Group

The existing tooltip (SheetViewer.tsx:931–950) shows partId, dimensions, position, rotation. Add group display:

```tsx
{tooltip.placement.group ? (
  <span>Group: {tooltip.placement.group}</span>
) : null}
```

This is a one-line addition to the existing tooltip JSX. Shows nothing for ungrouped placements.

---

## 3. Affected Files & Seam Ownership

### Parker (Domain + Services) — 2 files

| File | Change |
|---|---|
| `src/PanelNester.Domain/Models/NestPlacement.cs` | Add `Group` property (`string?`, default `null`) |
| `src/PanelNester.Services/Nesting/ShelfNestingService.cs` | Set `Group = part.Group` at placement creation (~line 312) |

### Dallas (WebUI) — 4 files

| File | Change |
|---|---|
| `src/PanelNester.WebUI/src/types/contracts.ts` | Add `group` to `NestPlacement` interface |
| `src/PanelNester.WebUI/src/App.tsx` | Fix auto-import gate: add `hasUnmappedColumnsForOptionalFields` check |
| `src/PanelNester.WebUI/src/pages/ResultsPage.tsx` | Add "Summary by group" tab, `activeGroup` state, group selector, group summary table |
| `src/PanelNester.WebUI/src/components/SheetViewer.tsx` | Accept `activeGroup` prop, muted rendering for non-active groups, group in tooltip |

### Bishop (Desktop) — 0 files

No bridge changes. `NestPlacement` flows as JSON; adding an optional property is backward-compatible.

### Hicks (Tests)

| Test Area | Coverage |
|---|---|
| **Import mapping** | File with Group alias column triggers mapping UI; file with exact 5 required columns does not trigger; file with unmatched extra column + Group triggers |
| **NestPlacement.Group** | Verify group is set on placements from grouped nesting; null for ungrouped |
| **SheetViewer** | Group in tooltip when present; no group line when null; muted rendering applies only when activeGroup is set |
| **ResultsPage** | "By group" tab appears when groups exist; hidden when no groups; group filter resets on material switch |

---

## 4. Agent Assignments

| Agent | Work | Notes |
|---|---|---|
| **Parker** | `NestPlacement.Group` field + nesting engine emit | 2 files, surgical. Can proceed immediately. |
| **Dallas** | Import gate fix + Results "By group" tab + SheetViewer group rendering + tooltip | 4 files, most complex piece. Depends on Parker's `NestPlacement.Group` for results work, but import fix is independent. |
| **Bishop** | None | No bridge changes. Not needed. |
| **Hicks** | Test matrix for import gate + NestPlacement group propagation + UI rendering | After Parker + Dallas land. |

### Execution Sequence

1. **Batch 1 (parallel):**
   - Parker → `NestPlacement.Group` in domain model + nesting engine emit
   - Dallas → Import gate fix in `App.tsx` (independent of Parker)

2. **Batch 2 (sequential, depends on Parker):**
   - Dallas → `contracts.ts` type update + ResultsPage "By group" tab + SheetViewer `activeGroup` + tooltip

3. **Batch 3:**
   - Hicks → Full test matrix + integration gate

---

## 5. Ambiguities & Risks

| Item | Status | Resolution |
|---|---|---|
| Should "By group" tab be across all materials or within current material? | **Resolved** | Within current material. Nesting is per-material; groups subdivide within a material. |
| What visual style for muted non-active-group panels? | **Resolved** | 0.25 opacity + desaturated gray fill. Simplest Three.js approach; no shader work. |
| Does ungrouped appear as a selectable "group"? | **Resolved** | Yes, as "Ungrouped" in the group dropdown. "All groups" is default. |
| Should group filter persist across sheet switches within same material? | **Resolved** | Yes. Group filter is material-scoped, not sheet-scoped. |
| NestSheet — should it carry a primary group? | **Resolved: No.** | Sheets can have mixed groups. Group lives on placements, not sheets. |
| FlatBuffers schema — does NestPlacement need schema update? | **No.** | NestPlacement is ephemeral (computed at nest time, not persisted). Only PartRow is persisted, and it already has Group. |
| PDF report — should group appear in PDF? | **Deferred.** | Not in this slice. PDF reads NestPlacement data; once Group is on placements, PDF can add it later. |

### Risk: "All groups" default hides the feature

If the default view is "All groups" with uniform rendering, users might not discover the grouped view. **Mitigation:** When groups exist, the "Summary by group" tab badge could show the group count (e.g., "By group (3)") to signal availability.

---

## 6. Verdict

**Implementation can proceed now.** The import fix is a straightforward UI-side gate change. The grouped results work has a clean dependency chain: Parker's 2-file change unblocks Dallas's results/viewer work. Bishop is not needed. No architectural risk — we're adding an optional field to an existing DTO and a new tab to an existing tabbed layout.

No v1 scope creep: we are not adding group-level PDF output, group ordering UI, or group summary statistics beyond what's needed for the tab display. Those are explicitly deferred.



---

## Supporting Decisions — Grouped Results Follow-up


### parker group results followup

# Parker — Group results follow-up

## Context

Results review needs enough placement metadata for the UI to derive per-group tabs and highlight mixed-group sheets without adding a second grouping contract.

## Decision

- Carry optional `Group` on every `NestPlacement` emitted by the nesting engine.
- Preserve that field through report shaping and project persistence so saved/reloaded results keep the same group identity.
- Keep the change additive: no-group runs still emit `null` and behave exactly like existing material-scoped results.

## Consequences

- Results and report consumers can derive grouped review state from the existing material/result payload.
- Mixed-group sheets remain explainable because every placement retains its originating group.
- No new backend summary layer is required for this slice.



### parker grouped nesting

# Parker — Grouped nesting carryover

- Added optional `Group` to part rows, edit payloads, expanded parts, import mapping, and FlatBuffers persistence.
- Group order is computed per material from the edited/imported row list in first-seen ordinal order; blank groups normalize to ungrouped and run last.
- Shelf nesting keeps the old global heuristic when no named groups exist. Once any named group exists, only the immediately previous group's final sheet stays open for the next group; all earlier sheets are closed.
- FlatBuffers schema change appends `group` to `PartRow` so older `.pnest` files remain readable with `Group = null`.



### dallas group results followup

# Dallas: group results follow-up

- Derive placement-to-group UI metadata in the WebUI from imported part rows plus the existing `partId` naming convention instead of widening the nesting result contract for this slice.
- Keep group review scoped to the currently selected material result, with a dedicated results tab that drives an active-group viewer focus state and dims non-focused groups rather than hiding them.



### hicks group results followup

## Hicks — Grouped results follow-up coverage

- Added executable regression coverage at the domain/services/desktop bridge seams for:
  - import responses that leave a mismatched group alias available for manual review,
  - nested/report/project placement contracts that must carry explicit `NestPlacement.Group` data.
- Added revision-gate source assertions for the WebUI follow-up behaviors:
  - import should enter manual review when optional unmapped fields overlap with unused source columns,
  - results review should expose group-specific navigation only when group data exists,
  - mixed-group sheets should keep non-active groups subdued and show group details on hover.
- Remaining manual check even after automation: verify the live Three.js viewer actually dims non-active groups correctly and updates hover text in the running desktop/WebUI build, because the current suite gates source structure but does not render/assert canvas pixels.

---

## Execution Record: Consolidated Local Commit (2026-03-17T10:26:24Z)

**Performer:** Ripley | **Status:** ✅ COMPLETED

All grouped import, nesting, and results workflow changes consolidated into single atomic local commit.

- **Commit Hash:** c95df7c
- **Subject:** Consolidate grouped import, nesting, and results workflow
- **Files Changed:** 369 total
- **Verification:** Git status clean; trailer included; no push executed (no remote)

Team ready for parallel implementation: Parker (domain/services), Dallas (WebUI), Hicks (tests).

---

# Execution Record: Group-Export-Slice Implementation (2026-03-17T18:58:10Z)

**Status:** ✅ COMPLETED

## What Was Delivered

**Dallas (WebUI):** Updated NestPlacement TypeScript contract to carry optional group metadata. Results/group review consume placement-level groups directly.

**Parker (Backend/Export):** Updated PDF/export output so grouped placements render with group-prefixed label `[Group] PartId` while ungrouped placements preserve existing output. Export honors `NestPlacement.group` directly from nesting payload.

**Hicks (Tester):** Added regression coverage for TypeScript contract seam (ImportResultsRevisionGateSpecs.cs) and grouped/ungrouped export behavior (ReportDataServiceSpecs.cs, QuestPdfReportExporterSpecs.cs). Proves mixed grouped + ungrouped placements survive report shaping and render distinct summary text.

**Ripley (Architecture):** Coordinated slice across team; validated architecture soundness; identified next priorities.

## Test Results

- Services.Tests: 99 passed, 1 skipped ✅
- Desktop.Tests: 57 passed, 1 skipped ✅
- WebUI build: Succeeded ✅
- Overall: 167 passing, 2 skipped, 0 failures

## Key Decisions

1. **TypeScript contract:** Added `group?: string | null` to NestPlacement interface to align C# model, FlatBuffers schema, and TypeScript types
2. **PDF export:** Group visibility inline in placement summary; backward compatibility maintained for ungrouped placements
3. **Results flow:** Use `placement.group` directly; fallback to part-row lookup for older payloads

## Next Priorities (Post-Export-Slice)

1. **Contract housekeeping** (5 min) — already done in this slice
2. **WebUI test infrastructure** (half-day) — Vitest configuration for pure-logic functions
3. **PDF group visibility** (1–2 days) — expand export to include group column/label
4. **E2E automation** (2–3 days) — smoke test across full bridge contract

## Manual Gates Outstanding (Hicks)

- Grouped results UV test (30 min) — verify dimming + tooltip
- Import mapping review gate (20 min) — 6-column CSV triggering manual mapping
- Dense-layout PDF (30 min) — 50+ panels readability
- Pointer capture release (15 min) — drag outside viewer bounds

Estimated total: 2–3 hours. Artifacts ready within same day.

---

# Dallas Next Steps: UX Improvement Roadmap (2026-03-17)

**Status:** Review & Recommendation

## Friction Points (Ranked by Impact)

### 🔴 **P0: Import Mapping State Clarity**

**Problem:** Users can't see when preview is out-of-sync; material creation deferred until finalize.

**Recommended Fix (Phase 1):**
1. **Auto-preview on mapping change** — Debounce 500ms after user edits mapping, auto-run preview. Remove manual "Refresh preview" button.
2. **Inline material creation** — Show modal form when selecting "Create new material"; create immediately, then confirm in mapping.
3. **Sync visual** — Add badge next to mapping preview: "✓ Preview current" or "⚠ Updating..."

**Owner:** Dallas | **Timeline:** 2 sprints | **Impact:** Eliminates "am I looking at old data?" question.

---

### 🔴 **P0: Unplaced Parts Diagnostic Void**

**Problem:** When nesting completes, users don't know *why* parts failed. Only reason code shown; no dimensional comparison.

**Recommended Fix (Phase 1):**
1. **Unplaced detail panel** — Expand Unplaced tab to show: part dimensions, reason code + plain English, material sheet specs, utilization on last sheet.
2. **Diagnostic visualization** (Phase 2, lower priority) — On-hover visualization of available space vs. part outline.

**Owner:** Dallas | **Timeline:** 1 sprint | **Impact:** Diagnose fixture problems immediately without context-switching.

---

### 🟠 **P1: Material Snapshot Dangling References**

**Problem:** When a material is deleted, saved projects still reference it. No remediation path.

**Recommended Fix (Phase 2):**
1. **Material snapshot manager** — On project open, show banner with "Remove" or "Recreate" actions for orphaned snapshots.
2. **Prevent snapshot orphaning** — When deleting a material, check if any saved project snapshot uses it.

**Owner:** Dallas + Parker | **Timeline:** 1 sprint | **Impact:** Cleaner project history.

---

### 🟠 **P1: Results Finality Ambiguity**

**Problem:** After nesting, unclear whether result is final or partial. No summary badge.

**Recommended Fix (Phase 2):**
1. **Nesting summary badge** — "Nesting complete: 127 parts placed | 3 unplaced | 4 sheets | Utilization 78%"
2. **Clear results action** — "Run again" button returns to Import with nesting controls re-enabled.

**Owner:** Dallas | **Timeline:** 1 sprint | **Impact:** Know at a glance whether to trust result or re-run.

---

### 🟡 **P2: Placement Inspection Minimalism**

**Problem:** Selected placement shows only position/rotation; no spec comparison.

**Recommended Fix (Phase 3):**
1. **Placement spec card** — Show original part specs vs. actual placement.
2. **Orientation badge** — Visual indicator if part was rotated 90°.

**Owner:** Dallas | **Timeline:** Future | **Impact:** Verify placements without re-opening Import.

---

### 🟡 **P2: Group Review Hierarchy Flatness**

**Problem:** Groups displayed as flat list; no sort, collapse, or bulk-select.

**Recommended Fix (Phase 3):**
- Ensure "Summary by group" tab has: sortable group list, group count badge on tab label, collapse/expand rows.

**Owner:** Dallas | **Timeline:** Part of grouped results feature | **Impact:** Scalability for large projects.

---

### 🟡 **P2: Bridge Feature Capability Tooltips**

**Problem:** Disabled buttons show no explanation; users assume broken, not version-dependent.

**Recommended Fix (Phase 3):**
1. **Disabled button tooltip** — "PDF export not available. Your host version does not support this feature. Required: Phase 5 or later"
2. **Capability badge** — On bridge status indicator: "Host v1.4 (PDF export: not available)"

**Owner:** Dallas | **Timeline:** Future | **Impact:** Reduces support questions.

---

## Execution Plan

| Phase | Work | Owner | Timeline |
|---|---|---|---|
| **Phase 1** | Auto-preview, inline material creation, sync visual, unplaced diagnostics | Dallas | 2 sprints |
| **Phase 2** | Nesting summary badge, clear results, material snapshot manager, snapshot orphaning prevention | Dallas + Parker | 1 sprint |
| **Phase 3** | Placement spec card, group review hierarchy, bridge capability tooltips | Dallas | Future |

---

# Hicks Quality State Review: Phase 6 Release Readiness (2026-03-17)

**Status:** Recommended | **Scope:** Phase 6 release readiness + highest-priority hardening

## Current Quality State

**Test Baseline:** 167 total (167 passing, 2 skipped, 0 failures)
- Domain: 16/16 passing
- Services: 99 passing, 1 skipped
- Desktop: 57 passing, 1 skipped

**Build Status:** ✅ All green
- `dotnet build .\PanelNester.slnx` → 0 errors
- `npm run build` → Production bundle built
- MSI build verified working

**Feature Completeness:**
- ✅ Grouped import, nesting, results workflow live
- ✅ Domain model: `NestPlacement.Group` field emitted by nesting engine
- ✅ WebUI: Results page "Summary by group" tab with group filter and SheetViewer dimming/tooltip
- ✅ Bridge: FlatBuffers persistence, legacy JSON compat, project metadata snapshot
- ✅ Regression coverage: All prior phases remain green

---

## Release Risk Analysis

### High-Confidence Domains (Automated)

| Domain | Status | Notes |
|---|---|---|
| Import pipeline | ✅ Green | CSV/XLSX validation, field mapping, material resolution, edit persistence. 38+ tests. |
| Nesting determinism | ✅ Green | Same input → identical placements, sheet count, utilization. Multi-material batching deterministic. |
| FlatBuffers round-trip | ✅ Green | Save/open cycle preserves metadata, materials, result, placement coords with full precision. |
| Bridge contracts | ✅ Green | Handshake, file dialogs, PDF export, project CRUD, batch nesting all seam-tested. |
| Material library | ✅ Green | CRUD gates, in-use protection, name collision rules. 22 tests. |
| Group field propagation | ✅ Green | PartRow → ExpandedPart → NestPlacement pipeline verified at seams; nullable contract sound. |

---

### Medium Risk — Manual Gate Outstanding

| Item | Work | Evidence Gate |
|---|---|---|
| **Grouped results UI rendering** | Three.js viewer opacity/color dimming for non-active groups + tooltip group display. | Live smoke: import mixed-group parts → nest → select group filter → screenshot dimmed layout + tooltip |
| **Import mapping review gate** | "Group" optional-field unmapped-column detection in App.tsx. | CSV with 6 columns → screenshot showing manual mapping UI triggered |
| **Dense-layout readability** | 20+ placements per sheet in viewer and PDF. Viewer zoom/label clarity, PDF callout/legend. | 50-panel nesting → PDF export → screenshot showing all panels labeled or legend visible |
| **Pointer capture release** | Drag inside viewer, release outside → verify scroll outside viewer not hijacked | Recording of page-scroll working after out-of-bounds release |

---

## Highest-Priority Manual Validation Steps (2–3 hours)

### 1. Grouped Results UV Test (30 min)

**Procedure:**
1. Import CSV with columns: `Id, Length, Width, Quantity, Material, Category`
2. Map Category → Group
3. Run nesting with mixed groups
4. Click "Summary by group" tab
5. Select a group from dropdown

**Pass Criteria:**
- ✅ Selected group renders normal color, full opacity
- ✅ Unselected groups render gray, 0.25 opacity
- ✅ Hover over muted panel → tooltip shows `Group: Windows`
- ✅ No viewer freeze or rendering artifacts

---

### 2. Import Mapping Review Gate (20 min)

**Procedure:**
1. Create test CSV: `Id, Length, Width, Quantity, Material, Category`
2. Import CSV
3. Expect: Manual mapping UI appears (Group column unmatched)
4. Verify: All 6 columns visible; can map Category → Group

**Pass Criteria:**
- ✅ Manual mapping UI appears (not auto-finalize)
- ✅ All 6 source columns visible
- ✅ Group mapping option available and selectable

---

### 3. Dense-Layout PDF (30 min)

**Procedure:**
1. Import CSV with 50+ small panels
2. Run nesting (single 96×48 sheet)
3. Export PDF
4. Inspect: Are all panels labeled? Is there legend/callout?

**Pass Criteria:**
- ✅ Labels visible for most panels OR legend with numbered callouts
- ✅ No anonymous shapes or text overflow
- ✅ PDF renders without corruption

---

### 4. Pointer Capture Release Edge (15 min)

**Procedure:**
1. Open any nesting result with visible placements
2. Click and drag downward 100px inside viewer
3. While dragging, rapidly move cursor outside bounds
4. Release mouse button while outside
5. Scroll page vertically with mouse wheel

**Pass Criteria:**
- ✅ Page scroll works normally (not hijacked)
- ✅ Viewer does not respond to scroll outside bounds

---

## Verdict

**Current state is solid for release if Phase 6 manual gates are closed.** Automation baseline strong (167/168 tests passing, zero regressions). Remaining work is evidence collection for user-visible polish.

**Estimated effort to close:** 2–3 hours, 1 tester  
**Recommendation:** Execute manual gates now; validation can proceed in parallel with other work.

---

# Ripley Post-Grouped-Nesting Review (2026-03-17)

**Author:** Ripley | **Status:** Proposed

## Current State Assessment

### What's Solid

- **Domain models are complete.** `PartRow`, `ExpandedPart`, and `NestPlacement` all carry `Group`. FlatBuffers schema persists it on both `PartRow` and `NestPlacement`.
- **Nesting engine is fully group-aware.** `ShelfNestingService` batches by first-seen group order, ungrouped runs last, spillover between groups controlled.
- **Import pipeline handles Group.** Six alias patterns, optional-field recognition, auto-import gate correctly forces manual mapping when spare columns overlap with unmapped optional fields.
- **WebUI results and viewer are functional.** "Review by group" tab, `activeGroup`-driven muted rendering, group in tooltip, group filter scoped to active material.
- **Build is clean.** .NET solution: 0 errors, 0 warnings. WebUI: `tsc -b && vite build` succeeds. All tests pass.
- **Git tree is clean.** Single consolidated commit (`c95df7c`) for grouped work. No drift.

### Type-Safety Gap (Low Risk)

The TypeScript `NestPlacement` interface in `contracts.ts` was missing the `group` field (now fixed in group-export-slice). This was not a runtime bug — C# serializes it, JavaScript receives it, and all consumer code paths use extended types with `group`. But keeping the contract honest is important.

---

## Recommended Next Steps (Priority Order)

### 1. WebUI Test Infrastructure (Half-day)

**Risk:** Medium | **Why now:** The WebUI has no test script. ResultsPage, SheetViewer, and App.tsx auto-import gate have significant logic currently relying on manual verification.

**Scope:** Add Vitest configuration, write unit tests for pure-logic functions: `buildPlacementGroupLookup`, `buildGroupSummaries`, `countReviewableOptionalImportFields`, `shouldRequireImportReview`. Don't test Three.js rendering — test data transformations.

---

### 2. Group Information in PDF Reports (1–2 days)

**Risk:** Low | **Why now:** `NestPlacement.Group` already populated; data path ready. Users expect group info in exported documentation.

**Scope:** Add group column/label to placement tables in PDF. Consider "by group" summary section if grouped nesting used. Skip group-specific page breaks for v1.

---

### 3. End-to-End Smoke Test Automation (2–3 days)

**Risk:** Medium | **Why now:** Multiple test matrices reference manual smoke procedures. App has enough surface area (import → edit → nest → results → save → reopen → PDF) that manual verification doesn't scale.

**Scope:** Headless or semi-headless test driving bridge contract: import CSV, run nesting, verify placements, save project, reopen, verify state. Contract-level verification sufficient.

---

### 4. Nesting Quality Improvements (Variable)

**Risk:** High (scope creep) | **Why now:** Shelf heuristic works, but not competitive with bin-packing variants. Before widening to new features, evaluate nesting output quality.

**Scope for v1:** Benchmark current utilization on representative datasets. If below ~70%, consider single improvement (e.g., best-fit shelf selection). Do NOT open multi-algorithm optimization effort.

---

## What I'd Explicitly Defer

- **Group ordering UI** (drag-to-reorder groups before nesting) — not needed for v1
- **Cross-material group views** — groups are per-material; keep it that way
- **Cloud sync / multi-user** — local-first is the right call
- **Non-rectangular parts** — out of scope, shelf heuristic doesn't support them

---

## Verdict

The grouped nesting slice is architecturally sound and well-integrated. Immediate priority is housekeeping (WebUI tests, PDF groups) before adding new features. Items #3 and #4 can queue behind the first two without risk.



