import { useEffect, useRef, useState } from 'react';

type AppRoute = 'overview' | 'import' | 'materials' | 'extrusions' | 'results';
type UiDensity = 'relaxed' | 'condensed';

const uiDensityStorageKey = 'panelNester.uiDensity';

interface AppShellProps {
  activeRoute: AppRoute;
  onRouteChange: (route: AppRoute) => void;
  projectBusy: boolean;
  bridgeConnected: boolean;
  bridgeStatusMessage?: string;
  onCreateProject: () => Promise<void>;
  onOpenProject: () => Promise<void>;
  onSaveProject: () => Promise<void>;
  onSaveProjectAs: () => Promise<void>;
  canOpenProject: boolean;
  canSaveProject: boolean;
  canSaveProjectAs: boolean;
  onReconnect: () => Promise<void>;
  children: React.ReactNode;
}

type NavigationIcon = 'project' | 'import' | 'materials' | 'extrusions' | 'results';
type ProjectShortcutAction = 'new' | 'open' | 'save' | 'saveAs';

const navigationItems: Array<{
  route: AppRoute;
  label: string;
  icon: NavigationIcon;
}> = [
  { route: 'overview', label: 'Project', icon: 'project' },
  { route: 'import', label: 'Import', icon: 'import' },
  { route: 'materials', label: 'Materials', icon: 'materials' },
  { route: 'extrusions', label: 'Extrusions', icon: 'extrusions' },
  { route: 'results', label: 'Results', icon: 'results' },
];

const projectMenuShortcuts: Record<ProjectShortcutAction, string> = {
  new: 'Ctrl+N',
  open: 'Ctrl+O',
  save: 'Ctrl+S',
  saveAs: 'Ctrl+Shift+S',
};

function resolveProjectShortcut(event: KeyboardEvent): ProjectShortcutAction | null {
  if (event.defaultPrevented || event.repeat || event.altKey || !(event.ctrlKey || event.metaKey)) {
    return null;
  }

  const key = event.key.toLowerCase();

  if (key === 'n' && !event.shiftKey) {
    return 'new';
  }

  if (key === 'o' && !event.shiftKey) {
    return 'open';
  }

  if (key === 's') {
    return event.shiftKey ? 'saveAs' : 'save';
  }

  return null;
}

function NavigationGlyph({ icon }: { icon: NavigationIcon }) {
  switch (icon) {
    case 'project':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M4 8.5h6l1.8 2.2H20v7.8a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 18.5z" />
          <path d="M4 8.5V6.8A1.8 1.8 0 0 1 5.8 5h4.5l1.5 1.8h6A1.8 1.8 0 0 1 19.6 8v2.7" />
        </svg>
      );
    case 'import':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M10 5h8a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1h-8" />
          <path d="M12 8l-4 4 4 4" />
          <path d="M17 12H6" />
        </svg>
      );
    case 'materials':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M12 4l7 5-7 5-7-5z" />
          <path d="M5 12l7 5 7-5" />
          <path d="M5 15l7 5 7-5" />
        </svg>
      );
    case 'results':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M5 5h14v14H5z" />
          <path d="M9 15V11" />
          <path d="M12 15V8" />
          <path d="M15 15v-4" />
        </svg>
      );
    case 'extrusions':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M4 6h7v5H4z" />
          <path d="M13 6h7v5h-7z" />
          <path d="M4 13h7v5H4z" />
          <path d="M13 13h7v5h-7z" />
          <path d="M12 5v14" />
          <path d="M3 12h18" />
        </svg>
      );
    default:
      return null;
  }
}

function SettingsGlyph() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M12 8.8A3.2 3.2 0 1 0 12 15.2A3.2 3.2 0 1 0 12 8.8Z" />
      <path d="M19.4 13.1v-2.2l-2-.5a6 6 0 0 0-.5-1.1l1.1-1.8-1.6-1.6-1.8 1.1a6 6 0 0 0-1.1-.5l-.5-2H10.9l-.5 2a6 6 0 0 0-1.1.5L7.5 5.9 5.9 7.5 7 9.3a6 6 0 0 0-.5 1.1l-2 .5v2.2l2 .5a6 6 0 0 0 .5 1.1l-1.1 1.8 1.6 1.6 1.8-1.1a6 6 0 0 0 1.1.5l.5 2h2.2l.5-2a6 6 0 0 0 1.1-.5l1.8 1.1 1.6-1.6-1.1-1.8a6 6 0 0 0 .5-1.1z" />
    </svg>
  );
}

function MenuItemLabel({
  label,
  shortcut,
}: {
  label: string;
  shortcut?: string;
}) {
  return (
    <>
      <span className="app-shell__menu-item-label">{label}</span>
      {shortcut ? (
        <span aria-hidden="true" className="app-shell__menu-item-shortcut">
          {shortcut}
        </span>
      ) : null}
    </>
  );
}

export function AppShell({
  activeRoute,
  onRouteChange,
  projectBusy,
  bridgeConnected,
  bridgeStatusMessage,
  onCreateProject,
  onOpenProject,
  onSaveProject,
  onSaveProjectAs,
  canOpenProject,
  canSaveProject,
  canSaveProjectAs,
  onReconnect,
  children,
}: AppShellProps) {
  const [settingsMenuOpen, setSettingsMenuOpen] = useState(false);
  const [reconnectBusy, setReconnectBusy] = useState(false);
  const [uiDensity, setUiDensity] = useState<UiDensity>(() => {
    try {
      const storedDensity = window.localStorage.getItem(uiDensityStorageKey);
      return storedDensity === 'condensed' ? 'condensed' : 'relaxed';
    } catch {
      return 'relaxed';
    }
  });
  const settingsMenuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    try {
      window.localStorage.setItem(uiDensityStorageKey, uiDensity);
    } catch {
      // Ignore storage failures and keep the current session preference.
    }
  }, [uiDensity]);

  useEffect(() => {
    const rootStyle = document.documentElement.style;
    const syncViewportHeight = () => {
      const viewportHeight = Math.round(
        window.visualViewport?.height ?? window.innerHeight,
      );
      rootStyle.setProperty('--app-viewport-height', `${viewportHeight}px`);
    };

    syncViewportHeight();
    window.addEventListener('resize', syncViewportHeight);
    window.visualViewport?.addEventListener('resize', syncViewportHeight);

    return () => {
      window.removeEventListener('resize', syncViewportHeight);
      window.visualViewport?.removeEventListener('resize', syncViewportHeight);
    };
  }, []);

  useEffect(() => {
    if (!settingsMenuOpen) {
      return undefined;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (settingsMenuRef.current?.contains(event.target as Node)) {
        return;
      }

      setSettingsMenuOpen(false);
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setSettingsMenuOpen(false);
      }
    };

    document.addEventListener('mousedown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [settingsMenuOpen]);

  useEffect(() => {
    const handleProjectShortcut = (event: KeyboardEvent) => {
      const shortcut = resolveProjectShortcut(event);
      if (!shortcut) {
        return;
      }

      event.preventDefault();
      setSettingsMenuOpen(false);

      switch (shortcut) {
        case 'new':
          if (!projectBusy) {
            void onCreateProject();
          }
          break;
        case 'open':
          if (canOpenProject && !projectBusy) {
            void onOpenProject();
          }
          break;
        case 'save':
          if (canSaveProject && !projectBusy) {
            void onSaveProject();
          }
          break;
        case 'saveAs':
          if (canSaveProjectAs && !projectBusy) {
            void onSaveProjectAs();
          }
          break;
        default:
          break;
      }
    };

    document.addEventListener('keydown', handleProjectShortcut);

    return () => {
      document.removeEventListener('keydown', handleProjectShortcut);
    };
  }, [
    canOpenProject,
    canSaveProject,
    canSaveProjectAs,
    onCreateProject,
    onOpenProject,
    onSaveProject,
    onSaveProjectAs,
    projectBusy,
  ]);

  const runFileAction = (
    action: () => Promise<void>,
    disabled: boolean,
  ) => {
    if (disabled) {
      return;
    }

    setSettingsMenuOpen(false);
    void action();
  };

  const handleReconnect = async () => {
    if (reconnectBusy) {
      return;
    }

    setReconnectBusy(true);

    try {
      await onReconnect();
    } finally {
      setReconnectBusy(false);
    }
  };

  return (
    <div
      className={
        uiDensity === 'condensed'
          ? 'app-shell app-shell--condensed'
          : 'app-shell'
      }
    >
      <header className="app-shell__header">
        <div className="app-shell__brand">
          <span className="app-shell__brand-title">OptiFab V1.0</span>
        </div>

        <div className="app-shell__header-actions">
          {!bridgeConnected ? (
            <button
              className="secondary-button app-shell__reconnect-button"
              disabled={reconnectBusy}
              onClick={() => void handleReconnect()}
              title={bridgeStatusMessage ?? 'Desktop host connection unavailable.'}
              type="button"
            >
              {reconnectBusy ? 'Reconnecting…' : 'Reconnect'}
            </button>
          ) : null}

          <div className="app-shell__menu" ref={settingsMenuRef}>
            <button
              aria-expanded={settingsMenuOpen}
              aria-haspopup="menu"
              className={
                settingsMenuOpen
                  ? 'app-shell__icon-button app-shell__icon-button--open'
                  : 'app-shell__icon-button'
              }
              onClick={() => setSettingsMenuOpen((currentValue) => !currentValue)}
              title="Project actions"
              type="button"
            >
              <SettingsGlyph />
            </button>

            {settingsMenuOpen ? (
              <div className="app-shell__menu-dropdown" role="menu" aria-label="Project actions">
                <div className="app-shell__menu-label">Project actions</div>
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onCreateProject, projectBusy)}
                  role="menuitem"
                  disabled={projectBusy}
                  type="button"
                >
                  <MenuItemLabel
                    label="New"
                    shortcut={projectMenuShortcuts.new}
                  />
                </button>
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onOpenProject, !canOpenProject || projectBusy)}
                  role="menuitem"
                  disabled={!canOpenProject || projectBusy}
                  type="button"
                >
                  <MenuItemLabel
                    label="Open"
                    shortcut={projectMenuShortcuts.open}
                  />
                </button>
                <div className="app-shell__menu-divider" />
                <button
                  className="app-shell__menu-item"
                  onClick={() => runFileAction(onSaveProject, !canSaveProject || projectBusy)}
                  role="menuitem"
                  disabled={!canSaveProject || projectBusy}
                  type="button"
                >
                  <MenuItemLabel
                    label="Save"
                    shortcut={projectMenuShortcuts.save}
                  />
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
                  <MenuItemLabel
                    label="Save As"
                    shortcut={projectMenuShortcuts.saveAs}
                  />
                </button>

                <div className="app-shell__menu-divider" />
                <div className="app-shell__menu-section">
                  <div className="app-shell__menu-label">Display density</div>
                  <div
                    aria-label="Display density"
                    className="app-shell__density-toggle"
                    role="group"
                  >
                    <button
                      aria-pressed={uiDensity === 'relaxed'}
                      className={
                        uiDensity === 'relaxed'
                          ? 'app-shell__density-option app-shell__density-option--active'
                          : 'app-shell__density-option'
                      }
                      onClick={() => setUiDensity('relaxed')}
                      type="button"
                    >
                      Relaxed
                    </button>
                    <button
                      aria-pressed={uiDensity === 'condensed'}
                      className={
                        uiDensity === 'condensed'
                          ? 'app-shell__density-option app-shell__density-option--active'
                          : 'app-shell__density-option'
                      }
                      onClick={() => setUiDensity('condensed')}
                      type="button"
                    >
                      Condensed
                    </button>
                  </div>
                </div>

                {!bridgeConnected ? (
                  <>
                    <div className="app-shell__menu-divider" />
                    <button
                      className="app-shell__menu-item"
                      disabled={reconnectBusy}
                      onClick={() => {
                        setSettingsMenuOpen(false);
                        void handleReconnect();
                      }}
                      role="menuitem"
                      type="button"
                    >
                      {reconnectBusy ? 'Reconnecting…' : 'Reconnect host'}
                    </button>
                  </>
                ) : null}
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
              <span className="nav-button__icon">
                <NavigationGlyph icon={item.icon} />
              </span>
              <span className="nav-button__label">{item.label}</span>
            </button>
          ))}
        </nav>

        <main className="app-shell__content">{children}</main>
      </div>
    </div>
  );
}
