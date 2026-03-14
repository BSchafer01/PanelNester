# Phase 5 Results Viewer & PDF Reporting Test Matrix

This matrix gets ahead of the Phase 5 boundary: turn the existing `NestResponse`/`lastNestingResult` seam into a trustworthy results viewer and PDF export workflow without drifting from the approved Phase 4 import/edit pipeline. Hicks's rule is unchanged: viewer polish does not count if the rendered geometry, exported report, and saved project state disagree about what the latest nesting run actually produced.

## Legend

- **Now** — executable today in the current repo
- **Placeholder** — intended automated coverage once the seam lands
- **Manual Gate** — reviewer-run smoke once the feature is wired end to end
- **Scope Hold** — depends on Ripley's final Phase 5 scope before the contract should freeze

## Current Regression Guardrails

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-RG-01 | Current nesting result contract stays stable while Phase 5 grows around it | `NestResponse` still exposes sheets, placements, unplaced items, and summary totals; existing reason codes remain actionable | `tests\PanelNester.Domain.Tests\Models\NestResultContractSpecs.cs` | Now | §6.5 Output Per Material; §6.5 Unplaced Items |
| P5-RG-02 | Saved projects keep the last nesting result intact | `.pnest` save/open continues to round-trip `state.lastNestingResult` exactly while viewer/reporting state is added | `tests\PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | §6.1 Save/Reopen project; §8.1 preserve last known results |
| P5-RG-03 | Results page still has one trustworthy non-placeholder path | Existing material/sheets/placements/unplaced tables remain coherent even before the interactive viewer replaces the placeholder | `.squad\smoke-test-guide.md` | Manual Gate | §6.6 Results and Visualization |

## Results Viewer Data Fidelity

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-VW-01 | Results are presented in PRD order | UI presents material summary, then sheet-by-sheet detail, then unplaced items without hiding critical state only inside the canvas | Planned Web UI specs + smoke | Placeholder | §6.6 Results and Visualization |
| P5-VW-02 | Viewer geometry matches the nesting payload | Every `NestPlacement` lands on the correct sheet with the correct origin, dimensions, and rotation flag; no invented or missing parts | Planned Web UI viewer specs | Placeholder | §6.6 Sheet Detail View; Workflow step 9 |
| P5-VW-03 | Viewer interactions are informational only | Zoom, pan, hover, and click reveal metadata without mutating counts, placements, or current result selection | Planned Web UI interaction specs + smoke | Placeholder | §6.6 Viewer Requirements |
| P5-VW-04 | Empty and partial-result states stay clear | No-results, partial-results, and unplaced-only outcomes remain understandable, responsive, and user-visible | Planned Web UI specs + smoke | Placeholder | §6.6 Results and Visualization; §6.5 Unplaced Items |
| P5-VW-05 | Large result sets remain usable | Multiple sheets and dense placement lists stay performant enough to inspect without freezing the viewer or desynchronizing with the tables | Planned Web UI perf smoke | Scope Hold | §6.5 Performance Goal; §6.6 Viewer Requirements |
| P5-VW-06 | Summary scope is explicit | If Phase 5 still consumes a single-material `NestResponse`, the UI/report labels that scope honestly; if it expands to PRD-style multi-material summary, the new contract is typed and regression-tested | Planned domain + Web UI specs | Scope Hold | §6.6 Material Summary |

## PDF Reporting & Editable Fields

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-PD-01 | Editable report fields are complete and explicit | Company Name, Report Title, Project / Job Name, Project / Job Number, Date, and Notes are all editable before export | Planned Web UI + bridge specs | Placeholder | §6.7 Editable Report Fields |
| P5-PD-02 | PDF includes the default sections | Header, material summary, sheet count by material, utilization summary, sheet visuals, unplaced/invalid items, and notes all appear in the export | Planned service + smoke coverage | Placeholder | §6.7 PDF Default Sections |
| P5-PD-03 | Export uses current project data and latest nesting run | Export reflects the currently loaded project metadata/report edits plus the latest persisted or freshly run nesting result, not stale prior state | Planned service + bridge specs | Placeholder | §6.7 Report Behavior; Workflow steps 9-11 |
| P5-PD-04 | Export does not mutate the underlying result | Generating a PDF never changes `lastNestingResult`, placement coordinates, unplaced reasons, or material snapshots | Planned persistence + bridge specs | Placeholder | §8.1 preserve last known results |
| P5-PD-05 | Save-dialog and file-write failures stay actionable | Cancelled save, locked target, unsupported path, or generation failure surface specific user-visible outcomes with no silent partial success | Planned desktop bridge specs + smoke | Placeholder | §6.7 Report Behavior; Phase 6 error-handling theme |
| P5-PD-06 | PDF visuals stay faithful to the viewer | Sheet visuals in the PDF derive from the same placement data the viewer shows; "close enough" is acceptable only if differences are documented and reviewer-visible | Planned service + smoke coverage | Manual Gate | §6.7 Report Behavior |

## Bridge & Persistence Contract

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-BR-01 | Reporting bridge capability is explicit | Host handshake advertises any new report/export operation clearly; UI can disable/report unsupported hosts instead of guessing | Planned desktop contract specs | Placeholder | Cross-layer contract stability |
| P5-BR-02 | Report-edit persistence is deliberate | If report fields persist, `.pnest` round-trip restores them exactly; if they are transient, that behavior is explicit and not mistaken for a save bug | Planned project persistence specs + smoke | Scope Hold | §6.7 editable before export; §7.4 Reopen Workflow |
| P5-BR-03 | Reopen workflow restores the same result/report relationship | Reopened projects do not silently pair a current result with stale report fields from another run or another project | Planned project/open specs | Placeholder | §7.4 Reopen Workflow |
| P5-BR-04 | Export honors host ownership boundaries | Native save dialogs, file-system writes, and error codes stay desktop-owned if the bridge owns export; the Web UI does not silently fork its own incompatible export path | Planned desktop + Web UI specs | Scope Hold | Platform: Windows desktop; WebView2 host architecture |

## Invalid / Unplaced Output Coverage

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-IV-01 | Unplaced nesting failures stay visible across viewer and PDF | `outside-usable-sheet`, `no-layout-space`, `invalid-input`, and `empty-run` remain visible and consistent in UI and export | Planned domain + service + smoke coverage | Placeholder | §6.5 Unplaced Items; §6.7 PDF Default Sections |
| P5-IV-02 | Invalid import rows are handled explicitly in reporting scope | If the PDF includes invalid pre-nesting rows, they are sourced and labeled clearly; if not, the omission is intentional and documented | Planned service + smoke coverage | Scope Hold | §6.3 Validation Outcomes; §6.7 unplaced/invalid items |
| P5-IV-03 | Partial-success runs stay trustworthy | A run with placed and unplaced items still yields coherent summary counts, viewer output, and export sections with no double-counting | Planned service + Web UI specs | Placeholder | §6.5 Output Per Material; §6.6 Results and Visualization |

## Manual Reviewer Gate Hicks Will Apply

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P5-MG-01 | Viewer matches live results | Reviewer can compare cards/tables to the interactive viewer and find no drift in sheets, placements, utilization story, or unplaced reasons | `.squad\smoke-test-guide.md` | Manual Gate | §6.6 Results and Visualization |
| P5-MG-02 | Viewer interactions remain stable | Reviewer exercises zoom/pan/hover/click and sees informative behavior with no data corruption or stale selection bugs | `.squad\smoke-test-guide.md` | Manual Gate | §6.6 Viewer Requirements |
| P5-MG-03 | Edited report fields appear in exported PDF | Reviewer edits all report fields, exports once, and confirms the PDF reflects the current project/result state | `.squad\smoke-test-guide.md` | Manual Gate | §6.7 Editable Report Fields |
| P5-MG-04 | Export failures remain user-visible | Reviewer hits at least one export failure path and sees a clear, non-destructive outcome | `.squad\smoke-test-guide.md` | Manual Gate | §6.7 Report Behavior |
| P5-MG-05 | Save/open behavior matches the agreed persistence model | Reviewer saves and reopens a project to confirm whether report fields persist, reset, or regenerate exactly as Ripley specified | `.squad\smoke-test-guide.md` | Manual Gate | §7.4 Reopen Workflow |

## Reviewer Gate Hicks Will Apply

Phase 5 is **not review-ready** until all of the following are true:

1. **Regression gate:** `dotnet test .\PanelNester.slnx` and `npm run build` pass with no regressions to import/edit, nesting, project persistence, or the existing results tables.
2. **Viewer-fidelity gate:** the interactive viewer derives from the same result payload the cards/tables/PDF use; any drift in counts, geometry, utilization, or unplaced reasons is an automatic rejection.
3. **Export gate:** PDF output includes the agreed editable fields and required sections from the current project/result state, not stale cached data or a separate shadow model.
4. **Failure-surface gate:** cancelled exports, file-write failures, and no-result export attempts stay specific, user-visible, and non-destructive.
5. **Persistence gate:** if report fields or viewer state persist, save/open reproduces them exactly alongside `lastNestingResult`; if they are transient, that behavior is explicit and tested.
6. **Scope-reconciliation gate:** Ripley resolves the open Phase 5 scope holds below before implementation freezes; Hicks will not guess after the bridge and UI diverge.

## Scope Holds Pending Ripley's Final Phase 5 Design

- Whether Phase 5 expands the current single-material `NestResponse`/`MaterialSummary` seam to support the PRD's summary-by-material presentation, or keeps this slice scoped to the current single-material nesting result.
- Whether editable report fields live in persisted project state, reuse existing project metadata, or stay transient until export.
- Whether invalid import rows appear in the PDF beside unplaced items, given that current project persistence stores imported rows separately from `lastNestingResult`.
- Whether the desktop bridge owns PDF generation/save dialogs through a new capability (for example `export-pdf`) or the Web UI generates the document directly.
- Whether viewer state such as selected sheet, zoom/pan, and clicked-part inspection persists across reruns or save/open.
