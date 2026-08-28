import { useEffect, useState } from 'react';
import type { OptimizationGroup, RequiredPiece, RequiredPieceChange } from '../types/contracts';

interface RequiredPieceDraft {
  optimizationGroupId: string;
  requiredPieceId?: string;
  quantity: string;
  length: string;
  profileNumber: string;
  partName: string;
  finish: string;
  partNumber: string;
}

const emptyDraft: RequiredPieceDraft = {
  optimizationGroupId: '',
  quantity: '',
  length: '',
  profileNumber: '',
  partName: '',
  finish: '',
  partNumber: '',
};

function draftForPiece(groupId: string, piece?: RequiredPiece): RequiredPieceDraft {
  return piece ? {
    optimizationGroupId: groupId,
    requiredPieceId: piece.requiredPieceId,
    quantity: `${piece.quantity}`,
    length: piece.lengthText ?? `${piece.length}`,
    profileNumber: piece.profileNumber,
    partName: piece.partName ?? '',
    finish: piece.finish ?? '',
    partNumber: piece.partNumber ?? '',
  } : { ...emptyDraft, optimizationGroupId: groupId };
}

interface RequiredPieceDrawerProps {
  busy: boolean;
  groups: OptimizationGroup[];
  groupId?: string;
  piece?: RequiredPiece;
  onClose: () => void;
  onSave: (change: RequiredPieceChange) => void | Promise<void>;
}

export function RequiredPieceDrawer({
  busy,
  groups,
  groupId,
  piece,
  onClose,
  onSave,
}: RequiredPieceDrawerProps) {
  const [draft, setDraft] = useState(() => draftForPiece(groupId ?? groups[0]?.optimizationGroupId ?? '', piece));

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', closeOnEscape);
    return () => document.removeEventListener('keydown', closeOnEscape);
  }, [onClose]);

  const update = (field: keyof RequiredPieceDraft, value: string) =>
    setDraft((current) => ({ ...current, [field]: value }));
  const save = async () => {
    await onSave({
      type: draft.requiredPieceId ? 'update' : 'create',
      optimizationGroupId: draft.optimizationGroupId,
      ...(draft.requiredPieceId ? { requiredPieceId: draft.requiredPieceId } : {}),
      quantity: draft.quantity,
      length: draft.length,
      profileNumber: draft.profileNumber,
      partName: draft.partName,
      finish: draft.finish,
      partNumber: draft.partNumber,
    });
    onClose();
  };

  return <div className="stock-length-import__drawer-layer">
    <button aria-label="Dismiss Required Piece drawer" className="stock-length-import__drawer-backdrop" onClick={onClose} type="button" />
    <aside aria-label={piece ? 'Edit Required Piece' : 'Add Required Piece'} aria-modal="true" className="stock-length-import__piece-drawer" role="dialog">
      <div className="project-card__header">
        <div><p className="eyebrow">Required Pieces</p><h2>{piece ? 'Edit Required Piece' : 'Add Required Piece'}</h2></div>
        <button aria-label="Close Required Piece drawer" className="secondary-button" onClick={onClose} type="button">Close</button>
      </div>
      <div className="project-form-grid stock-length-import__piece-form">
        <label className="project-field"><span>Optimization Group</span><select aria-label="Optimization Group" disabled={busy} onChange={(event) => update('optimizationGroupId', event.target.value)} value={draft.optimizationGroupId}>
          <option value="">Choose an Optimization Group</option>
          {groups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}
        </select></label>
        {(['quantity', 'length', 'profileNumber', 'partName', 'finish', 'partNumber'] as const).map((field) => {
          const label = { quantity: 'Quantity', length: 'Length', profileNumber: 'Profile Number', partName: 'Part Name', finish: 'Finish', partNumber: 'Part Number' }[field];
          return <label className="project-field" key={field}><span>{label}{field === 'length' ? ' (in)' : ''}</span><input aria-label={label} disabled={busy} onChange={(event) => update(field, event.target.value)} required={field === 'quantity' || field === 'length' || field === 'profileNumber'} value={draft[field]} /></label>;
        })}
      </div>
      <div className="form-actions">
        <button className="primary-button" disabled={busy || !draft.optimizationGroupId || !draft.quantity || !draft.length || !draft.profileNumber.trim()} onClick={() => void save()} type="button">{piece ? 'Save Required Piece' : 'Save New Required Piece'}</button>
        <button className="secondary-button" onClick={onClose} type="button">Cancel</button>
      </div>
    </aside>
  </div>;
}
