# Ripley History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Core Context

I own architecture, scope control, and reviewer gating for the full product.

## Learnings

- 2026-03-14: Initial team staffing. I own architecture, scope control, and reviewer gating for the full product.
- 2026-03-14: Created 6-phase implementation plan. Key architecture insight: Phase 0 must prove WPF↔WebView2 message bridge before any domain work. Vertical slice (Phase 1) validates CSV→Nest→View data flow with minimal scope. Materials before projects (materials are referenced); import before reporting (reporting needs results). Viewer and PDF can parallelize once data contracts stabilize.
- 2026-03-14: Decision recorded in team decisions. Scribe merged inbox. Orchestration logged.
