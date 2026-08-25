import { useState, type ReactNode } from 'react';
import { StatusPill } from '../components/StatusPill';
import type {
  ImportResponse,
  NestResponse,
  OptimizationGroup,
  ProjectMaterialSnapshot,
  ProjectMetadata,
  ReportSettings,
  StiffenerTakeoffSettings,
} from '../types/contracts';

interface OverviewPageProps {
  metadata: ProjectMetadata;
  projectBusy: boolean;
  projectDirty: boolean;
  projectFilePath?: string;
  projectMessage: string;
  importResponse: ImportResponse;
  nestResponse: NestResponse;
  savedMaterialSnapshots: ProjectMaterialSnapshot[];
  kerfWidth: number;
  reportSettings: ReportSettings;
  stiffenerTakeoff: StiffenerTakeoffSettings;
  onMetadataChange: (field: keyof ProjectMetadata, value: string) => void;
  onKerfWidthChange: (value: number) => void;
  onReportSettingsChange: (field: keyof ReportSettings, value: string) => void;
  onStiffenerTakeoffChange: (settings: StiffenerTakeoffSettings) => void;
  companyLogoPath?: string | null;
  onPickCompanyLogo: () => Promise<string | undefined>;
  onSaveDesktopAppSettings: (settings: {
    companyLogoPath?: string | null;
    companyName?: string | null;
  }) => Promise<boolean>;
  optimizationGroups: OptimizationGroup[];
  activeOptimizationGroupId?: string;
  canManageOptimizationGroups: boolean;
  onActivateOptimizationGroup: (optimizationGroupId: string) => void;
  onCreateOptimizationGroup: (name: string) => Promise<void>;
  onRenameOptimizationGroup: (optimizationGroupId: string, name: string) => Promise<void>;
  onReorderOptimizationGroups: (orderedOptimizationGroupIds: string[]) => Promise<void>;
  onDeleteOptimizationGroup: (
    optimizationGroupId: string,
    removeOwnedContent: boolean,
  ) => Promise<void>;
}

type ProjectIcon = 'project' | 'team' | 'nesting' | 'stiffener' | 'measure';
type AlgorithmTabKey = 'general' | 'nesting' | 'stiffeners';
type ReportTabKey = 'general' | 'nesting' | 'stiffeners';

interface TabButton {
  key: string;
  label: string;
}

function ProjectSectionIcon({ icon }: { icon: ProjectIcon }) {
  switch (icon) {
    case 'project':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M12 4.5 18.5 8v8L12 19.5 5.5 16V8z" />
          <path d="M12 9.25a2.75 2.75 0 1 0 0 5.5a2.75 2.75 0 1 0 0-5.5Z" />
          <path d="M12 4.5v4.75" />
        </svg>
      );
    case 'team':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M12 4.5 18.5 8v8L12 19.5 5.5 16V8z" />
          <path d="M12 9.25a2.75 2.75 0 1 0 0 5.5a2.75 2.75 0 1 0 0-5.5Z" />
          <path d="M8.25 17.1c1.15-1.25 2.32-1.85 3.75-1.85s2.6.6 3.75 1.85" />
        </svg>
      );
    case 'nesting':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M5 5h5v5H5z" />
          <path d="M14 5h5v5h-5z" />
          <path d="M5 14h5v5H5z" />
          <path d="M14 14h5v5h-5z" />
        </svg>
      );
    case 'stiffener':
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M5 7h14v6H5z" />
          <path d="M8 7v6" />
          <path d="M12 7v4" />
          <path d="M16 7v3" />
        </svg>
      );
    case 'measure':
    default:
      return (
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <path d="M7 5h10v14H7z" />
          <path d="M10 5v14" />
          <path d="M13 8h4" />
          <path d="M13 11h4" />
          <path d="M13 14h4" />
        </svg>
      );
  }
}

function FieldTooltip({ description }: { description?: string }) {
  if (!description) {
    return null;
  }

  return (
    <span
      aria-label={description}
      className="project-field__tooltip"
      data-tooltip={description}
      role="img"
      title={description}
    >
      i
    </span>
  );
}

function SectionField({
  label,
  description,
  children,
}: {
  label: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <label className="project-field project-field--stacked">
      <span className="project-field__label">
        <span>{label}</span>
        <FieldTooltip description={description} />
      </span>
      {children}
    </label>
  );
}

function NumberField({
  label,
  description,
  value,
  unit,
  step,
  min,
  disabled,
  onChange,
}: {
  label: string;
  description?: string;
  value: number;
  unit: string;
  step: number;
  min: number;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <SectionField description={description} label={label}>
      <div className="project-inline-input">
        <input
          disabled={disabled}
          min={min}
          onChange={(event) => {
            const nextValue = Number.parseFloat(event.target.value);
            if (!Number.isNaN(nextValue)) {
              onChange(nextValue);
            }
          }}
          step={step}
          type="number"
          value={value}
        />
        <strong>{unit}</strong>
      </div>
    </SectionField>
  );
}

function TextField({
  label,
  description,
  value,
  disabled,
  placeholder,
  onChange,
  onBlur,
}: {
  label: string;
  description?: string;
  value: string;
  disabled: boolean;
  placeholder?: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
}) {
  return (
    <SectionField description={description} label={label}>
      <input
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        onBlur={onBlur}
        placeholder={placeholder}
        type="text"
        value={value}
      />
    </SectionField>
  );
}

function ToggleField({
  label,
  description,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  description?: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <div className="project-toggle-field">
      <div className="project-toggle-field__copy">
        <span className="project-field__label">
          <span>{label}</span>
          <FieldTooltip description={description} />
        </span>
      </div>
      <label className="project-toggle">
        <input
          checked={checked}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
          type="checkbox"
        />
        <span className="project-toggle__track">
          <span className="project-toggle__thumb" />
        </span>
      </label>
    </div>
  );
}

function fileNameFromPath(value: string): string {
  const parts = value.split(/[\\/]/);
  return parts[parts.length - 1] ?? value;
}

function FileField({
  label,
  description,
  value,
  disabled,
  onChoose,
  onClear,
}: {
  label: string;
  description?: string;
  value: string;
  disabled: boolean;
  onChoose: () => Promise<void>;
  onClear: () => Promise<void>;
}) {
  return (
    <SectionField description={description} label={label}>
      <div className="project-file-field">
        <input
          disabled
          placeholder="No file selected"
          readOnly
          type="text"
          value={value ? fileNameFromPath(value) : ''}
        />
        <div className="project-file-field__actions">
          <button
            className="secondary-button"
            disabled={disabled}
            onClick={() => void onChoose()}
            type="button"
          >
            Choose
          </button>
          <button
            className="secondary-button"
            disabled={disabled || value.trim().length === 0}
            onClick={() => void onClear()}
            type="button"
          >
            Clear
          </button>
        </div>
      </div>
    </SectionField>
  );
}

function ParameterTabs<T extends string>({
  buttons,
  activeTab,
  onChange,
}: {
  buttons: TabButton[];
  activeTab: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="project-tab-list" role="tablist">
      {buttons.map((button) => {
        const isActive = button.key === activeTab;

        return (
          <button
            aria-selected={isActive}
            className={`project-tab-button${isActive ? ' project-tab-button--active' : ''}`}
            key={button.key}
            onClick={() => onChange(button.key as T)}
            role="tab"
            type="button"
          >
            {button.label}
          </button>
        );
      })}
    </div>
  );
}

function EmptyTabState({ title, description }: { title: string; description: string }) {
  return (
    <div className="project-empty-state">
      <strong>{title}</strong>
      <span>{description}</span>
    </div>
  );
}

export function OverviewPage({
  metadata,
  projectBusy,
  projectDirty,
  projectFilePath,
  projectMessage,
  importResponse,
  nestResponse,
  savedMaterialSnapshots,
  kerfWidth,
  reportSettings,
  stiffenerTakeoff,
  onMetadataChange,
  onKerfWidthChange,
  onReportSettingsChange,
  onStiffenerTakeoffChange,
  companyLogoPath,
  onPickCompanyLogo,
  onSaveDesktopAppSettings,
  optimizationGroups,
  activeOptimizationGroupId,
  canManageOptimizationGroups,
  onActivateOptimizationGroup,
  onCreateOptimizationGroup,
  onRenameOptimizationGroup,
  onReorderOptimizationGroups,
  onDeleteOptimizationGroup,
}: OverviewPageProps) {
  const [algorithmTab, setAlgorithmTab] = useState<AlgorithmTabKey>('general');
  const [reportTab, setReportTab] = useState<ReportTabKey>('general');
  const [newOptimizationGroupName, setNewOptimizationGroupName] = useState('');
  const hasResults =
    nestResponse.sheets.length > 0 || nestResponse.unplacedItems.length > 0;
  const formattedDate = metadata.date || new Date().toISOString().slice(0, 10);
  const formattedRequiredDate = metadata.requiredDate || '';
  const nestingPlaceholderDescription =
    importResponse.parts.length > 0 || savedMaterialSnapshots.length > 0
      ? `${importResponse.parts.length} imported rows and ${savedMaterialSnapshots.length} saved snapshots are already attached to this job.`
      : 'Nesting-specific controls will land here as we add more algorithm tuning.';

  const chooseCompanyLogo = async () => {
    const nextPath = await onPickCompanyLogo();
    if (nextPath === undefined) {
      return;
    }

    await onSaveDesktopAppSettings({
      companyLogoPath: nextPath,
    });
  };

  const clearCompanyLogo = async () => {
    await onSaveDesktopAppSettings({
      companyLogoPath: null,
    });
  };

  const handleCompanyNameChange = (value: string) => {
    onReportSettingsChange('companyName', value);
    void onSaveDesktopAppSettings({
      companyName: value.trim() || null,
    });
  };

  const algorithmButtons: TabButton[] = [
    { key: 'general', label: 'General' },
    { key: 'nesting', label: 'Nesting' },
    { key: 'stiffeners', label: 'Stiffeners' },
  ];

  const reportButtons: TabButton[] = [
    { key: 'general', label: 'General' },
    { key: 'nesting', label: 'Nesting' },
    { key: 'stiffeners', label: 'Stiffeners' },
  ];

  const moveOptimizationGroup = async (index: number, offset: -1 | 1) => {
    const targetIndex = index + offset;
    if (targetIndex < 0 || targetIndex >= optimizationGroups.length) {
      return;
    }

    const orderedIds = optimizationGroups.map((group) => group.optimizationGroupId);
    [orderedIds[index], orderedIds[targetIndex]] = [
      orderedIds[targetIndex],
      orderedIds[index],
    ];
    await onReorderOptimizationGroups(orderedIds);
  };

  const deleteOptimizationGroup = async (group: OptimizationGroup) => {
    const hasSavedResults = Boolean(
      group.lastNestingResult || group.lastBatchNestingResult,
    );
    const removeOwnedContent = group.parts.length > 0 || hasSavedResults;
    const message = removeOwnedContent
      ? `Delete ${group.name} and explicitly remove its owned content (${group.parts.length} part(s)${hasSavedResults ? ' and saved results' : ''})? Reassign parts first to keep them.`
      : `Delete empty Optimization Group ${group.name}?`;
    if (window.confirm(message)) {
      await onDeleteOptimizationGroup(group.optimizationGroupId, removeOwnedContent);
    }
  };

  return (
    <div className="project-setup">
      <header className="project-setup__hero">
        <p className="project-setup__breadcrumb">Workspace / Configuration</p>
        <div className="project-setup__hero-row">
          <div>
            <h1>Project Setup</h1>
            <p className="project-setup__intro">{projectMessage}</p>
          </div>
          <div className="status-row">
            <StatusPill
              tone={projectDirty ? 'warn' : projectFilePath ? 'ok' : 'muted'}
              label={
                projectDirty
                  ? 'Unsaved changes'
                  : projectFilePath
                    ? 'Saved file'
                    : 'Not saved'
              }
            />
            <StatusPill
              tone={hasResults ? 'ok' : 'muted'}
              label={hasResults ? 'Results captured' : 'Results pending'}
            />
          </div>
        </div>
      </header>

      <div className="project-setup__dashboard">
        <section className="project-card project-card--wide optimization-groups-card">
          <div className="project-card__header project-card__header--split">
            <div className="project-card__title">
              <ProjectSectionIcon icon="nesting" />
              <h2>Optimization Groups</h2>
            </div>
            <span className="project-card__hint">Ordered optimization boundaries</span>
          </div>

          <div className="optimization-groups-list">
            {optimizationGroups.map((group, index) => {
              const isActive = group.optimizationGroupId === activeOptimizationGroupId;
              return (
                <div
                  className={
                    isActive
                      ? 'optimization-group-row optimization-group-row--active'
                      : 'optimization-group-row'
                  }
                  key={group.optimizationGroupId}
                >
                  <button
                    aria-pressed={isActive}
                    className="secondary-button optimization-group-row__active"
                    disabled={projectBusy}
                    onClick={() => onActivateOptimizationGroup(group.optimizationGroupId)}
                    type="button"
                  >
                    {isActive ? 'Active' : 'Make active'}
                  </button>
                  <input
                    aria-label={`Optimization Group ${index + 1} name`}
                    defaultValue={group.name}
                    disabled={projectBusy || !canManageOptimizationGroups}
                    key={`${group.optimizationGroupId}:${group.name}`}
                    onBlur={(event) => {
                      const name = event.target.value.trim();
                      if (name && name !== group.name) {
                        void onRenameOptimizationGroup(group.optimizationGroupId, name);
                      }
                    }}
                    type="text"
                  />
                  <span>{group.parts.length} part(s)</span>
                  <div className="table-actions">
                    <button
                      className="module-table-action"
                      disabled={projectBusy || !canManageOptimizationGroups || index === 0}
                      onClick={() => void moveOptimizationGroup(index, -1)}
                      type="button"
                    >
                      Up
                    </button>
                    <button
                      className="module-table-action"
                      disabled={
                        projectBusy ||
                        !canManageOptimizationGroups ||
                        index === optimizationGroups.length - 1
                      }
                      onClick={() => void moveOptimizationGroup(index, 1)}
                      type="button"
                    >
                      Down
                    </button>
                    <button
                      className="module-table-action module-table-action--danger"
                      disabled={
                        projectBusy ||
                        !canManageOptimizationGroups ||
                        optimizationGroups.length === 1
                      }
                      onClick={() => void deleteOptimizationGroup(group)}
                      type="button"
                    >
                      Delete
                    </button>
                  </div>
                </div>
              );
            })}
          </div>

          <div className="optimization-groups-create">
            <input
              aria-label="New Optimization Group name"
              disabled={projectBusy || !canManageOptimizationGroups}
              onChange={(event) => setNewOptimizationGroupName(event.target.value)}
              placeholder="New Optimization Group name"
              type="text"
              value={newOptimizationGroupName}
            />
            <button
              className="primary-button"
              disabled={
                projectBusy ||
                !canManageOptimizationGroups ||
                newOptimizationGroupName.trim().length === 0
              }
              onClick={() => {
                const name = newOptimizationGroupName.trim();
                void onCreateOptimizationGroup(name).then(() =>
                  setNewOptimizationGroupName(''),
                );
              }}
              type="button"
            >
              Add Optimization Group
            </button>
          </div>
        </section>

        <section className="project-card project-card--identity">
          <div className="project-card__header">
            <div className="project-card__title">
              <ProjectSectionIcon icon="project" />
              <h2>Project &amp; Team Identity</h2>
            </div>
          </div>

          <div className="project-form-grid project-form-grid--identity">
            <label className="project-field project-field--wide">
              <span>Project Name</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('projectName', event.target.value)}
                type="text"
                value={metadata.projectName}
              />
            </label>

            <label className="project-field">
              <span>Project Number</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('projectNumber', event.target.value)}
                type="text"
                value={metadata.projectNumber}
              />
            </label>

            <label className="project-field">
              <span>Customer</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('customerName', event.target.value)}
                type="text"
                value={metadata.customerName}
              />
            </label>

            <label className="project-field">
              <span>Estimator</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('estimator', event.target.value)}
                type="text"
                value={metadata.estimator}
              />
            </label>

            <label className="project-field">
              <span>Drafter</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('drafter', event.target.value)}
                type="text"
                value={metadata.drafter}
              />
            </label>

            <label className="project-field">
              <span>PM / APM</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('projectManager', event.target.value)}
                type="text"
                value={metadata.projectManager}
              />
            </label>

            <label className="project-field">
              <span>Date</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('date', event.target.value)}
                type="date"
                value={formattedDate}
              />
            </label>

            <label className="project-field">
              <span>Required Date</span>
              <input
                disabled={projectBusy}
                onChange={(event) =>
                  onMetadataChange('requiredDate', event.target.value)
                }
                type="date"
                value={formattedRequiredDate}
              />
            </label>

            <label className="project-field project-field--wide">
              <span>Revision</span>
              <input
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('revision', event.target.value)}
                type="text"
                value={metadata.revision}
              />
            </label>

            <label className="project-field project-field--wide">
              <span>Description / Build Notes</span>
              <textarea
                disabled={projectBusy}
                onChange={(event) => onMetadataChange('notes', event.target.value)}
                value={metadata.notes}
              />
            </label>
          </div>
        </section>

        <section className="project-card project-card--compact">
          <div className="project-card__header">
            <div className="project-card__title">
              <ProjectSectionIcon icon="nesting" />
              <h2>Algorithm Parameters</h2>
            </div>
          </div>

          <ParameterTabs
            activeTab={algorithmTab}
            buttons={algorithmButtons}
            onChange={setAlgorithmTab}
          />

          <div className="project-tab-panel">
            {algorithmTab === 'general' ? (
              <div className="project-tab-form">
                <NumberField
                  description="Tool diameter offset compensation"
                  disabled={projectBusy}
                  label="Kerf Allowance"
                  min={0}
                  onChange={onKerfWidthChange}
                  step={0.0625}
                  unit="IN"
                  value={kerfWidth}
                />
              </div>
            ) : null}

            {algorithmTab === 'nesting' ? (
              <EmptyTabState
                description={nestingPlaceholderDescription}
                title="No nesting overrides yet"
              />
            ) : null}

            {algorithmTab === 'stiffeners' ? (
              <div className="project-tab-form">
                <ToggleField
                  checked={stiffenerTakeoff.enabled}
                  description="Include stiffener stock takeoff in results and export flows"
                  disabled={projectBusy}
                  label="Enable Stiffener Takeoff"
                  onChange={(checked) =>
                    onStiffenerTakeoffChange({
                      ...stiffenerTakeoff,
                      enabled: checked,
                    })
                  }
                />
                <div className="project-tab-form-grid">
                  <NumberField
                    description="Minimum eligible panel length for stiffener takeoff"
                    disabled={projectBusy}
                    label="Min. Panel Length"
                    min={0}
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        minimumLengthInches: value,
                      })
                    }
                    step={1}
                    unit="IN"
                    value={stiffenerTakeoff.minimumLengthInches}
                  />
                  <NumberField
                    description="Minimum eligible panel width for stiffener takeoff"
                    disabled={projectBusy}
                    label="Min Panel Width"
                    min={0}
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        minimumWidthInches: value,
                      })
                    }
                    step={1}
                    unit="IN"
                    value={stiffenerTakeoff.minimumWidthInches}
                  />
                  <NumberField
                    description="Deduction applied before stiffener stock takeoff"
                    disabled={projectBusy}
                    label="Stiffener Deduction"
                    min={0}
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        widthDeductionInches: value,
                      })
                    }
                    step={1}
                    unit="IN"
                    value={stiffenerTakeoff.widthDeductionInches}
                  />
                  <NumberField
                    description="Available stock length for cut planning"
                    disabled={projectBusy}
                    label="Stock Length"
                    min={0.125}
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        stockLengthFeet: value,
                      })
                    }
                    step={0.5}
                    unit="FT"
                    value={stiffenerTakeoff.stockLengthFeet}
                  />
                </div>
              </div>
            ) : null}
          </div>
        </section>

        <section className="project-card project-card--compact">
          <div className="project-card__header">
            <div className="project-card__title">
              <ProjectSectionIcon icon="project" />
              <h2>Report Parameters</h2>
            </div>
          </div>

          <ParameterTabs
            activeTab={reportTab}
            buttons={reportButtons}
            onChange={setReportTab}
          />

          <div className="project-tab-panel">
            {reportTab === 'general' ? (
              <div className="project-tab-form">
                <FileField
                  description="Global company logo used by report exports and persisted with the app"
                  disabled={projectBusy}
                  label="Company Logo"
                  onChoose={chooseCompanyLogo}
                  onClear={clearCompanyLogo}
                  value={companyLogoPath ?? ''}
                />
                <TextField
                  description="Company name shown on both report families"
                  disabled={projectBusy}
                  label="Company Name"
                  onChange={handleCompanyNameChange}
                  placeholder="e.g. ACME Panels"
                  value={reportSettings.companyName ?? ''}
                />
              </div>
            ) : null}

            {reportTab === 'nesting' ? (
              <div className="project-tab-form">
                <TextField
                  description="Dedicated title for the nesting PDF"
                  disabled={projectBusy}
                  label="Report Title"
                  onChange={(value) => onReportSettingsChange('reportTitle', value)}
                  placeholder="e.g. Batch Nesting Report"
                  value={reportSettings.reportTitle ?? ''}
                />
                <div className="project-tab-form-grid">
                  <TextField
                    description="Release identifier carried into the nesting report"
                    disabled={projectBusy}
                    label="Release"
                    onChange={(value) => onReportSettingsChange('releaseId', value)}
                    placeholder="e.g. REL-04B"
                    value={reportSettings.releaseId ?? ''}
                  />
                  <TextField
                    description="Status shown on the nesting report"
                    disabled={projectBusy}
                    label="Status"
                    onChange={(value) => onReportSettingsChange('status', value)}
                    placeholder="e.g. Ready"
                    value={reportSettings.status ?? ''}
                  />
                </div>
              </div>
            ) : null}

            {reportTab === 'stiffeners' ? (
              <div className="project-tab-form">
                <TextField
                  description="Title shown at the top of the stiffener PDF"
                  disabled={projectBusy}
                  label="Report Title"
                  onChange={(value) =>
                    onStiffenerTakeoffChange({
                      ...stiffenerTakeoff,
                      reportTitle: value,
                    })
                  }
                  placeholder="e.g. Project Stiffener Takeoff"
                  value={stiffenerTakeoff.reportTitle}
                />
                <div className="project-tab-form-grid">
                  <TextField
                    description="Purchase order number shown on the stiffener report"
                    disabled={projectBusy}
                    label="P.O. #"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        poNumber: value,
                      })
                    }
                    placeholder="e.g. PO-88210"
                    value={stiffenerTakeoff.poNumber}
                  />
                  <TextField
                    description="Extrusion type carried into the stiffener report"
                    disabled={projectBusy}
                    label="Extrusion"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        extrusion: value,
                      })
                    }
                    placeholder="e.g. 1 x 2 aluminum tube"
                    value={stiffenerTakeoff.extrusion}
                  />
                  <TextField
                    description="Finish color shown on the stiffener report"
                    disabled={projectBusy}
                    label="Color"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        color: value,
                      })
                    }
                    placeholder="e.g. Bone White"
                    value={stiffenerTakeoff.color}
                  />
                  <TextField
                    description="Color number associated with the stiffener finish"
                    disabled={projectBusy}
                    label="Color #"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        colorNumber: value,
                      })
                    }
                    placeholder="e.g. BW-11"
                    value={stiffenerTakeoff.colorNumber}
                  />
                  <TextField
                    description="Manufacturer shown on the stiffener report"
                    disabled={projectBusy}
                    label="Manufacturer"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        manufacturer: value,
                      })
                    }
                    placeholder="e.g. Kovach"
                    value={stiffenerTakeoff.manufacturer}
                  />
                  <TextField
                    description="Release identifier carried into the stiffener report"
                    disabled={projectBusy}
                    label="Release"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        releaseId: value,
                      })
                    }
                    placeholder="e.g. REL-04B"
                    value={stiffenerTakeoff.releaseId}
                  />
                  <TextField
                    description="Status shown on the stiffener report"
                    disabled={projectBusy}
                    label="Status"
                    onChange={(value) =>
                      onStiffenerTakeoffChange({
                        ...stiffenerTakeoff,
                        status: value,
                      })
                    }
                    placeholder="e.g. Ready for production"
                    value={stiffenerTakeoff.status}
                  />
                </div>
              </div>
            ) : null}
          </div>
        </section>
      </div>
    </div>
  );
}
