# Adapt the Sheet Optimizer for Stock-Length Cut Plans

Stock-Length Projects will initially generate Cut Plans by adapting the existing deterministic sheet optimizer: every synthetic stock item and Required Piece is exactly one inch wide, rotation is disabled, and edge margin and two-dimensional spacing are zero. This reuses proven placement behavior but intentionally provides a first-fit-decreasing heuristic rather than a mathematically optimal cutting-stock solution; the Stock-Length Project and Optimization Result models therefore remain independent of the synthetic geometry so a purpose-built engine can replace it later.

## Consequences

Saw Kerf is the only placement allowance and is applied between adjacent pieces, producing `n - 1` kerfs for `n` pieces on a Stock Item. The adapter translates engine output into Cut Plans, deterministic result-local identities, domain-specific unplaced reasons, and Complete, Partial, or Failed status instead of exposing synthetic materials, coordinates, GUIDs, or the engine's raw success flag.
