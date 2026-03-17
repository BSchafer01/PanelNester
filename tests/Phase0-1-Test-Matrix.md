# Phase 0 / Phase 1 Test Matrix

This matrix turns the Phase 0 foundation slice and Phase 1 vertical slice into repeatable checks. It is intentionally concrete: each row has an expected outcome, a first automation target, and a blocker if the seam is not ready yet.

## Legend

- **Now** — covered by initial runnable spec tests in `tests\`
- **Placeholder** — test stub added with an explicit blocker
- **Manual** — needs host/runtime conditions that are not yet unit-testable

## Bridge Handshake

| ID | Phase | Scenario | Expected check | Initial coverage | Status | Ripley success criteria |
|---|---|---|---|---|---|---|
| P0-BR-01 | 0 | Desktop host remains a WPF Windows app | Project still targets `net8.0-windows` and `UseWPF=true` | `PanelNester.Desktop.Tests\ProjectConfiguration\DesktopProjectConfigurationTests.cs` | Now | SC1 |
| P0-BR-02 | 0 | Shell bootstraps a desktop entry point | `MainWindow` exists as the shell entry type | `PanelNester.Desktop.Tests\ProjectConfiguration\DesktopProjectConfigurationTests.cs` | Now | SC1 |
| P0-BR-03 | 0 | Bridge envelope stays serializable | Response preserves `requestId`, `type`, and serializable payload shape | `PanelNester.Desktop.Tests\Bridge\BridgeHandshakeSpecs.cs` | Now | SC1, SC6 |
| P0-BR-04 | 0 | Full bridge round-trip | Handshake request returns desktop capabilities with a matching `requestId` and no orphaned reply | `PanelNester.Desktop.Tests\Bridge\BridgeHandshakeSpecs.cs` | Now | SC1, SC6 |
| P0-BR-05 | 0 | Unknown message type | Bridge returns actionable error response instead of hanging UI | `PanelNester.Desktop.Tests\Bridge\BridgeHandshakeSpecs.cs` | Now | SC1, SC6 |
| P0-BR-06 | 0 | WebView2 runtime missing | Host reports a user-visible startup error instead of crashing | Manual + future desktop integration test | Placeholder | SC1 |

## CSV Import Validation

| ID | Phase | Scenario | Expected check | Initial coverage | Status | Ripley success criteria |
|---|---|---|---|---|---|---|
| P1-IM-01 | 1 | Required headers present in any order | Import accepts the file when exact header names exist, even if the columns are shuffled | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC2, SC3, SC6 |
| P1-IM-02 | 1 | Missing required header | Import stops with a file-level error that names the missing column | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3, SC6 |
| P1-IM-03 | 1 | Invalid numeric text | Row is marked error and preserved for UI display/correction | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3, SC6 |
| P1-IM-04 | 1 | Zero/negative dimensions or quantity | Row is rejected with a non-positive-value error | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3, SC6 |
| P1-IM-05 | 1 | Empty or unknown material | Row is rejected with `material-not-found` style feedback | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3, SC6 |
| P1-IM-06 | 1 | Duplicate `Id` values | Row remains importable but emits a warning | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3 |
| P1-IM-07 | 1 | Quantity > 10,000 | Row remains importable but emits a large-quantity warning; no expansion at import time | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3 |
| P1-IM-08 | 1 | Unicode part IDs | Imported ID round-trips unchanged through validation results | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3 |
| P1-IM-09 | 1 | Empty file | Import returns an actionable empty-file error instead of throwing | `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs` | Now | SC3 |
| P1-IM-10 | 1 | File-open path handoff | Phase 1 accepts a native dialog result and reuses the same import/nesting contracts end to end | `PanelNester.Desktop.Tests\Bridge\DesktopBridgeRoundTripSpecs.cs` | Now | SC2, SC3, SC4, SC5 |

## Nesting Boundaries

| ID | Phase | Scenario | Expected check | Initial coverage | Status | Ripley success criteria |
|---|---|---|---|---|---|---|
| P1-NE-01 | 1 | Empty input set | Run is rejected with an actionable `empty-run` style error | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC6 |
| P1-NE-02 | 1 | Part larger than usable sheet | Item is unplaced with an oversized-sheet reason code | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC5, SC6 |
| P1-NE-03 | 1 | Part exactly sheet size while margin > 0 | Item is unplaced because margins reduce usable area | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC5, SC6 |
| P1-NE-04 | 1 | Rotation changes the outcome | Part fits only when rotated and material rotation is allowed | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC5, SC6 |
| P1-NE-05 | 1 | Fit decision near floating-point boundary | 0.0001" tolerance prevents false negatives on near-exact fits | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC6 |
| P1-NE-06 | 1 | Part-to-part clearance | Effective clearance = material spacing + kerf width | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4 |
| P1-NE-07 | 1 | Identical parts stress case | Placements do not overlap and utilization remains deterministic | `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` | Now | SC4, SC5 |
| P1-NE-08 | 1 | Results payload for successful run | Response includes sheets, placements, unplaced items, and summary totals | `PanelNester.Domain.Tests\Models\NestResultContractSpecs.cs` | Now | SC5, SC6 |

## Combined Success Criteria Crosswalk

| Ripley success criteria | Covered by |
|---|---|
| SC1 — WPF app launches and displays React UI in WebView2 | P0-BR-01 through P0-BR-06 |
| SC2 — User can trigger CSV file open | P1-IM-10 |
| SC3 — CSV parses and validation errors display | P1-IM-01 through P1-IM-09 |
| SC4 — User can click Run Nesting | P0-BR-04, P1-IM-10, and P1-NE-01 |
| SC5 — Results display sheet count, utilization, unplaced items | P1-NE-02, P1-NE-03, P1-NE-07, P1-NE-08 |
| SC6 — At least one Hicks test covers bridge round-trip, import validation, nesting placement | `PanelNester.Desktop.Tests\Bridge\BridgeHandshakeSpecs.cs`, `PanelNester.Services.Tests\Import\CsvImportServiceSpecs.cs`, `PanelNester.Services.Tests\Nesting\NestingBoundarySpecs.cs` |
