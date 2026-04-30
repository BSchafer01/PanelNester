import { useEffect, useId, useMemo, useRef, useState } from 'react';

export interface ThemedSelectOption {
  value: string;
  label: string;
  note?: string;
}

interface ThemedSelectProps {
  ariaLabel: string;
  className?: string;
  disabled?: boolean;
  icon?: React.ReactNode;
  onChange: (value: string) => void;
  options: ThemedSelectOption[];
  value: string;
}

function ChevronGlyph() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="m7 10 5 5 5-5" />
    </svg>
  );
}

export function ThemedSelect({
  ariaLabel,
  className,
  disabled = false,
  icon,
  onChange,
  options,
  value,
}: ThemedSelectProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const listboxId = useId();
  const buttonId = useId();
  const selectedOption = useMemo(
    () => options.find((option) => option.value === value) ?? options[0],
    [options, value],
  );

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (rootRef.current?.contains(event.target as Node)) {
        return;
      }

      setOpen(false);
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  useEffect(() => {
    if (disabled && open) {
      setOpen(false);
    }
  }, [disabled, open]);

  return (
    <div
      className={className ? `module-select ${className}` : 'module-select'}
      ref={rootRef}
    >
      <button
        aria-controls={listboxId}
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-label={ariaLabel}
        className="module-select__trigger"
        disabled={disabled}
        id={buttonId}
        onClick={() => setOpen((currentValue) => !currentValue)}
        type="button"
      >
        {icon ? <span className="module-select__icon">{icon}</span> : null}
        <span className="module-select__value">
          {selectedOption?.label ?? value}
        </span>
        <span className="module-select__caret">
          <ChevronGlyph />
        </span>
      </button>

      {open ? (
        <div
          aria-labelledby={buttonId}
          className="module-select__menu"
          id={listboxId}
          role="listbox"
        >
          {options.map((option) => {
            const isSelected = option.value === value;

            return (
              <button
                aria-selected={isSelected}
                className={
                  isSelected
                    ? 'module-select__option module-select__option--selected'
                    : 'module-select__option'
                }
                key={option.value}
                onClick={() => {
                  onChange(option.value);
                  setOpen(false);
                }}
                role="option"
                type="button"
              >
                <span>{option.label}</span>
                {option.note ? <small>{option.note}</small> : null}
              </button>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
