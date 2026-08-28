interface ConfirmationDialogProps {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

export function ConfirmationDialog({
  title,
  message,
  confirmLabel = 'Continue',
  cancelLabel = 'Cancel',
  danger = false,
  busy = false,
  onCancel,
  onConfirm,
}: ConfirmationDialogProps) {
  return <div className="results-dialog-backdrop" role="presentation">
    <section aria-labelledby="confirmation-dialog-title" aria-modal="true" className="results-dialog app-confirm-dialog" role="dialog">
      <h2 id="confirmation-dialog-title">{title}</h2>
      <p>{message}</p>
      <div className="form-actions">
        <button className="secondary-button" disabled={busy} onClick={onCancel} type="button">{cancelLabel}</button>
        <button className={danger ? 'danger-button' : 'primary-button'} disabled={busy} onClick={onConfirm} type="button">{confirmLabel}</button>
      </div>
    </section>
  </div>;
}
