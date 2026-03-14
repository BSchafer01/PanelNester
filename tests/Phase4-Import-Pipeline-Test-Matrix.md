# Phase 4 Import Pipeline Hardening Test Matrix

This matrix gets ahead of the Phase 4 boundary: XLSX parity, richer import validation, and inline editing without regressing the approved Phase 1 CSV slice or the Phase 3 project persistence slice. Hicks's rule stays the same: keep the current checks executable, turn missing seams into explicit placeholders, and refuse hand-wavy "it probably works" around row validation or save/open drift.

## Legend

- **Now** — executable today in the current repo
- **Placeholder** — intended automated coverage once the seam lands
- **Manual Gate** — reviewer-run smoke once the feature is wired end to end
- **Scope Hold** — depends on Ripley's final Phase 4 scope before the contract should freeze

## Current Regression Guardrails

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-RG-01 | CSV import rules do not regress while Phase 4 expands formats | Required columns stay exact-name, any-order; row-level errors remain actionable; duplicate IDs stay warning-only | `tests\PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | §6.3 Required Columns; §6.3 Import Rules; §6.3 Validation Outcomes |
| P4-RG-02 | Desktop import vertical slice stays live | File dialog → `import-csv` → nesting still produces one executable end-to-end path while new format/edit seams are added | `tests\PanelNester.Desktop.Tests\Bridge\DesktopBridgeRoundTripSpecs.cs` | Now | Workflow: import → nest → results |
| P4-RG-03 | Saved projects keep imported rows stable | `.pnest` save/open preserves imported part rows, source file path, and validation state already stored in project state | `tests\PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | §6.1 Save/Reopen project; Phase 3 persistence slice |

## Format Expansion: CSV + XLSX

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-IM-01 | Import surface accepts `.xlsx` alongside `.csv` | Desktop file selection and service layer accept both extensions without breaking existing CSV import | Planned service + bridge coverage | Placeholder | Goal: CSV and XLSX import; §6.3 Part Import |
| P4-IM-02 | XLSX required columns match CSV rules | Exact column names remain required, column order does not matter, extra columns are ignored | Planned service specs | Placeholder | §6.3 Required Columns; §6.3 Import Rules |
| P4-IM-03 | Workbook/file-level failures stay specific | Missing sheet/header, empty workbook, corrupt workbook, or unsupported type return actionable errors instead of crashes or silent empties | Planned service + bridge specs | Placeholder | §6.3 Validation Outcomes; Phase 6 error handling theme |
| P4-IM-04 | CSV/XLSX parity for identical data | The same row content imported from CSV or XLSX yields the same `PartRow` payload, statuses, and error/warning codes | Planned service parity specs | Placeholder | Goal: import part lists from CSV and XLSX |

## Validation Hardening

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-VA-01 | Multi-issue rows remain fully visible | One row can surface multiple validation messages without dropping later issues or losing its `rowId` | Planned service + Web UI coverage | Placeholder | §6.3 Rows with invalid data should be flagged |
| P4-VA-02 | Inline correction revalidates immediately | Editing a bad value updates the row status/messages without requiring reimport or nesting first | Planned Web UI + bridge coverage | Placeholder | §6.3 Correct validation errors inline |
| P4-VA-03 | Duplicate IDs stay warning-only after edits too | Changing a row ID into a duplicate warns but does not silently block the full import set | Planned service/edit specs | Placeholder | §6.3 Duplicate IDs allowed; warnings for duplicate IDs |
| P4-VA-04 | Exact material-match failures stay explicit | Unknown or case-drifted material names remain actionable and are never silently auto-created or silently remapped | Current CSV specs + planned edit smoke | Now + Manual Gate | §5 exact text match; §6.2 Material names matched exactly; §6.3 Material must exactly match existing material |
| P4-VA-05 | Large import warning/performance threshold is documented | Quantity/import-size warning behavior is stable and reviewer-visible, not an accidental side effect | Current CSV warning specs + Ripley scope review | Scope Hold | §6.3 Example warnings: very large quantity; §6.5 performance goal |

## Inline Editing / Row Management

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-ED-01 | Edit imported row inline | User can update dimensions/material/quantity in-place and see validation refresh immediately | Planned Web UI + bridge + service coverage | Placeholder | §6.3 Edit imported rows; §6.3 Correct validation errors inline |
| P4-ED-02 | Delete imported row | Deleted row disappears from table, counts update, and nesting availability recalculates | Planned Web UI + bridge coverage | Placeholder | §6.3 Delete imported rows |
| P4-ED-03 | Add row manually | New manual rows validate the same way imported rows do and participate in nesting/save-open flows | Planned Web UI + bridge coverage | Scope Hold | §6.3 Add rows manually |
| P4-ED-04 | Filter/sort remains trustworthy after edits | Material filtering/sorting reflects the current edited row state, not stale pre-edit data | Planned Web UI coverage | Placeholder | §6.3 Filter/sort by material |
| P4-ED-05 | Empty-table recovery is graceful | Deleting/fixing rows down to zero nestable rows disables nesting cleanly and keeps the import view stable | Planned Web UI + smoke coverage | Placeholder | Import workflow reliability |

## Bridge + Persistence Contract

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-BR-01 | Phase 4 bridge vocabulary is explicit and typed | New import/edit operations have stable request/response contracts and do not break the existing Phase 0-3 bridge vocabulary | Planned desktop contract specs | Scope Hold | Cross-layer contract stability |
| P4-BR-02 | Import edits mark the project dirty | Any inline import change flips dirty-state and save/open clears it only after a real persistence round-trip | Planned Web UI + project bridge coverage | Placeholder | §6.1 Save/Reopen project; Phase 3 dirty-state precedent |
| P4-BR-03 | Edited import state survives `.pnest` save/open | Added, deleted, and edited rows rehydrate exactly with validation messages/status intact | Planned project persistence specs + smoke | Placeholder | §6.1 Save/Reopen project; §6.3 Post-Import Editing |

## Manual Reviewer Gate Hicks Will Apply

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P4-MG-01 | XLSX happy path | Reviewer imports a valid workbook and gets the same visible result quality currently expected from CSV | `.squad\smoke-test-guide.md` | Manual Gate | Goal: import CSV and XLSX |
| P4-MG-02 | Inline fix of invalid row | Reviewer corrects a row error inline and sees status/messages/counts update without reimport | `.squad\smoke-test-guide.md` | Manual Gate | §6.3 Correct validation errors inline |
| P4-MG-03 | Edit/delete/add survive save/open | Reviewer changes the import table, saves, reopens, and confirms exact import-state fidelity | `.squad\smoke-test-guide.md` | Manual Gate | §6.1 Save/Reopen; §6.3 Post-Import Editing |
| P4-MG-04 | Failure surfaces stay actionable | Reviewer hits bad material/bad numeric/multi-issue/corrupt workbook cases and sees specific, user-visible failures with no crash/hang/silent drop | `.squad\smoke-test-guide.md` | Manual Gate | §6.3 Validation Outcomes; Phase 6 error handling theme |

## Reviewer Gate Hicks Will Apply

Phase 4 is **not review-ready** until all of the following are true:

1. **Regression gate:** `dotnet test .\PanelNester.slnx` and `npm run build` pass with no regressions to the current CSV import, nesting, project persistence, or material-library coverage.
2. **Format-parity gate:** CSV and XLSX produce the same row payload/status behavior for equivalent data, aside from format-specific file errors.
3. **Edit gate:** inline import changes revalidate immediately, update counts/material filters, and never require a stealth reimport to become real.
4. **Persistence gate:** edited import state survives `.pnest` save/open exactly, including row deletions, validation messages, and nesting readiness.
5. **Failure-surface gate:** row/file/workbook errors remain actionable and user-visible; crashy, silent, or generic behavior is an automatic rejection.
6. **Scope-reconciliation gate:** Ripley resolves the open scope holds below before implementation freezes; Hicks will not guess the contract after the fact.

## Scope Holds Pending Ripley's Final Phase 4 Design

- Whether Phase 4 includes full row CRUD (`add`, `edit`, `delete`) or only edit/delete of already imported rows.
- Whether the bridge keeps `import-csv` and broadens its payload/file-filter behavior, or introduces a new generic import contract.
- Whether richer validation adds new rules beyond the current CSV checks (for example unusually small dimensions, import-size/performance warnings, or field-level validation timing).
- Whether library drift after project reopen stays a plain inline import error or requires a dedicated material remap workflow.
