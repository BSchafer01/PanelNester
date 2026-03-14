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

## Learnings

- 2026-03-14: Initial team staffing. I own import, validation, domain services, and the nesting engine.
- 2026-03-14: Phase 0/1 backend contracts stay cleaner if import keeps quantity on compact `PartRow` records, nesting expands instances just-in-time, and all sheet geometry stays in `decimal` with kerf treated as added spacing instead of part resizing.
- 2026-03-14: Parallel agent execution and decision-driven architecture allow teams to de-risk at seams early and move fast once contracts stabilize.
