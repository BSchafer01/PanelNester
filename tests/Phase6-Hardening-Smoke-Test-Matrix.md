# Phase 6 Hardening & Smoke Verification Test Matrix

This matrix defines the concrete checks Hicks expects before Phase 6 is considered review-ready.

## Empty-Result Export Tests

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P6-ER-01 | Zero sheets export | `QuestPdfReportExporter` produces valid PDF when `BatchNestResponse.MaterialResults` contains materials with zero sheets | Unit test |
| P6-ER-02 | Zero placements export | PDF generated when all sheets have zero placements (theoretical edge) | Unit test |
| P6-ER-03 | Null nesting result | Export with `LastBatchNestingResult = null` produces PDF with "No nesting results" notice | Unit test |
| P6-ER-04 | Empty project export | New project with no imports, no nesting → export → valid PDF with empty-state message | Integration test |
| P6-ER-05 | Bridge success on empty | `generate-pdf-report` returns `success: true` for empty-result export, not error code | Bridge test |

## Dense Layout Tests

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P6-DL-01 | Minimum stroke width | SVG output for 20+ panel sheet has stroke-width ≥ 1pt | Unit test |
| P6-DL-02 | Minimum label font | Labels in SVG have font-size ≥ 6pt (or legend reference if too small) | Unit test |
| P6-DL-03 | Adjacent panel contrast | Alternating or distinct fills for adjacent panels | Manual gate |
| P6-DL-04 | Legend fallback | Panels too small for inline label have legend/callout marker | Manual gate |
| P6-DL-05 | 30-panel stress | PDF with 30 panels on one sheet remains human-readable | Manual gate |

## Viewer Edge Case Tests

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P6-VW-01 | Reset-to-fit on sheet switch | Camera auto-fits bounds when user selects different sheet | Manual gate |
| P6-VW-02 | Zero-placement sheet | Viewer shows sheet outline with "No placements" indicator, not blank | Manual gate |
| P6-VW-03 | Label overflow | Tiny panels show truncated label or callout marker, no text overflow clipping | Manual gate |
| P6-VW-04 | Single-panel fit | Single large panel fills viewer correctly without excessive margins | Manual gate |

## Bridge Error Surface Tests

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P6-BE-01 | All errors have userMessage | Every `BridgeError` response includes non-empty `userMessage` field | Unit test |
| P6-BE-02 | No raw codes to user | Error messages are human-readable sentences, not codes like `project-not-found` alone | Code review |
| P6-BE-03 | Dispatcher catches edge exceptions | Unexpected exceptions in handlers return generic error with userMessage, not crash | Unit test |
| P6-BE-04 | Cancel returns expected code | Cancelled dialogs return expected error code without userMessage pollution | Bridge test |

## Native Dialog Reliability

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P6-ND-01 | PDF dialog cancel/retry | Cancel PDF save dialog 3x in succession, then save successfully | Manual gate |
| P6-ND-02 | Project dialog cancel/retry | Cancel project save dialog 3x, then save-as successfully | Manual gate |
| P6-ND-03 | Rapid dialog cycling | Open→Cancel→Open→Cancel loop 5x does not freeze app | Manual gate |

## Manual Smoke Test Execution

| ID | Scenario | Evidence required | Coverage |
|---|---|---|---|
| P6-SM-01 | smoke-test-guide.md Section 1 | Screenshot/log of handshake and initial state | Manual gate |
| P6-SM-02 | smoke-test-guide.md Section 2 | Screenshot/log of project save/open/save-as cycle | Manual gate |
| P6-SM-03 | smoke-test-guide.md Section 3 | Screenshot/log of import and nesting workflow | Manual gate |
| P6-SM-04 | smoke-test-guide.md Section 4 | Screenshot/log of PDF export with viewer comparison | Manual gate |
| P6-SM-05 | Empty-result smoke | Screenshot of empty-result export attempt → valid PDF or clear error | Manual gate |
| P6-SM-06 | Dense-layout smoke | Screenshot of 20+ panel PDF with readable labels/legend | Manual gate |

## Integration Gate Scenario

**Seed scenario for integration gate:**

1. New project
2. Skip import (no parts)
3. Run nesting → empty result
4. Export PDF → verify "No nesting results" message in PDF
5. Open saved project → verify state persists
6. Import CSV with 30+ panels
7. Run nesting → dense result
8. Export PDF → verify readable layout with labels/legend
9. Save project → close → reopen → verify result persistence

## Hicks Ready / No-Go Rule

This Phase 6 slice is **not ready** if any of these occur:

- Empty-result export crashes or produces invalid PDF
- Dense layout (20+ panels) has unreadable labels without legend
- Zero-placement sheet shows blank viewer (no outline)
- Any bridge error shows raw code without userMessage
- Native dialog cancel/retry leaves app in broken state
- Smoke test checklist has any unchecked items without documented exception
- Test baseline regresses below 112 tests or introduces failures

## Current Baseline

- Test count: **112 total** (110 passed, 2 skipped, 0 failures)
- Solution: `dotnet test PanelNester.slnx --nologo` ✅
- WebUI: `npm run build` ✅
- Persistence: FlatBuffers with legacy JSON backward compat ✅
