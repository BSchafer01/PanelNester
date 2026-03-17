import { useEffect, useRef, useState } from 'react';

type AppRoute = 'overview' | 'import' | 'materials' | 'results';

interface AppShellProps {
  activeRoute: AppRoute;
  onRouteChange: (route: AppRoute) => void;
  projectBusy: boolean;
  onCreateProject: () => Promise<void>;
  onOpenProject: () => Promise<void>;
  onSaveProject: () => Promise<void>;
  onSaveProjectAs: () => Promise<void>;
  canOpenProject: boolean;
  canSaveProject: boolean;
  canSaveProjectAs: boolean;
  children: React.ReactNode;
}

const navigationItems: Array<{ route: AppRoute; label: string; abbr: string }> = [
  { route: 'overview', label: 'Project', abbr: 'PRJ' },
  { route: 'import', label: 'Import', abbr: 'IMP' },
  { route: 'materials', label: 'Materials', abbr: 'MAT' },
  { route: 'results', label: 'Results', abbr: 'RES' },
];

export function AppShell({
  activeRoute,
  onRouteChange,
  projectBusy,
  onCreateProject,
  onOpenProject,
  onSaveProject,
  onSaveProjectAs,
  canOpenProject,
  canSaveProject,
  canSaveProjectAs,
  children,
}: AppShellProps) {
  const [fileMenuOpen, setFileMenuOpen] = useState(false);
  const fileMenuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!fileMenuOpen) {
      return undefined;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (fileMenuRef.current?.contains(event.target as Node)) {
        return;
      }

      setFileMenuOpen(false);
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setFileMenuOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [fileMenuOpen]);

  const runFileAction = (
    action: () => Promise<void>,
    disabled: boolean,
  ) => {
    if (disabled) {
      return;
    }

    setFileMenuOpen(false);
    void action();
  };

  return (
    <div className="app-shell">
      <header className="app-shell__header">
        <div className="app-shell__menu-bar" role="menubar" aria-label="Application menu">
          <div className="app-shell__menu" ref={fileMenuRef}>
            <button
              aria-expanded={fileMenuOpen}
              aria-haspopup="menu"
              className={
                fileMenuOpen
                  ? 'app-shell__menu-button app-shell__menu-button--open'
                  : 'app-shell__menu-button'
              }
              onClick={() => setFileMenuOpen((currentValue) => !currentValue)}
              type="button"
            >
              File
            </button>
            {fileMenuOpen ? (
              <div className="app-shell__menu-dropdown" role="menu" aria-label="File">
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onCreateProject, projectBusy)}
                  role="menuitem"
                  disabled={projectBusy}
                  type="button"
                >
                  New
                </button>
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onOpenProject, !canOpenProject || projectBusy)}
                  role="menuitem"
                  disabled={!canOpenProject || projectBusy}
                  type="button"
                >
                  Open
                </button>
                <div className="app-shell__menu-divider" />
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onSaveProject, !canSaveProject || projectBusy)}
                  role="menuitem"
                  disabled={!canSaveProject || projectBusy}
                  type="button"
                >
                  Save
                </button>
                <button
                  className="app-shell__menu-item"
                  onClick={() =>
                    runFileAction(onSaveProjectAs, !canSaveProjectAs || projectBusy)
                  }
                  role="menuitem"
                  disabled={!canSaveProjectAs || projectBusy}
                  type="button"
                >
                  Save As
                </button>
              </div>
            ) : null}
          </div>
        </div>
      </header>

      <div className="app-shell__body">
        <nav className="app-shell__nav" aria-label="Primary">
          {navigationItems.map((item) => (
            <button
              key={item.route}
              className={
                item.route === activeRoute
                  ? 'nav-button nav-button--active'
                  : 'nav-button'
              }
              onClick={() => onRouteChange(item.route)}
              type="button"
              title={item.label}
            >
              {item.abbr}
            </button>
          ))}
        </nav>

        <main className="app-shell__content">{children}</main>
      </div>
    </div>
  );
}
