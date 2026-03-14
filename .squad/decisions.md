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

### Decision: Hicks Phase 0/1 Batch Review

**Author:** Hicks  
**Date:** 2026-03-14  
**Status:** Rejected for next-step integration

#### Context

Initial Phase 0/1 batch was not reproducibly verifiable due to bridge vocabulary mismatch, missing live integration, and nesting failure code drift.

#### Verdict & Rationale

1. **Solution targets .NET 10 but verification stops at restore/build** — no .NET tests can run yet
2. **Bridge vocabulary mismatch:** Desktop host exposes `host.ready`, `dialog.openFile`, `import.csv` while Web UI initializes with `bridge-handshake` and `open-file-dialog`
3. **Vertical slice remained stubbed:** File-open, CSV import, and nesting are `not-ready`; React shell renders placeholder data instead of live invocations
4. **Nesting failure codes drifted:** Service reports `part-too-large` and `no-space` instead of agreed `outside-usable-sheet`, `no-layout-space`, `invalid-input`, `empty-run`

#### Team Impact

- Do not call this batch an integrated Phase 0/1 slice
- Close bridge contract drift before more UI or viewer work lands
- Land one real import → nest → display round-trip before expanding scope

---

### Decision: Ripley Phase 0/1 Revision

**Author:** Ripley  
**Date:** 2026-03-14  
**Status:** Implemented

#### Context

Hicks rejected the initial batch because the desktop host, Web UI, and services still behaved like parallel placeholders instead of one integrated slice. Contract drift was the main failure mode.

#### Decision

Close Phase 0 and Phase 1 on one live vertical slice with unified vocabulary and failure codes:

1. **One bridge vocabulary** across C# and TypeScript:
   - `bridge-handshake`
   - `open-file-dialog`
   - `import-csv`
   - `run-nesting`
   - `*-response` for correlated replies

2. **One demo material contract**:
   - `Demo Material`
   - `96" x 48"` sheet
   - `0.125"` default spacing
   - `0.5"` default edge margin
   - `0.0625"` demo kerf in the Web UI flow

3. **One bounded nesting failure vocabulary**:
   - `outside-usable-sheet`
   - `no-layout-space`
   - `invalid-input`
   - `empty-run`

4. **No placeholder-only host seams** — desktop host must actually open file dialog, invoke CSV import, invoke nesting, and return live payloads.

#### Consequences

- Bridge layer favors domain import/nesting contracts directly, reducing DTO drift
- Empty runs treated as run-level failures; invalid rows collapse to `invalid-input`; oversize/heuristic misses stay distinct
- Phase 0/1 review gate focuses on integrated slice and its tests, not deferred viewer/reporting

---

### Decision: Hicks Phase 0/1 Re-review

**Author:** Hicks  
**Date:** 2026-03-14  
**Status:** Approved

#### Context

Second review of Phase 0/1 vertical slice after contract unification. Verifies the slice is production-ready for manual smoke testing and Phase 2 commencement.

#### Decision

Approve Phase 0/1 for next implementation step.

#### Rationale

1. **Contract drift is closed.** Desktop bridge types, Web UI bridge types, and desktop tests use unified vocabulary: `bridge-handshake`, `open-file-dialog`, `import-csv`, `run-nesting`.
2. **Vertical slice is live.** Desktop host wires native file dialog, `CsvImportService`, and `ShelfNestingService`; React shell consumes live import/nesting payloads instead of placeholders.
3. **Gating contracts are stable.** `Demo Material`, demo kerf, and bounded nesting failure codes align across domain, services, Web UI, and tests.
4. **Checks are repeatable.** `dotnet build`, `dotnet test`, and Web UI build all pass; desktop round-trip exercises file-open → import → nesting on real services.

#### Consequences

- Phase 0/1 acts as a regression gate before scope widens to Phase 2 material library
- Manual smoke-test guide (`smoke-test-guide.md`) confirms readiness across CSV import, nesting, error handling, and results display
- No rework expected before Phase 2 begins

---

### Decision: Desktop Web UI Content Resolution

**Author:** Bishop  
**Date:** 2026-03-14  
**Status:** Implemented

#### Context

The desktop app was resolving `WebApp\index.html` placeholder even when a real Phase 0/1 Web UI build existed at `src\PanelNester.WebUI\dist\index.html`. The resolver was checking the app's bin directory before searching ancestor roots.

#### Decision

When the desktop app resolves web content, it must search all ancestor roots for `src\PanelNester.WebUI\dist\index.html` before accepting any bundled placeholder page from `WebApp\index.html`.

#### Rationale

Running the desktop project from `src\PanelNester.Desktop\bin\Debug\net10.0-windows` places a copied `WebApp\index.html` directly under the app base directory. If resolution checks placeholder content while still walking upward, the copied fallback wins too early and hides a valid repo-root Web UI build.

#### Consequences

- Desktop debug runs now prefer the real Phase 0/1 Web UI whenever the built `dist` folder exists.
- The bundled placeholder remains a legitimate fallback when no Web UI build is available.

---

### Decision: Hicks Phase 0/1 Smoke-Test Guide

**Author:** Hicks  
**Date:** 2026-03-14  
**Status:** Complete

#### Context

Team needs a practical, runnable smoke-test checklist for verifying Phase 0/1 vertical slice without requiring full UI automation, Three.js viewer, or PDF export tooling.

#### Decision

Produce `.squad/smoke-test-guide.md` with:

1. **Preflight checklist** — `dotnet restore`, `dotnet build`, `dotnet test` (expects 36 passed, 1 skipped WebView2 integration test)
2. **Happy-path scenario** — Three-row CSV with valid parts → import succeeds → nesting places all 7 instances on 1–2 sheets → results display utilization
3. **Four failure-mode test cases:**
   - Oversized part (100"×50" → `outside-usable-sheet` error)
   - Unknown material → `material-not-found` error
   - Invalid numeric text → `invalid-numeric` error
   - Zero quantity → `non-positive-quantity` error
4. **Expected outcomes** for each step (pass vs. fail conditions, no crashes/hangs)
5. **Demo Material reference** (96"×48" sheet, 0.5" edge margin, 0.125" spacing, 0.0625" kerf)
6. **Acceptance criteria** — 9-item checklist covering CSV validation, nesting placement, error codes, and UI rendering

#### Rationale

- **Concrete examples:** Copy-paste CSV rows with no guessing at format
- **Error code specificity:** Verifies error messages match agreed vocabulary, not generic "error"
- **Failure modes as first-class tests:** Proves oversized parts, bad materials, and invalid input do not crash or hang
- **Bridge/import/nesting coverage:** Exercises SC1–SC5 from test matrix without full UI automation

#### Consequences

- Manual smoke test gates Phase 0/1 completeness
- No new tooling required; all scenarios use existing service layer APIs
- Failure mode tells you which seam needs investigation (CSV import, nesting, results display, error handling)
- When all nine acceptance criteria pass, Phase 0/1 is ready for Phase 2 material library work

---

### Decision: VS Code Dark Theme for Web UI

**Author:** Dallas  
**Date:** 2026-03-14  
**Status:** Implemented

#### Context

Before starting Phase 2 material CRUD, Brandon requested the Web UI be restyled to match VS Code's dark theme for a more professional, IDE-like feel.

#### Decision

Restyle the Phase 0/1 Web UI to adopt VS Code dark theme conventions without introducing a theme framework or changing Phase 0/1 behavior.

#### Changes Made

1. **Color palette replaced with VS Code defaults:**
   - Editor background: `#1e1e1e`
   - Activity bar: `#181818`
   - Sidebar: `#252526`
   - Borders: `#2d2d30`
   - Text: `#cccccc` (primary), `#9da0a6` (muted)
   - Accent: `#007acc`

2. **Structural styling flattened:**
   - Removed backdrop-filter blur and radial gradients
   - Reduced border-radius from 14–20px to 0–4px
   - Removed decorative shadows
   - Panels separated by 1px borders instead of gaps

3. **Navigation simplified to VS Code-like activity bar:**
   - 48px vertical rail with abbreviated labels (OVR/IMP/RES)
   - Active state shown by left accent border
   - Sidebar moved to right with bridge status and activity log

4. **Typography scaled to 13px base with monospace for data:**
   - Tables, tokens, and timestamps use Consolas/monospace
   - Headers are compact (13–14px) with muted eyebrow labels

#### Rationale

- VS Code's visual language is familiar to developers and operators
- Flat surfaces and restrained colors reduce cognitive load
- No external theme library needed—CSS custom properties suffice
- Build passes; no behavior changes required

#### Consequences

- Future UI additions should follow the same palette (use `var(--vsc-*)` custom properties)
- Icon-based navigation may need adjustment if more routes are added
- Sidebar collapses on narrower viewports; consider a toggle in Phase 6 polish

---

### Decision: Hicks VS Code Theme Review & Approval

**Author:** Hicks  
**Date:** 2026-03-14  
**Status:** Approved

#### Context

Dallas completed the VS Code dark-theme styling refresh for Phase 0/1 before Phase 2 commencement. Hicks performed independent regression and reviewer gate to validate dark-theme alignment, workflow preservation, and build stability.

#### Verdict

Approved the VS Code-inspired UI cleanup. The palette and structural cues align with the reference image (`UIColorPalette.png`) for the stated goal: dark editor surface, darker activity/sidebar rails, muted body text, bright headings, and VS Code blue accent/button treatment. The left activity bar, editor-like panels, and right utility sidebar preserve the overall Visual Studio Code feel without disturbing the Phase 0/1 slice's workflow seams.

#### Verification

1. **Theme review**
   - CSS tokens match the expected VS Code dark family: `#1e1e1e` editor background, `#252526` sidebar, `#181818` activity rail, `#007acc` accent, `Segoe UI` at compact desktop sizing
   - Reference image sampling showed dominant near-black neutrals, which is consistent with the updated stylesheet
   - Component/page updates remain presentational: no bridge contract, import payload, or nesting payload shapes were changed

2. **Regression gate**
   - Web UI build passed via `npm run build`
   - Solution build passed via `dotnet build .\PanelNester.slnx`
   - Solution tests passed via `dotnet test .\PanelNester.slnx --no-build` with **38 passed, 1 skipped**

3. **Workflow preservation**
   - Import page still exposes CSV validation, warnings, and errors required by the smoke guide
   - Results page still renders sheet counts, utilization, placements, and unplaced items
   - App shell/navigation changes are cosmetic and do not alter route wiring

#### Rationale

- Static verification and regression checks are strong; dark-theme styling successfully applied
- Phase 0/1 regression gate now includes dark-theme styling checkpoint
- No rework required before Phase 2 material library work

#### Consequences

- Phase 0/1 is approved for next phase commencement
- Future UI additions should follow VS Code palette conventions
- Residual docs note: `.squad/smoke-test-guide.md` test count is stale (expects 36 passed, actual 38 passed) — requires docs-only follow-up

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
