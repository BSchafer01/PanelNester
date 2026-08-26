import { useEffect, useState } from 'react';
import type {
  InchDisplayFormat,
  OptimizationGroup,
  RequiredPiece,
  RequiredPieceChange,
} from '../types/contracts';

interface RequiredPiecesPageProps {
  optimizationGroups: OptimizationGroup[];
  inchDisplayFormat: InchDisplayFormat;
  busy: boolean;
  message?: string;
  onCreateOptimizationGroup: (name: string, stockLength: string) => void | Promise<void>;
  onUpdateStockLength: (optimizationGroupId: string, stockLength: string) => void | Promise<void>;
  onCreateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onUpdateRequiredPiece: (change: RequiredPieceChange) => void | Promise<void>;
  onDeleteRequiredPiece: (optimizationGroupId: string, requiredPieceId: string) => void | Promise<void>;
  onInchDisplayFormatChange: (format: InchDisplayFormat) => void;
}

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

const displayFormatOptions: Array<{ value: InchDisplayFormat; label: string }> = [
  { value: 'decimal', label: 'Decimal' },
  { value: 'fractional16', label: 'Fractional — nearest 1/16' },
  { value: 'fractional32', label: 'Fractional — nearest 1/32' },
  { value: 'fractional64', label: 'Fractional — nearest 1/64' },
];

function greatestCommonDivisor(left: number, right: number): number {
  let a = Math.abs(left);
  let b = Math.abs(right);
  while (b !== 0) {
    [a, b] = [b, a % b];
  }
  return a || 1;
}

export function formatInches(value: number, format: InchDisplayFormat): string {
  if (format === 'decimal') {
    return `${Number(value.toFixed(6))} in`;
  }

  const denominator = format === 'fractional16' ? 16 : format === 'fractional32' ? 32 : 64;
  const totalNumerator = Math.round(value * denominator);
  const whole = Math.floor(totalNumerator / denominator);
  const numerator = totalNumerator % denominator;
  if (numerator === 0) {
    return `${whole} in`;
  }

  const divisor = greatestCommonDivisor(numerator, denominator);
  const fraction = `${numerator / divisor}/${denominator / divisor}`;
  return `${whole > 0 ? `${whole} ` : ''}${fraction} in`;
}

function toDraft(groupId: string, piece: RequiredPiece): RequiredPieceDraft {
  return {
    optimizationGroupId: groupId,
    requiredPieceId: piece.requiredPieceId,
    quantity: String(piece.quantity),
    length: String(piece.length),
    profileNumber: piece.profileNumber,
    partName: piece.partName ?? '',
    finish: piece.finish ?? '',
    partNumber: piece.partNumber ?? '',
  };
}

export function RequiredPiecesPage({
  optimizationGroups,
  inchDisplayFormat,
  busy,
  message,
  onCreateOptimizationGroup,
  onUpdateStockLength,
  onCreateRequiredPiece,
  onUpdateRequiredPiece,
  onDeleteRequiredPiece,
  onInchDisplayFormatChange,
}: RequiredPiecesPageProps) {
  const [newGroupName, setNewGroupName] = useState('');
  const [newStockLength, setNewStockLength] = useState('');
  const [stockLengthDrafts, setStockLengthDrafts] = useState<Record<string, string>>({});
  const [draft, setDraft] = useState<RequiredPieceDraft>(emptyDraft);

  useEffect(() => {
    if (!draft.optimizationGroupId && optimizationGroups.length > 0) {
      setDraft((current) => ({
        ...current,
        optimizationGroupId: optimizationGroups[0].optimizationGroupId,
      }));
    }
  }, [draft.optimizationGroupId, optimizationGroups]);

  const updateDraft = (field: keyof RequiredPieceDraft, value: string) =>
    setDraft((current) => ({ ...current, [field]: value }));

  const submitRequiredPiece = async () => {
    const change: RequiredPieceChange = {
      type: draft.requiredPieceId ? 'update' : 'create',
      optimizationGroupId: draft.optimizationGroupId,
      ...(draft.requiredPieceId ? { requiredPieceId: draft.requiredPieceId } : {}),
      quantity: draft.quantity,
      length: draft.length,
      profileNumber: draft.profileNumber,
      partName: draft.partName,
      finish: draft.finish,
      partNumber: draft.partNumber,
    };
    if (draft.requiredPieceId) {
      await onUpdateRequiredPiece(change);
    } else {
      await onCreateRequiredPiece(change);
    }
    setDraft({ ...emptyDraft, optimizationGroupId: draft.optimizationGroupId });
  };

  const runAction = (action: () => void | Promise<void>) => {
    void Promise.resolve(action()).catch(() => undefined);
  };

  return (
    <div className="stock-length-import">
      <header className="page-header">
        <div>
          <p className="eyebrow">Stock-Length Project</p>
          <h1>Required Pieces</h1>
          <p>Configure Stock Length by Optimization Group, then enter the pieces to cut.</p>
          {message ? <p className="section-note" role="status">{message}</p> : null}
        </div>
        <label className="project-field stock-length-import__format">
          <span>Length Display</span>
          <select
            aria-label="Length Display"
            disabled={busy}
            onChange={(event) => onInchDisplayFormatChange(event.target.value as InchDisplayFormat)}
            value={inchDisplayFormat}
          >
            {displayFormatOptions.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>
      </header>

      <section className="project-card stock-length-import__group-create">
        <div className="project-card__header"><h2>Optimization Groups</h2></div>
        <div className="project-form-grid">
          <label className="project-field">
            <span>Optimization Group name</span>
            <input aria-label="Optimization Group name" onChange={(event) => setNewGroupName(event.target.value)} value={newGroupName} />
          </label>
          <label className="project-field">
            <span>Stock Length (in)</span>
            <input aria-label="Stock Length" inputMode="decimal" onChange={(event) => setNewStockLength(event.target.value)} value={newStockLength} />
          </label>
          <button
            className="primary-button"
            disabled={busy}
            onClick={() => runAction(() => onCreateOptimizationGroup(newGroupName, newStockLength))}
            type="button"
          >
            Add Optimization Group
          </button>
        </div>
        {optimizationGroups.map((group) => {
          const stockLengthValue = stockLengthDrafts[group.optimizationGroupId] ??
            (group.stockLength == null ? '' : String(group.stockLength));
          return (
            <div className="stock-length-import__group-row" key={group.optimizationGroupId}>
              <strong>{group.name}</strong>
              <label>
                <span>Stock Length</span>
                <input
                  aria-label={`Stock Length for ${group.name}`}
                  disabled={busy}
                  onChange={(event) => setStockLengthDrafts((current) => ({
                    ...current,
                    [group.optimizationGroupId]: event.target.value,
                  }))}
                  value={stockLengthValue}
                />
              </label>
              <button
                className="secondary-button"
                disabled={busy}
                onClick={() => runAction(() => onUpdateStockLength(group.optimizationGroupId, stockLengthValue))}
                type="button"
              >Save Stock Length</button>
            </div>
          );
        })}
      </section>

      <section className="project-card">
        <div className="project-card__header"><h2>{draft.requiredPieceId ? 'Edit Required Piece' : 'Add Required Piece'}</h2></div>
        <div className="project-form-grid stock-length-import__piece-form">
          <label className="project-field">
            <span>Optimization Group</span>
            <select aria-label="Optimization Group" disabled={busy} onChange={(event) => updateDraft('optimizationGroupId', event.target.value)} value={draft.optimizationGroupId}>
              <option value="">Choose an Optimization Group</option>
              {optimizationGroups.map((group) => <option key={group.optimizationGroupId} value={group.optimizationGroupId}>{group.name}</option>)}
            </select>
          </label>
          {(['quantity', 'length', 'profileNumber', 'partName', 'finish', 'partNumber'] as const).map((field) => {
            const label = {
              quantity: 'Quantity', length: 'Length', profileNumber: 'Profile Number',
              partName: 'Part Name', finish: 'Finish', partNumber: 'Part Number',
            }[field];
            return (
              <label className="project-field" key={field}>
                <span>{label}{field === 'length' ? ' (in)' : ''}</span>
                <input
                  aria-label={label}
                  disabled={busy}
                  onChange={(event) => updateDraft(field, event.target.value)}
                  required={field === 'quantity' || field === 'length' || field === 'profileNumber'}
                  value={draft[field]}
                />
              </label>
            );
          })}
        </div>
        <div className="form-actions">
          <button className="primary-button" disabled={busy} onClick={() => runAction(submitRequiredPiece)} type="button">
            {draft.requiredPieceId ? 'Save Required Piece' : 'Add Required Piece'}
          </button>
          {draft.requiredPieceId ? <button className="secondary-button" onClick={() => setDraft({ ...emptyDraft, optimizationGroupId: draft.optimizationGroupId })} type="button">Cancel</button> : null}
        </div>
      </section>

      <section className="project-card">
        <div className="project-card__header"><h2>Manual Required Pieces</h2></div>
        {optimizationGroups.every((group) => group.requiredPieces.length === 0) ? (
          <p className="section-note">No Required Pieces yet.</p>
        ) : (
          <div className="table-wrap"><table><thead><tr><th>Optimization Group</th><th>Qty</th><th>Length</th><th>Profile Number</th><th>Finish</th><th>Part Name</th><th>Part Number</th><th>Actions</th></tr></thead><tbody>
            {optimizationGroups.flatMap((group) => group.requiredPieces.map((piece) => {
              const stockGroup = group.stockGroups.find((item) => item.requiredPieceIds.includes(piece.requiredPieceId));
              return <tr key={piece.requiredPieceId}><td>{group.name}</td><td>{piece.quantity}</td><td>{formatInches(piece.length, inchDisplayFormat)}</td><td>{piece.profileNumber}</td><td>{stockGroup?.finish || piece.finish || 'No finish specified'}</td><td>{piece.partName || '—'}</td><td>{piece.partNumber || '—'}</td><td><div className="table-actions"><button aria-label={`Edit Required Piece ${piece.requiredPieceId}`} className="secondary-button" onClick={() => setDraft(toDraft(group.optimizationGroupId, piece))} type="button">Edit</button><button aria-label={`Delete Required Piece ${piece.requiredPieceId}`} className="danger-button" onClick={() => runAction(() => onDeleteRequiredPiece(group.optimizationGroupId, piece.requiredPieceId))} type="button">Delete</button></div></td></tr>;
            }))}
          </tbody></table></div>
        )}
      </section>
    </div>
  );
}
