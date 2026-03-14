# Squad Decisions## Active Decisions### Decision: Implementation Phases for PanelNester**Author:** Ripley  
**Date:** 2026-03-14  
**Status:** Proposed#### ContextBreaking the PRD into sequenced implementation phases. The goal is de-risking integration early while enabling parallel workstreams where architecture allows.#### DecisionSix phases with a vertical slice in Phase 1 proving the WPF→WebView2→.NET round-trip before committing to full domain complexity.**Phase 0 — Foundation (Scaffold):** Stand up WPF shell, WebView2 integration, and message bridge. Validate bidirectional communication. No business logic yet.**Phase 1 — Vertical Slice (Import → Nest → View):** Minimal end-to-end: CSV import of parts, single-material nesting, basic results display. Hardcoded material. Proves data flows across all layers.**Phase 2 — Material Library:** CRUD for materials, local persistence, material selection in projects.**Phase 3 — Project Management & Persistence:** Create/save/open projects, metadata editing, SQLite or JSON persistence, snapshot materials into projects.**Phase 4 — Full Import Pipeline:** XLSX support, validation with warnings/errors, inline editing, quantity expansion.**Phase 5 — Results Viewer & PDF Reporting:** Three.js-based interactive viewer, PDF export with QuestPDF, editable report fields.**Phase 6 — Polish & Edge Cases:** Error handling, large quantity performance, unplaced item reporting UX, file dialog integration, final testing.#### Rationale- Phase 0 de-risks the WPF/WebView2 seam—the riskiest integration surface.
- Phase 1 proves domain round-trip before adding complexity.
- Materials before projects because projects reference materials.
- Import before reporting because reporting depends on nesting results.
- Viewer and PDF can be built in parallel once Phase 1 proves data shape.#### Consequences- Early phases deliberately limited in scope to avoid rework.
- Some UI placeholders expected through Phase 3.
- PDF and viewer are deferred but not blocked—they can begin once data contracts stabilize in Phase 1.## Phase 0 Decisions### Decision: Phase 0 Host Uses Bundled Fallback and Receiver Shim**Author:** Bishop  
**Date:** 2026-03-14  
**Status:** Active#### ContextPhase 0 and Phase 1 need the WPF shell to boot immediately, even if Dallas has not produced a Web UI bundle yet. At the same time, the host-to-web contract should match Ripley's review and avoid leaking WebView2-specific listener details into every page.#### DecisionThe desktop host will:

- live in `src\PanelNester.Desktop\` and be the project referenced by `PanelNester.slnx`
- keep typed bridge envelopes and dispatch registration under `src\PanelNester.Desktop\Bridge\`
- resolve `src\PanelNester.WebUI\dist` first when it exists, but fall back to a bundled local `WebApp\index.html`       
- inject a WebView2 document-created shim that forwards host `postMessage` traffic into `window.hostBridge.receive(...)`
` whenever the Web UI exposes that seam
- register explicit placeholder handlers for file-dialog, import, and nesting requests instead of guessing at Parker's i
implementation

#### Consequences

- Dallas can target `window.hostBridge.receive(...)` without depending on raw WebView2 event wiring
- The desktop shell stays runnable and demonstrable before the real Web UI build exists
- Native file dialogs and Parker's import/nesting services still have clear host seams, but their logic remains unimplem
mented until the owning workstream lands

---

### Decision: Phase 0/1 UI Scaffold Stays Contract-First

**Author:** Dallas
**Date:** 2026-03-14
**Status:** Active

#### Context

The Web UI needs to start in parallel with the desktop host and backend workstreams. We need a usable shell now without 
 inventing temporary state shapes that will get ripped out once Bishop and Parker land their pieces.

#### Decision

Phase 0/1 Web UI uses a small React + TypeScript + Vite scaffold with:

- one explicit app-shell state for route + bridge snapshot
- stable DTOs in `src/PanelNester.WebUI/src/types/contracts.ts`
- a `window.hostBridge.receive(...)` seam plus JSON envelope handling in `src/PanelNester.WebUI/src/bridge/hostBridge.ts
s`
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

Test requirements were identified before the bridge, import service, and nesting service exist. Key contracts locked ear
rly ensure later automation is not mushy.

#### Decisions & Requests

1. **CSV header order should not matter.** Exact header names still matter, but the importer should accept `Id/Length/Wi
idth/Quantity/Material` in any order. That gives us one less brittle file-format failure without relaxing schema matching
g.

2. **Bridge failures need a stable response contract.** For any handled bridge request, the response should preserve the
e original `requestId` and surface failures through a structured payload (`success`, machine-readable code, human-readabl
le message). Without this, timeout, unknown-message, and deserialization tests cannot assert anything stable.

3. **Import needs a non-file-system seam.** Keep `filePath` for the UI contract, but the service layer should also accep
pt a `Stream` or `TextReader`. That keeps Hicks' import tests fast and deterministic instead of leaning on temp files for
r every case.

4. **Nesting needs stable failure codes and a numeric tolerance.** Phase 1 should reserve at least `outside-usable-sheet
t`, `no-layout-space`, `invalid-input`, and `empty-run`, with fit comparisons honoring a 0.0001" tolerance. Otherwise the
e edge-case tests around oversize parts and near-boundary fits will be too vague to gate on.

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

Phase 1 needs a stable backend seam before material CRUD, persistence, and the viewer are fully in place. The import and
d nesting path also has to stay deterministic enough for Hicks' future tests and Dallas/Bishop's bridge work.

#### Decision

- Put the first pure request/response/data contracts in `src\PanelNester.Domain\Models` and the service interfaces in `s
src\PanelNester.Domain\Contracts`
- Keep geometry, spacing, margins, kerf, coordinates, and utilization inputs in `decimal` to avoid avoidable floating-po
oint drift in fit checks
- Keep `PartRow.Quantity` compact through import; expand to instance-level `ExpandedPart` items only inside `ShelfNestin
ngService`
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

Initial Phase 0/1 batch was not reproducibly verifiable due to bridge vocabulary mismatch, missing live integration, and
d nesting failure code drift.

#### Verdict & Rationale

1. **Solution targets .NET 10 but verification stops at restore/build** — no .NET tests can run yet
2. **Bridge vocabulary mismatch:** Desktop host exposes `host.ready`, `dialog.openFile`, `import.csv` while Web UI initi
ializes with `bridge-handshake` and `open-file-dialog`
3. **Vertical slice remained stubbed:** File-open, CSV import, and nesting are `not-ready`; React shell renders placehol
lder data instead of live invocations
4. **Nesting failure codes drifted:** Service reports `part-too-large` and `no-space` instead of agreed `outside-usable-
-sheet`, `no-layout-space`, `invalid-input`, `empty-run`

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

Hicks rejected the initial batch because the desktop host, Web UI, and services still behaved like parallel placeholders
s instead of one integrated slice. Contract drift was the main failure mode.

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

4. **No placeholder-only host seams** — desktop host must actually open file dialog, invoke CSV import, invoke nesting, 
 and return live payloads.

#### Consequences

- Bridge layer favors domain import/nesting contracts directly, reducing DTO drift
- Empty runs treated as run-level failures; invalid rows collapse to `invalid-input`; oversize/heuristic misses stay dis
stinct
- Phase 0/1 review gate focuses on integrated slice and its tests, not deferred viewer/reporting

---

### Decision: Hicks Phase 0/1 Re-review

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Approved

#### Context

Second review of Phase 0/1 vertical slice after contract unification. Verifies the slice is production-ready for manual 
 smoke testing and Phase 2 commencement.

#### Decision

Approve Phase 0/1 for next implementation step.

#### Rationale

1. **Contract drift is closed.** Desktop bridge types, Web UI bridge types, and desktop tests use unified vocabulary: `b
bridge-handshake`, `open-file-dialog`, `import-csv`, `run-nesting`.
2. **Vertical slice is live.** Desktop host wires native file dialog, `CsvImportService`, and `ShelfNestingService`; Rea
act shell consumes live import/nesting payloads instead of placeholders.
3. **Gating contracts are stable.** `Demo Material`, demo kerf, and bounded nesting failure codes align across domain, s
services, Web UI, and tests.
4. **Checks are repeatable.** `dotnet build`, `dotnet test`, and Web UI build all pass; desktop round-trip exercises fil
le-open → import → nesting on real services.

#### Consequences

- Phase 0/1 acts as a regression gate before scope widens to Phase 2 material library
- Manual smoke-test guide (`smoke-test-guide.md`) confirms readiness across CSV import, nesting, error handling, and res
sults display
- No rework expected before Phase 2 begins

---

### Decision: Desktop Web UI Content Resolution

**Author:** Bishop
**Date:** 2026-03-14
**Status:** Implemented

#### Context

The desktop app was resolving `WebApp\index.html` placeholder even when a real Phase 0/1 Web UI build existed at `src\Pa
anelNester.WebUI\dist\index.html`. The resolver was checking the app's bin directory before searching ancestor roots.    

#### Decision

When the desktop app resolves web content, it must search all ancestor roots for `src\PanelNester.WebUI\dist\index.html`
` before accepting any bundled placeholder page from `WebApp\index.html`.

#### Rationale

Running the desktop project from `src\PanelNester.Desktop\bin\Debug\net10.0-windows` places a copied `WebApp\index.html`
` directly under the app base directory. If resolution checks placeholder content while still walking upward, the copied 
 fallback wins too early and hides a valid repo-root Web UI build.

#### Consequences

- Desktop debug runs now prefer the real Phase 0/1 Web UI whenever the built `dist` folder exists.
- The bundled placeholder remains a legitimate fallback when no Web UI build is available.

---

### Decision: Hicks Phase 0/1 Smoke-Test Guide

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Complete

#### Context

Team needs a practical, runnable smoke-test checklist for verifying Phase 0/1 vertical slice without requiring full UI a
automation, Three.js viewer, or PDF export tooling.

#### Decision

Produce `.squad/smoke-test-guide.md` with:

1. **Preflight checklist** — `dotnet restore`, `dotnet build`, `dotnet test` (expects 36 passed, 1 skipped WebView2 inte
egration test)
2. **Happy-path scenario** — Three-row CSV with valid parts → import succeeds → nesting places all 7 instances on 1–2 sh
heets → results display utilization
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

Before starting Phase 2 material CRUD, Brandon requested the Web UI be restyled to match VS Code's dark theme for a more
e professional, IDE-like feel.

#### Decision

Restyle the Phase 0/1 Web UI to adopt VS Code dark theme conventions without introducing a theme framework or changing P
Phase 0/1 behavior.

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

Dallas completed the VS Code dark-theme styling refresh for Phase 0/1 before Phase 2 commencement. Hicks performed indep
pendent regression and reviewer gate to validate dark-theme alignment, workflow preservation, and build stability.       

#### Verdict

Approved the VS Code-inspired UI cleanup. The palette and structural cues align with the reference image (`UIColorPalett
te.png`) for the stated goal: dark editor surface, darker activity/sidebar rails, muted body text, bright headings, and V
VS Code blue accent/button treatment. The left activity bar, editor-like panels, and right utility sidebar preserve the o
overall Visual Studio Code feel without disturbing the Phase 0/1 slice's workflow seams.

#### Verification

1. **Theme review**
   - CSS tokens match the expected VS Code dark family: `#1e1e1e` editor background, `#252526` sidebar, `#181818` activi
ity rail, `#007acc` accent, `Segoe UI` at compact desktop sizing
   - Reference image sampling showed dominant near-black neutrals, which is consistent with the updated stylesheet      
   - Component/page updates remain presentational: no bridge contract, import payload, or nesting payload shapes were ch
hanged

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
- Residual docs note: `.squad/smoke-test-guide.md` test count is stale (expects 36 passed, actual 38 passed) — requires 
 docs-only follow-up

---

### Decision: Hicks Smoke-Test Guide Count Update

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Complete

#### Context

The approved VS Code theme refresh maintained regression stability: 38 passed, 1 skipped. However, `.squad/smoke-test-gu
uide.md` still documented the older count (36 passed, 1 skipped) as the preflight expectation, creating reviewer trust is
ssues.

#### Decision

Update `.squad/smoke-test-guide.md` preflight expectations to reflect the current validated regression gate exactly: `38
8 passed, 1 skipped`.

#### Rationale

- Reviewers use the smoke guide as a trust anchor before manual validation
- A stale test count creates false negatives during sign-off even when the code is healthy
- Synchronized documentation maintains reviewer confidence through Phase 2 commencement

#### Consequences

- `.squad/smoke-test-guide.md` preflight now reflects current baseline
- No behavioral changes to Phase 0/1 slice
- Phase 2 material library work may proceed with accurate regression baseline

---

---

## Second-Pass Theme Revision Decisions

### Decision: Bishop Second-Pass Titlebar Theming

**Author:** Bishop
**Date:** 2026-03-14
**Status:** Proposed

#### Decision

- Keep the desktop window on standard WPF chrome and extend the dark theme with native DWM titlebar attributes instead o
of building a custom titlebar.
- Implementation: enable immersive dark mode with the Windows 10 legacy/current attribute fallback, then apply caption, 
 text, and border colors that match the host's neutral VS Code-like shell surfaces.
- Rationale: this preserves native move/resize behavior and keeps the change low-risk while removing the last blue-tinte
ed host chrome mismatch with the Web UI.

---

### Decision: Neutralize Second-Pass Web UI Placeholder Accents

**Author:** Dallas
**Date:** 2026-03-14
**Status:** Proposed

#### Context

The remaining obvious header/footer bars in the latest screenshot appear to come from the desktop host, but the Web UI s
still had a couple of blue-leaning accents that made the dark shell feel less cohesive.

#### Decision

Keep the Phase 0/1 Web UI on the existing VS Code dark surface stack, but make placeholder/supporting chrome neutral whe
ere it is not carrying semantic meaning:

- capability tokens use muted text plus neutral borders instead of blue text
- the viewer placeholder sheet outline uses subtle neutral border/fill instead of a blue dashed accent
- active, navigation, and action accents stay unchanged so operator affordances remain obvious

#### Consequences

- The Web UI reads closer to the native VS Code dark shell without churn to layout or behavior.
- The desktop titlebar/footer mismatch remains a host-owned follow-up rather than a web styling problem.

---

### Decision: Hicks Second-Pass Theme Review

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Rejected

#### Context

Reviewed the second-pass chrome cleanup for the remaining header/footer blue styling and the request to carry the dark t
theme into the desktop titlebar.

#### Verification

1. `npm run build` in `src\PanelNester.WebUI` ✅
2. `dotnet test .\PanelNester.slnx` ✅ — 38 passed, 1 skipped
3. Runtime evidence review via `secondpassUI.png` ❌

#### Verdict

Reject this pass for user-visible acceptance.

#### Rationale

1. The supplied runtime screenshot still shows the desktop host header and footer in the older blue chrome instead of th
he intended neutral dark surfaces.
2. The native titlebar is still light/white in the screenshot, so the dark theme is not yet carrying into the titlebar i
in the actual rendered app.
3. Source changes in `MainWindow.xaml`, `MainWindow.xaml.cs`, `NativeTitleBarStyler.cs`, and the Web UI stylesheet are d
directionally correct, but acceptance has to follow the rendered result, not code intent.

#### Required Follow-up

- Reproduce the running desktop host after a fresh build and confirm the neutral dark chrome is what actually ships.    
- Verify the native titlebar styling path is effective on the target runtime/OS rather than only present in source.     
- Provide updated runtime evidence after the rebuilt host is launched.

#### Revision Owner

Per reviewer lockout, route the revision to **Ripley** for cross-layer runtime verification and rework, not the original
l authors.

---

### Decision: Host-Owned Chrome Must Be Themed In Desktop Layer

**Author:** Ripley
**Date:** 2026-03-14
**Status:** Implemented

#### Context

The prior UI polish pass updated the React shell, but reviewer evidence still showed a blue desktop header/footer and a 
 light system title bar. Those surfaces are not fully controlled by the Web UI bundle.

#### Decision

- Treat title bar, top host band, and bottom status band as desktop-owned chrome and theme them in WPF/C#.
- Keep the Web UI on the VS Code dark palette, but also retheme the bundled desktop `WebApp` fallback so the host does n
not regress to legacy blue when `src\PanelNester.WebUI\dist` is missing or stale.
- Use explicit dark caption colors through `NativeTitleBarStyler` and a custom WPF title bar so runtime chrome stays dar
rk even outside the WebView surface.

#### Consequences

- Theme acceptance for PanelNester must be verified at runtime across both WebView content and host chrome.
- Future polish work cannot assume CSS-only changes are sufficient when the visible surface spans WPF and WebView2.     
- The fallback host page remains a first-class verification seam and must stay visually aligned with the shipped shell. 

---

### Decision: Ripley Second-Pass Theme Cleanup Design Review & Seams

**Author:** Ripley
**Date:** 2026-03-14
**Status:** Complete (Archived)

#### Scope

Header, footer, and titlebar dark-blue styling consolidation.

#### Key Findings

1. **Web UI header** already correctly styled with `var(--vsc-bg-sidebar)` (#252526)
2. **WPF footer** already aligned with dark theme (#111827)
3. **Blue accent** visible in screenshots is viewer placeholder outline (Phase 5 semantic cue, intentional)
4. **Titlebar** is OS-level chrome requiring WPF-layer dark mode attributes, not CSS-only change

#### Recommendations Executed

- **Dallas:** No Web UI header changes needed; placeholder accent is intentional design
- **Bishop:** Apply native dark titlebar via DWM attributes; extend client area if Option B selected
- **Both:** Parallel-safe work with low risk

#### Verification

All recommendations implemented and verified:
- `npm run build` passed
- `dotnet test` passed (38 passed, 1 skipped)
- Runtime screenshot shows dark titlebar and neutral host surfaces
- Bundled fallback page rethemed to match shipped UI

#### Rationale

Clarified surface ownership: titlebar/host chrome are desktop-owned; Web UI CSS is not sufficient for rendering changes 
 outside WebView bounds. Detailed surface inventory and implementation plan enabled clean parallel work and reduced rewor
rk risk.

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction


## Phase 2 Decisions

### Decision: Phase 2 Material Library Slice

**Author:** Ripley
**Date:** 2026-03-14
**Status:** Proposed

#### Context

Phase 0/1 proved the vertical slice: CSV import → shelf nesting → results display with a hardcoded Demo Material. Phase 
 2 must replace that demo material with a real material library that persists locally, supports CRUD, and integrates with
h the existing import/nesting flow.

#### Scope: Narrowest Practical Slice

Phase 2 adds:

1. **Material CRUD** — Create, read, update, delete materials with the full PRD schema
2. **Local persistence** — Materials survive app restart via JSON file storage
3. **Material selection** — Import/nesting flow uses a user-selected material instead of hardcoded demo
4. **Bridge extension** — New message types for material operations

**Explicitly deferred to Phase 3+:**
- Project persistence (projects just hold import state in memory)
- Project-material snapshots (materials are always fetched live from library)
- Material import/export from external files
- Material duplication or templating

#### Persistence Choice

**Decision:** JSON file storage for the material library.

**Location:** %LOCALAPPDATA%\PanelNester\materials.json

**Rationale:**
- Simpler than SQLite for a single-entity library
- Human-readable and debuggable
- Sufficient for expected material counts (<100)
- SQLite introduces unnecessary complexity before project persistence needs it in Phase 3

**Implementation:** A new IMaterialRepository interface in PanelNester.Domain.Contracts with an implementation in PanelN
Nester.Services using System.Text.Json.

#### Architecture Seams

**Domain Layer (PanelNester.Domain)**
- Models/Material.cs — Already exists, no changes needed
- Contracts/IMaterialRepository.cs — New: CRUD interface

**Services Layer (PanelNester.Services)**
- Materials/JsonMaterialRepository.cs — New: JSON file persistence
- Materials/MaterialValidationService.cs — New: Unique name checks, field validation

**Desktop Layer (PanelNester.Desktop)**
- Bridge/BridgeContracts.cs — Extend with material message types
- Bridge/DesktopBridgeRegistration.cs — Register material handlers

**WebUI Layer (PanelNester.WebUI)**
- src/types/contracts.ts — Add material CRUD message types
- src/pages/MaterialsPage.tsx — New: Material list + editor UI
- src/bridge/hostBridge.ts — Add material request helpers

#### Bridge Contract Extension

New message types (following established vocabulary pattern):

\\\
list-materials         → list-materials-response
get-material           → get-material-response
create-material        → create-material-response
update-material        → update-material-response
delete-material        → delete-material-response
\\\

#### Workstream Splits

**Parker (Domain/Services)**

Deliverables:
1. IMaterialRepository interface in PanelNester.Domain.Contracts
2. JsonMaterialRepository implementation in PanelNester.Services/Materials
3. MaterialValidationService for business rules (unique names, positive dimensions)
4. Unit tests for repository and validation

Interfaces Owned:
- IMaterialRepository { GetAllAsync(), GetByIdAsync(id), CreateAsync(material), UpdateAsync(material), DeleteAsync(id) }
- Repository returns domain Material records
- Validation throws MaterialValidationException with machine-readable codes

Dependencies: None — can start immediately

**Bishop (Desktop Bridge)**

Deliverables:
1. Bridge contracts for material CRUD messages in BridgeContracts.cs
2. Handler registrations in DesktopBridgeRegistration.cs
3. Wire handlers to Parker's IMaterialRepository

Interfaces Consumed:
- IMaterialRepository from Parker

Interfaces Owned:
- Request/response contracts: ListMaterialsRequest, CreateMaterialRequest, etc.
- Error codes: material-not-found, material-name-exists, material-in-use

Dependencies: Parker's IMaterialRepository interface (not implementation)

**Dallas (WebUI)**

Deliverables:
1. Material CRUD contracts in contracts.ts
2. Materials page: list view with add/edit/delete
3. Material editor component (form for all PRD fields)
4. Material selector on Import page (dropdown replacing hardcoded demo)
5. Bridge helper functions for material operations

Interfaces Consumed:
- Bridge message types from Bishop's contract

Interfaces Owned:
- UI component props and local state shapes
- Material form validation (client-side, mirrors service rules)

Dependencies: Bishop's bridge contract types (can stub responses initially)

**Hicks (Tests & Review)**

Deliverables:
1. MaterialRepositoryTests — CRUD operations, persistence round-trip, concurrent access
2. MaterialValidationTests — Unique name rejection, dimension bounds, required fields
3. MaterialBridgeTests — Request/response contract compliance
4. Updated smoke-test guide for material workflows
5. Final integration review gate

Interfaces Consumed:
- Parker's repository and validation services
- Bishop's bridge handlers

Dependencies: All three workstreams (review gate at end)

#### Parallel Execution Plan

Day 1:
- Parker: IMaterialRepository interface + JsonMaterialRepository stub
- Bishop: Bridge contracts (can work from interface, not impl)
- Dallas: UI scaffold + contracts.ts types (can stub bridge)

Day 2:
- Parker: Repository implementation + validation service + unit tests
- Bishop: Handler wiring (once Parker's interface is stable)
- Dallas: Materials page + editor (using stubbed bridge)

Day 3:
- Bishop: Integration with Parker's implementation
- Dallas: Wire real bridge calls, material selector on Import
- Hicks: Begin test coverage

Day 4:
- Hicks: Integration tests + smoke guide update + review gate

#### Success Criteria

Phase 2 is complete when:

1. User can create a material with all PRD fields
2. Materials persist across app restart
3. User can edit and delete existing materials
4. Import page shows material dropdown instead of hardcoded demo
5. Nesting uses the selected material's settings
6. All existing Phase 0/1 tests still pass
7. New material CRUD tests pass
8. Smoke guide updated and verified

#### Consequences

- Demo material becomes a seed entry, not hardcoded behavior
- Import/nesting flows gain material selection step
- Phase 3 can build on this persistence pattern for projects
- No breaking changes to existing Phase 0/1 contracts (additive only)

#### Open Questions (None Blocking)

1. Should we seed the demo material on first run? **Recommended: Yes**
2. Should delete fail if material is referenced in current import? **Recommended: Yes, with \material-in-use\ error**   

---

### Decision: Hicks Theme Revision Review

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Approved

#### Verdict

Approved.

#### Acceptance

Compared secondpassUI.png against       heme-revision-printwindow.png. The prior capture still had a light native titleb
bar and legacy blue host chrome in the top and bottom host bands; the revised capture shows dark titlebar chrome and dark
k host header/footer with no visible blue seam left.

#### Evidence

The desktop host now defines dark titlebar/footer/header surfaces in src\PanelNester.Desktop\MainWindow.xaml, applies im
mmersive dark caption styling in src\PanelNester.Desktop\NativeTitleBarStyler.cs, and keeps the bundled fallback page on 
 the same dark palette in src\PanelNester.Desktop\WebApp\index.html.

#### Validation

Re-ran
pm run build in src\PanelNester.WebUI and dotnet test .\PanelNester.slnx; both passed, with tests at 38 passed / 1 skipp
ped.

#### Risk

Low for this acceptance slice. No further revision lockout needed.

---

___BEGIN___COMMAND_DONE_MARKER___0
PS F:\Users\brand\source\AgentRepos\PanelNester>

# Hicks Phase 2 material test contract

- Hicks is locking the Phase 2 bridge wire names to `list-materials`, `create-material`, `update-material`, and `delete-material` for spec-first test scaffolding. If implementation needs different names, change the tests and this decision together so the desktop and Web UI contracts do not drift.
- Review gate decision: Phase 2 will be rejected if any post-selection import or delete path silently falls back to `Demo Material`. The approved failure codes are `material-name-exists`, `material-in-use`, and `material-not-found`.
- Scaffolding approach: keep contract tests runnable now (JSON material round-trip, duplicate-name, in-use delete, exact-match import selection), and leave the live JSON repository and bridge round-trip tests skipped until those seams exist.

## Phase 2 Materials — Parker & Bishop (2026-03-14T17:16:57Z)

# Parker — Phase 2 materials

- Material library persistence lives at `%LOCALAPPDATA%\PanelNester\materials.json` by default in the backend repository implementation, so desktop wiring does not need to hardcode a separate storage contract.
- Material names are treated as unique with `OrdinalIgnoreCase` comparisons to prevent ambiguous library entries, but CSV/import matching remains exact `Ordinal` text so user-facing imports stay deterministic and explainable.
- `material-in-use` is enforced at the material service/bridge seam with additive request context (`selectedMaterialId`, imported material names) until Phase 3 project persistence and material snapshots exist.

# Bishop Phase 2 Materials

- The desktop host owns the concrete material library path at `%LOCALAPPDATA%\PanelNester\materials.json` via `DesktopStoragePaths`, while `PanelNester.Services.Materials.JsonMaterialRepository` stays path-driven and WPF-agnostic.
- Phase 2 bridge handlers use `IMaterialService` for CRUD responses and `IMaterialRepository` for import-material lookup so material validation stays aligned with the shared service contracts.
- The JSON material repository seeds the Phase 1 demo material on first load so existing import/nesting flows keep working until project-scoped material snapshots arrive in Phase 3.

# Dallas Phase 2 Materials

- Import flow now treats the chosen library material as the active nesting target for the current run.
- Phase 2 only sends imported rows whose `Material` value exactly matches the selected library material; mismatched rows stay visible in the import table instead of being silently coerced.
- Delete requests include current selector/import context so `material-in-use` blocks removing the actively selected or currently imported material.

---

## Decision: Phase 2 Material Library Slice — Final Test Gate Approval

**Author:** Hicks
**Date:** 2026-03-14
**Status:** Approved

### Context

Phase 2 material library implementation complete. Required final verification of CRUD persistence, bridge wiring, import material selection behavior, error code surfacing, and Demo Material fallback handling.

### Verdict

**APPROVED** — Phase 2 clears gate.

### Evidence

- Regression suite: **63 total, 61 passed, 2 expected skips**
- Web UI production build: **Green**
- Material CRUD persistence: **Live** (%LOCALAPPDATA%\PanelNester\materials.json, deterministic writes, Phase 2 demo entry seeded on first load)
- Bridge wiring: **Live** (material CRUD routed through IMaterialService, error codes forwarded)
- Import material selection: **Live** (exact match, no silent fallback to Demo Material)
- Failure codes surfaced: **All three live** (material-name-exists, material-in-use, material-not-found)

### Scope Checked

- src/PanelNester.Domain
- src/PanelNester.Services 
- src/PanelNester.Desktop
- src/PanelNester.WebUI
- Test suite coverage
- PRD compliance

### Residual Risks

1. No browser-level UI automation for material selector/delete affordances—current coverage relies on service/desktop tests and code inspection
2. Placeholder test MaterialLibrarySpecs.Import_flow_uses_selected_project_materials_instead_of_the_phase_one_demo_material remains skipped; should retire or replace early to keep regression gate honest

### Next Boundary

**Phase 3: Project persistence and material snapshots**—live-library references will need durable project-scoped behavior.

### Consequences

- Phase 2 material library slice is production-ready
- Phase 3 can proceed with confidence that material CRUD and exact-match import behavior are stable
- Early Phase 3 work: retire skipped test and add Web UI e2e coverage for material selector/delete flow
