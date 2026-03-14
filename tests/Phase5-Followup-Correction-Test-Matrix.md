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

## Suggested Seed Scenario

Use one repeatable smoke setup so the corrections are easy to spot:

1. A nesting result with at least one sheet near **60% utilization**.
2. At least **two clearly different panel IDs** so graphic labels are easy to verify.
3. A mix of panel sizes so one label-fit edge case is visible.
4. One manual pass at a **large app window size** to expose viewer growth and pointer-focus problems.

## Hicks Ready / No-Go Rule for This Batch

This correction batch is **not ready** if any one of these happens:

- The viewer is still effectively a static/placeholder renderer instead of the live Three.js path.
- The viewer can be tilted/orbited out of 2D plan view.
- Enlarging the window lets the viewer crowd out the rest of the Results page.
- Mouse wheel/drag inside the viewer still scrolls or manipulates the page.
- The report graphic omits panel labels.
- Any utilization surface renders a decimal fraction as a double-multiplied percentage such as `6000%`.
