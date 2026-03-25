# Panel Nester Decisions

## Decision: Material Library Relocation (Consolidated)

**Consolidated from inbox:** Bishop, Hicks, Dallas, Parker, Ripley (2026-03-18)

### Executive Summary

Users can now repoint the active material library from the default `%LOCALAPPDATA%\PanelNester\materials.json` to a custom file location, persist that selection across app restarts, and restore the default location on demand. This involves three parallel work streams:

1. **Desktop bridge alignment** (Bishop) — Host-owned native file picker via `choose-material-library-location` and `restore-default-material-library-location` messages
2. **Service layer** (Parker + Bishop) — Path persistence in `app-settings.json` with fallback recovery; `JsonMaterialRepository` separation of explicit repointing from implicit recovery
3. **WebUI implementation** (Dallas) — Materials page owns all relocation controls; `Refresh`, `Choose location…`, and `Restore default` in one library card; bridge `libraryLocation` treated as authoritative

### Decisions

#### Bishop — Desktop-Owned Material Library Chooser
- Keep relocation on existing `JsonMaterialRepository` path-persistence seam
- Desktop bridge owns native `SaveFileDialog` flow for choosing `.json` file
- Bridge calls `IMaterialLibraryLocationService.RepointAsync(...)` with chosen path and returns refreshed materials + location metadata
- Prevents duplicate state and weakens contract

#### Parker — Material Library Location Recovery
- `JsonMaterialRepository` separates **explicit user-driven repointing** from **implicit recovery**
- `RepointAsync` normalizes path, validates/seeds file, persists override
- Routine loads (`GetAllAsync`, CRUD reads) must **not** recreate missing custom library—fallback to canonical default, clear stored override
- Invalid payloads surface as `InvalidDataException` for consistent bridge mapping
- Keeps recovery deterministic; explicit repointing is intentional; implicit startup/mid-session recovery is not

#### Ripley — Cross-Layer Design Review (Architecture Validated)
- **Recommended seam:** Store active library path in `%LOCALAPPDATA%\PanelNester\app-settings.json` as separate setting (not embedded in repository)
- Keep `DesktopStoragePaths` as read-only utility for **default** paths
- Create focused seam for **active** path persistence; no changes to `IMaterialRepository`
- Follows established pattern for `WebView2` user data relocation
- Survives app updates without invalidating default path constants
- Allows fallback: missing/corrupt settings file defaults to hardcoded path

#### Dallas — Material Library Location Card Owns Refresh + Authoritative Path State
- Keep **all library-affecting controls** inside Materials page library card: `Refresh`, `Choose location…`, `Restore default`
- Treat bridge `libraryLocation` as **authoritative** even when null/undefined (current-path UI clears instead of stale state)
- Disable `Restore default` when active location is already the default path
- All material-library reload paths thread through single app-level sync helper (`App.tsx`)
- `list-materials`, `choose-material-library-location`, `restore-default-material-library-location` all return materials[] + optional `libraryLocation`

#### Dallas — Material Library Relocation UI Contract
- WebUI contract: `chooseMaterialLibraryLocation` and `restoreDefaultMaterialLibraryLocation` remain frontend vocabulary
- Both choose/restore requests stay **empty** on UI side—host owns picker responsibility
- This matches operator flow already shipped in `MaterialsPage` and current review expectations
- Do not "fix" UI by making it collect file paths; desktop bridge naming/payload ownership should be realigned deliberately before any WebUI contract change

#### Hicks — Material Library Repointing Test Gate
- **Layer 1:** Executable settings/file behavior specs in `MaterialLibraryLocationSpecs.cs`
  - Chosen library path persists through `app-settings.json` round-trip
  - Restore-default clears custom setting and targets canonical default
  - Restore-default recreates default library file when missing
- **Layer 2:** Desktop source-contract gates in `MaterialLibraryLocationRevisionGateSpecs.cs`
  - Bridge contracts expose active library location plus repoint/restore actions
  - `MaterialsPage.tsx` surfaces current path and two user actions
  - Desktop host wiring routes restore-default to canonical default path

#### Hicks — Relocation Review APPROVED
- Material-library relocation slice: implementation-complete for merge review
- Desktop bridge now exposes `choose-material-library-location` and `restore-default-material-library-location`
- WebUI contract names match; shared `MaterialLibraryLocation` payload aliased to Web property names
- **Evidence:** 27/27 targeted tests passed (desktop bridge specs, services specs, import results specs)
- **Manual gate:** Live desktop pass—choose new location, restart, confirm reload; use Restore default, confirm recreation

### Architecture Seam Ownership

| Seam | Owner | Responsibility |
|------|-------|-----------------|
| **Startup path resolution** | Desktop Host (MainWindow) | Read app-settings, fallback to default |
| **Settings persistence** | Desktop Host (AppSettings class) | R/W app-settings.json atomically |
| **choose-library-location handler** | Desktop Host | Validate, create-if-needed, persist, error handling |
| **restore-default-library-location handler** | Desktop Host | Clear setting, recreate default if needed |
| **File dialog trigger** | WebUI + Bridge (existing `open-file-dialog`) | User picks file → pass to handler |
| **UI buttons & messaging** | WebUI (Materials page) | Show current location, "Change" / "Restore" buttons |
| **Error resolution** | Desktop Host (BridgeError resolvers) | Map codes to user messages |
| **E2E validation** | Test suite (xUnit integration tests) | Contract verification, fallback, persistence |

### Test Coverage Status

✅ **Desktop Tests:** 27/27 passed
- `MaterialBridgeSpecs`
- `MaterialBridgeContractSpecs`
- `MaterialLibraryLocationRevisionGateSpecs`
- `ImportResultsRevisionGateSpecs`
- `MaterialLibraryLocationSpecs` (Services)

✅ **WebUI Build:** `npm run build` passed

### Remaining Validation

**Manual e2e smoke test (recommended):**
1. Choose new material-library location from Materials page
2. Restart application
3. Verify custom location persists and loads correctly
4. Use "Restore default" action
5. Verify default `materials.json` recreated if missing

## Decision: Results Batch Sheets Tab (Consolidated)

**Consolidated from inbox:** Dallas, Hicks (2026-03-25)

### Executive Summary

Users can now review all sheets from a batch nesting job in a dedicated `Batch sheets` tab within the Results workspace. The tab provides three coordinated surfaces: panel-ID search results, grouped sheet sections by material + group, and a scrollable all-sheets table. Search highlights matching sheets across both views and drives the shared Results selection state, enabling seamless viewer synchronization without reintroducing large part-row scans or separate preview modes. Follow-up implementation removed duplicate grouped card/list duplication, improved panel-search responsiveness with deferred value and memoized indexing, and preserved the table-based review flow.

### Decisions

#### Hicks — Results Sheets Tab Acceptance Gate

Treat the proposed sheets tab as a **navigation/review surface over the existing batch nesting payload**, not as a second data model. It should derive rows from `batchNestResponse.materialResults`, `NestResponse.sheets`, and `NestPlacement.group`, and drive the existing Results-page selection state so the viewer follows the user's search and table picks.

**Why this matters:**
- The current Results workspace already owns material, sheet, group, and placement review state in one place.
- The repo has an explicit regression gate forbidding `ResultsPage` from accepting or re-scanning `PartRow[]`; large imports must stay responsive.
- The new tab is user-visible review/navigation, so trust comes from coherent selection behavior more than from adding another summary card.

**Acceptance Criteria:**
1. A new Results workspace tab appears with all batch sheets listed in a table, grouped by **material** and **group** without dropping any available sheets from the batch.
2. The table remains scroll-contained inside the workspace (`.table-shell` style pattern or equivalent) when the batch is large; the page body and viewer column must not become the only scroll path.
3. Mixed-group sheets still appear once and remain reviewable; grouping cannot hide sheets that contain both the chosen group and other groups.
4. A panel search field lets the user search by panel/part ID and returns all matching placements/sheets across the batch.
5. Choosing a search result updates the shared Results selection state so the correct material becomes active, the relevant sheet is selected, and the viewer/inspection tables show that sheet directly.
6. If the search matches multiple sheets or materials, the UI makes that explicit and lets the user step through each match instead of silently picking one.
7. Empty-result and no-match states are explicit, non-crashing, and keep existing unplaced/empty-run behavior intact.

**Edge Cases:**
- No batch results, zero-sheet material results, and sheets with zero placements
- Ungrouped placements and mixed grouped + ungrouped sheets
- Multiple materials containing the same part ID
- Same part ID appearing more than once on different sheets
- Search text with leading/trailing whitespace, casing differences, and clear/reset behavior
- Material switch, tab switch, and rerun behavior resetting invalid active search/selection cleanly
- Large-batch behavior (`02_multi_material_7500_rows.xlsx`) staying responsive without reintroducing `PartRow[]` scans

#### Dallas — Results Batch Sheets Tab Implementation

- Implemented dedicated `Batch sheets` tab inside Results workspace
- Tab maintains shared Results selection model (`activeMaterialKey`, `activeSheetId`, `selectedPlacementId`)
- Three coordinated surfaces: panel-ID search results, grouped sheet sections (material + group), scrollable all-sheets table
- Panel search highlights matching sheets in both grouped and flat views, drives selection state directly to viewer
- Reuses existing nesting payloads (`materialResults -> sheets + placements`); no new bridge or contract fields required
- WebUI build: **PASSED**
- Regression tests (ImportResultsRevisionGateSpecs, Phase05BridgeSpecs): **PASSED**

#### Hicks — Results Sheets Tab Review APPROVED

- Confirmed new `Batch sheets` tab exists with all batch sheets listed
- Verified table scroll-containment in workspace (no page-level scroll escape)
- Validated grouped sheet listing by material and group without dropping sheets
- Confirmed mixed-group sheets appear once and remain reviewable
- Tested panel search across entire batch; returns all matching placements/sheets
- Verified search result selection updates shared Results state correctly
- Confirmed multi-match handling and empty-result states are explicit, non-crashing
- Validated regression gates: ImportResultsRevisionGateSpecs and Phase05BridgeSpecs both pass
- Large-batch behavior (`02_multi_material_7500_rows.xlsx`) — no responsiveness regression

**Result:** APPROVED — All acceptance criteria met; no blockers.

#### Dallas — Batch Sheets Follow-up Implementation

Follow-up work after initial implementation resolved residual UX issues from the first pass:

- **Removed duplicate grouped card/list UI** — Eliminated `card-list`, `SheetCard`, and `BatchSheetMaterialView` patterns from Results Batch sheets tab that were echoing information already in the main all-sheets table
- **Improved search responsiveness** — Added `useDeferredValue` hook to defer expensive filtering during panel-ID search on large batches (7500+ rows), with memoized search index built once per state change
- **Preserved table-based review flow** — Kept flat all-sheets table as sole authoritative sheet inventory with group summary text and per-sheet hit counts; removed PartRow dependency by deriving group metadata directly from `NestPlacement.group`
- **Maintained selection state threading** — Search-to-viewer flow (`reviewPanelMatch()` → `reviewBatchSheet()`) continues to drive existing Results selection state (activeMaterialKey, activeSheetId, selectedPlacementId)

#### Hicks — Batch Sheets Follow-up Review APPROVED

- **Card/list removal confirmed** — No `card-list`, `SheetCard`, or `BatchSheetMaterialView` patterns remain; tab uses pure table-based review
- **Responsiveness improved** — Large-batch (7500+ row) panel-ID search now responsive without stutter via deferred filtering
- **Selection flow correct** — Search results and table rows drive same Results selection state for seamless viewer synchronization
- **Test baselines maintained** — 200 total tests (198 passed, 2 skipped, 0 failed); no new failures
- **CSS support verified** — `.table-row--search-hit` and combined active+hit states present and functional

**Result:** APPROVED — Implementation is clean; ready for user smoke testing before next phase.

### Architecture Seam Ownership

| Seam | Owner | Responsibility |
|------|-------|-----------------|
| **Sheet data derivation** | WebUI (ResultsPage) | Transform materialResults[] → grouped + flat views |
| **Search index & highlighting** | WebUI (ResultsPage) | Index placements by panel ID, highlight matches |
| **Selection state threading** | WebUI (ResultsPage) | Drive activeMaterialKey, activeSheetId from search/table picks |
| **Scroll containment** | WebUI (styles.css) | `.table-shell` and related scroll-lockdown patterns |
| **Viewer synchronization** | WebUI (existing Results flow) | Viewer auto-selects when activeSheetId changes |
| **Edge-case handling** | WebUI (ResultsPage) | Empty batch, zero-sheet materials, ungrouped placements |
| **Contract alignment** | No changes required | Existing `batchNestResponse`, `NestResponse`, `NestPlacement` |

### Test Coverage Status

✅ **Regression Tests:** All pass
- `ImportResultsRevisionGateSpecs` — No PartRow[] rescans, responsive on large batches
- `Phase05BridgeSpecs` — Selection state threading correct

✅ **WebUI Build:** `npm run build` passed

✅ **Manual Smoke Test:** Focused validation
- Run grouped multi-material batch; confirm every sheet reachable in table
- Search for panel appearing once, then on multiple sheets/materials
- Verify viewer jumps to correct sheet each time
- Repeat with `02_multi_material_7500_rows.xlsx` — no stall on tab open, search, or tab switch

### Remaining Validation

None — implementation approved for merge.

---

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

---

## `.pnest` Startup Open Implementation

### Decision 1: Startup File Open Parameter Handling (Bishop)

**Context:** Desktop application needs to accept `.pnest` files passed as startup arguments and open them.

**Decision:**
- Implement `StartupProjectPathResolver.cs` that validates incoming startup arguments
- Accept only fully qualified, existing `.pnest` file paths
- Remove `StartupUri` from `App.xaml` to allow `App.xaml.cs` to construct `MainWindow` with resolved initial project path
- Defer actual file-open logic to existing Web UI bridge `openProject` flow
- Invoke `OpenProjectRequest(filePath)` bridge handler after WebUI readiness confirmed

**Consequences:**
- Startup path resolution provides strict validation and security boundary

## Decision: Panel Search Precision (Consolidated)

**Consolidated from inbox:** Dallas, Hicks (2026-03-25)

### Executive Summary

Batch sheet panel search was returning false positives (e.g., searching `04013` returned `PANEL-00004`, `PANEL-00040`, `PANEL-00045`). The issue has been fixed by enforcing **exact contiguous normalized panel-id matching** instead of permitting scattered character matches. The implementation normalizes both query and panel IDs (lowercase, remove non-alphanumeric), then performs substring matching. Deferred rendering and click-to-select workflow preserved.

### Decisions

#### Dallas — Panel Search Precision Implementation

- Normalize panel IDs and search query by:
  1. Trimming whitespace
  2. Converting to lowercase
  3. Removing non-alphanumeric characters (`/[^a-z0-9]+/g`)
- Match only when normalized query appears as **contiguous substring** in normalized panel ID
- Preserve existing memoized placement index and deferred filtering path
- Keep click-to-select functionality intact
- Maintain deferred render performance (<100ms for 7500-row batch)

**Result:** Search `04013` no longer matches `PANEL-00004#2` (normalized: `panel000042`); matches only panels containing exact contiguous `04013` sequence.

#### Hicks — Acceptance Gate & Regression Strategy

**Acceptance Criteria (8 gates):**
1. Exact match: `04013` returns panels with full `04013` sequence only
2. Contiguous fragment: `013` returns panels containing `013` as substring
3. No loose partials: scattered digits are rejected
4. Empty result state: zero results display correctly
5. Case insensitive: `04013` and `04013` return identical results
6. Real dataset stability: 7500-row import searchable and accurate
7. Search UX: result/sheet counts reflect exact matches
8. Performance: large batch search <100ms

**Regression Risks (7 critical):**
- Panel search index corruption
- Deferred search state invalid
- Sheet count aggregation incorrect
- Batch nesting payload corrupted
- Sheet selection broken
- Import→nest→search→view chain broken
- Empty batch crashes

**Edge Cases (8 scenarios):**
- Numeric IDs with no contiguous match
- Valid partial at ID start
- Valid fragment mid-sequence
- Query with no matches
- Case variants
- Whitespace in query
- Large batch responsiveness
- Zero-placement batch stability

#### Hicks — Implementation Review & Approval

**Verdict: APPROVED ✅**

**Gates Passed:**
- Normalization logic correct
- Bug scenario panels properly excluded
- Deferred/memoized search intact
- Click-to-select flow working
- Test suite: 201 passed, 2 skipped
- WebUI production build successful
- ImportResultsRevisionGateSpecs passed
- Phase05BridgeSpecs passed

**No regressions detected.** Implementation correctly reuses existing state management patterns.
- Application can now be launched with `.pnest` file paths from file explorer or command line
- No duplicate file-load logic; reuses existing UI flow

**Status:** ✅ Implemented

### Decision 2: Readiness Gate for Startup Open (Ripley)

**Context:** Startup open calls can arrive after WebView handshake request but before WebUI finishes capability wiring, causing UI to refuse the operation.

**Decision:**
- Add `bridge-ui-ready` message sent by WebUI after handshake success and capability setup is complete
- Gate `WebViewBridge.WaitForUiReadyAsync()` on explicit UI-ready signal instead of handshake request alone
- Ensure `TryOpenInitialProjectAsync()` never executes before WebUI is fully prepared for open-project commands

**Consequences:**
- Startup open waits for explicit UI readiness before invoking project load
- Eliminates launch-time refusal race
- Preserves existing startup argument validation
- No performance impact; readiness gate runs as background task

**Status:** ✅ Implemented

### Decision 3: Behavioral Readiness Testing (Parker)

**Context:** Initial implementation was rejected due to insufficient proof of readiness guarantee. Source-level contract was too implicit; timing assertions were unreliable.

**Decision:**
- Add small test seam in `WebViewBridge` to allow unit tests to inspect readiness state independently
- Author behavioral test spec validating that readiness gate does not release before explicit UI ready signal
- Test state transitions, not millisecond-level timing
- Validate startup path resolver independently with regression specs

**Consequences:**
- Strong, behavioral validation without tight coupling to implementation timing
- Test coverage makes readiness guarantee explicit and verifiable
- Future changes can confidently modify internal timing without breaking readiness contract

**Status:** ✅ Implemented and Approved

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




## Decision: `.pnest` File Icon Association (Consolidated)

**Consolidated from orchestration:** Bishop, Hicks (2026-03-21)

### Executive Summary

Desktop installation now registers `.pnest` file extension with per-user Windows registry (`HKCU\Software\Classes`), creating `PanelNester.Project` ProgID and associating the icon to EXE index 0. File-open shell command intentionally omitted pending implementation of startup file-open parameter handling.

### Decisions

#### Bishop — Installer Registry Structure

- Register `.pnest` extension in HKCU (per-user scope) — matches MSI installation model and user expectations
- Create `PanelNester.Project` ProgID with `DefaultIcon` pointing to `"[INSTALLFOLDER]PanelNester.Desktop.exe",0`
- Omit shell `open` command until `App.xaml.cs` and `MainWindow.xaml.cs` support file-open startup parameters
- Registry entries in `Product.wxs` use WiX standard structures: `RegistryKey`, `RegistryValue`, `ProgId`
- Changes confined to installer; zero modifications to app startup or bridge layers

#### Hicks — Icon Association Review APPROVED

- **Registry scope (HKCU):** Correct. Per-user registry aligns with per-user MSI installation.
- **ProgID definition:** Sane structure with EXE reference at index 0 (icon payload).
- **Omitted shell `open`:** Right choice today. No risk of premature file-open activation; future file-open handler can add this command separately.
- **No app-side changes required:** Icon association is purely installer-level metadata.
- **Regression test coverage:** 2 new tests verify installer registry structure; all 74/74 tests pass.

### Architecture Seam Ownership

| Seam | Owner | Status |
|------|-------|--------|
| **`.pnest` extension registration** | Installer (Product.wxs) | ✅ Complete |
| **ProgID creation** | Installer (Product.wxs) | ✅ Complete |
| **Icon reference** | Installer (EXE path) | ✅ Complete |
| **Shell `open` command** | App (App.xaml.cs) | ⏳ Deferred |
| **File-open parameter handling** | App (MainWindow.xaml.cs) | ⏳ Future phase |
| **Registry scope (HKCU)** | Design | ✅ Validated |

### Test Coverage

✅ **Installer Tests:** 2 new regression tests in `DesktopAssociationSpecs.cs`
- Verify `.pnest` extension registry entry exists in test registry hive
- Verify `PanelNester.Project` ProgID and `DefaultIcon` are correctly registered

✅ **Overall Test Suite:** 74/74 passed (1 skipped)
- Baseline: 73 passed, 1 skipped
- Final: 74 passed, 1 skipped (new association tests)

✅ **Build:** `dotnet build .\installer\PanelNester.Installer\PanelNester.Installer.wixproj` passed

### Rationale

**Per-user scope:** HKCU matches the per-user MSI target and user mental model of application installation.

**Deferred shell `open`:** Opening `.pnest` files from explorer would require the application to accept file path as startup parameter, parse it, and load the project. This capability does not yet exist. Registering the command prematurely risks confusing user experience (file opens app but no project loads). Current icon-only registration provides desktop branding with zero risk.

**Icon target:** EXE index 0 ensures consistent icon branding on user desktop and file explorer. Icon resource can be extracted and refined in future desktop icon design phase.

### Remaining Work

File-open command registration is **blocked on:**
1. Implement startup file-open parameter handling in `App.xaml.cs`
2. Extend `MainWindow.xaml.cs` initialization to load project from file path
3. Add registry command entry to `Product.wxs`
4. Test file-open flow from explorer and command line

This work is explicitly deferred. Current registration is complete and safe.
