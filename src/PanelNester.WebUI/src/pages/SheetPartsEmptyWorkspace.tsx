import { ProjectEntriesEmptyState } from './ProjectEntriesEmptyState';

interface SheetPartsEmptyWorkspaceProps {
  busy: boolean;
  projectDirty?: boolean;
  message?: string;
  onImportFile?: (filePath?: string) => void | Promise<void>;
  onImportDroppedFile?: (file: File) => void | Promise<void>;
}

export function SheetPartsEmptyWorkspace({
  busy,
  projectDirty = false,
  message,
  onImportFile,
  onImportDroppedFile,
}: SheetPartsEmptyWorkspaceProps) {
  return <div className="stock-length-workspace project-entries-workspace">
    <header className="stock-length-workspace__header">
      <div>
        <div className="stock-length-workspace__title-line">
          <h1>Sheet Part Entries</h1>
          {projectDirty ? <span className="stock-length-workspace__unsaved">● Unsaved changes</span> : null}
        </div>
        <p>Import rectangular parts, then resolve their materials before nesting.</p>
        {message ? <p className="section-note" role="status">{message}</p> : null}
      </div>
    </header>
    <ProjectEntriesEmptyState
      busy={busy}
      canAddManually={false}
      onImportDroppedFile={onImportDroppedFile}
      onImportFile={onImportFile}
      projectKind="sheet"
    />
  </div>;
}
