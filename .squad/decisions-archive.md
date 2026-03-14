# Archived Squad Decisions (Before Phase 3)

## Rationale for Archival

As of 2026-03-14T17:56:50Z, Phase 3 (Project Persistence) work is in progress. Earlier Phase 0/1 and Phase 2 decisions remain valid but are archived here to keep the active decisions file focused and below 20KB for operational efficiency.

---

## Phase 0/1 Decisions (Archived)

### Decision: Phase 0 Host Uses Bundled Fallback and Receiver Shim
**Author:** Bishop | **Date:** 2026-03-14 | **Status:** Active
- Desktop host in `src\PanelNester.Desktop\` with typed bridge envelopes
- Resolves `src\PanelNester.WebUI\dist` first, falls back to `WebApp\index.html`
- WebView2 document-created shim forwards `postMessage` to `window.hostBridge.receive(...)`

### Decision: Phase 0/1 UI Scaffold Stays Contract-First
**Author:** Dallas | **Date:** 2026-03-14 | **Status:** Active
- React + TypeScript + Vite scaffold with stable DTOs in `src/PanelNester.WebUI/src/types/contracts.ts`
- `window.hostBridge.receive(...)` seam for message handling
- Read-only Import and Results pages matching future payload shapes

### Decision: Phase 0/1 Test Contract Notes
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Active
- CSV header order should not matter (exact names still required)
- Bridge failures use structured response: `success`, machine-readable code, human-readable message
- Import service accepts `Stream` or `TextReader` alongside `filePath`
- Nesting reserves: `outside-usable-sheet`, `no-layout-space`, `invalid-input`, `empty-run` with 0.0001" tolerance

### Decision: Phase 1 Domain Contracts
**Author:** Parker | **Date:** 2026-03-14 | **Status:** Implemented
- Contracts in `src\PanelNester.Domain\Models` and service interfaces in `src\PanelNester.Domain\Contracts`
- Use `decimal` for geometry/spacing/margins/kerf/coordinates to avoid floating-point drift
- Quantity compacted through import; expanded to `ExpandedPart` inside `ShelfNestingService`
- Hardcoded `Demo Material` (96"×48", 0.125" spacing, 0.5" edge margin) until Phase 2 material CRUD

### Decision: Hicks Phase 0/1 Batch Review
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Rejected for next-step integration
- Initial batch had bridge vocabulary mismatch, missing live integration, and nesting code drift
- Verdict: Do not call this batch integrated Phase 0/1 without contract unification

### Decision: Ripley Phase 0/1 Revision
**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Implemented
- Unified bridge vocabulary: `bridge-handshake`, `open-file-dialog`, `import-csv`, `run-nesting`, `*-response`
- Demo material: `Demo Material`, 96"×48" sheet, 0.125" spacing, 0.5" margin, 0.0625" kerf
- Bounded nesting failure vocabulary: `outside-usable-sheet`, `no-layout-space`, `invalid-input`, `empty-run`
- No placeholder-only host seams—desktop host must invoke real services

### Decision: Hicks Phase 0/1 Re-review
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved
- Contract drift closed, vertical slice live, gating contracts stable
- `dotnet build`, `dotnet test`, Web UI build all pass
- Desktop round-trip exercises file-open → import → nesting on real services
- Phase 0/1 acts as regression gate before Phase 2 scope expansion

### Decision: Desktop Web UI Content Resolution
**Author:** Bishop | **Date:** 2026-03-14 | **Status:** Implemented
- Desktop app searches all ancestor roots for `src\PanelNester.WebUI\dist\index.html` before accepting bundled placeholder
- Resolver checks `dist` before `WebApp\index.html` to prefer real builds over fallback

### Decision: Hicks Phase 0/1 Smoke-Test Guide
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Complete
- Produced `.squad/smoke-test-guide.md` with preflight, happy-path, and four failure-mode scenarios
- Preflight: `dotnet restore`, `dotnet build`, `dotnet test` (38 passed, 1 skipped)
- CSV data provided for happy path (three-row import) and four failure modes (oversized, unknown material, invalid numeric, zero quantity)
- Demo Material reference included; 9-item acceptance criteria checklist

---

## Phase 2 Decisions (Archived)

### Decision: Hicks Smoke-Test Guide Count Update
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Complete
- Updated `.squad/smoke-test-guide.md` preflight to reflect current regression gate: 38 passed, 1 skipped (not 36 passed, 1 skipped)

### Decision: VS Code Dark Theme for Web UI
**Author:** Dallas | **Date:** 2026-03-14 | **Status:** Implemented
- Restyled Phase 0/1 Web UI to match VS Code dark theme
- Palette: `#1e1e1e` editor, `#181818` activity bar, `#252526` sidebar, `#007acc` accent
- Flat surfaces, 1px borders, 13px base typography, monospace for data
- No behavior changes, all tests pass (38 passed, 1 skipped)

### Decision: Hicks VS Code Theme Review & Approval
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved
- CSS tokens match VS Code dark family
- Reference image sampling validated palette alignment
- Component updates remain presentational; no contract changes
- Web UI build passed, solution tests passed (38 passed, 1 skipped)
- Phase 0/1 approved for next phase commencement

### Decision: Bishop Second-Pass Titlebar Theming
**Author:** Bishop | **Date:** 2026-03-14 | **Status:** Proposed
- WPF shell titlebar must reflect dark theme to avoid light-on-dark contrast clash

### Decision: Neutralize Second-Pass Web UI Placeholder Accents
**Author:** Dallas | **Date:** 2026-03-14 | **Status:** Implemented
- Removed bright accent borders and placeholder colors from modal stub and unimplemented routes
- Unified accent to `#007acc` (VS Code blue) across all interactive states

### Decision: Hicks Second-Pass Theme Review
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved
- Verified palette neutralization and dark-theme consistency
- All tests passing (38 passed, 1 skipped)
- No workflow changes required

### Decision: Host-Owned Chrome Must Be Themed In Desktop Layer
**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Active
- WPF shell titlebar, window chrome, and system integration must reflect dark theme
- Desktop layer owns chrome styling; Web UI owns content styling
- Coordination point for Phase 3 and beyond

### Decision: Ripley Second-Pass Theme Cleanup Design Review & Seams
**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Approved
- Approved Bishop's titlebar theming and Dallas's placeholder cleanup
- Confirmed seams between host chrome and Web UI content are cleanly separated

### Decision: Phase 2 Material Library Slice
**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Proposed
- Phase 2 scope: CRUD for materials, local persistence, material selection in projects
- Deferred to Phase 2 after Phase 0/1 vertical slice approved
- Material service interface and domain model prepared in Phase 1 prep

### Decision: Hicks Theme Revision Review
**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved
- All theme updates validated; regression baseline held at 38 passed, 1 skipped
- Ready for Phase 2 commencement with dark-theme styling gate closed

---

## Summary

All Phase 0/1 and Phase 2 decisions remain **Active** for implementation reference. They have been archived to maintain operational focus on Phase 3 (Project Persistence) work in the active decisions file.

**Archival Date:** 2026-03-14T17:56:50Z  
**Rationale:** Decisions file size reduction + Phase 3 focus  
**Retention:** All decisions preserved for historical reference and future phases
