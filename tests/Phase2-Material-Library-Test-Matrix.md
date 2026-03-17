# Phase 2 Material Library Test Matrix

This matrix covers the Phase 2 material library slice: CRUD for reusable materials, local JSON persistence, and material selection in the import flow. It keeps Hicks's rule in force: lock the contract now, then convert placeholders into live round-trip tests as Parker, Bishop, and Dallas finish the slice.

## Legend

- **Now** — executable today in `dotnet test`
- **Placeholder** — skipped with a concrete blocker
- **Manual Gate** — reviewer-run smoke once the slice is wired end to end

## Material Domain, Validation, and Persistence

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P2-MA-01 | Material JSON persistence shape | A material survives JSON serialize/deserialize with name, sheet settings, notes, finish, and cost intact | `PanelNester.Domain.Tests\Models\MaterialContractSpecs.cs` | Now | §6.2 Material Fields; Goal: preserve reusable materials locally |
| P2-MA-02 | Seed catalog remains safe | Bootstrap catalog names stay unique so Phase 2 can migrate off the hardcoded demo path cleanly | `PanelNester.Domain.Tests\Models\MaterialContractSpecs.cs` | Now | §6.2 Material Rules |
| P2-MA-03 | Duplicate material name on create/update | Exact duplicate name is rejected with `material-name-exists`; rename-to-self does not false-positive | `PanelNester.Services.Tests\Materials\MaterialLibrarySpecs.cs` | Now | §6.2 Material Rules |
| P2-MA-04 | Delete material that is still referenced | Delete is rejected with `material-in-use` while parts/projects still reference the material | `PanelNester.Services.Tests\Materials\MaterialLibrarySpecs.cs` | Now | §6.2 Material Actions; §6.3 Import Rules |
| P2-MA-05 | Import selection exact match | Import only accepts material names selected into the current project; wrong case or missing selection returns `material-not-found` | `PanelNester.Services.Tests\Materials\MaterialLibrarySpecs.cs` | Now | §6.2 Select materials into current project; §6.3 exact match |
| P2-MA-06 | CRUD service persists through reload | Create → list → update → delete round-trips through the JSON store and survives process restart | `PanelNester.Services.Tests\Materials\MaterialLibrarySpecs.cs` | Placeholder | Needs a real material CRUD service plus JSON repository |

## Desktop Bridge and UI Wiring

| ID | Scenario | Expected check | Coverage | Status | PRD trace |
|---|---|---|---|---|---|
| P2-BR-01 | Material bridge wire names stay stable | Bridge message names remain `list-materials`, `create-material`, `update-material`, `delete-material`, with normal `-response` suffixes | `PanelNester.Desktop.Tests\Bridge\MaterialBridgeContractSpecs.cs` | Now | Cross-layer contract stability |
| P2-BR-02 | Desktop host advertises material capabilities | Handshake advertises material CRUD capabilities so Web UI can gate on live support instead of assuming | `PanelNester.Desktop.Tests\Bridge\MaterialBridgeContractSpecs.cs` | Placeholder | Needs bridge contracts + DesktopBridgeRegistration handlers |
| P2-BR-03 | Import flow no longer relies on the demo fallback | Import uses project-selected materials from the Phase 2 library rather than the Phase 1 hardcoded demo material | `PanelNester.Services.Tests\Materials\MaterialLibrarySpecs.cs` + manual smoke | Placeholder | Needs import request + UI state updates for project material selection |

## Reviewer Gate Hicks Will Apply

Phase 2 is **not review-ready** until all of the following are true:

1. **Regression gate:** `npm run build` and `dotnet test .\PanelNester.slnx` both pass with the current Hicks scaffolding counts.
2. **Live CRUD gate:** the placeholder CRUD persistence test is converted to a real passing test against JSON-backed storage.
3. **Bridge gate:** the placeholder desktop bridge test is converted to a real passing handshake/dispatch test with the material capability set above.
4. **User-flow gate:** manual smoke proves all three user-visible failure modes with the exact codes:
   - duplicate material create/update → `material-name-exists`
   - delete referenced material → `material-in-use`
   - import with unselected or missing material → `material-not-found`
5. **No fallback gate:** any path that silently falls back to `Demo Material` after a project material has been selected is an automatic rejection.

## Manual Smoke Checklist for Final Review

- Create a new material, restart the app, and confirm the material reloads from JSON.
- Edit that material and confirm import + nesting use the edited values.
- Attempt to create a second material with the same name and confirm `material-name-exists`.
- Select one material into the project, import rows for that material plus one unknown material, and confirm only the unknown row reports `material-not-found`.
- Attempt to delete a material that is still referenced by imported rows or project selection and confirm `material-in-use`.
