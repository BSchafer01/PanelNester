import { useState } from 'react';
import { projectKindLabels } from '../projectKind';
import type { ProjectKind } from '../types/contracts';

interface NewProjectDialogProps {
  onCancel: () => void;
  onCreate: (projectKind: ProjectKind) => void | Promise<void>;
}

export function NewProjectDialog({ onCancel, onCreate }: NewProjectDialogProps) {
  const [projectKind, setProjectKind] = useState<ProjectKind>('sheet');

  return (
    <div className="results-dialog-backdrop" role="presentation">
      <div
        aria-labelledby="new-project-title"
        aria-modal="true"
        className="results-dialog app-confirm-dialog project-kind-dialog"
        role="dialog"
      >
        <div className="results-dialog__header">
          <div>
            <p className="eyebrow">New Project</p>
            <h3 id="new-project-title">Choose Project Kind</h3>
          </div>
        </div>
        <p className="section-note">
          Project Kind controls the available workflow and project settings.
        </p>
        <fieldset className="project-kind-options">
          <legend>Project Kind</legend>
          {(['sheet', 'stockLength'] as const).map((kind) => (
            <label className="project-kind-option" key={kind}>
              <input
                checked={projectKind === kind}
                name="new-project-kind"
                onChange={() => setProjectKind(kind)}
                type="radio"
              />
              <span>{projectKindLabels[kind]}</span>
            </label>
          ))}
        </fieldset>
        <div className="form-actions">
          <button className="secondary-button" onClick={onCancel} type="button">
            Cancel
          </button>
          <button
            className="primary-button"
            onClick={() => void onCreate(projectKind)}
            type="button"
          >
            Create Project
          </button>
        </div>
      </div>
    </div>
  );
}

interface ProjectKindControlProps {
  projectKind: ProjectKind;
  canChange: boolean;
  disabled?: boolean;
  onChange: (projectKind: ProjectKind) => void | Promise<void>;
}

export function ProjectKindControl({
  projectKind,
  canChange,
  disabled = false,
  onChange,
}: ProjectKindControlProps) {
  const [pendingKind, setPendingKind] = useState<ProjectKind | null>(null);
  const [busy, setBusy] = useState(false);

  const confirmChange = async () => {
    if (!pendingKind) {
      return;
    }

    setBusy(true);
    try {
      await onChange(pendingKind);
      setPendingKind(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <label className="project-field">
        <span>Project Kind</span>
        <select
          aria-label="Project Kind"
          disabled={disabled || !canChange}
          onChange={(event) => {
            const nextKind = event.target.value as ProjectKind;
            if (nextKind !== projectKind) {
              setPendingKind(nextKind);
            }
          }}
          value={projectKind}
        >
          <option value="sheet">Sheet Project</option>
          <option value="stockLength">Stock-Length Project</option>
        </select>
        {!canChange ? (
          <small>Project Kind can change only when there are no sheet parts or Required Pieces.</small>
        ) : null}
      </label>

      {pendingKind ? (
        <div className="results-dialog-backdrop" role="presentation">
          <div
            aria-labelledby="change-project-kind-title"
            aria-modal="true"
            className="results-dialog app-confirm-dialog project-kind-dialog"
            role="dialog"
          >
            <div className="results-dialog__header">
              <div>
                <p className="eyebrow">Project Settings</p>
                <h3 id="change-project-kind-title">Change Project Kind?</h3>
              </div>
            </div>
            <p className="section-note">
              Change to {projectKindLabels[pendingKind]}? Finalized import data,
              results, material snapshots, and kind-specific settings will be cleared.
              Project identity and general metadata will be retained.
            </p>
            <div className="form-actions">
              <button
                className="secondary-button"
                disabled={busy}
                onClick={() => setPendingKind(null)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="primary-button"
                disabled={busy}
                onClick={() => void confirmChange()}
                type="button"
              >
                {busy ? 'Changing…' : 'Change Project Kind'}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
