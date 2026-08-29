# Assign Oversized Stock After Optimization

OptiFab assigns overlong Piece Instances to Oversized Stock Items as a post-generation operation rather than running a second optimization. Each assigned instance consumes one Oversized Stock Item, keeping the regular deterministic heuristic unchanged and making the deliberately unoptimized material usage explicit in results and reports.

## Consequences

Oversized assignments are part of the saved Optimization Result, count as placed work, and are cleared whenever regular optimization inputs invalidate or regenerate that result. Changing or removing the assignment reconstructs the affected oversized and unplaced instances without touching regular Stock Items.
