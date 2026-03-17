interface StatusPillProps {
  tone: 'ok' | 'warn' | 'error' | 'muted';
  label: string;
}

export function StatusPill({ tone, label }: StatusPillProps) {
  return (
    <span className={`status-pill status-pill--${tone}`}>
      {label}
    </span>
  );
}
