import { useEffect, useMemo, useState } from 'react';

const maxVisibleOptions = 8;

interface MaterialComboboxProps {
  inputId: string;
  value: string;
  materials: string[];
  onChange: (value: string) => void;
  disabled?: boolean;
}

function normalizeMaterialName(value: string): string {
  return value.trim().toLocaleLowerCase();
}

function getVisibleMaterialOptions(materials: string[], value: string): string[] {
  const normalizedValue = normalizeMaterialName(value);

  if (normalizedValue.length === 0) {
    return materials.slice(0, maxVisibleOptions);
  }

  const prefixMatches: string[] = [];
  const containsMatches: string[] = [];

  for (const materialName of materials) {
    const normalizedMaterialName = materialName.toLocaleLowerCase();

    if (normalizedMaterialName.startsWith(normalizedValue)) {
      prefixMatches.push(materialName);
      continue;
    }

    if (normalizedMaterialName.includes(normalizedValue)) {
      containsMatches.push(materialName);
    }
  }

  return [...prefixMatches, ...containsMatches].slice(0, maxVisibleOptions);
}

export function MaterialCombobox({
  inputId,
  value,
  materials,
  onChange,
  disabled = false,
}: MaterialComboboxProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);

  const listboxId = `${inputId}-listbox`;
  const visibleOptions = useMemo(
    () => getVisibleMaterialOptions(materials, value),
    [materials, value],
  );
  const showSuggestions = !disabled && isOpen && visibleOptions.length > 0;
  const activeOptionId =
    showSuggestions && activeIndex >= 0
      ? `${listboxId}-option-${activeIndex}`
      : undefined;

  useEffect(() => {
    if (disabled) {
      setIsOpen(false);
      setActiveIndex(-1);
      return;
    }

    if (visibleOptions.length === 0) {
      setActiveIndex(-1);
      return;
    }

    if (activeIndex >= visibleOptions.length) {
      setActiveIndex(visibleOptions.length - 1);
    }
  }, [activeIndex, disabled, visibleOptions]);

  const commitSelection = (nextValue: string) => {
    onChange(nextValue);
    setIsOpen(false);
    setActiveIndex(-1);
  };

  const moveActiveOption = (direction: 1 | -1) => {
    if (visibleOptions.length === 0) {
      return;
    }

    setIsOpen(true);
    setActiveIndex((current) => {
      if (current < 0) {
        return direction === 1 ? 0 : visibleOptions.length - 1;
      }

      const nextIndex = current + direction;

      if (nextIndex < 0) {
        return visibleOptions.length - 1;
      }

      if (nextIndex >= visibleOptions.length) {
        return 0;
      }

      return nextIndex;
    });
  };

  return (
    <div className="material-combobox">
      <input
        aria-activedescendant={activeOptionId}
        aria-autocomplete="list"
        aria-controls={listboxId}
        aria-expanded={showSuggestions}
        aria-haspopup="listbox"
        autoComplete="off"
        className={showSuggestions ? 'material-combobox__input material-combobox__input--open' : 'material-combobox__input'}
        disabled={disabled}
        id={inputId}
        onBlur={() => {
          setIsOpen(false);
          setActiveIndex(-1);
        }}
        onChange={(event) => {
          onChange(event.target.value);
          setIsOpen(true);
          setActiveIndex(-1);
        }}
        onClick={() => {
          if (visibleOptions.length > 0) {
            setIsOpen(true);
          }
        }}
        onFocus={() => {
          if (visibleOptions.length > 0) {
            setIsOpen(true);
          }
        }}
        onKeyDown={(event) => {
          switch (event.key) {
            case 'ArrowDown':
              event.preventDefault();
              moveActiveOption(1);
              break;
            case 'ArrowUp':
              event.preventDefault();
              moveActiveOption(-1);
              break;
            case 'Enter':
              if (showSuggestions && activeIndex >= 0) {
                event.preventDefault();
                commitSelection(visibleOptions[activeIndex]);
              }
              break;
            case 'Escape':
              if (isOpen) {
                event.preventDefault();
                setIsOpen(false);
                setActiveIndex(-1);
              }
              break;
            case 'Tab':
              setIsOpen(false);
              setActiveIndex(-1);
              break;
            default:
              break;
          }
        }}
        role="combobox"
        spellCheck={false}
        type="text"
        value={value}
      />
      {showSuggestions ? (
        <ul className="material-combobox__list" id={listboxId} role="listbox">
          {visibleOptions.map((materialName, index) => {
            const isActive = index === activeIndex;

            return (
              <li
                aria-selected={isActive}
                className={
                  isActive
                    ? 'material-combobox__option material-combobox__option--active'
                    : 'material-combobox__option'
                }
                id={`${listboxId}-option-${index}`}
                key={materialName}
                onMouseDown={(event) => {
                  event.preventDefault();
                }}
                onClick={() => commitSelection(materialName)}
                role="option"
              >
                {materialName}
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
  );
}
