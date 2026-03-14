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

## Phase 3 — Project Persistence & Material Snapshots (IN PROGRESS)

**Ownership:** Parker (Domain/Services foundation) 🚀

**Assignment:** Domain models, `IProjectService` interface, `ProjectSerializer` JSON serialization with version handling

**Deliverables:**
1. Domain models: `Project`, `ProjectMetadata`, `ProjectSettings`, `ProjectState`
2. `IProjectService` interface with `NewAsync`, `LoadAsync`, `SaveAsync`, `UpdateMetadataAsync`
3. `ProjectSerializer` for JSON round-trip with schema versioning
4. Unit tests for serialization, version handling, snapshot behavior

**Key Decisions:**
- JSON file format (`.pnest` extension) — matches material library pattern, human-readable, no SQLite overhead
- Project snapshots materials at save time — prevents corruption when library materials are renamed/deleted
- Schema version: 1 — forward compatible
- Material snapshot interaction: existing projects use snapshots, new imports use live library

**Parallel Workstreams:**
- Bishop (Desktop bridge layer): Bridge contracts and handlers
- Dallas (WebUI): Project page and metadata form
- Hicks (Tests & review): Service tests, bridge tests, smoke guide update

**Execution Timeline:**
- Day 1: Domain models + `IProjectService` interface stub
- Day 2: `ProjectSerializer` implementation + unit tests
- Day 3: `ProjectService` orchestration + snapshot logic
- Day 4: Bug fixes from integration

## Learnings

- 2026-03-14: Initial team staffing. I own import, validation, domain services, and the nesting engine.
- 2026-03-14: Phase 0/1 backend contracts stay cleaner if import keeps quantity on compact `PartRow` records, nesting expands instances just-in-time, and all sheet geometry stays in `decimal` with kerf treated as added spacing instead of part resizing.
- 2026-03-14: Parallel agent execution and decision-driven architecture allow teams to de-risk at seams early and move fast once contracts stabilize.
- 2026-03-14: Phase 3 extends Phase 2 architecture: projects persist as JSON files with metadata, settings, and material snapshots. Design keeps repo/serializer seams clean by versioning the schema early and deferring XLSX/multi-material/export to later phases.
