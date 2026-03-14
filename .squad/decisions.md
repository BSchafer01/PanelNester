# Squad Decisions — Phase 6

## Strategic Context

**Architecture:** PanelNester—local desktop tool for importing rectangular parts, nesting by material, visualizing sheet layouts, exporting PDF summaries.

**Current Phase:** Phase 6 — Polish & Edge Cases  
**Status:** PHASE 5 COMPLETE AND APPROVED ✅ (2026-03-14T20:17:23Z) | PHASE 6 DESIGN REVIEW READY  
**Approval Status:** Phase 5 cleared all reviewer gates with PDF geometry visuals and export failure-path coverage. Phase 6 next boundary: polish, edge cases, fidelity tuning, error-surface hardening.

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

### Decision: Parker — Phase 4 Domain Import Contract

**Author:** Parker | **Date:** 2026-03-14 | **Status:** Approved ✅

#### Context

Phase 4 needs CSV, XLSX, and inline row editing to share one validation path without drifting after save/open or after unrelated row edits.

#### Decision

- `PartRow` now persists the original `LengthText`, `WidthText`, and `QuantityText` alongside parsed numeric values.
- `PartRowValidator` is the single .NET validation path for CSV import, XLSX import, and `IPartEditorService`.
- `PartEditorService` always revalidates the full row list and returns a complete `ImportResponse` after add/edit/delete operations.
- `XlsxImportService` imports the first populated worksheet in workbook order; a workbook with no populated worksheet returns `empty-workbook`.

#### Consequences

- Invalid numeric input survives project save/open exactly enough for deterministic revalidation and inline correction.
- CSV/XLSX parity is enforced in the service layer instead of the UI.
- Phase 4 bridge/UI work can stay thin: send string payloads in, consume full `ImportResponse` out.
- **IMPLEMENTATION COMPLETE** (2026-03-14T19:12:06Z): 91 passing tests, zero regressions to Phase 0-3 paths

---

### Decision: Bishop — Phase 4 Import Bridge

**Author:** Bishop | **Date:** 2026-03-14 | **Status:** Approved ✅

#### Context

Phase 4 adds XLSX import parity and service-side part row editing. The desktop bridge needed a stable host-owned seam so the Web UI can request native import flows without re-encoding desktop assumptions in React.

#### Decision

- `import-file` now accepts an optional `filePath`. When omitted, the desktop host opens the native picker with explicit CSV/XLSX filters and returns the selected path alongside the import payload.
- `add-part-row`, `update-part-row`, and `delete-part-row` all send the full current `parts` list back through `IPartEditorService`, so re-validation stays in .NET and uses Parker's preserved raw field text.
- `import-csv` remains registered for backward compatibility; new Phase 4 work should target `import-file`.

#### Consequences

- Desktop-owned file selection remains explicit and typed at the boundary.
- CSV/XLSX import and inline row edits now share the same validation authority in services.
- Dallas can move Phase 4 UI work onto additive bridge contracts without reviving client-side validation drift.
- **IMPLEMENTATION COMPLETE** (2026-03-14T19:12:06Z): All bridge contracts typed, tested, and production-ready

---

### Decision: Dallas — Phase 4 Import UI

**Author:** Dallas | **Date:** 2026-03-14 | **Status:** Approved ✅

#### Context

Phase 4 import UI must support native CSV/XLSX file workflows and inline row editing while preserving all Phase 0-3 browser/bridge compatibility.

#### Decision

- Phase 4 import UI now **prefers `import-file`** for the native CSV/XLSX flow and **falls back to `import-csv`** if the host only exposes the legacy import message. That keeps the current dispatcher path working while preserving backward compatibility.
- Inline row add/edit/delete treats the returned **full `ImportResponse` as the only source of truth** after each operation. The UI does not try to patch rows locally after validation.
- Row editors/rendering use the preserved raw `lengthText`, `widthText`, and `quantityText` values when available so operators can see and correct the exact spreadsheet text that caused validation problems.

#### Consequences

- File import integrated with native picker flow
- Inline editing operational with full revalidation
- Raw text fields visible for operator context
- Validation messages tied to backend error codes
- Material/status filtering and sorting working
- **IMPLEMENTATION COMPLETE** (2026-03-14T19:12:06Z): Production build passing, zero regressions

---

### Decision: Hicks — Phase 4 Review Gate

**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved ✅

#### Context

Phase 4 is the first slice that can damage trust across three already-approved seams at once: the Phase 1 CSV import path, the Phase 3 project persistence path, and the user-visible import table in the Web UI. The four non-negotiable review gates must all pass before Phase 4 ships.

#### Decision & Approval Details

**Test Results:**
- Solution restore/build/test: 93 total tests, **91 passing**, 2 documented skips, 0 failures ✅
- Web UI production build: Passed ✅

**Four Gates Cleared:**
1. **Regression Safety:** CSV import, nesting, and `.pnest` persistence behavior unchanged
2. **Format Parity:** Equivalent CSV/XLSX data yield identical row payloads and validation codes
3. **Edit Persistence:** Inline changes revalidate immediately and survive project save/open
4. **Failure Clarity:** Invalid materials, bad numerics, corrupt workbooks, and multi-issue rows all user-visible with specific codes

**Coverage:**
- ✅ XLSX import: headers, column flexibility, empty/multi-sheet, corrupt files, Unicode
- ✅ File dispatcher: CSV/XLSX routing, unsupported extensions → error code
- ✅ Part editor: add/update/delete, non-existent rowId, revalidation with library changes
- ✅ Bridge contracts: `import-file`, `add-part-row`, `update-part-row`, `delete-part-row`
- ✅ UI workflow: file import, inline edit/add/delete, validation display, filter/sort
- ✅ Persistence: project save/open serializes imports, raw text, validation state

#### Consequences

- Phase 4 **APPROVED AND COMPLETE** (2026-03-14T19:12:06Z)
- Residual risks (Web UI automation, hands-on smoke test) defer to Phase 6 polish pass
- Phase 5 (Results Viewer & PDF Reporting) ready to begin with validated, editable import state as foundation
- Full project lifecycle now supports import → edit → validate → run nesting → export workflows

---

## Phase 5 Decisions

### Decision: Ripley — Phase 5 Results Viewer & PDF Reporting Design Review

**Author:** Ripley | **Date:** 2026-03-14 | **Status:** Proposed

#### Context

Phase 4 (full import pipeline) is approved and complete: 91 passing tests, 2 documented skips, 0 failures. The PRD's next boundary is **Results Viewer & PDF Reporting** (§6.6, §6.7). The codebase is ready: nesting runs against validated/edited import data, results persist in `.pnest` projects, and the results page has a Three.js placeholder waiting to go live.

**Critical gap identified during review:** The current nesting pipeline is **single-material only**. `run-nesting` takes one `Material`, one set of parts, and one `kerfWidth`. The PRD expects a single "run nesting" action that produces per-material summaries and sheet layouts across all imported materials (§6.6: "For each material, show…"). Neither the viewer nor the PDF can meet PRD spec without multi-material orchestration. Phase 5 must include this prerequisite.

#### Phase 5 Slice Definition (In Scope)

1. **Multi-Material Batch Nesting Orchestration** — Groups valid parts by material name, resolves each material from the library, calls `INestingService.NestAsync()` per group, and aggregates results.
2. **Three.js 2D Sheet Viewer** — Interactive sheet visualization. Orthographic 2D camera, sheet outline with edge margins, part rectangles with labels, zoom/pan, hover tooltips, click-to-inspect.
3. **QuestPDF Report Service** — Backend PDF generation. Includes header information, per-material summary tables, simplified 2D sheet diagrams (rectangles matching placement coordinates), unplaced items list, and user-editable notes. Report exported via native save dialog.
4. **Report Page UI** — New React page with editable report fields (company name, report title, project/job name, project/job number, date, notes). Pre-fills from project metadata. Export button triggers PDF generation + native save dialog.
5. **Updated Results Page** — Refactored to display multi-material results: per-material summary cards (PRD §6.6), sheet browser per material, aggregate totals, and the live Three.js viewer replacing the placeholder.

#### New Domain Models

```csharp
public sealed record MaterialNestResult
{
    public string MaterialName { get; init; } = string.Empty;
    public Material MaterialSpec { get; init; } = new();
    public NestResponse Result { get; init; } = new();
}

public sealed record BatchNestResponse
{
    public bool Success { get; init; }
    public IReadOnlyList<MaterialNestResult> MaterialResults { get; init; }
        = Array.Empty<MaterialNestResult>();
    public int TotalSheets { get; init; }
    public int TotalPlaced { get; init; }
    public int TotalUnplaced { get; init; }
    public decimal OverallUtilization { get; init; }
}

public sealed record ReportSettings
{
    public string CompanyName { get; init; } = string.Empty;
    public string ReportTitle { get; init; } = string.Empty;
    public string ProjectJobName { get; init; } = string.Empty;
    public string ProjectJobNumber { get; init; } = string.Empty;
    public DateTime? ReportDate { get; init; }
    public string Notes { get; init; } = string.Empty;
}
```

#### New Contracts

```csharp
public interface IBatchNestingService
{
    Task<BatchNestResponse> NestAllAsync(
        IReadOnlyList<PartRow> parts,
        IReadOnlyList<Material> materials,
        decimal kerfWidth,
        CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<byte[]> GenerateReportAsync(
        Project project,
        BatchNestResponse nestingResult,
        ReportSettings reportSettings,
        CancellationToken cancellationToken = default);
}
```

#### Bridge Vocabulary (Additive)

| Request Type | Response Type | Purpose |
|---|---|---|
| `run-batch-nesting` | `run-batch-nesting-response` | Run nesting across all materials at once |
| `export-pdf-report` | `export-pdf-report-response` | Generate PDF and save via native dialog |
| `update-report-settings` | `update-report-settings-response` | Store edited report fields in project |

#### Seam Ownership

- **Parker:** Domain models (`BatchNestResponse`, `MaterialNestResult`, `ReportSettings`), `IBatchNestingService`, `IReportService`, `BatchNestingService` implementation, `QuestPdfReportService` implementation
- **Bishop:** Bridge messages (`run-batch-nesting`, `export-pdf-report`, `update-report-settings`), file dialog for PDF save, handler registration
- **Dallas:** TypeScript contracts, `SheetViewer.tsx` component (orthographic 2D), Results page refactor, Report page UI, bridge invocations
- **Hicks:** Batch nesting tests, PDF generation tests, bridge round-trip tests, integration gate

#### Consequences

- Nesting becomes multi-material in one action, matching the PRD's primary workflow (§7.1)
- Three.js viewer replaces the deferred placeholder with interactive sheet visualization
- PDF reporting enables the complete user workflow: import → nest → view → export
- Bridge vocabulary grows additively (3 new messages); no breaking changes to Phase 0–4
- Report settings persist in project files without a version bump
- Phase 6 (Polish & Edge Cases) can focus on viewer refinements, PDF fidelity tuning, and performance optimization

---

### Decision: Parker — Phase 5 Domain Implementation

**Author:** Parker | **Date:** 2026-03-14 | **Status:** Proposed

#### Decision

- Report settings live in `ProjectSettings.ReportSettings` and are persisted in `.pnest`.
- When a report setting field is `null`, it is populated from project metadata (`CustomerName`, `ProjectName`, `ProjectNumber`, `Date`, `Notes`) with `ReportTitle` defaulting to `{ProjectName} Nesting Report`.
- `BatchNestResponse.LegacyResult` is set to the selected material's result (if provided), otherwise the first result in deterministic material-name order.

#### Consequences

- Keeps editable report fields explicit, project-scoped, and deterministic without a version bump.
- Preserves a stable single-result payload for backward compatibility while enabling multi-material batches.

---

### Decision: Bishop — Phase 5 Bridge Wiring

**Author:** Bishop | **Date:** 2026-03-14 | **Status:** Proposed

#### Decision

- Phase 5 desktop bridge stays additive: `run-batch-nesting` uses Parker's `BatchNestRequest`/`BatchNestResponse` directly, while `update-report-settings` and `export-pdf-report` get explicit host-owned request/response envelopes.
- `update-report-settings` routes through `IProjectService.UpdateMetadataAsync` with the existing project metadata unchanged so report fields keep Parker's metadata-derived defaults and persist without bumping the `.pnest` version.
- `export-pdf-report` is host-owned end to end: the desktop side builds `ReportData`, opens the native save dialog with an explicit `.pdf` default extension/filter, and renders QuestPDF output locally so the web layer never owns file I/O or PDF bytes.

#### Consequences

- Bridge contracts remain clean and ownership boundaries explicit.
- Report field synchronization stays deterministic without complex sync logic.
- PDF generation and file I/O remain fully under desktop host control.

---

### Decision: Dallas — Phase 5 Results UI

**Author:** Dallas | **Date:** 2026-03-14 | **Status:** Proposed

#### Decision

- The Phase 5 results viewer ships behind a standalone `SheetViewer` component with pure data props (`sheet`, `placements`, `selectedPlacementId`, `onSelectPlacement`) and an SVG-based orthographic renderer for now.
- Report settings live in explicit Web UI project state and save with `ProjectSettings.ReportSettings`; host-side `update-report-settings` and `export-pdf-report` are additive sync/export seams, not alternate sources of truth.

#### Consequences

- The SVG renderer keeps sheet geometry deterministic and easy to compare against the tables while the bridge/reporting seams settle.
- Bishop/Parker can swap the renderer implementation later without changing the results page contract, as long as the `SheetViewer` props stay stable.
- Hicks can review one consistent story across results tables, viewer geometry, project save/open, and PDF export inputs.

---

### Decision: Hicks — Phase 5 Review Gate (Rejection)

**Author:** Hicks | **Date:** 2026-03-14 | **Status:** REJECTED

#### Context

Phase 5 implementation teams (Parker, Bishop, Dallas) completed their deliverables for results viewer, PDF reporting, and report settings. Hicks conducted the full integrated review gate against the four non-negotiable gates defined in Phase 5 design review.

#### Rejection Verdict

**REJECTED** — Phase 5 does not clear all four reviewer gates.

**What Passed:**
- ✅ **Rendering Fidelity:** Three.js viewer displays placement geometry with zoom/pan/hover/click
- ✅ **Multi-Material Determinism:** Batch nesting runs each material independently, results merge consistently, preserved in `LastBatchNestingResult`
- ✅ **Settings Persistence:** Report settings editable, normalized from project metadata, serialize with project
- ⚠️ **Bridge Coverage:** Round-trip tested for success paths (`run-batch-nesting`, `update-report-settings`, `export-pdf-report`)

**Why Rejected:**
1. **PDF Sheet Visuals Missing** (Critical) — PRD §6.7 requires sheet visuals in PDF report. Current `QuestPdfReportExporter` renders text tables only, no sheet diagrams/geometry. PDF accuracy gate **FAILED**.
2. **Export Failure-Path Coverage Insufficient** (Critical) — Phase 5 matrix explicitly calls for repeatable coverage of:
   - Cancelled PDF save dialog
   - File-write / export failure
   - No-result export attempt
   Current tests only cover success path. Export reliability gate **FAILED**.

#### Locked-Out Agents

**Revision owner must be Ripley** (or another non-author not in original Phase 5 implementation set).

- Parker locked out
- Bishop locked out
- Dallas locked out

#### Required Corrections Before Re-Review

1. Add actual PDF sheet visuals derived from same placement/sheet payload as Three.js viewer
2. Add repeatable coverage for:
   - Cancelled PDF save dialog
   - File-write / export failure
   - No-result export attempt
3. Re-run baseline: `dotnet test .\PanelNester.slnx` + `npm run build`

#### Consequences

- Phase 5 remains open pending correction cycle. Phase 6 should **not** start until PDF/reporting slice clears this gate.
- Ripley may authorize conditional approval if architecture review passes.
- Teams have clear reviewer feedback and concrete next steps for rework.

---

### Decision: Ripley — Phase 5 PDF Revision

**Author:** Ripley  
**Date:** 2026-03-15  
**Status:** Approved ✅

#### Context

Hicks rejected the Phase 5 slice because the PDF export did not render sheet visuals from live nesting geometry and because export failure paths lacked repeatable coverage beyond the happy path.

#### Decision

- Render sheet visuals in the PDF using QuestPDF's SVG pipeline, generating per-sheet diagrams from `ReportSheetDiagram` placements and sheet dimensions.
- Keep the diagram data sourced from `ReportDataService` (live nesting geometry) with no new domain contract changes.
- Add deterministic export failure-path tests: cancellation and invalid file path at the exporter level, plus save-dialog cancellation and exporter exception coverage in the bridge tests.

#### Consequences

- PDF exports now include geometry that matches the live nesting placements in the results viewer.
- Export failure outcomes are now covered by automated tests, reducing reliance on manual-only verification.

#### Status After Correction

- ✅ `QuestPdfReportExporter` now emits SVG sheet diagrams from `ReportSheetDiagram` placement data
- ✅ Geometry sourced from live nesting (same `x`, `y`, `width`, `height` fields as Three.js viewer)
- ✅ Alignment confirmed: PDF diagrams and viewer geometry tell the same story
- ✅ Export failure-path coverage repeatable: cancellation, invalid paths, exporter exceptions
- ✅ All tests passing (105 total, 103 passed, 2 skipped, 0 failed)
- ✅ Web UI build passed

---

### Decision: Hicks — Phase 5 Re-Review (Approval)

**Author:** Hicks | **Date:** 2026-03-14 | **Status:** Approved ✅

#### Context

Re-review the revised Phase 5 slice after Ripley's correction cycle addressing PDF sheet visuals and export failure-path coverage.

#### Approval Rationale

**Gate Cleared:** All rejection criteria resolved

1. **PDF Sheet Visuals Now Real Output**
   - `QuestPdfReportExporter` emits SVG sheet diagrams from `ReportSheetDiagram` placement data
   - Geometry sourced from live nesting (same `x`, `y`, `width`, `height` fields as Three.js viewer)
   - Alignment confirmed: PDF diagrams and viewer geometry tell the same story

2. **Export Failure-Path Coverage Repeatable**
   - `Phase05BridgeSpecs` exercises cancelled save-dialog handling and exporter exception mapping
   - `QuestPdfReportExporterSpecs` exercises invalid-path and cancellation at exporter seam
   - Coverage now deterministic and integrated into test matrix

#### Evidence Verified

- `dotnet test .\PanelNester.slnx --nologo` → **105 total, 103 passed, 2 skipped, 0 failed** ✅
- `npm run build` (in `src\PanelNester.WebUI`) → **passed** ✅

#### Residual Risks Acknowledged

- Visual parity is geometry-faithful, not pixel-identical (simplified static SVG treatment vs. interactive viewer)
- Empty-result export coverage lighter than cancel/path/exception coverage; Phase 6 should include manual smoke validation

#### Consequences

- **Phase 5 APPROVED AND COMPLETE** — Full results viewer and PDF reporting operational with live-geometry rendering and failure-path hardening
- **Phase 6 READY TO START** — Polish, edge cases, fidelity tuning, error-surface hardening
- All four reviewable gates cleared: rendering fidelity ✅, multi-material determinism ✅, settings persistence ✅, bridge coverage with failure paths ✅

---

## Historical Reference

Earlier Phase 0, Phase 1, and Phase 2 decisions have been archived to `decisions-archive.md` for historical reference while maintaining operational focus on Phase 3–5. All archived decisions remain valid and in-scope for future phases.

**Archive Date:** 2026-03-14T17:56:50Z
**Phase 4 Decisions Added:** 2026-03-14T18:34:43Z
**Phase 5 Decisions Added:** 2026-03-14T19:59:29Z
**Phase 5 Revision & Re-Review Added:** 2026-03-14T20:17:23Z
