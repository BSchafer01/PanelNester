# Squad Decisions — Phase 3

## Strategic Context

**Architecture:** PanelNester—local desktop tool for importing rectangular parts, nesting by material, visualizing sheet layouts, exporting PDF summaries.

**Current Phase:** Phase 3 — Project Management & Persistence  
**Status:** COMPLETED ✅ (2026-03-14T18:14:59Z)  
**Approval Status:** All three layers integrated and tested. Phase 4 ready.

---

## Implementation Phases (Strategic Decision)

**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Proposed

- **Phase 0:** WPF shell, WebView2 integration, message bridge (COMPLETED)
- **Phase 1:** Vertical slice—CSV import, single-material nesting, basic results (COMPLETED)
- **Phase 2:** Material library—CRUD, local persistence, material selection (IN PROGRESS)
- **Phase 3:** Project management—Create/save/open projects, metadata editing, JSON persistence, snapshot materials (IN PROGRESS)
- **Phase 4:** Full import pipeline—XLSX, validation, inline editing
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

## Historical Reference

Earlier Phase 0/1 and Phase 2 decisions have been archived to `decisions-archive.md` for historical reference while maintaining operational focus on Phase 3. All archived decisions remain valid and in-scope for future phases.

**Archive Date:** 2026-03-14T17:56:50Z
