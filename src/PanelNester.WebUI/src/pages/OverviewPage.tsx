import { StatusPill } from '../components/StatusPill';
import type {
  ImportResponse,
  NestResponse,
  ProjectMaterialSnapshot,
  ProjectMetadata,
} from '../types/contracts';

interface OverviewPageProps {
  metadata: ProjectMetadata;
  projectBusy: boolean;
  projectDirty: boolean;
  projectFilePath?: string;
  projectMessage: string;
  importResponse: ImportResponse;
  nestResponse: NestResponse;
  savedMaterialSnapshots: ProjectMaterialSnapshot[];
  kerfWidth: number;
  onMetadataChange: (field: keyof ProjectMetadata, value: string) => void;
  onKerfWidthChange: (value: number) => void;
}

export function OverviewPage({
  metadata,
  projectBusy,
  projectDirty,
  projectFilePath,
  projectMessage,
  importResponse,
  nestResponse,
  savedMaterialSnapshots,
  kerfWidth,
  onMetadataChange,
  onKerfWidthChange,
}: OverviewPageProps) {
  const hasResults =
    nestResponse.sheets.length > 0 || nestResponse.unplacedItems.length > 0;

  return (
    <div className="page-grid">
      <section className="panel hero-panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Project</p>
            <h2>File-backed projects, metadata, and saved material context</h2>
          </div>
          <div className="status-row">
            <StatusPill
              tone={projectDirty ? 'warn' : projectFilePath ? 'ok' : 'muted'}
              label={projectDirty ? 'Unsaved changes' : projectFilePath ? 'Saved file' : 'Not saved'}
            />
            <StatusPill
              tone={hasResults ? 'ok' : 'muted'}
              label={hasResults ? 'Results captured' : 'Results pending'}
            />
          </div>
        </div>
        <p className="muted">{projectMessage}</p>
        <div className="stats-grid">
          <article className="stat-card">
            <span>Project</span>
            <strong>{metadata.projectName.trim() || 'Untitled Project'}</strong>
          </article>
          <article className="stat-card">
            <span>Imported rows</span>
            <strong>{importResponse.parts.length}</strong>
          </article>
          <article className="stat-card">
            <span>Saved snapshots</span>
            <strong>{savedMaterialSnapshots.length}</strong>
          </article>
          <article className="stat-card">
            <span>Sheets</span>
            <strong>{nestResponse.summary.totalSheets}</strong>
          </article>
        </div>
      </section>

      <section className="panel">
        <p className="eyebrow">Metadata</p>
        <h3>Project identity</h3>
        <div className="form-grid form-grid--two-column">
          <label className="field field--wide">
            <span>Project name</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('projectName', event.target.value)}
              type="text"
              value={metadata.projectName}
            />
          </label>

          <label className="field">
            <span>Project number</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('projectNumber', event.target.value)}
              type="text"
              value={metadata.projectNumber}
            />
          </label>

          <label className="field">
            <span>Customer</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('customerName', event.target.value)}
              type="text"
              value={metadata.customerName}
            />
          </label>

          <label className="field">
            <span>Estimator</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('estimator', event.target.value)}
              type="text"
              value={metadata.estimator}
            />
          </label>

          <label className="field">
            <span>Drafter</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('drafter', event.target.value)}
              type="text"
              value={metadata.drafter}
            />
          </label>

          <label className="field">
            <span>PM</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('projectManager', event.target.value)}
              type="text"
              value={metadata.projectManager}
            />
          </label>

          <label className="field">
            <span>Date</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('date', event.target.value)}
              type="date"
              value={metadata.date}
            />
          </label>

          <label className="field">
            <span>Revision</span>
            <input
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('revision', event.target.value)}
              type="text"
              value={metadata.revision}
            />
          </label>

          <label className="field field--wide">
            <span>Notes</span>
            <textarea
              disabled={projectBusy}
              onChange={(event) => onMetadataChange('notes', event.target.value)}
              value={metadata.notes}
            />
          </label>
        </div>
      </section>

      <section className="panel">
        <p className="eyebrow">Nesting Options</p>
        <h3>Kerf and spacing settings</h3>
        <div className="form-grid form-grid--two-column">
          <label className="field">
            <span>Kerf width (inches)</span>
            <input
              disabled={projectBusy}
              min="0"
              onChange={(event) => {
                const value = parseFloat(event.target.value);
                if (!isNaN(value)) {
                  onKerfWidthChange(value);
                }
              }}
              step="0.0625"
              type="number"
              value={kerfWidth}
            />
          </label>
        </div>
      </section>
    </div>
  );
}
