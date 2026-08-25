# Separate Optimization Groups from Part Groups

OptiFab will model an Optimization Group as a stable, ordered execution boundary whose parts and panels never mix with another Optimization Group. It will remain distinct from the existing Part Group classification, which continues to control sequencing and domain-specific reporting or extrusion behavior within an Optimization Group; this preserves existing behavior while allowing Worksheets to be optimized either together or in isolation.

## Consequences

Optimization results follow the hierarchy Optimization Group, then Material, then Panels. Existing projects migrate into one Optimization Group while retaining their current `Group` values as Part Groups, and downstream results, reports, exports, and filters must represent both concepts without conflating them.
