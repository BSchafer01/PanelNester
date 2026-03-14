# Dallas History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own web UI flows, viewer behavior, and operator-facing clarity.
- 2026-03-14: Phase 0/1 UI scaffold works best as a contract-first shell—typed bridge DTOs, a `window.hostBridge.receive(...)` seam, and read-only pages keep Bishop/Parker unblocked without fake product behavior.
- 2026-03-14: Completed web UI scaffold. Hicks deployed test strategy; Ripley driving cross-agent design review. Ready for Phase 1 integration.
- 2026-03-14: Restyled Web UI to VS Code dark theme. Key changes: replaced glassy card effects with flat surfaces, switched to VS Code-aligned color palette (#1e1e1e editor, #181818 activity bar, #252526 sidebar, #007acc accent), reduced border radii to 0–4px, removed backdrop-filter and decorative shadows, switched nav from text buttons to icon-like abbreviations (OVR/IMP/RES) in a 48px activity bar. Build passes cleanly.
