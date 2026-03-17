# Parker History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Recent Updates

📌 2026-03-14: Phase 0/1 domain and services complete  
📌 2026-03-14: CSV import, nesting service, and contracts tested  
📌 2026-03-14: Orchestration and session logs created  
📌 2026-03-14: **PHASE 2 ASSIGNMENT: Material Library Domain/Services Lead**
📌 2026-03-14T17:16:57Z: **PHASE 2 COMPLETE** — Material CRUD contracts, JSON persistence, validation service, and bridge handlers delivered. 60 tests passing.
📌 2026-03-14T20:17:23Z: **PHASE 5 COMPLETE** — Batch nesting orchestration, report settings, report data shaping, and test coverage delivered. 103 tests passing.
📌 2026-03-15T23:07:13Z: **STOCK-WIDTH PREFERENCE COMPLETE** — Updated `ShelfNestingService` with stock-width-matching new-shelf orientation preference. 137 tests passing, all gates cleared by Hicks.

📌 2026-03-16T00:09:58Z: **IMPORT MAPPING FEATURE APPROVED** — Extended CsvImportService with explicit column-mapping options and material-to-library resolution. Import requests accept column overrides and material mappings. Responses surface source column detection, proposed field mappings, and per-material resolution status. New materials created via IMaterialService during bridge finalize step, not inside pure import path. Service layer remains UI-independent; side effects (library mutation) isolated to bridge layer. All existing tests passing; new coverage added for mapping scenarios. 143 tests / 141 passed / 2 skipped. Hicks review gate APPROVED ✅

📌 2026-03-17T04:04:02Z: **EDITABLE KERF WIDTH COMPLETE** — Removed hardcoded `KerfWidth = 0.0625m` from ProjectSettings. Made kerf explicit editable setting via new `DefaultKerfWidth` constant in ProjectService (NewAsync, NormalizeSettings). Added default kerf fallback in ProjectFlatBufferSerializer for legacy projects. Backend ready for Dallas UI binding. Kerf persists with project (FlatBuffers + legacy JSON); UI will pass value via existing `updateProjectMetadata` bridge contract. All existing tests passing; backward compatibility maintained.

📌 2026-03-17T04:38:49Z: **IMPORT REVISION COMPLETE** — Restored two-step client flow for file selection so import triggers reliably on first try. Combined dialog+import pattern caused bridge timeout expirations before file dialog completed. Now: UI owns file selection, preserves import-file contract for mapped workflows, provides sufficient time for first-attempt completion. Revision gate designed by Hicks with mixed executable+source-contract validation. All tests passing; no regressions.


## Phase 2 — Material Library CRUD (COMPLETE)

**Ownership:** Parker (Domain/Services foundation) ✅

**Delivered:**
1. ✅ `IMaterialRepository` interface in `PanelNester.Domain.Contracts` — CRUD contract with async methods
2. ✅ `JsonMaterialRepository` implementation in `PanelNester.Services/Materials` — JSON file persistence at `%LOCALAPPDATA%\PanelNester\materials.json`
3. ✅ `MaterialValidationService` in `PanelNester.Services/Materials` — Business rules: unique names, positive dimensions, required fields
4. ✅ Comprehensive unit tests for repository, validation, service, and bridge handlers

**Key Decisions:**
- Material persistence path owned by backend, WPF-agnostic
- Case-insensitive name uniqueness (`OrdinalIgnoreCase`) with exact-match import (`Ordinal`)
- Additive `material-in-use` enforcement at service/bridge seam with request context
- Phase 1 demo material auto-seeded on first load
- `dotnet test PanelNester.slnx --nologo` result: **60 passed, 3 skipped**

**Interfaces Owned:**
- `IMaterialRepository { GetAllAsync(), GetByIdAsync(id), CreateAsync(material), UpdateAsync(material), DeleteAsync(id) }`
- Repository returns domain `Material` records
- Validation throws `MaterialValidationException` with machine-readable codes

**Success Criteria Met:**
- ✅ IMaterialRepository interface contract stable and tested
- ✅ JsonMaterialRepository round-trip tested
- ✅ Validation logic tested for edge cases
- ✅ All Phase 2 tests passing; ready for Phase 3 (desktop UI, project persistence)

## Phase 3 — Project Persistence & Material Snapshots (COMPLETE)

**Ownership:** Parker (Domain/Services foundation) ✅

**Completed Deliverables:**
1. ✅ Domain models: `Project`, `ProjectMetadata`, `ProjectSettings`, `ProjectState`, `MaterialSnapshot`  
2. ✅ `IProjectService` interface with `NewAsync`, `LoadAsync`, `SaveAsync`, `UpdateMetadataAsync`  
3. ✅ `ProjectSerializer` for JSON round-trip with version handling  
4. ✅ `ProjectService` implementation with snapshot-first restore logic  
5. ✅ Unit tests for serialization, version handling, snapshot behavior  
6. ✅ Integration tests with full domain stack  

**Test Results:** 79 passed, 3 skipped ✅

**Key Achievement:** Projects now persist as deterministic snapshots, locked in at save time. Reopening a `.pnest` file restores exact materials from the snapshot, immune to later library edits—core requirement for Phase 3.

**Next Handoffs:**
- Hicks: QA approval gate (snapshot-first expectations)  
- Dallas: UI integration (show saved vs. pending snapshots)  
- Bishop: Desktop bridge implementation  

## Phase 5 — Results Viewer & PDF Reporting (REJECTED ❌)

**Ownership:** Parker (Domain/Services foundation) — LOCKED OUT

**Assignment:** Batch nesting service, PDF report generation, report settings persistence

**Implementation Status (2026-03-14T19:59:29Z):**
- ✅ `BatchNestResponse`, `MaterialNestResult`, `ReportSettings` models delivered
- ✅ `IBatchNestingService` and `IReportService` interfaces implemented
- ✅ `BatchNestingService` (delegates to shelf algorithm per-material) operational
- ✅ `QuestPdfReportService` (text tables, unplaced items, notes sections) deployed
- ✅ Report settings persist in project files without version bump
- ✅ Multi-material determinism validated; batch results merge correctly
- ✅ 99 tests passing, 2 skipped, 0 failures
- ✅ Zero regressions to Phase 0–4 paths

**Rejection Reasons:**
1. **PDF Sheet Visuals Missing** (Critical) — Current `QuestPdfReportExporter` renders text tables only; PRD §6.7 requires sheet diagrams/geometry.
2. **Export Failure-Path Coverage Insufficient** (Critical) — Phase 5 matrix calls for cancelled save dialog, file-write failure, and no-result export tests; current coverage only validates success path.

**Locked Out:** Parker cannot participate in revision cycle. Ripley (or non-author reviewer) owns next phase.

## Phase 5 Follow-Up: Report Formatting Fixes (2026-03-14T23:47:32Z)

**Assignment:** PDF panel labels, utilization percentage formatting

**Deliverables:**
- ✅ `QuestPdfReportExporter` now renders deterministic SVG text labels from `NestPlacement.PartId`
- ✅ Labels ordered by placement coordinates to ensure reproducible sheet visuals
- ✅ `FormatPercent()` fixed: treats inputs as already-percent values (60.0m → 60.0% instead of 6000%)
- ✅ Fix confined to export formatting layer; bridge/report data contracts unchanged
- ✅ Exporter tests updated for label rendering and percent formatting validation
- ✅ 105 total tests: 103 passed, 2 skipped, 0 failed
- ✅ Zero regressions to Phase 0–4 domain/service paths
- ✅ Hicks re-review: APPROVED ✅

**Status:** COMPLETE — Phase 5 follow-up integrated and approved

## Phase 6 — FlatBuffers `.pnest` Migration (2026-03-15T00:52:22Z)

**Ownership:** Parker (Persistence layer lead) ✅

**Assignment:** Implement FlatBuffers `.pnest` migration and resolve the current save crash in the persistence layer.

**Deliverables:**
- ✅ FlatBuffers schema (`.fbs` format) and generated C# types
- ✅ PNST-header persistence format (4-byte magic header + FlatBuffers binary payload)
- ✅ Dual-read compatibility: existing JSON `.pnest` files open without corruption, newly saved files use FlatBuffers binary
- ✅ Deterministic duplicate-material snapshot handling (consistent ordering across save/reload cycles)
- ✅ Persistence test coverage: crash reproduction (before/after fix), legacy JSON load, FlatBuffers round-trip, precision validation (decimal ↔ double), edge cases (empty projects, large results, null results), cancelled/failed save recovery
- ✅ Save crash fix integrated into `ProjectService`

**Test Results:**
- 110/112 tests passing
- 2 skipped (documented)
- Zero regressions to Phase 0–5 domain/service paths
- WebUI build verified

**Key Decisions:**
- FlatBuffers binary chosen for compression, determinism, and schema evolution safety
- Dual-read gate ensures graceful legacy JSON support during transition
- Decimal → double precision verified within acceptable tolerance for dimensional data
- Snapshot determinism achieved through sorted material ordering before serialization

**Hicks Review:** ✅ APPROVED (2026-03-15T00:52:22Z) — All validation gates cleared; migration ready for merge

**Status:** COMPLETE — Migration approved and ready for production

## Phase 6 — Hardening & Smoke Verification (2026-03-15)

**Ownership:** Parker (Reporting hardening) ✅

**Assignment:** PDF export empty-result handling, dense-layout SVG refinements

**Deliverables:**
- ✅ `ReportDataService.HasResults` only returns true when ≥1 sheet has ≥1 placement (previously counted any sheet)
- ✅ `QuestPdfReportExporter` shows "No nesting results are available for this report" when no renderable layouts
- ✅ Dense sheet diagrams: 1pt minimum stroke width, 6pt minimum label font floor
- ✅ Panels too small for inline labels use numbered callout badges, ordered placement summary as legend
- ✅ Zero-placement sheets render explicit "No placements" state instead of blank outline
- ✅ Test coverage: ReportDataServiceSpecs (empty/zero-sheet/zero-placement tests), QuestPdfReportExporterSpecs (dense-layout callout validation)
- ✅ 127 total tests: 125 passed, 2 skipped, 0 failures (net +15 from baseline 112)

**Key Decisions:**
- Empty-result exports succeed and remain readable (consistency with empty material behavior)
- Reuse existing placement summary as legend/callout reference (avoids widening report contract)
- Numbered callouts deterministic (placement index-based) for stable PDF reproduction
- Bridge payloads, persistence, FlatBuffers unchanged (reporting layer only)

**Hicks Review:** ✅ APPROVED (2026-03-15) — All empty-result and dense-layout gates cleared

**Status:** COMPLETE — Phase 6 reporting hardening integrated

## Learnings

- 2026-03-14: Initial team staffing. I own import, validation, domain services, and the nesting engine.
- 2026-03-14: Phase 0/1 backend contracts stay cleaner if import keeps quantity on compact `PartRow` records, nesting expands instances just-in-time, and all sheet geometry stays in `decimal` with kerf treated as added spacing instead of part resizing.
- 2026-03-14: Parallel agent execution and decision-driven architecture allow teams to de-risk at seams early and move fast once contracts stabilize.
- 2026-03-14: Phase 3 extends Phase 2 architecture: projects persist as JSON files with metadata, settings, and material snapshots. Design keeps repo/serializer seams clean by versioning the schema early and deferring XLSX/multi-material/export to later phases.
- 2026-03-14T19:59:29Z: Phase 5 rejection: PDF reporting requires both sheet visuals (geometry rendering) and comprehensive failure-path coverage. Success-path validation is necessary but not sufficient for reviewer sign-off on mission-critical export workflows.
- 2026-03-15: Phase 6 hardening reveals that "results available" should mean renderable sheet placements (≥1 placement), not just non-empty material records. This allows zero-sheet and zero-placement exports to stay successful while showing correct empty-state.
- 2026-03-15: Dense PDF layouts stay deterministic and readable if tiny panels fall back to numbered callouts while the existing ordered placement summary acts as the legend seam; that avoids widening report contracts and keeps phase scope bounded.
- 2026-03-15: Orientation-preference fixes are safest when they add a narrow priority key ahead of the existing heuristic, so the engine can honor an explainable special case (like a panel already spanning full sheet width) without changing rotation behavior everywhere else.
- 2026-03-15: Import-time mapping stays explainable if the backend keeps exact-match defaults, returns detected headers/material resolutions on every import response, and treats user overrides plus bridge-led material creation as explicit inputs instead of fuzzy silent fallbacks.
- 2026-03-16: Making hardcoded values editable is cleanest when you move the default constant into the service layer (where normalization lives) rather than the domain record. This lets persistence round-trip zero/missing values gracefully while keeping the domain contract explicit about what's required vs. optional. Existing tests often already validate the full path if the setting flows through established patterns.


📌 2026-03-17T15:46:43Z: **GROUPED NESTING IMPLEMENTATION COMPLETE** — Domain models, import pipeline, nesting engine, and persistence layer. Added Group property to PartRow/PartRowUpdate/ExpandedPart. Implemented group-partitioned nesting with eligible-sheet tracking and spillover. Optional field mapping with aliases (group, groupname, groupid, category, section, batch, set, zone). FlatBuffers schema appended (backward-compatible). Integration: end-to-end grouped nesting with material isolation. Backend ready for validation.

📌 2026-03-17T16-41-49Z : **GROUPED RESULTS FOLLOW-UP — NestPlacement.Group COMPLETE** — Added optional \Group\ field to NestPlacement domain model. Updated ShelfNestingService to set Group = part.Group at placement creation. Group flows through report shaping and project persistence. No-group runs emit null, preserving backward compatibility. Enables WebUI grouped results tabs and mixed-group dimming. All tests passing (167/169, 2 skipped).
