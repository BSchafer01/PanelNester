# Bishop History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own desktop host integration, local persistence wiring, and export plumbing.
- 2026-03-14: Phase 0/1 host scaffolding works best when the desktop shell prefers a future `src\PanelNester.WebUI\dist` build but still ships a bundled placeholder page and a `window.hostBridge.receive(...)` receiver shim so the bridge stays stable before the real UI lands.
- 2026-03-14: If the desktop output bundles `WebApp`, content resolution must search every ancestor for `src\PanelNester.WebUI\dist` before accepting a placeholder; otherwise running from `bin\Debug\net10.0-windows` masks a valid real UI build.

## Recent Work (2026-03-14)

- ✓ Fixed Web UI content resolver to prioritize built `dist` folder over bundled placeholder
- ✓ Added focused resolver order tests
- ✓ Validated `dotnet test` and `npm run build` pass with no regressions
- ✓ Desktop app now correctly loads Phase 0/1 vertical-slice Web UI when available
