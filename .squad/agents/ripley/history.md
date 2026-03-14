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
- 2026-03-14: Completed Phase 0+1 Design Review. Defined solution structure (Desktop/Domain/Services/WebUI projects), seam contracts (JSON-over-postMessage bridge pattern), agent work splits (Bishop→bridge, Dallas→React UI, Parker→domain/services, Hicks→tests), and 8 stable DTOs. Key architectural decisions: shelf heuristic nesting, domain isolation, kerf-as-spacing, quantity expansion at nest time. Wrote handoff to `.squad/decisions/inbox/ripley-phase0-1-design-review.md`.
- 2026-03-14: Hicks' rejection boiled down to drift, not missing ambition. The stable repair was to collapse Phase 0/1 onto one vocabulary (`bridge-handshake`, `open-file-dialog`, `import-csv`, `run-nesting`), one demo material contract, and one bounded set of nesting failure codes across WPF, services, React, and tests.
- 2026-03-14: A believable vertical slice required turning placeholder host seams live before polishing UI. Native file-open, CSV import, and shelf nesting now run through the desktop host, and the tests only became reviewer-usable after the skipped contract specs were converted into executable checks.
- 2026-03-14: Phase 0/1 revision complete. Unified bridge vocabulary across C# and TypeScript, established demo material contract (96"×48" sheet, 0.125" spacing, 0.5" margin, 0.0625" kerf), fixed nesting failure codes (outside-usable-sheet, no-layout-space, invalid-input, empty-run). Validated with `dotnet test` and Web UI build. Decision merged by Scribe into team consensus.
