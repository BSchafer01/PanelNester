# Squad Decisions — Phase 4

## Strategic Context

**Architecture:** PanelNester—local desktop tool for importing rectangular parts, nesting by material, visualizing sheet layouts, exporting PDF summaries.

**Current Phase:** Phase 4 — Full Import Pipeline  
**Status:** DESIGN REVIEW COMPLETE ✅ (2026-03-14T18:34:43Z) | READY TO IMPLEMENT  
**Approval Status:** Phase 3 cleared all reviewer gates. Phase 4 design review complete. Implementation sequence assigned and ready to begin.

---

## Implementation Phases (Strategic Decision)

**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Proposed

- **Phase 0:** WPF shell, WebView2 integration, message bridge (COMPLETED)
- **Phase 1:** Vertical slice—CSV import, single-material nesting, basic results (COMPLETED)
- **Phase 2:** Material library—CRUD, local persistence, material selection (COMPLETED)
- **Phase 3:** Project management—Create/save/open projects, metadata editing, JSON persistence, snapshot materials (COMPLETED)
- **Phase 4:** Full import pipeline—XLSX, validation, inline editing (READY)
- **Phase 5:** Results viewer & PDF reporting—Three.js, QuestPDF, editable fields
- **Phase 6:** Polish & edge cases—error handling, performance, UX refinement

---

## Phase 3 Decisions

### Decision: Parker — Phase 3 Project Persistence

**Author:** Parker | **Date:** 2026-03-14 | **Status:** Active

#### Context
Phase 3 requires a stable project service interface and JSON serialization contract before Bishop (desktop bridge) and Dallas (Web UI) implement their layers.

#### Decision
- `IProjectService` interface in `src/PanelNester.Domain/Services/IProjectService.cs`
- Six core methods: `CreateProjectAsync`, `OpenProjectAsync`, `SaveProjectAsync`, `SaveProjectAsAsync`, `GetProjectMetadataAsync`, `UpdateProjectMetadataAsync`
- `ProjectSerializer` in `src/PanelNester.Services/Serialization/ProjectSerializer.cs` for versioned JSON
- Project file format: `.pnest` (versioned JSON envelope)
- Project payload: metadata (name, created, modified) + import state + last nesting result + settings + material snapshots
- Material snapshots captured at project creation to preserve configuration across nesting runs

#### Consequences
- Bridge layer (Bishop) can wire file dialogs and handlers to stable contracts
- Web UI (Dallas) can target predictable project get/update payloads
- Serialization format supports forward/backward compatibility through versioning

---

### Decision: Bishop — Phase 3 Desktop Bridge & File Dialogs

**Author:** Bishop | **Date:** 2026-03-14 | **Status:** In Progress

#### Context
Desktop host must implement typed bridge contracts for project messages and integrate with `IProjectService` while maintaining backward compatibility with Phase 0/1 seams.

#### Decision
- Six bridge message types in `src/PanelNester.Desktop/Bridge/BridgeContracts.cs`:
  - `new-project` → `new-project-response`
  - `open-project` → `open-project-response`
  - `save-project` → `save-project-response`
  - `save-project-as` → `save-project-as-response`
  - `get-project-metadata` → `get-project-metadata-response`
  - `update-project-metadata` → `update-project-metadata-response`
- Error codes: `project-not-found`, `project-corrupt`, `project-unsupported-version`, `project-save-failed`
- Native `.pnest` file dialogs (WPF OpenFileDialog/SaveFileDialog) for open/save-as
- Handlers registered in bridge dispatcher and wired to `IProjectService`
- Bridge test coverage: round-trip, metadata, error handling
- No changes to Phase 0/1 bridge vocabulary (`bridge-handshake`, `import-csv`, `run-nesting`)

#### Consequences
- Desktop bridge fully functional for project operations independent of Web UI
- File dialogs follow platform conventions (.pnest filter)
- Materialsnapshots preserved across project save/open cycles
- Import state and nesting results recoverable on project open

---

### Decision: Hicks — Phase 3 Review Gate (Snapshot-First)

**Author:** Hicks | **Date:** 2026-03-14 | **Status:** In Progress

#### Context
Phase 3 adds persistence, snapshot capture, and metadata workflows. Review gate must validate round-trip integrity, material snapshot isolation, and error recovery before final approval.

#### Decision
- **Snapshot-First Review Gate:** Active
- Test matrix: Phase 3 Persistence/Snapshot Matrix (`tests/Phase3-Persistence-Matrix.md`)
- Bridge round-trip tests: new/open/save/save-as workflows
- Snapshot consistency tests: capture at creation, preserve across saves, restore on open
- Metadata get/update tests: title, description, settings
- Error recovery: corrupt file handling, version mismatch, save failure
- Baseline: `dotnet test .\PanelNester.slnx` → all Phase 3 tests passing

#### Gate Status
- ✓ Bridge contracts typed and implemented
- ✓ Service layer integration tested
- ✓ Serialization round-trip validated
- ✓ Web UI Phase 3 implementation complete (2026-03-14T18:14:59Z)
- ✓ **GATE CLEARED** — All three layers integrated and tested
- ✓ Final approval: **GRANTED** — Ready for Phase 4

#### Consequences
- Backend persistence infrastructure gated at high confidence
- Web UI integration complete with all bridge operations
- Manual smoke-test validates end-to-end project workflow
- Phase 3 FULLY COMPLETE and ready for Phase 4 design
- Full project lifecycle operational: new → save → open → edit → save-as

---

### Decision: Dallas — Phase 3 Project UI

**Author:** Dallas | **Date:** 2026-03-14 | **Status:** COMPLETED ✅

#### Context
Web UI must implement project page (create/open/save/save-as) and metadata editing form while coordinating with Bishop's bridge and Hicks' test gate.

#### Decision
- Project page in `src/PanelNester.WebUI/src/pages/Project.tsx`
- Bridge message handling for all six project operations
- Project form component for metadata editing (name, description, settings)
- Recent projects list and new-project button
- Open/save-as dialogs trigger native file dialogs through bridge
- Project metadata display after successful open
- Error handling displays bridge error codes to user
- Integration points: `window.hostBridge.receive` for project responses

#### Delivery (2026-03-14T18:14:59Z)
✅ **COMPLETED**
- Project page with full metadata form (projectName, projectNumber, customerName, estimator, drafter, pm, date, revision, notes)
- Bridge message handlers for all six project operations wired and tested
- Dirty-state tracking in app shell with navigation guards
- Material snapshot display showing saved vs pending materials
- TypeScript project contracts aligned with Bishop's bridge definitions
- Web UI build passing
- Round-trip project workflows validated: new → save → open → edit → save-as

#### Consequences
- Project management workflow fully available to end users
- Metadata persistence via project JSON complete
- Material snapshot consumption in project open flow operational
- **Phase 3 FULLY COMPLETE** — Parker domain, Bishop bridge, and Dallas Web UI all integrated
- **Hicks' review gate CLEARED** — Final approval granted
- Phase 4 design can proceed with confidence in complete project persistence layer

---

## Phase 4 Decisions

### Decision: Ripley — Phase 4 Full Import Pipeline Design Review

**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Approved

#### Context

Phase 3 (project persistence) is fully approved. Phase 4 is the next boundary: hardening the import pipeline with XLSX support, richer validation, and inline editing. This review defines the exact shippable slice, seam contracts, ownership splits, and constraints.

#### Phase 4 Slice Definition (In Scope)

1. **XLSX Import** — ClosedXML-based reader, same `ImportResponse` contract, first-sheet-only in v1
2. **Unified File Import Dispatcher** — Routes `.csv` → `CsvImportService`, `.xlsx` → `XlsxImportService` by extension
3. **Part Row Editing** — Update, delete, and add part rows after import with automatic re-validation
4. **Re-validation Pipeline** — Runs the same validation rules on edited rows against current material library
5. **Bridge Extensions** — New messages for unified import and row editing operations
6. **Import Page UI Hardening** — Inline editing controls, add/delete rows, filter by material and validation status, sort columns

#### Seam Ownership

- **Parker:** `IPartEditorService` interface, `PartRowUpdate` DTO, `PartRowValidator` extraction, `XlsxImportService`, `FileImportDispatcher`, `PartEditorService`
- **Bishop:** Bridge messages (`import-file`, `update-part-row`, `delete-part-row`, `add-part-row`), file dialog filter update, state management for edits
- **Dallas:** Import page UI refactor (file button, inline editing, add/delete rows, filter/sort), TypeScript contracts
- **Hicks:** XLSX import tests, dispatcher tests, part editor tests, bridge round-trip tests, integration gate

#### Implementation Sequence

**Batch 1 (Day 1): Contracts + Foundation**
- Parker: interfaces, DTOs, validation extraction, service implementations
- Hicks: parametrized tests against stable contracts

**Batch 2 (Day 2): Bridge + UI (Parallel)**
- Bishop: bridge handlers
- Dallas: UI implementation

**Batch 3 (Day 3): Integration Gate**
- Hicks: end-to-end validation (import XLSX → edit rows → revalidate → run nesting)

#### Constraints

1. ClosedXML reads first worksheet only (no sheet selection UI in v1)
2. `PartRowUpdate` uses string fields (service parses and validates, not UI)
3. Each edit operation returns the full `ImportResponse` (no partial updates, no client-side validation divergence)
4. Material name matching stays exact (case-sensitive, ordinal comparison, per PRD §5)
5. `import-csv` bridge message not removed (backward compatible)
6. Filter/sort is client-side only (no additional bridge messages needed)

#### Consequences

- Import pipeline supports both CSV and XLSX with identical validation behavior
- Users can fix import errors in-app instead of returning to their spreadsheet
- Validation stays in .NET domain (no client-side validation divergence)
- Phase 5 (results viewer + PDF) can assume clean, validated part data
- Bridge vocabulary grows additively; no breaking changes to Phase 0-3 messages

---

### Decision: Hicks — Phase 4 Test Gate

**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved

#### Context

Phase 4 is the first slice that can damage trust across three already-approved seams at once: the Phase 1 CSV import path, the Phase 3 project persistence path, and the user-visible import table in the Web UI. Current code is still CSV-only and read-only at the import-table level, so Phase 4 needs an explicit reviewer gate before implementation spreads assumptions across services, bridge contracts, and UI behavior.

#### Four Non-Negotiable Review Gates

1. **Regression safety:** Current CSV import, nesting, and `.pnest` persistence behavior cannot regress while XLSX/editing land
2. **Format parity:** Equivalent CSV and XLSX data must yield the same row payload shape, validation statuses, and actionable error/warning codes
3. **Edit persistence:** Inline import-table changes must revalidate immediately and survive save/open exactly, including deletes and validation messages
4. **Failure clarity:** Missing materials, bad numerics, corrupt workbooks, and multi-issue rows must stay user-visible and specific; no crashes, hangs, or silent drops

#### Test Coverage

- **XLSX Import Tests** — Required headers, column order flexibility, empty file, multi-sheet, corrupt file, Unicode round-trip
- **File Import Dispatcher Tests** — `.csv` and `.xlsx` routing, unknown extensions → `unsupported-file-type`
- **Part Editor Tests** — Update valid/error rows, delete with recalculation, add with auto-rowId, non-existent rowId error, revalidate with material library changes
- **Bridge Round-Trip Tests** — `import-file` CSV parity, `import-file` with XLSX, `update-part-row`, `delete-part-row`, `add-part-row`
- **Integration Tests** — End-to-end: import XLSX → edit rows → revalidate → run nesting

#### Consequences

- Teams can implement against a stable review target instead of vague "import hardening" language
- Any Phase 4 batch that ships XLSX support but leaves edits non-persistent should be rejected
- Any Phase 4 batch that ships inline editing with generic failure messaging should be rejected
- The smoke guide and Phase 4 matrix track the same reviewer gate, so manual and automated evidence converge

---

## Historical Reference

Earlier Phase 0, Phase 1, and Phase 2 decisions have been archived to `decisions-archive.md` for historical reference while maintaining operational focus on Phase 3 and 4. All archived decisions remain valid and in-scope for future phases.

**Archive Date:** 2026-03-14T17:56:50Z
**Phase 4 Decisions Added:** 2026-03-14T18:34:43Z
