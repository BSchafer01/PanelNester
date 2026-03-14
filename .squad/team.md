# Squad Team

> Desktop WPF sheet-nesting app with a WebView2 UI, local spreadsheet import, heuristic rectangular nesting, interactive sheet visualization, and PDF export.

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Ripley | Lead | `.squad/agents/ripley/charter.md` | ✅ Active |
| Dallas | Frontend Dev | `.squad/agents/dallas/charter.md` | ✅ Active |
| Bishop | Platform Dev | `.squad/agents/bishop/charter.md` | ✅ Active |
| Parker | Backend Dev | `.squad/agents/parker/charter.md` | ✅ Active |
| Hicks | Tester | `.squad/agents/hicks/charter.md` | ✅ Active |
| Scribe | Session Logger | `.squad/agents/scribe/charter.md` | 📋 Silent |
| Ralph | Work Monitor | — | 🔄 Monitor |

## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage additions and flaky-test cleanup
- Small WPF or web UI fixes following existing patterns
- Import validation follow-ups with clear acceptance criteria
- Boilerplate, scaffolding, and documentation updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear PRD slices and acceptance criteria
- Refactors with solid test coverage
- Focused reporting or persistence work that follows established seams

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Nesting algorithm strategy or performance-critical packing work
- WPF/WebView2 bridge design and cross-layer contracts
- Ambiguous requirements needing clarification
- Security-critical changes

## Project Context

- **Owner:** Brandon Schafer
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.
- **Created:** 2026-03-14
