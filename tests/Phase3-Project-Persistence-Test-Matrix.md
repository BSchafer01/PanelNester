# Phase 3 Project Persistence & Snapshot Test Matrix

This matrix covers the Phase 3 slice: `.pnest` save/open flows, editable project metadata, and material snapshots that preserve project fidelity even when the shared library drifts later. Hicks's rule still applies: executable checks first, explicit blockers for seams that are not live yet, and no approval if reopen behavior silently rereads the live library.

## Legend

- **Now** — executable today in `dotnet test`
- **Placeholder** — skipped with a concrete blocker
- **Manual Gate** — reviewer-run smoke once the slice is wired end to end

## Domain Serializer & Metadata Contract

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P3-DM-01 | Project JSON schema keys stay stable | Serialized project uses `version`, `projectId`, `metadata`, `settings`, `materialSnapshots`, and `state`, with `lastNestingResult` nested under `state` and camelCase metadata fields | `PanelNester.Domain.Tests\Models\ProjectPersistenceContractSpecs.cs` | Now | §6.1 Project Metadata; save/reopen goals |
| P3-DM-02 | Metadata round-trip keeps all PRD fields | Project name, number, customer, estimator, drafter, PM, date, revision, notes, and kerf survive JSON round-trip | `PanelNester.Domain.Tests\Models\ProjectPersistenceContractSpecs.cs` | Now | §6.1 Project Metadata; §6.4 kerf |
| P3-DM-03 | Project load/save error codes stay actionable | Missing, corrupt, unsupported-version, and save failures use specific codes | `PanelNester.Domain.Tests\Models\ProjectPersistenceContractSpecs.cs` | Now | Phase 3 decision error codes |

## Services Snapshot & Persistence Behavior

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P3-SV-01 | Save snapshots all referenced materials | Save logic snapshots selected materials plus exact-match imported material names only | `PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | §6.1 Save project; Phase 3 snapshot decision |
| P3-SV-02 | Open existing project restores saved snapshot values | Reopen uses `.pnest` material snapshots instead of silently adopting changed live-library dimensions/notes/costs | `PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | Goal: restore project fidelity; Phase 3 success criterion 7 |
| P3-SV-03 | Load failures remain specific | Missing file → `project-not-found`; bad JSON/schema → `project-corrupt`; wrong version → `project-unsupported-version` | `PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | Phase 3 bridge/service contract |
| P3-SV-04 | Serializer round-trip through `.pnest` | Real serializer persists metadata, project state, snapshots, and nesting results through disk I/O | `PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | `.pnest` round-trip requirement |
| P3-SV-05 | Metadata update keeps snapshot context stable | Project service updates metadata/settings without dropping saved snapshots or mutating reopened project context to live-library drift | `PanelNester.Services.Tests\Projects\ProjectPersistenceSpecs.cs` | Now | Metadata editing + snapshot fidelity |

## Desktop Bridge Contract

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P3-BR-01 | Project bridge message names stay stable | `new/open/save/save-as/get-project-metadata/update-project-metadata` keep the standard `-response` suffix pattern | `PanelNester.Desktop.Tests\Bridge\ProjectBridgeContractSpecs.cs` | Now | Phase 3 bridge extension |
| P3-BR-02 | Project bridge forwards project error codes | Bridge contract exposes `project-not-found`, `project-corrupt`, `project-unsupported-version`, `project-save-failed` | `PanelNester.Desktop.Tests\Bridge\ProjectBridgeContractSpecs.cs` | Now | Phase 3 bridge/service contract |
| P3-BR-03 | Handshake/dispatcher expose project operations | Bridge registration advertises all six project message types and routes them to live handlers, including native save/open dialog contracts | `PanelNester.Desktop.Tests\Bridge\ProjectBridgeContractSpecs.cs` + `PanelNester.Desktop.Tests\Bridge\ProjectBridgeSpecs.cs` | Now | Phase 3 bridge extension |

## Manual Reviewer Gate Smoke

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P3-MG-01 | Metadata save/open round-trip | User fills every PRD metadata field, saves `.pnest`, reopens, and sees exact values restored | `.squad\smoke-test-guide.md` | Manual Gate | §6.1 Project Metadata |
| P3-MG-02 | Snapshot survives live library drift | Save project, edit or delete the live material, reopen project, and confirm project still shows saved snapshot values | `.squad\smoke-test-guide.md` | Manual Gate | Goal: restore snapped materials, not live library |
| P3-MG-03 | Corrupt/unsupported project file is rejected cleanly | Broken JSON or version mismatch surfaces the correct error code with no crash or silent reset | `.squad\smoke-test-guide.md` | Manual Gate | Phase 3 error handling |

## Reviewer Gate Hicks Will Apply

Phase 3 is **not review-ready** until all of the following are true:

1. **Regression gate:** `npm run build` and `dotnet test .\PanelNester.slnx` both pass with only the documented Hicks placeholder skips remaining.
2. **Serializer gate:** the live `.pnest` serializer round-trip test passes and preserves metadata, project state, snapshots, and nesting results.
3. **Service gate:** metadata update and reopen tests pass without dropping snapshots or rehydrating from live-library drift.
4. **Bridge gate:** project bridge contract + round-trip tests pass with all six message types wired through the desktop dispatcher and native dialog contracts.
5. **Snapshot gate:** automated or manual evidence proves a saved project reopens with its own snapshotted material values after live-library edit/delete; any silent reread of live library values is an automatic rejection.
6. **Metadata gate:** save/open preserves all PRD metadata fields and kerf without trimming to defaults, blanking dates, or dropping notes.
7. **Failure-surface gate:** missing, corrupt, and unsupported project files surface `project-not-found`, `project-corrupt`, and `project-unsupported-version` respectively; generic or crashy behavior is a rejection.
