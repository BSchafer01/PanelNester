---
name: "tabbed-resizable-workspace"
description: "Use a tabbed detail pane plus resizable live-view pane to reduce repeated UI context"
domain: "frontend-layout"
confidence: "high"
source: "dallas-implementation"
---

## Context

Operator-focused desktop web UIs often need both editable/detail-heavy controls and a live visual surface at the same time. Deep content inspection requires scrolling without losing chrome or tab context.

## Pattern

1. Keep summary/status cards above the workspace.
2. Move secondary detail sections into a single tabbed left pane with an explicit, stable tab order.
3. Keep the live viewer or canvas pinned in a right pane.
4. Add a lightweight in-page splitter with local state and min-width guards for both panes.
5. **Make tabs sticky** — tabs stay visible at top of workspace while content scrolls independently.
6. **Use grid layout** — header (auto), tabs (auto, sticky), panel (1fr, overflow-y: auto).
7. **Span viewer full height** — viewer column uses grid-row: 1 / -1 to span entire split height.
8. Collapse to a single-column stack below a responsive breakpoint.

## Why

- Removes repeated headings and "echo" panels that make production UI feel like validation scaffolding.
- Preserves focus: operators can change tabs without losing the live visual context.
- Sticky tabs prevent navigation loss during deep content inspection.
- Independent scroll keeps workspace exploration separate from viewer interaction.
- Keeps layout adaptable for large desktop windows without introducing saved preferences or heavy state.

## Implementation Details

### CSS Structure
Results workspace uses 3-row grid: header (auto), sticky tabs (auto), scrolling panel (1fr).
Viewer column spans full height with grid-row: 1 / -1 and contains header + viewer.

### Combobox Selectors
For space efficiency in multi-item scenarios, replace card grids with select comboboxes:
- Material selector: MaterialName — N sheet(s) · M placed
- Sheet selector: Sheet #N — X.X% utilized

Saves vertical space, maintains selection clarity, reduces visual noise.

### Table Scroll Limits
Add max-height: 400px to .table-shell for contained scrolling. Large tables scroll inside workspace instead of pushing content down.

### Shared Selection State
When adding a new review tab (search/finder, grouped review, sheet audit), do not create a second viewer-selection model. Route picks back through the page's existing material/sheet/placement state so the live viewer, sheet table, and placement table stay synchronized.

For PanelNester's Results page, that means new workspace affordances should update the same `activeMaterialKey`, `activeSheetId`, and `selectedPlacementId` flow already used by sheet detail, placement inspection, and group review.

### Searchable Batch Review
When operators need batch-wide lookup, keep the batch-review tab to two synchronized surfaces:
- a search-results table for exact panel hits
- one flat, scroll-contained all-sheets table for exhaustive coverage

Build a memoized placement index once from `materialResults -> sheets + placements`, then filter it with `useDeferredValue` so search stays responsive on large runs without changing contracts or rescanning import rows. Panel-ID search should highlight matching sheets in the all-sheets table, then let a click reuse the shared selection state to move the pinned viewer immediately. If a grouped card/list repeats the same sheet inventory already present in the table, remove it—the duplicate surface costs vertical space and rerender time without improving review confidence.

When the searched value is a structured operator ID (panel ID, part ID, ticket number), normalize both the indexed value and the query first—lowercase, strip separators—then require the normalized query to appear as one contiguous substring. That preserves forgiving input (`panel-04-013`, `04013`) without sliding into loose ordered-character matches that erode review trust.

Treat the filtered search result as a **single shared data object**. The summary count, rendered result rows, and sheet-highlight counts should all read from the same memoized search-results structure so the UI cannot report "7 matches across 3 sheets" while a broader row set or scrollbar survives from a different source.

## Example

- src\PanelNester.WebUI\src\pages\ResultsPage.tsx
- src\PanelNester.WebUI\src\styles.css

## Anti-Pattern

- Rendering report fields, summary tables, sheet details, placement tables, unplaced reasons, and viewer as a long vertical stack that forces constant scrolling and repeats context below the active visual surface.
- Using card grids for material/sheet selection when comboboxes are more space-efficient.
- Letting large tables grow unbounded without scroll containers.
- Adding a search or finder tab that keeps its own independent "active result" state and leaves the viewer pointed somewhere else.
- Rendering a second grouped sheet card inventory beside an authoritative all-sheets table when both surfaces answer the same review question.
