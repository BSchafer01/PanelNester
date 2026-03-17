
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


