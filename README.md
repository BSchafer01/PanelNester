# PanelNester

PanelNester is a Windows desktop application for importing rectangular part lists, grouping them by material, running sheet-nesting layouts, reviewing the results visually, and exporting job documentation.

The project is built as a **WPF desktop host** with a **WebView2-embedded React/TypeScript UI** and a **Three.js-based results viewer**, backed by .NET services for import, nesting, persistence, and reporting.

## What it does

PanelNester currently supports these core workflows:

- **Import part data** from CSV and XLSX files
- **Validate imported rows** and surface row-level errors and warnings
- **Manage a reusable material library** stored locally on the machine
- **Run rectangular sheet nesting** with margins, spacing, kerf-aware clearance, and optional 90° rotation
- **Batch nesting by material**
- **Review results visually** in the desktop app, including sheet-level layouts and utilization
- **Export PDF reports** for nesting output
- **Save and reopen projects** as `.pnest` files, including prior results and project metadata

## Current architecture

PanelNester is intentionally split by responsibility:

```text
WPF desktop host (.NET 8, Windows only)
    └─ WebView2
        └─ React + TypeScript web UI
            └─ Three.js results viewer

.NET service layer
    ├─ import services (CSV/XLSX)
    ├─ nesting services
    ├─ project persistence
    └─ PDF reporting

Pure domain layer
    └─ models + contracts
```

### Key layers

- **`src\PanelNester.Desktop`**  
  WPF shell, WebView2 hosting, native file dialogs, desktop bridge wiring, and Windows-specific startup/runtime handling.

- **`src\PanelNester.Domain`**  
  Core models and contracts, kept separate from UI and host concerns.

- **`src\PanelNester.Services`**  
  Application logic for import, material management, nesting, project persistence, and report generation.

- **`src\PanelNester.WebUI`**  
  React + TypeScript UI, including the main application flow and the Three.js viewer used on the results side.

- **`installer\PanelNester.Installer`**  
  WiX-based per-user MSI packaging project.

## Major capabilities

### Import and validation

- CSV import with required-field validation
- XLSX import through the same application pipeline
- Row-level validation feedback for dimensions, quantities, and materials
- Inline correction/editing support in the web UI
- Material mapping/import review flow in the current UI

### Materials and projects

- Local material library persisted to `%LOCALAPPDATA%\PanelNester\materials.json`
- Project metadata editing
- Saved project material snapshots so reopened work does not silently drift with later library edits
- `.pnest` save/open support
- Current project saves write the newer FlatBuffers-backed format, while older JSON `.pnest` files are still readable

### Nesting and output

- Shelf-based rectangular nesting heuristic
- Multi-material batch nesting support
- Utilization summaries, placed-part coordinates, and unplaced-item reporting
- PDF export generated from the .NET reporting layer
- Interactive results viewer powered by Three.js inside the WebView2 UI

## Runtime prerequisites

### To build from source

- **Windows 10 or Windows 11**
- **.NET 8 SDK**
- **Node.js 18+** and npm

> The repository uses the WiX SDK from the installer project itself, so a separate WiX installation should not be necessary for normal MSI builds from this repo.

### To run the installed application

- **x64 .NET 8 Desktop Runtime**
- **Microsoft Edge WebView2 Runtime**

These prerequisites are still relevant to the current codebase. The desktop host explicitly targets `net8.0-windows`, and the application depends on WebView2 for its UI shell.

## Building and running locally

From the repository root:

```powershell
dotnet restore .\PanelNester.slnx
dotnet build .\PanelNester.slnx -nologo
dotnet test .\PanelNester.slnx -nologo
```

Build the web UI:

```powershell
Set-Location .\src\PanelNester.WebUI
npm ci
npm run build
Set-Location ..\..
```

Run the desktop app:

```powershell
dotnet run --project .\src\PanelNester.Desktop\PanelNester.Desktop.csproj
```

### Important local-run note

The desktop host looks for a built web app at `src\PanelNester.WebUI\dist`. If that bundle exists, it is loaded automatically. If it does not, the host falls back to the placeholder content bundled under `src\PanelNester.Desktop\WebApp`.

In practice: **build the WebUI before launching the desktop app** if you want the full React interface instead of the placeholder page.

## Building the MSI installer

Build the installer from the repo root:

```powershell
dotnet build .\installer\PanelNester.Installer\PanelNester.Installer.wixproj -c Release -nologo
```

Output:

```text
installer\PanelNester.Installer\bin\Release\PanelNester-PerUser.msi
```

### What the installer build does

The WiX project is wired to do more than package existing binaries:

1. Restore WebUI dependencies if `node_modules` is missing
2. Build the React/TypeScript app
3. Publish the desktop host for `win-x64`
4. Replace the desktop project's placeholder `WebApp` content with the real built `dist` output
5. Produce a **per-user MSI**

The installer is configured to install under:

```text
%LOCALAPPDATA%\Programs\PanelNester
```

## Repository highlights

If you are browsing the codebase for the first time, these are good entry points:

- **`# PRD — Flat Sheet Nesting Desktop.md`**  
  Product definition and scope history for the app.

- **`src\PanelNester.Desktop\MainWindow.xaml.cs`**  
  Desktop shell startup and WebView2 initialization.

- **`src\PanelNester.Desktop\Bridge\`**  
  WPF ↔ WebView2 message bridge and native dialog integration.

- **`src\PanelNester.Services\Nesting\ShelfNestingService.cs`**  
  Core nesting heuristic implementation.

- **`src\PanelNester.Services\Projects\ProjectSerializer.cs`**  
  Project-file load/save flow, including legacy JSON read support and FlatBuffers-backed saves.

- **`src\PanelNester.Services\Reporting\QuestPdfReportExporter.cs`**  
  PDF report generation.

- **`src\PanelNester.WebUI\src\App.tsx`**  
  Front-end state orchestration across import, materials, projects, and results.

- **`tests\`**  
  xUnit coverage for domain, services, and desktop bridge behavior, plus phased test matrices and smoke guidance.

- **`happy-path.csv` / `happy-path.xlsx`**  
  Useful sample input files when trying the app locally.

## Current status

PanelNester is an **active in-repo product under ongoing hardening**, not a polished general-release package.

Current signals from the repository:

- Core import, material, nesting, project, results, and PDF flows are implemented
- The current solution build, test run, WebUI build, and MSI build all succeed in this repository
- The latest local test baseline is **143 tests: 141 passing, 2 documented skips**
- Some scenarios still rely on phased smoke guidance and manual verification rather than full end-to-end automation

## Caveats and scope boundaries

This repository is already useful, but it is not trying to solve every nesting problem:

- **Windows only** at present
- Focused on **rectangular parts**
- Uses a **shelf heuristic**, not a global optimization engine
- Rotation support is limited to practical sheet rotation rules in the current implementation
- Data is **local-first**; there is no cloud sync or multi-user collaboration layer
- Runtime setup still matters on target machines because the installed app depends on the **.NET 8 Desktop Runtime** and **WebView2 Runtime**

## Development workflow at a glance

Typical local loop:

1. Build/test the .NET solution
2. Build the WebUI bundle
3. Run the desktop host
4. Use `happy-path.csv` or `happy-path.xlsx` to exercise the import flow
5. Build the MSI when you need an installer artifact

## Notes for GitHub readers

This repo is strongest if you evaluate it as a **real, layered desktop application in active development**:

- desktop host concerns live in WPF
- application logic lives in .NET services
- UI iteration happens in React/TypeScript
- visualization is handled with Three.js
- packaging is first-class through the WiX installer project

If you want to understand the system quickly, start with the service layer and bridge contracts, then trace into the WebUI pages and the desktop shell.

## License

This repository is published under the `MIT` License. See [`LICENSE`](LICENSE) for the full text.
