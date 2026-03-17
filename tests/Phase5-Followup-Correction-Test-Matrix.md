# Phase 5 Follow-Up Correction Test Matrix

This is a narrow correction-batch matrix for the viewer/report notes raised after the original Phase 5 slice. It does **not** replace the broader `tests\Phase5-Results-Viewer-PDF-Reporting-Test-Matrix.md`; it sharpens the concrete checks Hicks expects before this correction batch is considered review-ready.

## Viewer Behavior Checks

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P5-FU-VW-01 | Three.js viewer path is active | The Results page uses the live Three.js viewer path rather than a placeholder or static-only renderer on the normal results workflow | Manual gate |
| P5-FU-VW-02 | Navigation is locked to 2D | Zoom and pan work, but rotate/orbit/tilt do not move the camera out of plan view; reset returns to the same top-down orientation | Manual gate |
| P5-FU-VW-03 | Viewer size stays bounded | Enlarging the app makes the viewer more readable without letting it consume disproportionate page real-estate; summary/report controls remain usable without excessive scrolling | Manual gate |
| P5-FU-VW-04 | Pointer input is owned by the viewer while hovered | Mouse wheel and drag gestures inside the viewer affect the viewer only; outside the viewer, page scrolling and normal mouse behavior resume immediately | Manual gate |
| P5-FU-VW-05 | Viewer remains trustworthy while interacting | Hover, click, zoom, and pan never desynchronize labels, placements, or sheet selection from the surrounding result tables/cards | Manual gate |

## Report Output & Percentage Checks

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P5-FU-RP-01 | Report graphic labels panels | Every placed panel is identifiable from the exported sheet graphic itself; tiny parts require an unmistakable legend/callout rather than anonymous shapes | Manual gate |
| P5-FU-RP-02 | Utilization consumes decimal fractions exactly once | A result value such as `0.6` renders as `60.0%` (or the agreed precision) in all user-visible surfaces; no `6000%`, `0.6%`, or raw decimal leaks | Manual gate |
| P5-FU-RP-03 | UI and PDF tell the same percentage story | Average utilization, per-sheet utilization, and PDF utilization all agree numerically and follow the same rounding/precision rule | Manual gate |

## Current Bugfix Batch — Camera Orientation & Save Dialog Reliability

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P5-FU-BG-01 | Initial viewer camera opens in plan view | On first render, reset/fit, and sheet changes, the viewer shows the sheet face-on/top-down rather than edge-on; reviewers can read the layout without recovering the camera manually | Manual gate |
| P5-FU-BG-02 | Camera fix does not weaken the 2D lock | After the orientation fix, zoom and pan still work while rotate/orbit/tilt/roll remain blocked or auto-corrected immediately | Manual gate |
| P5-FU-BG-03 | Native PDF save dialog stays usable | The desktop save dialog opens without crashing, accepts filename edits, allows folder changes, and reaches Save normally | Manual gate |
| P5-FU-BG-04 | Chosen dialog path is honored | Export writes the PDF to the exact folder and renamed filename the user selected, not a stale default path or previous attempt | Manual gate + existing bridge/service regression specs |
| P5-FU-BG-05 | Cancel/failure can recover on the next attempt | Cancelling or hitting a clean file-write failure leaves the app stable and the next export attempt fully usable | Manual gate + existing bridge/service regression specs |

## Current Re-Review Gate — Import Launch & Results Split Recovery

| ID | Scenario | Concrete acceptance check | Coverage |
|---|---|---|---|
| P5-FU-RG-01 | First selected file actually imports on the first try | From the Import page, choosing a valid `.csv` or `.xlsx` in the native picker immediately advances to either the live imported payload or the import review state; no second click, no silent stall, and no "nothing happened" pause after file selection | `tests\PanelNester.Desktop.Tests\Bridge\ImportBridgeSpecs.cs`, `tests\PanelNester.Desktop.Tests\Bridge\ImportResultsRevisionGateSpecs.cs`, manual gate |
| P5-FU-RG-02 | Desktop import path still carries the chosen file through the host bridge | The desktop bridge only opens the dialog when no file path is already known, reuses the selected path for the real import request, keeps dialog calls serialized, and marshals the response back onto the WebView thread so the Web UI sees the first response | `tests\PanelNester.Desktop.Tests\Bridge\NativeFileDialogServiceSpecs.cs`, `tests\PanelNester.Desktop.Tests\Bridge\ImportResultsRevisionGateSpecs.cs` |
| P5-FU-RG-03 | Results layout stays workspace-left / viewer-right | On desktop-width layouts, the Results workspace renders on the left, the viewer renders on the right, and the splitter remains between them instead of letting the workspace drift right or the viewer steal the left column | `tests\PanelNester.Desktop.Tests\Bridge\ImportResultsRevisionGateSpecs.cs`, manual gate |
| P5-FU-RG-04 | Resize affordance stays visible and grabbable | The divider is visually obvious at desktop widths, including a visible grip/center line and a column-resize cursor, so reviewers can discover and drag it without pixel hunting | `tests\PanelNester.Desktop.Tests\Bridge\ImportResultsRevisionGateSpecs.cs`, manual gate |
| P5-FU-RG-05 | Workspace scroll remains independent from the viewer | Long Results workspace content scrolls inside the left workspace panel while the viewer column stays pinned; the sticky tabs remain visible and the entire page does not become the only scroll container | `tests\PanelNester.Desktop.Tests\Bridge\ImportResultsRevisionGateSpecs.cs`, manual gate |

## Suggested Seed Scenario

Use one repeatable smoke setup so the corrections are easy to spot:

1. A nesting result with at least one sheet near **60% utilization**.
2. At least **two clearly different panel IDs** so graphic labels are easy to verify.
3. A mix of panel sizes so one label-fit edge case is visible.
4. One manual pass at a **large app window size** to expose viewer growth and pointer-focus problems.
5. One export pass where the reviewer **renames the PDF in the native save dialog** before clicking Save.

## Hicks Ready / No-Go Rule for This Batch

This correction batch is **not ready** if any one of these happens:

- The viewer is still effectively a static/placeholder renderer instead of the live Three.js path.
- The viewer can be tilted/orbited out of 2D plan view.
- The first frame/reset/sheet switch still opens edge-on instead of in plan view.
- The first import selection still appears to do nothing after the user picks a valid file.
- The Results workspace is no longer on the left with the viewer on the right at desktop widths.
- The resize divider is invisible, ungrabbable, or visually indistinguishable from the surrounding panels.
- Results details scroll the full page instead of staying contained within the workspace panel.
- Enlarging the window lets the viewer crowd out the rest of the Results page.
- Mouse wheel/drag inside the viewer still scrolls or manipulates the page.
- The native PDF save dialog crashes, freezes, or refuses filename edits before Save.
- The chosen save-dialog filename/path is not the one that gets written.
- A cancelled or failed export leaves the next export attempt broken.
- The report graphic omits panel labels.
- Any utilization surface renders a decimal fraction as a double-multiplied percentage such as `6000%`.
