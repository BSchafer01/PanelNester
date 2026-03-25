# Ripley History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Core Context

I own architecture, scope control, and reviewer gating for the full product.

### Phase 0-5 Completed (2026-03-14 to 2026-03-14T20:17:23Z)

Executed six-phase vertical slice from bridge validation (Phase 0-1) through results & PDF (Phase 5). Key lessons:
- **Phase 0 prerequisite:** Bridge contract must prove before domain work.
- **Unified vocabulary:** One bridge dialect (bridge-handshake, open-file-dialog, import-csv, run-nesting, etc.) across WPF, Services, React, and tests.
- **Demo material contract:** 96"×48" sheet, 0.125" spacing, 0.5" margin, 0.0625" kerf; nesting failure codes (outside-usable-sheet, no-layout-space, invalid-input, empty-run).
- **Shelf heuristic nesting:** Domain-isolated, kerf-as-spacing, quantity expansion at nest time.
- **Phase 5 gate clarity:** PDF requires live-geometry SVG rendering, not text tables; export must handle failure paths (cancellation, file-write, no-result).

### Material/Project/Import/Results Hardening (2026-03-15 to 2026-03-15T02:40:13Z)

Completed Phase 2 (materials), Phase 3 (project save/open), Phase 4 (import + edit), Phase 5 correction, Phase 6 hardening, and UI cleanup.
- **Packaging risk identified:** Per-user MSI breaks if runtime creates mutable folders inside INSTALLFOLDER; solution: explicit WebView2 profile location at `%LOCALAPPDATA%\PanelNester\WebView2\UserData`.
- **Review artifact drift:** Retargeting binaries alone is incomplete; validation docs and runtime prerequisites must align.
- **Titlebar pattern:** WebView2 `DocumentTitleChanged` event + `document.title` is cleaner than bridge message.

### Framework & Infrastructure (2026-03-15 to 2026-03-16)

- **.NET 8 retarget:** Dropped .NET 7; updated validation artifacts and runtime prereqs; tests green (134 total, 132 passed, 2 skipped).
- **FlatBuffers migration:** `.pnest` schema with 8-byte PNST header, dual-read backward compat, fixed duplicate-key crash.
- **Responsive layout recalibration:** Workspace-left / viewer-right default; narrow breakpoint 900px; resize handle visible and functional across desktop sizes.

### Last Approved Gates

- **UI Cleanup (2026-03-15T02:40:13Z):** Right-sidebar removed, dark navigation visible 320–1440px, titlebar dirty-indicator stable.
- **PER-USER MSI (2026-03-15T16-39-48Z):** WebView2 profile relocation, clean uninstall, full installer/smoke validation.
- **Results Layout (2026-03-17T04:38:49Z):** Workspace-left/viewer-right split restored, resize handle functional at 900px+.

---

## Learnings Archived

2026-03-14 to 2026-03-14T20:17:23Z: Phases 0–5 vertical slice. Lock-out pattern (reviewer rejects → original author locked, fresh perspective on revision) proven effective. Immutable install root requirement + explicit profile configuration critical for uninstall correctness. Retarget completeness: executables, validation docs, runtime prerequisites must all align.



## Recent Work (2026-03-15T17:06:36Z)

---

### Group-Export-Slice Coordination (2026-03-17T18:58:10Z)

**Status:** ✅ COMPLETED

Coordinated group export slice across Dallas (WebUI contract), Parker (PDF export), and Hicks (test gates). Validated that grouped import/nesting/results workflow is architecturally sound and well-integrated. Identified low-risk housekeeping priorities:

1. **WebUI test infrastructure** (half-day) — Vitest for pure-logic functions
2. **PDF group visibility** (1–2 days) — expand export to include group column/label
3. **E2E smoke test automation** (2–3 days) — contract-level bridge validation
4. **Nesting quality improvements** (variable) — benchmark utilization before optimizing

All delivery confirmed; orchestration logs recorded; team updates appended to agent histories.

- ✓ **.NET 8 DOC FIX REVISION COMPLETE.** Assigned as lock-out author for rejected .NET 8 retarget slice (pattern: original author locked from self-revision). Hicks' rejection was on review-critical artifact drift, not implementation quality. Updated `tests\Phase0-1-Test-Matrix.md` to state desktop host target as `net8.0-windows` and updated `.squad\smoke-test-guide.md` to distinguish local build requirement (`.NET 8.0.x` SDK) from installed-app requirement (x64 `.NET 8 Desktop Runtime` + `Microsoft Edge WebView2 Runtime`). Append-only history files left untouched. Validation stayed green (134 total / 132 passed / 2 skipped / 0 failed). Hicks final re-review approved. **NET 8 RETARGET APPROVED 2026-03-15T17:06:36Z**. Decisions merged; inbox deleted; agent histories updated.

## Key Insights (Per-User MSI Cycle & .NET Retarget)

1. **Lock-out pattern strength:** When a reviewer rejects an artifact, preventing the original author from revising forces fresh perspective. Both Ripley's WebView2 revision (MSI cycle) and .NET 8 doc fix demonstrate this—narrowly focused changes, reduced regression risk.
2. **Immutable install root requirement:** Per-user MSI lifecycle is only trustworthy if the install directory stays unchanged after first launch and is fully removable on uninstall. Any runtime-created folders must be relocated outside `INSTALLFOLDER`.
3. **Explicit profile configuration:** Don't rely on framework defaults for runtime profiles when packaging for uninstall. Make the profile path an explicit parameter passed through the layered initialization.
4. **Framework retarget completeness:** Retargeting is only review-complete when executables, validation docs, and runtime prerequisites all tell the same TFM story. Missing any one layer creates reviewer confusion and user-visible breakage.

## Next Steps

- Phase 7 scoping or additional hardening work

📌 2026-03-17T15:46:43Z: **GROUPED NESTING DESIGN REVIEW COMPLETE** — Architecture contract for optional panel grouping during import/editing. Groups control nesting order (first-seen), ungrouped parts nest last, spillover rule allows last sheet of group N to accept group N+1. No changes to Phase 0-5 vocabulary or behavior. Backward-compatible FlatBuffers schema append. Parker owns 13 files (domain/import/nesting/persistence), Dallas owns 2 (TypeScript types + UI column), Hicks test matrix (8 ShelfNestingService scenarios + import/editor/persistence). Decision merged into decisions.md. Ready for team execution.

📌 2026-03-17T16-41-49Z : **GROUPED RESULTS FOLLOW-UP — Design Review APPROVED** — Documented import mapping fix (UI-side gate enhancement for optional field/spare column detection), Group field propagation (NestPlacement.Group added by Parker), and grouped results UX (tabs, filtering, mixed-group dimming, tooltips implemented by Dallas). Full seam ownership matrix and execution sequence documented. Risk mitigation: group count badge on tab to signal feature availability. No architectural risk; additive changes to existing contracts and UI. Ready for production.

- 2026-03-17T10:26:24Z: **LOCAL COMMIT EXECUTION COMPLETE.** Consolidated all grouped import, nesting, and results workflow changes into single atomic commit hash c95df7c. All 369 files staged and committed with Co-authored-by trailer. Git status clean. No push (no remote configured). Decision record appended to `decisions.md`. Orchestration log and session log created. Team ready for parallel implementation: Parker (domain model + nesting engine), Dallas (import gate fix + results UI), Hicks (test matrix).

## Learnings (Continued)

### Material Library Repointing Design Review (2026-03-19T00:00:00Z)

**Context:** User requested ability to repoint material library to custom location, persist across restarts, and restore default with auto-creation if missing.

**Key decisions:**
1. **Separate settings file** — Store active library path in `app-settings.json` (new), not embedded in repository. Keeps `DesktopStoragePaths` as read-only utility for defaults.
2. **Two new bridge messages** — `change-library-location` and `restore-default-library-location` with clean error codes and fallback behavior.
3. **Silent fallback** — If custom path becomes inaccessible at startup, revert to default without orphaning app. User gets status message, not modal error.
4. **No repository contract change** — `JsonMaterialRepository` and `IMaterialRepository` stay unchanged. Active path is resolved *before* instantiation.
5. **Seam ownership** — Desktop host owns settings R/W, handlers, and fallback logic. WebUI owns buttons and messaging. Bridge owns contract definition.

**Risk mitigations:**
- Tested fallback: custom path deleted mid-session → graceful revert to default.
- Settings file is optional at startup (missing file = use default).
- Persisted path doesn't break projects (they snapshot materials).
- Error codes map to user-friendly messages in bridge error resolver.

**Estimated delivery:** 3–4 engineering days across team (no blockers, additive feature).

**Files modified:** AppSettings (new), MainWindow, DesktopBridgeRegistration, contracts.ts, MaterialsPage.tsx, BridgeContracts.cs error mappings.

**Decision record:** `.squad/decisions/inbox/ripley-material-library-repointing.md`

### .pnest Startup Open Readiness (2026-03-19T00:00:00Z)

**Context:** Startup `.pnest` opens could fire before the WebUI finished handshake/capability wiring.

**Key decisions:**
1. **Explicit UI-ready signal** — WebUI sends `bridge-ui-ready` after handshake success; desktop waits on that signal before startup open.
2. **Readiness gate abstraction** — `BridgeHostReadinessGate` isolates the ready signal and is covered by a behavioral test.
3. **Startup flow remains safe** — Startup arg parsing and invalid-path rejection stay unchanged; only the readiness seam moved.
