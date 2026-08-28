import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import App from '../src/App';
import { AppShell } from '../src/components/AppShell';
import {
  NewProjectDialog,
  ProjectKindControl,
} from '../src/components/ProjectKindControls';
import { OverviewPage } from '../src/pages/OverviewPage';
import { ResultsPage } from '../src/pages/ResultsPage';
import {
  defaultStiffenerTakeoffSettings,
  emptyBatchNestResponse,
  emptyImportResponse,
  emptyNestResponse,
} from '../src/types/contracts';

const shellDefaults = {
  onRouteChange: vi.fn(),
  projectBusy: false,
  bridgeConnected: true,
  onCreateProject: async () => undefined,
  onOpenProject: async () => undefined,
  onSaveProject: async () => undefined,
  onSaveProjectAs: async () => undefined,
  canOpenProject: true,
  canSaveProject: true,
  canSaveProjectAs: true,
  onReconnect: async () => undefined,
};

const metadata = {
  projectName: 'Mesa Canopy',
  projectNumber: '',
  customerName: '',
  estimator: '',
  drafter: '',
  projectManager: '',
  date: '2026-08-26',
  requiredDate: '',
  revision: '',
  notes: '',
};

const reportSettings = {
  companyName: '',
  reportTitle: '',
  projectJobName: '',
  projectJobNumber: '',
  releaseId: '',
  status: '',
  reportDate: '2026-08-26',
  notes: '',
};

describe('Project Kind lifecycle', () => {
  it('runs the rendered App lifecycle and restores the Sheet workflow without losing report metadata', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByTitle('Project actions'));
    await user.click(screen.getByRole('menuitem', { name: /New/ }));
    await user.click(screen.getByRole('radio', { name: 'Stock-Length Project' }));
    await user.click(screen.getByRole('button', { name: 'Create Project' }));

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Materials' })).not.toBeInTheDocument();
    });
    const sawKerf = screen.getByRole('textbox', { name: /Saw Kerf/ });
    expect(sawKerf).toHaveValue('0');
    await user.clear(sawKerf);
    await user.type(sawKerf, '1/16');
    await user.tab();
    expect(sawKerf).toHaveValue('0.0625');
    await user.click(screen.getByRole('button', { name: 'Import' }));
    expect(screen.getByRole('heading', { name: 'Required Piece Entries' })).toBeInTheDocument();
    expect(screen.queryByText(/Material Resolution/)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Project' }));

    const companyName = screen.getByRole('textbox', { name: /^Company Name/ });
    await user.clear(companyName);
    await user.type(companyName, 'Configured Company');

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Project Kind' }),
      'sheet',
    );
    await user.click(screen.getByRole('button', { name: 'Change Project Kind' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Materials' })).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Extrusions' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /^Company Name/ })).toHaveValue(
      'Configured Company',
    );
    expect(screen.getByRole('spinbutton', { name: /Kerf Allowance/ })).toHaveValue(0.0625);
    expect(screen.getAllByRole('tab', { name: 'Stiffeners' })).toHaveLength(2);
  });

  it('creates a Sheet Project by default and lets the user choose Stock-Length Project', async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn();
    render(<NewProjectDialog onCancel={vi.fn()} onCreate={onCreate} />);

    expect(screen.getByRole('radio', { name: 'Sheet Project' })).toBeChecked();
    await user.click(screen.getByRole('radio', { name: 'Stock-Length Project' }));
    await user.click(screen.getByRole('button', { name: 'Create Project' }));

    expect(onCreate).toHaveBeenCalledWith('stockLength');
  });

  it('guards Sheet-only routes and navigation in a Stock-Length Project', async () => {
    const onRouteChange = vi.fn();
    render(
      <AppShell
        {...shellDefaults}
        activeRoute="materials"
        onRouteChange={onRouteChange}
        projectKind="stockLength"
      >
        <div>Workspace</div>
      </AppShell>,
    );

    await waitFor(() => expect(onRouteChange).toHaveBeenCalledWith('overview'));
    expect(screen.queryByRole('button', { name: 'Materials' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Extrusions' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Project' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Results' })).toBeInTheDocument();
  });

  it('retains every existing route for a Sheet Project', () => {
    render(
      <AppShell {...shellDefaults} activeRoute="overview" projectKind="sheet">
        <div>Workspace</div>
      </AppShell>,
    );

    expect(screen.getByRole('button', { name: 'Materials' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Extrusions' })).toBeInTheDocument();
  });

  it('requires confirmation before changing an empty project kind', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <ProjectKindControl
        canChange
        onChange={onChange}
        projectKind="sheet"
      />,
    );

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Project Kind' }),
      'stockLength',
    );
    expect(screen.getByRole('dialog', { name: 'Change Project Kind?' })).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Change Project Kind' }));
    expect(onChange).toHaveBeenCalledWith('stockLength');
  });

  it('disables kind changes when the project contains kind-specific entries', () => {
    render(
      <ProjectKindControl
        canChange={false}
        onChange={vi.fn()}
        projectKind="sheet"
      />,
    );

    expect(screen.getByRole('combobox', { name: 'Project Kind' })).toBeDisabled();
    expect(screen.getByText(/no sheet parts or Required Pieces/i)).toBeInTheDocument();
  });

  it('removes stiffener settings and result controls from Stock-Length Projects', () => {
    const { rerender } = render(
      <OverviewPage
        canChangeProjectKind
        companyLogoPath={null}
        importResponse={emptyImportResponse}
        kerfWidth={0}
        metadata={metadata}
        nestResponse={emptyNestResponse}
        onKerfWidthChange={vi.fn()}
        onMetadataChange={vi.fn()}
        onPickCompanyLogo={async () => undefined}
        onProjectKindChange={vi.fn()}
        onReportSettingsChange={vi.fn()}
        onSaveDesktopAppSettings={async () => true}
        onStiffenerTakeoffChange={vi.fn()}
        projectBusy={false}
        projectDirty={false}
        projectKind="stockLength"
        projectMessage="Ready"
        reportSettings={reportSettings}
        savedMaterialSnapshots={[]}
        stiffenerTakeoff={defaultStiffenerTakeoffSettings}
      />,
    );

    expect(screen.queryByRole('tab', { name: 'Stiffeners' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Nesting' })).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Saw Kerf/ })).toHaveValue('0');
    expect(screen.queryByRole('spinbutton', { name: /Kerf Allowance/ })).not.toBeInTheDocument();

    rerender(
      <ResultsPage
        activeOptimizationGroupId={undefined}
        batchNestResponse={emptyBatchNestResponse}
        canExportExcelReport={false}
        canExportReport={false}
        canExportStiffenerReport={false}
        canPreviewStiffenerTakeoff={false}
        canSyncReportSettings={false}
        companyLogoPath={null}
        kerfWidth={0}
        nestResponse={emptyNestResponse}
        onExportExcelReport={async () => undefined}
        onExportReport={async () => undefined}
        onExportStiffenerReport={async () => undefined}
        onPickCompanyLogo={async () => undefined}
        onReportSettingsChange={vi.fn()}
        onSaveDesktopAppSettings={async () => true}
        onSelectOptimizationGroup={vi.fn()}
        onStiffenerTakeoffChange={vi.fn()}
        optimizationGroups={[]}
        pendingMaterialSnapshots={[]}
        projectDirty={false}
        reportBusy={false}
        reportMessage="Ready"
        reportSettings={reportSettings}
        savedMaterialSnapshots={[]}
        showStiffenerControls={false}
        statusMessage="Ready"
        stiffenerBusy={false}
        stiffenerMessage=""
        stiffenerTakeoffEnabled={false}
        stiffenerTakeoffReport={null}
        stiffenerTakeoffSettings={defaultStiffenerTakeoffSettings}
      />,
    );

    expect(screen.queryByRole('tab', { name: 'Stiffeners' })).not.toBeInTheDocument();
  });
});
