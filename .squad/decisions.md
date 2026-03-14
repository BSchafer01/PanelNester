# Squad Decisions

## Active Decisions

### Decision: Implementation Phases for PanelNester

**Author:** Ripley  
**Date:** 2026-03-14  
**Status:** Proposed

#### Context

Breaking the PRD into sequenced implementation phases. The goal is de-risking integration early while enabling parallel workstreams where architecture allows.

#### Decision

Six phases with a vertical slice in Phase 1 proving the WPF→WebView2→.NET round-trip before committing to full domain complexity.

**Phase 0 — Foundation (Scaffold):** Stand up WPF shell, WebView2 integration, and message bridge. Validate bidirectional communication. No business logic yet.

**Phase 1 — Vertical Slice (Import → Nest → View):** Minimal end-to-end: CSV import of parts, single-material nesting, basic results display. Hardcoded material. Proves data flows across all layers.

**Phase 2 — Material Library:** CRUD for materials, local persistence, material selection in projects.

**Phase 3 — Project Management & Persistence:** Create/save/open projects, metadata editing, SQLite or JSON persistence, snapshot materials into projects.

**Phase 4 — Full Import Pipeline:** XLSX support, validation with warnings/errors, inline editing, quantity expansion.

**Phase 5 — Results Viewer & PDF Reporting:** Three.js-based interactive viewer, PDF export with QuestPDF, editable report fields.

**Phase 6 — Polish & Edge Cases:** Error handling, large quantity performance, unplaced item reporting UX, file dialog integration, final testing.

#### Rationale

- Phase 0 de-risks the WPF/WebView2 seam—the riskiest integration surface.
- Phase 1 proves domain round-trip before adding complexity.
- Materials before projects because projects reference materials.
- Import before reporting because reporting depends on nesting results.
- Viewer and PDF can be built in parallel once Phase 1 proves data shape.

#### Consequences

- Early phases deliberately limited in scope to avoid rework.
- Some UI placeholders expected through Phase 3.
- PDF and viewer are deferred but not blocked—they can begin once data contracts stabilize in Phase 1.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
