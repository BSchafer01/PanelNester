# Separate Sheet and Stock Item Viewers

Sheet Projects will retain the existing SheetViewer interface, while Stock-Length Projects will use a dedicated StockItemViewer interface that owns its Three.js viewport, overlaid DOM Cut Sequence card, stable Piece Instance selection, and stock-specific camera and accessibility behavior. The two viewers may share internal Three.js implementation, but neither exposes the other's domain model; this keeps the modules deep, prevents synthetic one-inch optimizer geometry from entering the UI interface, and protects existing Sheet Project behavior.

## Consequences

The StockItemViewer renders a schematic-width stock bar with proportional length, selectable Piece Instances, nonselectable Kerf and Remainder visuals, and a responsive Cut Sequence card layered over the canvas or moved below it on narrow screens. Results coordinates Optimization Group, Stock Group, Stock Item, and Piece Instance selection through stable domain identities rather than transient engine IDs.
