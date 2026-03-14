# Bishop History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own desktop host integration, local persistence wiring, and export plumbing.
- 2026-03-14: Phase 0/1 host scaffolding works best when the desktop shell prefers a future `src\PanelNester.WebUI\dist` build but still ships a bundled placeholder page and a `window.hostBridge.receive(...)` receiver shim so the bridge stays stable before the real UI lands.
