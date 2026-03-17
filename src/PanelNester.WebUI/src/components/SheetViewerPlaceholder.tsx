interface SheetViewerPlaceholderProps {
  title: string;
  subtitle: string;
}

export function SheetViewerPlaceholder({
  title,
  subtitle,
}: SheetViewerPlaceholderProps) {
  return (
    <section className="panel viewer-placeholder">
      <div>
        <p className="eyebrow">Viewer</p>
        <h3>{title}</h3>
        <p className="muted">{subtitle}</p>
      </div>
      <div className="viewer-placeholder__canvas" aria-hidden="true">
        <div className="viewer-placeholder__sheet-outline">
          <span>Three.js viewer placeholder</span>
        </div>
      </div>
    </section>
  );
}
