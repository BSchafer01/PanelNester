import { useState } from 'react';
import type { ProjectKind } from '../types/contracts';

interface ProjectEntriesEmptyStateProps {
  projectKind: ProjectKind;
  busy: boolean;
  canAddManually: boolean;
  onImportFile?: (filePath?: string) => void | Promise<void>;
  onImportDroppedFile?: (file: File) => void | Promise<void>;
  onAddManually?: () => void;
}

export function ProjectEntriesEmptyState({
  projectKind,
  busy,
  canAddManually,
  onImportFile,
  onImportDroppedFile,
  onAddManually,
}: ProjectEntriesEmptyStateProps) {
  const [draggingImport, setDraggingImport] = useState(false);
  const [dropMessage, setDropMessage] = useState('');
  const isSheet = projectKind === 'sheet';
  const entryName = isSheet ? 'Sheet Part' : 'Required Piece';
  const run = (action: () => void | Promise<void>) =>
    void Promise.resolve(action()).catch(() => undefined);

  const importDroppedFile = (dataTransfer: DataTransfer) => {
    setDraggingImport(false);
    const file = dataTransfer.files[0] as (File & { path?: string }) | undefined;
    const uri = dataTransfer.getData('text/uri-list').split(/\r?\n/)
      .find((value) => value && !value.startsWith('#'));
    const plainText = dataTransfer.getData('text/plain').trim();
    let filePath = file?.path || uri || plainText;
    if (filePath.toLocaleLowerCase().startsWith('file://')) {
      try {
        filePath = decodeURIComponent(new URL(filePath).pathname)
          .replace(/^\/(?:([A-Za-z]:))/, '$1');
      } catch {
        filePath = '';
      }
    }
    const displayName = file?.name || filePath;
    if (!/\.(csv|xlsx|xlsm)$/i.test(displayName)) {
      setDropMessage('Drop a CSV, XLSX, or XLSM Import Source.');
      return;
    }
    if (file && !file.path && onImportDroppedFile) {
      setDropMessage('');
      run(() => onImportDroppedFile(file));
      return;
    }
    if (!filePath) {
      setDropMessage('OptiFab could not read the dropped file path. Use Import Workbook or CSV instead.');
      return;
    }
    setDropMessage('');
    if (onImportFile) run(() => onImportFile(filePath));
  };

  return <section
    aria-label={`Import ${entryName}s`}
    className={draggingImport
      ? 'project-card stock-length-workspace__empty stock-length-workspace__empty--dragging'
      : 'project-card stock-length-workspace__empty'}
    onDragEnter={(event) => {
      event.preventDefault();
      setDraggingImport(true);
    }}
    onDragLeave={(event) => {
      if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
        setDraggingImport(false);
      }
    }}
    onDragOver={(event) => {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
    }}
    onDrop={(event) => {
      event.preventDefault();
      importDroppedFile(event.dataTransfer);
    }}
    role="region"
  >
    <div className="stock-length-workspace__empty-icon" aria-hidden="true">◇<span>◇◇</span></div>
    <h2>No {entryName}s have been added yet.</h2>
    <p>Drop a Workbook or CSV here, choose one below, or add a {entryName} manually.</p>
    <div className="form-actions">
      <button
        className="primary-button"
        disabled={busy || !onImportFile}
        onClick={() => onImportFile && run(() => onImportFile())}
        type="button"
      >
        Import Workbook or CSV
      </button>
      <button
        className="secondary-button"
        disabled={busy || !canAddManually || !onAddManually}
        onClick={onAddManually}
        type="button"
      >
        ＋ Add {entryName} manually
      </button>
    </div>
    {dropMessage ? <p className="stock-import-workflow__attention" role="alert">{dropMessage}</p> : null}
  </section>;
}
