---
name: "tabbed-resizable-workspace"
description: "Use a tabbed detail pane plus resizable live-view pane to reduce repeated UI context"
domain: "frontend-layout"
confidence: "medium"
source: "dallas-implementation"
---

## Context

Operator-focused desktop web UIs often need both editable/detail-heavy controls and a live visual surface at the same time.

## Pattern

1. Keep summary/status cards above the workspace.
2. Move secondary detail sections into a single tabbed left pane with an explicit, stable tab order.
3. Keep the live viewer or canvas pinned in a right pane.
4. Add a lightweight in-page splitter with local state and min-width guards for both panes.
5. Collapse to a single-column stack below a responsive breakpoint.

## Why

- Removes repeated headings and “echo” panels that make production UI feel like validation scaffolding.
- Preserves focus: operators can change tabs without losing the live visual context.
- Keeps layout adaptable for large desktop windows without introducing saved preferences or heavy state.

## Example

- `src\PanelNester.WebUI\src\pages\ResultsPage.tsx`
- `src\PanelNester.WebUI\src\styles.css`

## Anti-Pattern

- Rendering report fields, summary tables, sheet details, placement tables, unplaced reasons, and viewer as a long vertical stack that forces constant scrolling and repeats context below the active visual surface.
