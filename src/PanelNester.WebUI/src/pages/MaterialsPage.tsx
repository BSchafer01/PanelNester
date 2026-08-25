import { useEffect, useState } from 'react';
import type {
  ImportResponse,
  Material,
  MaterialDraft,
  MaterialLibraryLocation,
} from '../types/contracts';

interface MaterialsPageProps {
  materials: Material[];
  materialLibraryLocation?: MaterialLibraryLocation | null;
  materialLibraryUnavailable: boolean;
  selectedMaterialId?: string;
  importResponse: ImportResponse;
  materialsBusy: boolean;
  materialsMessage: string;
  canChooseMaterialLibraryLocation: boolean;
  canRestoreDefaultMaterialLibraryLocation: boolean;
  onRefreshMaterials: () => Promise<void>;
  onChooseMaterialLibraryLocation: () => Promise<void>;
  onRestoreDefaultMaterialLibraryLocation: () => Promise<void>;
  onSelectMaterial: (materialId: string) => void;
  onLoadMaterial: (materialId: string) => Promise<Material>;
  onCreateMaterial: (draft: MaterialDraft) => Promise<Material>;
  onUpdateMaterial: (material: Material) => Promise<Material>;
  onDeleteMaterial: (materialId: string) => Promise<void>;
}

function createEmptyDraft(): MaterialDraft {
  return {
    name: '',
    colorFinish: '',
    notes: '',
    sheetLength: 96,
    sheetWidth: 48,
    allowRotation: true,
    defaultSpacing: 0.125,
    defaultEdgeMargin: 0.5,
    costPerSheet: null,
  };
}

function draftFromMaterial(material: Material): MaterialDraft {
  return {
    materialId: material.materialId,
    name: material.name,
    colorFinish: material.colorFinish ?? '',
    notes: material.notes ?? '',
    sheetLength: material.sheetLength,
    sheetWidth: material.sheetWidth,
    allowRotation: material.allowRotation,
    defaultSpacing: material.defaultSpacing,
    defaultEdgeMargin: material.defaultEdgeMargin,
    costPerSheet: material.costPerSheet ?? null,
  };
}

function materialFromDraft(draft: MaterialDraft): Material {
  return {
    materialId: draft.materialId ?? '',
    name: draft.name.trim(),
    colorFinish: draft.colorFinish.trim() || null,
    notes: draft.notes.trim() || null,
    sheetLength: draft.sheetLength,
    sheetWidth: draft.sheetWidth,
    allowRotation: draft.allowRotation,
    defaultSpacing: draft.defaultSpacing,
    defaultEdgeMargin: draft.defaultEdgeMargin,
    costPerSheet: draft.costPerSheet,
  };
}

function validateDraft(draft: MaterialDraft): string | null {
  if (draft.name.trim().length === 0) {
    return 'Material name is required.';
  }

  if (draft.sheetLength <= 0) {
    return 'Sheet length must be greater than zero.';
  }

  if (draft.sheetWidth <= 0) {
    return 'Sheet width must be greater than zero.';
  }

  if (draft.defaultSpacing < 0) {
    return 'Default spacing cannot be negative.';
  }

  if (draft.defaultEdgeMargin < 0) {
    return 'Default edge margin cannot be negative.';
  }

  if (draft.costPerSheet != null && draft.costPerSheet < 0) {
    return 'Cost per sheet cannot be negative.';
  }

  return null;
}

function formatCost(costPerSheet?: number | null): string {
  return costPerSheet == null ? '—' : `$${costPerSheet.toFixed(2)}`;
}

function formatMeasurement(value: number): string {
  return value.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
}

function MaterialsGlyph({ icon }: { icon: 'create' | 'library' | 'location' }) {
  switch (icon) {
    case 'create':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M12 5.5v13" />
          <path d="M5.5 12h13" />
          <path d="M4.5 4.5h15v15h-15z" />
        </svg>
      );
    case 'location':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M12 20s5-4.8 5-9a5 5 0 1 0-10 0c0 4.2 5 9 5 9Z" />
          <circle cx="12" cy="11" r="1.8" />
        </svg>
      );
    case 'library':
    default:
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M6 6h3v12H6z" />
          <path d="M10.5 4.5H14v13h-3.5z" />
          <path d="M15.5 7h2.5v10h-2.5z" />
        </svg>
      );
  }
}

function shouldShowMaterialsStatus(message: string): boolean {
  const normalized = message.trim().toLowerCase();
  if (normalized.length === 0) {
    return false;
  }

  return (
    !normalized.startsWith('loaded ') &&
    !normalized.startsWith('material library synced')
  );
}

export function MaterialsPage({
  materials,
  materialLibraryLocation,
  materialLibraryUnavailable,
  selectedMaterialId,
  importResponse,
  materialsBusy,
  materialsMessage,
  canChooseMaterialLibraryLocation,
  canRestoreDefaultMaterialLibraryLocation,
  onRefreshMaterials,
  onChooseMaterialLibraryLocation,
  onRestoreDefaultMaterialLibraryLocation,
  onSelectMaterial,
  onLoadMaterial,
  onCreateMaterial,
  onUpdateMaterial,
  onDeleteMaterial,
}: MaterialsPageProps) {
  const [draft, setDraft] = useState<MaterialDraft>(() => createEmptyDraft());
  const [mode, setMode] = useState<'create' | 'edit'>('create');
  const [editorBusy, setEditorBusy] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Material | null>(null);
  const [editorMessage, setEditorMessage] = useState(
    'Create a reusable material or load one for editing.',
  );

  const referencedMaterials = new Set(
    importResponse.parts
      .map((part) => part.materialName.trim())
      .filter((name) => name.length > 0),
  );
  const currentLibraryPath = materialLibraryLocation?.currentPath?.trim();
  const defaultLibraryPath = materialLibraryLocation?.defaultPath?.trim();
  const usingDefaultLocation = materialLibraryLocation?.usingDefaultLocation ?? false;
  const canManageLibraryLocation =
    canChooseMaterialLibraryLocation || canRestoreDefaultMaterialLibraryLocation;
  const locationStatusLabel = materialLibraryUnavailable
    ? 'Library unavailable'
    : currentLibraryPath
    ? usingDefaultLocation
      ? 'Default location'
      : 'Custom location'
    : 'Location pending';

  useEffect(() => {
    if (
      mode === 'edit' &&
      draft.materialId &&
      !materials.some((material) => material.materialId === draft.materialId)
    ) {
      setMode('create');
      setDraft(createEmptyDraft());
      setEditorMessage('The edited material is no longer in the library.');
    }
  }, [draft.materialId, materials, mode]);

  const updateDraft = <T extends keyof MaterialDraft>(
    field: T,
    value: MaterialDraft[T],
  ) => {
    setDraft((current) => ({
      ...current,
      [field]: value,
    }) as MaterialDraft);
  };

  const handleCreateNew = () => {
    setMode('create');
    setDraft(createEmptyDraft());
    setEditorMessage('Creating a new material. Save to add it to the library.');
  };

  const handleEdit = async (materialId: string) => {
    setEditorBusy(true);

    try {
      const material = await onLoadMaterial(materialId);
      setMode('edit');
      setDraft(draftFromMaterial(material));
      setEditorMessage(`Editing ${material.name}.`);
    } catch (error) {
      setEditorMessage(
        error instanceof Error
          ? error.message
          : 'Material could not be loaded for editing.',
      );
    } finally {
      setEditorBusy(false);
    }
  };

  const handleSave = async () => {
    const validationMessage = validateDraft(draft);
    if (validationMessage) {
      setEditorMessage(validationMessage);
      return;
    }

    setEditorBusy(true);

    try {
      const savedMaterial =
        mode === 'edit' && draft.materialId
          ? await onUpdateMaterial(materialFromDraft(draft))
          : await onCreateMaterial(draft);

      setMode('edit');
      setDraft(draftFromMaterial(savedMaterial));
      setEditorMessage(`${savedMaterial.name} is ready for reuse.`);
      onSelectMaterial(savedMaterial.materialId);
    } catch (error) {
      setEditorMessage(
        error instanceof Error ? error.message : 'Material could not be saved.',
      );
    } finally {
      setEditorBusy(false);
    }
  };

  const handleSaveAsNewMaterial = async () => {
    const validationMessage = validateDraft(draft);
    if (validationMessage) {
      setEditorMessage(validationMessage);
      return;
    }

    setEditorBusy(true);

    try {
      const savedMaterial = await onCreateMaterial({
        ...draft,
        materialId: undefined,
      });

      setMode('edit');
      setDraft(draftFromMaterial(savedMaterial));
      setEditorMessage(`${savedMaterial.name} was saved as a new material.`);
      onSelectMaterial(savedMaterial.materialId);
    } catch (error) {
      setEditorMessage(
        error instanceof Error ? error.message : 'Material could not be saved as new.',
      );
    } finally {
      setEditorBusy(false);
    }
  };

  const handleDelete = async () => {
    if (!pendingDelete) {
      return;
    }

    setEditorBusy(true);

    try {
      await onDeleteMaterial(pendingDelete.materialId);
      if (draft.materialId === pendingDelete.materialId) {
        setMode('create');
        setDraft(createEmptyDraft());
      }
      setEditorMessage(`${pendingDelete.name} was removed from the library.`);
      setPendingDelete(null);
    } catch (error) {
      setEditorMessage(
        error instanceof Error ? error.message : 'Material could not be deleted.',
      );
    } finally {
      setEditorBusy(false);
    }
  };

  const handleRestoreDefaultLocation = () => {
    if (
      !window.confirm(
        'Restore the default material library location? OptiFab will point back to the standard materials.json file. If that file is unreadable, OptiFab will preserve it and create a fresh library.',
      )
    ) {
      return;
    }

    void onRestoreDefaultMaterialLibraryLocation().catch(() => undefined);
  };

  const handleRefreshMaterials = () => {
    void onRefreshMaterials().catch(() => undefined);
  };

  const handleChooseMaterialLibraryLocation = () => {
    void onChooseMaterialLibraryLocation().catch(() => undefined);
  };

  return (
    <div className="materials-library">
      <header className="module-hero module-hero--materials">
        <div className="module-hero__copy materials-page__hero-copy">
          <div className="materials-page__hero-head">
            <h1>Material Library</h1>
            <div className="materials-page__hero-meta">
              <p className="module-hero__intro">
                Configure structural substrates and nesting parameters.
              </p>
              <p className="module-hero__meta">
                {materials.length} saved material(s), {referencedMaterials.size} referenced by the current import.
              </p>
            </div>
          </div>
          {shouldShowMaterialsStatus(materialsMessage) ? (
            <p className="module-hero__meta">{materialsMessage}</p>
          ) : null}
        </div>
      </header>

      <div className="materials-library__grid">

      <section className="module-panel materials-editor">
        <div className="materials-section-title">
          <MaterialsGlyph icon="create" />
          <h3>{mode === 'edit' ? 'Edit Material' : 'Create Material'}</h3>
        </div>
        <p className="muted materials-editor__message">
          {materialLibraryUnavailable
            ? 'Repair the default library or choose another location before saving materials.'
            : editorMessage}
        </p>

        <div className="form-grid form-grid--two-column materials-editor__grid">
          <label className="field field--wide">
            <span>Material name</span>
            <input
              placeholder="e.g. Aluminum 6061-T6"
              onChange={(event) => updateDraft('name', event.target.value)}
              type="text"
              value={draft.name}
            />
          </label>

          <label className="field">
            <span>Finish / grade</span>
            <input
              onChange={(event) => updateDraft('colorFinish', event.target.value)}
              type="text"
              value={draft.colorFinish}
            />
          </label>

          <label className="field">
            <span>Cost / sheet ($)</span>
            <input
              min="0"
              onChange={(event) =>
                updateDraft(
                  'costPerSheet',
                  event.target.value === '' ? null : Number(event.target.value),
                )
              }
              step="0.01"
              type="number"
              value={draft.costPerSheet ?? ''}
            />
          </label>

          <label className="field">
            <span>Sheet length (in)</span>
            <input
              min="0"
              onChange={(event) =>
                updateDraft('sheetLength', Number(event.target.value) || 0)
              }
              step="0.125"
              type="number"
              value={draft.sheetLength}
            />
          </label>

          <label className="field">
            <span>Sheet width (in)</span>
            <input
              min="0"
              onChange={(event) =>
                updateDraft('sheetWidth', Number(event.target.value) || 0)
              }
              step="0.125"
              type="number"
              value={draft.sheetWidth}
            />
          </label>

          <label className="field">
            <span>Spacing (in)</span>
            <input
              min="0"
              onChange={(event) =>
                updateDraft('defaultSpacing', Number(event.target.value) || 0)
              }
              step="0.0625"
              type="number"
              value={draft.defaultSpacing}
            />
          </label>

          <label className="field">
            <span>Edge margin (in)</span>
            <input
              min="0"
              onChange={(event) =>
                updateDraft('defaultEdgeMargin', Number(event.target.value) || 0)
              }
              step="0.0625"
              type="number"
              value={draft.defaultEdgeMargin}
            />
          </label>

          <label className="materials-toggle-field field--wide">
            <span>Allow 90 deg rotation</span>
            <span className="materials-toggle-field__control project-toggle">
              <input
                checked={draft.allowRotation}
                onChange={(event) =>
                  updateDraft('allowRotation', event.target.checked)
                }
                type="checkbox"
              />
              <span className="project-toggle__track">
                <span className="project-toggle__thumb" />
              </span>
            </span>
          </label>

          <label className="field field--wide">
            <span>Notes</span>
            <textarea
              onChange={(event) => updateDraft('notes', event.target.value)}
              value={draft.notes}
            />
          </label>
        </div>

        <div className="form-actions">
          <button
            className="secondary-button"
            disabled={editorBusy}
            onClick={handleCreateNew}
            type="button"
          >
            Clear
          </button>
          <button
            className="primary-button materials-editor__submit"
            disabled={editorBusy || materialsBusy || materialLibraryUnavailable}
            onClick={() => void handleSave()}
            type="button"
          >
            {editorBusy ? 'Saving…' : mode === 'edit' ? 'Save changes' : 'Add material'}
          </button>
          {mode === 'edit' ? (
            <button
              className="secondary-button materials-editor__submit"
              disabled={editorBusy || materialsBusy || materialLibraryUnavailable}
              onClick={() => void handleSaveAsNewMaterial()}
              type="button"
            >
              Save as New Material
            </button>
          ) : null}
        </div>
      </section>

      <section className="module-panel materials-table-panel">
        <div className="module-panel__header">
          <div>
            <div className="materials-section-title">
              <MaterialsGlyph icon="library" />
              <h3>Current Materials</h3>
            </div>
          </div>
          <span className="materials-count-badge">{materials.length} items active</span>
        </div>
        <div className="library-location-card materials-location-card">
          <div className="library-location-card__header">
            <div className="row-stack">
              <span>Library file</span>
              <strong>{locationStatusLabel}</strong>
            </div>
            {currentLibraryPath ? (
              <span
                className={`status-pill ${
                  materialLibraryUnavailable
                    ? 'status-pill--error'
                    : usingDefaultLocation
                      ? 'status-pill--ok'
                      : 'status-pill--muted'
                }`}
              >
                {materialLibraryUnavailable
                  ? 'Needs attention'
                  : usingDefaultLocation
                    ? 'Default'
                    : 'Custom'}
              </span>
            ) : null}
          </div>

          <p className="import-path">
            {currentLibraryPath ??
              'Location data will appear here when the desktop host supports material library repointing.'}
          </p>

          {materialLibraryUnavailable ? (
            <p className="section-note">
              OptiFab could not open this library. Choose another JSON library, or
              repair the default library. Repair preserves an unreadable file beside
              the recreated materials.json file.
            </p>
          ) : null}

          <p className="section-note">
            {defaultLibraryPath ? (
              <span>
                Restore default switches back to{' '}
                <span className="library-location-inline-path">
                  {defaultLibraryPath}
                </span>{' '}
                and recreates materials.json there if needed.
              </span>
            ) : (
              'Restore default switches back to the standard materials.json file and recreates it if needed.'
            )}
          </p>

          <div className="library-location-actions">
            <button
              className="secondary-button"
              disabled={materialsBusy}
              onClick={handleRefreshMaterials}
              type="button"
            >
              Refresh
            </button>
            {canChooseMaterialLibraryLocation ? (
              <button
                className="secondary-button"
                disabled={materialsBusy}
                onClick={handleChooseMaterialLibraryLocation}
                type="button"
              >
                Choose location…
              </button>
            ) : null}
            {canRestoreDefaultMaterialLibraryLocation ? (
              <button
                className="secondary-button"
                disabled={
                  materialsBusy || (usingDefaultLocation && !materialLibraryUnavailable)
                }
                onClick={handleRestoreDefaultLocation}
                type="button"
              >
                {materialLibraryUnavailable && usingDefaultLocation
                  ? 'Repair default'
                  : 'Restore default'}
              </button>
            ) : null}
          </div>

          {!canManageLibraryLocation ? (
            <p className="section-note">
              Location controls will light up when the connected desktop host
              exposes this library-management capability.
            </p>
          ) : null}
        </div>

        <p className="section-note">
          Delete is blocked while the current import still references a material.
        </p>

        {materials.length > 0 ? (
          <div className="table-shell materials-table-shell">
            <table className="materials-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Sheet (in)</th>
                  <th>Spacing</th>
                  <th>Edge</th>
                  <th>Finish</th>
                  <th>Cost</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {materials.map((material) => {
                  const isSelected = material.materialId === selectedMaterialId;

                  return (
                    <tr
                      className={isSelected ? 'table-row--active' : undefined}
                      key={material.materialId}
                    >
                      <td>
                        <div className="row-stack">
                          <strong>{material.name}</strong>
                        </div>
                      </td>
                      <td>
                        {formatMeasurement(material.sheetWidth)} x {formatMeasurement(material.sheetLength)}
                      </td>
                      <td>{formatMeasurement(material.defaultSpacing)}</td>
                      <td>{formatMeasurement(material.defaultEdgeMargin)}</td>
                      <td>{material.colorFinish?.trim() || '—'}</td>
                      <td>{formatCost(material.costPerSheet)}</td>
                      <td>
                        <div className="table-actions">
                          <button
                            className="module-table-action"
                            disabled={editorBusy}
                            onClick={() => void handleEdit(material.materialId)}
                            type="button"
                          >
                            Edit
                          </button>
                          <button
                            className="module-table-action module-table-action--danger"
                            disabled={editorBusy}
                            onClick={() => setPendingDelete(material)}
                            type="button"
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="empty-state">
            <strong>No materials saved</strong>
            <span>Create the first reusable sheet definition from the editor.</span>
          </div>
        )}
      </section>
      </div>
      {pendingDelete ? (
        <div
          aria-labelledby="material-delete-dialog-title"
          aria-modal="true"
          className="material-delete-dialog"
          role="dialog"
        >
          <div className="material-delete-dialog__panel">
            <h3 id="material-delete-dialog-title">Delete material?</h3>
            <p>
              Delete <strong>{pendingDelete.name}</strong> from the material library?
            </p>
            <div className="material-delete-dialog__actions">
              <button
                className="secondary-button"
                disabled={editorBusy}
                onClick={() => setPendingDelete(null)}
                type="button"
              >
                Cancel
              </button>
              <button
                className="primary-button material-delete-dialog__danger"
                disabled={editorBusy}
                onClick={() => void handleDelete()}
                type="button"
              >
                {editorBusy ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
