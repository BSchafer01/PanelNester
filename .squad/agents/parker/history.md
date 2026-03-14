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

## Phase 2 Scope (Material Library CRUD)

**Ownership:** Parker (Domain/Services foundation)

**Deliverables:**
1. `IMaterialRepository` interface in `PanelNester.Domain.Contracts` — CRUD contract with async methods
2. `JsonMaterialRepository` implementation in `PanelNester.Services/Materials` — JSON file persistence at `%LOCALAPPDATA%\PanelNester\materials.json`
3. `MaterialValidationService` in `PanelNester.Services/Materials` — Business rules: unique names, positive dimensions, required fields
4. Unit tests for both repository and validation service

**Interfaces Owned:**
- `IMaterialRepository { GetAllAsync(), GetByIdAsync(id), CreateAsync(material), UpdateAsync(material), DeleteAsync(id) }`
- Repository returns domain `Material` records
- Validation throws `MaterialValidationException` with machine-readable codes

**Dependencies:** None — can start immediately

**Parallel Workstreams:**
- Bishop (Desktop bridge contracts and handlers) — depends on this interface, not implementation
- Dallas (WebUI CRUD UI) — can stub bridge responses initially
- Hicks (Tests and integration gate) — final workstream dependency

**Success Criteria:**
- IMaterialRepository interface contract agreed and stable
- JsonMaterialRepository round-trip tested (write, read, verify persistence across app restart)
- Validation logic tested for all edge cases (duplicate names, negative dimensions, missing fields)
- All tests passing before handoff to Bishop/Dallas for integration

## Learnings

- 2026-03-14: Initial team staffing. I own import, validation, domain services, and the nesting engine.
- 2026-03-14: Phase 0/1 backend contracts stay cleaner if import keeps quantity on compact `PartRow` records, nesting expands instances just-in-time, and all sheet geometry stays in `decimal` with kerf treated as added spacing instead of part resizing.
- 2026-03-14: Parallel agent execution and decision-driven architecture allow teams to de-risk at seams early and move fast once contracts stabilize.
