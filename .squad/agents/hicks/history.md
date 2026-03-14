# Hicks History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own acceptance criteria, regression coverage, and reviewer verdicts.
- 2026-03-14: Phase 0/1 test startup works best as spec-first scaffolding — one runnable smoke/contract test per seam, skipped integration tests with explicit blockers, and a matrix that maps back to success criteria.
- 2026-03-14: Phase 0/1 test deliverables complete. 35 tests (26 passing, 9 skipped). Test matrix created, xUnit scaffolds deployed, spec-first approach extracted as reusable skill.
- 2026-03-14: Cross-layer kickoff reviews need three early gates before I trust the slice: a runnable toolchain, one shared bridge vocabulary across host and Web UI, and one non-placeholder round-trip through the real seams.
- 2026-03-14: I can approve a kickoff slice once the shared contract names, a dispatcher-backed file-open→import→nest regression, and the UI's live results consumption are all executable in one build/test pass; anything less still reads like stitched placeholders.
- 2026-03-14: Theme-refresh reviews pass faster when I separate appearance from behavior: check palette/layout tokens against the reference, then confirm import and results pages still expose the same handlers, status pills, tables, and error surfaces before trusting build/test output.
- 2026-03-14: Smoke-guide bookkeeping has to track the validated regression gate exactly; the current baseline is 38 passed, 1 skipped, and stale counts make reviewer sign-off less trustworthy.
- 2026-03-14: Phase 3 testing focuses on JSON serialization stability, version migration, material snapshot edge cases, and project metadata round-trip. Snapshot-first gate: projects reopen from snapshots only, no silent live-library rehydration. Final approval deferred pending Dallas Web UI Phase 3 completion.

## Recent Work (2026-03-14T18:14:59Z)

- ✓ Created Phase 3 Persistence/Snapshot Test Matrix (`tests/Phase3-Persistence-Matrix.md`)
- ✓ Added `ProjectSerializerTests` — round-trip, version handling, corrupt file handling
- ✓ Added `ProjectServiceTests` — new/load/save flows, metadata updates
- ✓ Added `ProjectBridgeTests` — request/response contract compliance  
- ✓ Updated smoke-test guide with project workflows (create/open/save/metadata)
- ✓ Established snapshot-first review gate (material snapshots captured at creation, no live-library rehydration)
- ✓ All 80 tests passing; 2 documented skips
- ✓ Recorded orchestration log (`.squad/orchestration-log/2026-03-14T17-56-50Z-hicks.md`)
- ✓ **PHASE 3 FULLY COMPLETE** (2026-03-14T18:14:59Z) — Dallas Web UI implementation finished:
  - Project page with metadata form (all PRD fields)
  - Dirty-state tracking and navigation guards
  - Material snapshot display with saved vs pending distinction
  - Web UI build passing
  - Complete end-to-end project lifecycle operational from desktop host through Web UI
  - **Final approval: GATE CLEARED** — All three layers (Parker domain, Bishop bridge, Dallas Web UI) integrated and tested
  - Ready to begin Phase 4 design

- ✓ **PHASE 3 FULLY COMPLETE** (2026-03-14T18:14:59Z) — Dallas Web UI implementation finished:
   - Project page with metadata form (all PRD fields)
   - Dirty-state tracking and navigation guards
   - Material snapshot display with saved vs pending distinction
   - Web UI build passing
   - Complete end-to-end project lifecycle operational from desktop host through Web UI
   - **Final approval: GATE CLEARED** — All three layers (Parker domain, Bishop bridge, Dallas Web UI) integrated and tested
   - Ready to begin Phase 4 design

## Phase 4 Scope (Import Pipeline Tests & Integration Gate)

**Ownership:** Hicks (Tests and integration review gate)

**Gate Status (2026-03-14T18:34:43Z):** DESIGN REVIEW COMPLETE — Phase 4 ready to implement

**Four Non-Negotiable Review Gates:**
1. **Regression safety** — Current CSV import, nesting, `.pnest` persistence cannot regress while XLSX/editing land
2. **Format parity** — Equivalent CSV and XLSX data must yield same row payload shape, validation statuses, error/warning codes
3. **Edit persistence** — Inline import-table changes must revalidate immediately and survive save/open exactly
4. **Failure clarity** — Missing materials, bad numerics, corrupt workbooks must stay user-visible and specific

**Test Coverage Plan:**
- XLSX Import Tests: headers, column order, empty file, multi-sheet, corrupt file, Unicode round-trip
- File Import Dispatcher Tests: `.csv` and `.xlsx` routing, unknown extensions → error
- Part Editor Tests: update/delete/add operations, revalidation, non-existent rowId error
- Bridge Round-Trip Tests: `import-file`, `update-part-row`, `delete-part-row`, `add-part-row`
- Integration Tests: end-to-end import XLSX → edit → revalidate → nesting

**Deliverables:**
1. Phase 4 Test Matrix (`tests/Phase4-Import-Pipeline-Test-Matrix.md`) — ✓ COMPLETE
2. Smoke Test Guide Phase 4 Extension — ✓ COMPLETE
3. Service/Bridge tests (parametrized against Parker contracts) — Ready for Batch 1
4. Integration tests — Ready for Batch 3

## Phase 5 Scope (Results Viewer & PDF Reporting Tests & Integration Gate)

**Ownership:** Hicks (Tests and integration review gate)

**Gate Status (2026-03-14T19:23:22Z):** DESIGN REVIEW COMPLETE → (2026-03-14T19:59:29Z) INTEGRATED REVIEW: REJECTED ❌

**Four Non-Negotiable Review Gates:**
1. **Rendering fidelity** — Three.js viewer displays all materials with correct colors, geometry, and user interactions ✅ PASSED
2. **PDF accuracy** — QuestPDF reports include sheet visuals matching placement coordinates ❌ FAILED (missing geometry)
3. **Multi-material determinism** — Each material independently runs shelf algorithm, results merge consistently ✅ PASSED
4. **Settings persistence** — Report settings save/load with project, PDF generation uses persisted values ✅ PASSED

**Test Results (2026-03-14T19:59:29Z):**
- `dotnet test .\PanelNester.slnx` → **99 passed, 2 skipped, 0 failures**
- `npm run build` → **passed**
- Bridge round-trip tests: `run-batch-nesting`, `update-report-settings`, `export-pdf-report` (success paths only)
- Integration tests: multi-material nesting, project persistence, Three.js viewer rendering

**Rejection Verdict:**

Two critical gaps prevent gate clearance:

1. **PDF Sheet Visuals Missing (Critical)** — PRD §6.7 requires sheet visuals in PDF reports. Current `QuestPdfReportExporter` renders text tables of placements and unplaced items but **does not** render sheet diagrams/geometry. That fails the PDF accuracy gate.

2. **Failure-Surface Coverage Too Thin (Critical)** — Phase 5 matrix explicitly calls for repeatable coverage of:
   - Cancelled PDF save dialog
   - File-write / export failure
   - No-result export attempt
   Current tests only cover the success path. Export reliability cannot be claimed without failure-path evidence.

**Locked-Out Agents:**
- Parker (revision locked)
- Bishop (revision locked)
- Dallas (revision locked)
- Revision owner: Ripley (or non-author reviewer) must authorize next phase

**Required Corrections Before Re-Review:**
1. Add actual PDF sheet visuals (QuestPDF Canvas rectangles matching Three.js placement payload)
2. Add repeatable test coverage for export failure paths
3. Re-run baseline: `dotnet test .\PanelNester.slnx` + `npm run build`

**Next Boundary:**
- Phase 5 remains open pending correction cycle
- Phase 6 should **not** start until Phase 5 gate clears
- Ripley owns authorization for revision architect and timeline

---

**Phase 2 Scope (Material Library Tests & Verification)

**Ownership:** Hicks (Tests and integration review gate)

**Deliverables:**
1. `MaterialRepositoryTests` — CRUD operations, persistence round-trip, concurrent access, edge cases
2. `MaterialValidationTests` — Unique name rejection, dimension bounds, required fields, error codes
3. `MaterialBridgeTests` — Request/response contract compliance, error mapping, handler behavior
4. Updated smoke-test guide (.squad/smoke-test-guide.md) — Material workflow scenarios
5. Final integration review gate before Phase 2 close

**Interfaces Consumed:**
- Parker's repository and validation services (via integration)
- Bishop's bridge handlers (via contract compliance)

**Interfaces Owned:**
- Test suites for all material operations
- Acceptance criteria matrix for material workflows
- Updated smoke-test guide with material CRUD scenarios

**Dependencies:** All three workstreams (Parker, Bishop, Dallas) — final review gate

**Parallel Execution:**
- Days 1-3: Scaffold tests, begin spec-first suites, collaborate with parallel workstreams
- Day 4: Integration tests, smoke guide update, final gate verdict

**Success Criteria:**
- All material CRUD operations have test coverage
- Persistence verified across app restart
- Bridge contract compliance verified
- Smoke-test guide updated with material workflows
- All 38+ tests passing (no new skips)
- Ready-to-ship verdict for Phase 2 close
