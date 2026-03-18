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
  const locationStatusLabel = currentLibraryPath
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

  const handleDelete = async (materialId: string, materialName: string) => {
    if (!window.confirm(`Delete "${materialName}" from the library?`)) {
      return;
    }

    setEditorBusy(true);

    try {
      await onDeleteMaterial(materialId);
      if (draft.materialId === materialId) {
        setMode('create');
        setDraft(createEmptyDraft());
      }
      setEditorMessage(`${materialName} was removed from the library.`);
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
        'Restore the default material library location? PanelNester will point back to the standard materials.json file and recreate it there if needed.',
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
    <div className="page-grid">
      <section className="panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Materials</p>
            <h2>Reusable material library</h2>
          </div>
        </div>

        {shouldShowMaterialsStatus(materialsMessage) ? (
          <p className="muted">{materialsMessage}</p>
        ) : null}

        <div className="stats-grid">
          <article className="stat-card">
            <span>Materials</span>
            <strong>{materials.length}</strong>
          </article>
          <article className="stat-card">
            <span>Referenced</span>
            <strong>{referencedMaterials.size}</strong>
          </article>
        </div>
      </section>

      <section className="panel">
        <p className="eyebrow">Editor</p>
        <h3>{mode === 'edit' ? 'Edit material' : 'Create material'}</h3>
        <p className="muted">{editorMessage}</p>

        <div className="form-grid form-grid--two-column">
          <label className="field field--wide">
            <span>Material name</span>
            <input
              onChange={(event) => updateDraft('name', event.target.value)}
              type="text"
              value={draft.name}
            />
          </label>

          <label className="field">
            <span>Color / finish</span>
            <input
              onChange={(event) => updateDraft('colorFinish', event.target.value)}
              type="text"
              value={draft.colorFinish}
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
            <span>Default spacing (in)</span>
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
            <span>Default edge margin (in)</span>
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

          <label className="field">
            <span>Cost per sheet</span>
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

          <label className="checkbox-field">
            <input
              checked={draft.allowRotation}
              onChange={(event) =>
                updateDraft('allowRotation', event.target.checked)
              }
              type="checkbox"
            />
            <span>Allow 90° rotation</span>
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
            className="primary-button"
            disabled={editorBusy || materialsBusy}
            onClick={() => void handleSave()}
            type="button"
          >
            {editorBusy ? 'Saving…' : mode === 'edit' ? 'Save changes' : 'Create material'}
          </button>
        </div>
      </section>

      <section className="panel">
        <div className="section-header">
          <div>
            <p className="eyebrow">Library</p>
            <h3>Current materials</h3>
          </div>
        </div>
        <div className="library-location-card">
          <div className="library-location-card__header">
            <div className="row-stack">
              <span>Library file</span>
              <strong>{locationStatusLabel}</strong>
            </div>
            {currentLibraryPath ? (
              <span
                className={`status-pill ${usingDefaultLocation ? 'status-pill--ok' : 'status-pill--muted'}`}
              >
                {usingDefaultLocation ? 'Default' : 'Custom'}
              </span>
            ) : null}
          </div>

          <p className="import-path">
            {currentLibraryPath ??
              'Location data will appear here when the desktop host supports material library repointing.'}
          </p>

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
                disabled={materialsBusy || usingDefaultLocation}
                onClick={handleRestoreDefaultLocation}
                type="button"
              >
                Restore default
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
          Delete is blocked while the current import or active selector still
          references a material.
        </p>

        {materials.length > 0 ? (
          <div className="table-shell">
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Sheet</th>
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
                        {material.sheetLength}" × {material.sheetWidth}"
                      </td>
                      <td>{material.defaultSpacing}"</td>
                      <td>{material.defaultEdgeMargin}"</td>
                      <td>{material.colorFinish?.trim() || '—'}</td>
                      <td>{formatCost(material.costPerSheet)}</td>
                      <td>
                        <div className="table-actions">
                          <button
                            className="secondary-button"
                            disabled={editorBusy}
                            onClick={() => void handleEdit(material.materialId)}
                            type="button"
                          >
                            Edit
                          </button>
                          <button
                            className="secondary-button"
                            disabled={editorBusy}
                            onClick={() =>
                              void handleDelete(material.materialId, material.name)
                            }
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
  );
}
