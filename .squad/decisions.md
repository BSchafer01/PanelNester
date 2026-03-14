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

## Phase 0 Decisions

### Decision: Phase 0 Host Uses Bundled Fallback and Receiver Shim

**Author:** Bishop  
**Date:** 2026-03-14  
**Status:** Active

#### Context

Phase 0 and Phase 1 need the WPF shell to boot immediately, even if Dallas has not produced a Web UI bundle yet. At the same time, the host-to-web contract should match Ripley's review and avoid leaking WebView2-specific listener details into every page.

#### Decision

The desktop host will:

- live in `src\PanelNester.Desktop\` and be the project referenced by `PanelNester.slnx`
- keep typed bridge envelopes and dispatch registration under `src\PanelNester.Desktop\Bridge\`
- resolve `src\PanelNester.WebUI\dist` first when it exists, but fall back to a bundled local `WebApp\index.html`
- inject a WebView2 document-created shim that forwards host `postMessage` traffic into `window.hostBridge.receive(...)` whenever the Web UI exposes that seam
- register explicit placeholder handlers for file-dialog, import, and nesting requests instead of guessing at Parker's implementation

#### Consequences

- Dallas can target `window.hostBridge.receive(...)` without depending on raw WebView2 event wiring
- The desktop shell stays runnable and demonstrable before the real Web UI build exists
- Native file dialogs and Parker's import/nesting services still have clear host seams, but their logic remains unimplemented until the owning workstream lands

---

### Decision: Phase 0/1 UI Scaffold Stays Contract-First

**Author:** Dallas  
**Date:** 2026-03-14  
**Status:** Active

#### Context

The Web UI needs to start in parallel with the desktop host and backend workstreams. We need a usable shell now without inventing temporary state shapes that will get ripped out once Bishop and Parker land their pieces.

#### Decision

Phase 0/1 Web UI uses a small React + TypeScript + Vite scaffold with:

- one explicit app-shell state for route + bridge snapshot
- stable DTOs in `src/PanelNester.WebUI/src/types/contracts.ts`
- a `window.hostBridge.receive(...)` seam plus JSON envelope handling in `src/PanelNester.WebUI/src/bridge/hostBridge.ts`
- read-only Import and Results pages that already match the future import/nesting payload shapes
- a viewer placeholder instead of early Three.js work

#### Consequences

- Bishop can attach the WPF shell to a stable handshake and message seam without UI refactoring
- Parker can target the same import/nesting contract names while the UI remains mostly placeholder-first
- We defer editable import grids, real results rendering, and viewer behavior until data is flowing end-to-end

---

### Decision: Phase 0/1 Test Contract Notes

**Author:** Hicks  
**Date:** 2026-03-14  
**Status:** Active

#### Context

Test requirements were identified before the bridge, import service, and nesting service exist. Key contracts locked early ensure later automation is not mushy.

#### Decisions & Requests

1. **CSV header order should not matter.** Exact header names still matter, but the importer should accept `Id/Length/Width/Quantity/Material` in any order. That gives us one less brittle file-format failure without relaxing schema matching.

2. **Bridge failures need a stable response contract.** For any handled bridge request, the response should preserve the original `requestId` and surface failures through a structured payload (`success`, machine-readable code, human-readable message). Without this, timeout, unknown-message, and deserialization tests cannot assert anything stable.

3. **Import needs a non-file-system seam.** Keep `filePath` for the UI contract, but the service layer should also accept a `Stream` or `TextReader`. That keeps Hicks' import tests fast and deterministic instead of leaning on temp files for every case.

4. **Nesting needs stable failure codes and a numeric tolerance.** Phase 1 should reserve at least `outside-usable-sheet`, `no-layout-space`, `invalid-input`, and `empty-run`, with fit comparisons honoring a 0.0001" tolerance. Otherwise the edge-case tests around oversize parts and near-boundary fits will be too vague to gate on.

#### Why This Matters

- These choices make the Phase 0/1 tests assertive instead of descriptive
- They reduce rework once Bishop and Parker land the real seams
- They give Dallas stable UI states to render for errors, warnings, and unplaced items

---

### Decision: Phase 1 Domain Contracts

**Author:** Parker  
**Date:** 2026-03-14  
**Status:** Implemented

#### Context

Phase 1 needs a stable backend seam before material CRUD, persistence, and the viewer are fully in place. The import and nesting path also has to stay deterministic enough for Hicks' future tests and Dallas/Bishop's bridge work.

#### Decision

- Put the first pure request/response/data contracts in `src\PanelNester.Domain\Models` and the service interfaces in `src\PanelNester.Domain\Contracts`
- Keep geometry, spacing, margins, kerf, coordinates, and utilization inputs in `decimal` to avoid avoidable floating-point drift in fit checks
- Keep `PartRow.Quantity` compact through import; expand to instance-level `ExpandedPart` items only inside `ShelfNestingService`
- Use one hardcoded `Demo Material` in Phase 1 as the default/known material path until Phase 2 material CRUD exists
- Treat kerf as added placement clearance on top of material spacing, not as a reduction of rendered part dimensions

#### Consequences

- Bridge/UI work can stabilize against the Phase 1 shapes without pulling in WPF concerns
- Import validation remains row-oriented and easier to present/edit later
- Nesting remains deterministic: same rows + same material + same kerf produce the same expansion order and placements
- Material mismatch stays explicit in validation/unplaced outputs until the real material library arrives

---

### Design Review: Phase 0 + Phase 1 Kickoff

**Author:** Ripley  
**Date:** 2026-03-14  
**Status:** Active  
**Scope:** Foundation scaffold + vertical slice handoff

See `.squad/decisions/inbox/ripley-phase0-1-design-review.md` (comprehensive architectural guidance on solution structure, seams, contracts, risks, and success criteria).

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
