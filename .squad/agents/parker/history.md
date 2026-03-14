# Parker History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own import, validation, domain services, and the nesting engine.
- 2026-03-14: Phase 0/1 backend contracts stay cleaner if import keeps quantity on compact `PartRow` records, nesting expands instances just-in-time, and all sheet geometry stays in `decimal` with kerf treated as added spacing instead of part resizing.
