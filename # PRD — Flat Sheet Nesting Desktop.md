# PRD — Flat Sheet Nesting Desktop Application

**Working Title:** Panel / Sheet Nesting Tool  
**Platform:** Windows Desktop  
**Host Framework:** WPF  
**UI Layer:** WebView2-based full application UI  
**Version:** V1  
**Audience:** Internal fabrication / estimating / drafting users

---

## 1. Purpose

Build a local desktop application that allows users to import rectangular part data from CSV or Excel, define reusable material sheet settings, run a practical nesting routine, visualize nested sheets, and export a PDF report summarizing sheet usage and unplaced items.

The application is intended to be a **clean internal tool** focused on speed, usability, and clarity rather than perfect optimization or cloud collaboration.

---

## 2. Problem Statement

Users need a simple way to determine how many stock sheets of each material are required for a set of rectangular parts. Today, this process is often manual, slow, inconsistent, and difficult to document clearly for estimating, drafting, and shop planning.

The application should:
- reduce manual planning time,
- provide visual confidence in nesting results,
- clearly identify unplaced items,
- generate a professional internal PDF report,
- preserve reusable materials and project data locally.

---

## 3. Goals

### Primary Goals
- Import part lists from **CSV** and **XLSX**
- Support **rectangular parts only** in v1
- Support reusable **material library**
- Run a fast, practical nesting algorithm
- Show results visually sheet-by-sheet
- Summarize required sheet counts by material
- Identify and report unplaced items
- Export a configurable PDF report
- Save and reopen local nesting projects

### Non-Goals for V1
- Irregular polygon nesting
- Hole/cutout-aware nesting
- Free-angle rotation
- Part grain direction
- Manual drag/drop sheet editing
- Multi-sheet-size selection per material
- Cloud sync or collaboration
- Cost rollups in output
- Undo/redo
- Saved PDF report templates

---

## 4. Users

### Primary Users
- Estimators
- Drafters
- Project managers
- Fabrication planners

### User Characteristics
- Need quick sheet requirement answers
- Need confidence via visual layouts
- Need printable/exportable reporting
- Work primarily in Windows desktop environments
- Prefer local file-based workflows

---

## 5. Key Assumptions for V1

- Parts are **rectangles only**
- Units are always **inches**
- Numeric dimensions are **decimal only**
- Rotation is **90° only**
- Rotation permission is controlled **per material**
- Each material has **one stock sheet size**
- Material matching from import is **exact text match only**
- Parts cannot span sheets
- Invalid or too-large parts should appear in **Unplaced Items**
- Nesting occurs **separately per material**
- Output is **read-only** after nesting in v1

---

## 6. Functional Scope

### 6.1 Project Management

The application shall allow users to create, save, reopen, and export local nesting projects.

#### Project Metadata
Each project shall store:
- Project Name
- Project Number
- Customer Name
- Estimator
- Drafter
- PM
- Date
- Revision
- Notes

#### Project Capabilities
- Create new project
- Save project
- Save As
- Reopen existing project
- Export project/job data
- Track basic project version/revision fields manually

---

### 6.2 Material Library

The application shall include a reusable locally stored material library shared across projects.

#### Material Fields
Each material shall support:
- Material Name
- Color / Finish
- Notes
- Sheet Length
- Sheet Width
- Allow Rotation (true/false)
- Default Spacing Between Parts
- Default Edge Margin
- Cost Per Sheet

#### Material Rules
- Material name must be unique
- Material names are matched exactly during import
- One sheet size per material in v1
- Materials are reusable across projects
- Projects may reference saved material definitions

#### Material Actions
- Create material
- Edit material
- Delete material
- View materials
- Select materials into current project

---

### 6.3 Part Import

The application shall support importing part rows from:
- `.csv`
- `.xlsx`

#### Required Columns
Import files must contain exact column names:
- `Id`
- `Length`
- `Width`
- `Quantity`
- `Material`

#### Import Rules
- Exact column names are required
- Units are inches
- Numeric values must be decimal
- Each row is treated independently
- Duplicate IDs are allowed
- Duplicate IDs should generate a warning
- Quantity must be expanded logically for nesting calculations
- Material name must exactly match an existing material
- Rows with invalid data should be flagged

#### Validation Outcomes
The import process should categorize rows as:
- Valid
- Warning
- Error

#### Example Validation Cases

Warnings:
- Duplicate Id found
- Unusually small dimensions
- Quantity is very large

Errors:
- Missing required column
- Invalid numeric value
- Material not found
- Length <= 0
- Width <= 0
- Quantity <= 0

#### Post-Import Editing
Users shall be able to:
- Edit imported rows
- Delete imported rows
- Add rows manually
- Filter/sort by material
- Correct validation errors inline

---

### 6.4 Global / Project Nesting Settings

The application shall support the following global/project nesting settings:

#### Global Settings
- Kerf Width

#### Material-Specific Settings
- Part Spacing
- Edge Margin
- Rotation Allowed

#### Nesting Constraints
- No part splitting across sheets
- 90° rotation only
- Nesting must honor:
  - sheet boundaries,
  - per-material edge margin,
  - per-material part spacing,
  - global kerf width

---

### 6.5 Nesting Engine

The application shall provide a single default nesting strategy in v1 focused on **good practical nesting quickly**.

#### Engine Requirements
- Group parts by material
- Use only the stock sheet defined for that material
- Honor 90° rotation when material allows rotation
- Respect edge margin, spacing, and kerf rules
- Produce sheet-by-sheet placements
- Flag items that cannot be placed

#### Output Per Material
For each material, the engine shall produce:
- Total sheets required
- Total parts placed
- Total parts unplaced
- Total sheet area used
- Utilization summary
- Individual sheet layouts

#### Unplaced Items
An item shall be considered unplaced if:
- It cannot fit within the usable sheet area
- It cannot fit under rotation rules
- It is invalid for nesting due to data issues
- No remaining layout space exists in generated sheets under the algorithm

#### Performance Goal
The system should favor:
- predictable runtime,
- understandable output,
- practical material usage.

It does not need to guarantee mathematically optimal packing in v1.

---

### 6.6 Results and Visualization

The application shall present results in this order:
1. Material Summary
2. Sheet-by-Sheet Details
3. Unplaced Items

#### Material Summary
For each material, show:
- Material Name
- Sheet Size
- Total Input Part Count
- Total Sheets Required
- Total Placed Parts
- Total Unplaced Parts
- Overall Utilization %

#### Sheet Detail View
For each generated sheet, show:
- Sheet Number
- Material Name
- Sheet Dimensions
- Utilization %
- Part outlines
- Part labels
- Part dimensions
- Unused area visually apparent

#### Viewer Requirements
The result viewer shall be interactive and rendered in the WebView2 UI using a web-based rendering layer.

Planned viewer interactions:
- Zoom
- Pan
- Hover tooltip / metadata
- Click part to inspect details

#### Visualization Style
- Same fill style/color for all parts
- Labels only for differentiation
- Clear sheet outline
- Clear unused area
- Readable part IDs and dimensions

#### Viewer Technology
The application is expected to use **Three.js** for this portion of the interface, even though the nesting output itself is 2D. This is acceptable as long as the resulting viewer remains simple and performant.

---

### 6.7 PDF Reporting

The application shall allow users to export a PDF report summarizing the nesting results.

#### Editable Report Fields
Users shall be able to edit:
- Company Name
- Report Title
- Project / Job Name
- Project / Job Number
- Date
- Notes

#### PDF Default Sections
The report shall include:
- Header information
- Summary by material
- Sheet count by material
- Utilization summary
- Sheet visuals
- Unplaced / invalid items
- Notes section

#### Report Behavior
- Report content is based on current project data and latest nesting run
- Report fields are editable before export
- Report visuals should match the application view as closely as practical
- PDF output should be readable for printing and digital review

#### Exclusions
V1 does not include:
- saved report templates
- full WYSIWYG report designer
- cost breakdowns
- custom layout engine for arbitrary report sections

---

## 7. User Workflows

### 7.1 Primary Workflow
1. User creates or opens a project
2. User enters project metadata
3. User creates/selects materials from material library
4. User imports CSV/XLSX part file
5. Application validates import
6. User reviews and edits imported rows if needed
7. User confirms nesting settings
8. User runs nesting
9. Application displays:
   - material summary,
   - sheet-by-sheet layouts,
   - unplaced items
10. User edits report fields
11. User exports PDF report
12. User saves project

### 7.2 Material Setup Workflow
1. Open material library
2. Create or edit material
3. Save reusable material
4. Use material in current project

### 7.3 Import Correction Workflow
1. Import file
2. Review warnings/errors
3. Fix invalid rows in-app
4. Revalidate
5. Proceed to nesting

### 7.4 Reopen Workflow
1. Open saved project
2. Review prior imported items and materials
3. Rerun nesting if needed
4. Export updated report

---

## 8. Functional Requirements

### 8.1 Project Requirements
- System shall save project data locally
- System shall reopen saved project files
- System shall preserve project metadata, imported parts, selected materials, settings, and last known results

### 8.2 Material Requirements
- System shall persist reusable materials locally across projects
- System shall validate unique material names
- System shall allow create/edit/delete operations

### 8.3 Import Requirements
- System shall support CSV and XLSX import
- System shall require exact column headers
- System shall validate numeric and material data
- System shall warn on duplicate Id values
- System shall support inline editing after import

### 8.4 Nesting Requirements
- System shall nest per material only
- System shall use one stock sheet size per material
- System shall honor rotation, spacing, margin, and kerf rules
- System shall produce unplaced item list
- System shall prevent part splitting

### 8.5 Results Requirements
- System shall show material summary
- System shall show sheet detail views
- System shall show unplaced items
- System shall provide interactive zoom/pan/inspect behavior

### 8.6 Reporting Requirements
- System shall export PDF
- System shall include visuals and summaries
- System shall allow editing of report metadata fields before export

---

## 9. Data Model Overview

### 9.1 Material
- MaterialId
- Name
- ColorFinish
- Notes
- SheetLength
- SheetWidth
- AllowRotation
- DefaultSpacing
- DefaultEdgeMargin
- CostPerSheet

### 9.2 Project
- ProjectId
- ProjectName
- ProjectNumber
- CustomerName
- Estimator
- Drafter
- PM
- Date
- Revision
- Notes
- KerfWidth

### 9.3 PartRow
- RowId
- ImportedId
- Length
- Width
- Quantity
- MaterialName
- ValidationStatus
- ValidationMessages

### 9.4 ExpandedPartInstance
- InstanceId
- SourceRowId
- PartId
- Length
- Width
- MaterialName

### 9.5 NestSheet
- SheetId
- MaterialName
- SheetNumber
- SheetLength
- SheetWidth
- UsableArea
- UsedArea
- UtilizationPercent

### 9.6 NestPlacement
- PlacementId
- SheetId
- PartInstanceId
- X
- Y
- Width
- Height
- Rotated90

### 9.7 UnplacedItem
- UnplacedId
- PartInstanceId or SourceRowId
- ReasonCode
- ReasonDescription

### 9.8 ReportSettings
- CompanyName
- ReportTitle
- ProjectJobName
- ProjectJobNumber
- ReportDate
- Notes

---

## 10. UX / UI Requirements

### 10.1 General
- Entire primary UI hosted inside WebView2
- WPF acts as desktop shell and native host
- Modern clean internal-tool aesthetic
- Fast navigation between setup, import, results, and reporting

### 10.2 Suggested Main Navigation
- Projects
- Materials
- Import / Parts
- Nesting
- Results
- Report

### 10.3 Screen Expectations

#### Project Screen
- Create/open/save project
- Edit job metadata

#### Materials Screen
- View material list
- Add/edit/delete material
- Reuse material definitions

#### Import Screen
- Import file
- Show validation state
- Edit rows inline
- Filter by material/status

#### Nesting Screen
- Show nesting settings
- Run nesting action
- Show progress state

#### Results Screen
- Material summary cards/table
- Sheet browser
- Interactive visual viewer
- Unplaced items panel

#### Report Screen
- Edit report fields
- Preview included sections
- Export PDF

---

## 11. Error Handling

The application shall clearly report:
- missing import columns
- invalid numeric values
- missing materials
- oversized parts
- zero or negative dimensions
- empty projects
- nesting run failures
- PDF export failures
- corrupted or unsupported project files

Errors should be actionable and shown in a user-friendly way.

---

## 12. Persistence Requirements

### Local Persistence Only
The application shall operate fully locally.

### Data to Persist
- Material library
- Saved projects
- Report settings per project
- Last nesting results per project
- Exported PDFs
- Exported project/job data

### Suggested Storage Approach
- Local JSON or SQLite-backed project persistence
- Local JSON or SQLite-backed material library
- File-based export for PDF and project data

---

## 13. Reporting / Metrics

Useful output metrics for v1:
- total sheets by material
- placed part count
- unplaced part count
- overall utilization per material
- per-sheet utilization

Not included in v1:
- cost summaries
- historical analytics
- production dashboards

---

## 14. Success Criteria

V1 is successful if a user can:
- define materials once and reuse them,
- import a valid spreadsheet quickly,
- correct minor data problems in-app,
- run nesting in a predictable amount of time,
- clearly understand how many sheets are required,
- visually inspect layouts,
- identify unplaced items,
- export a usable PDF report,
- save and reopen the job later.

---

## 15. Technical Architecture Recommendations

Since you asked for architecture guidance too, this is the recommended direction.

### 15.1 Desktop Architecture
- **WPF** for native desktop host
- **WebView2** for full application UI
- **MVVM/service-oriented backend** in .NET
- JS/HTML frontend rendered inside WebView2
- Bidirectional communication between WPF and WebView2 via message bridge / host objects

### 15.2 Recommended Logical Layers
- **Host/Desktop Layer**
  - WPF shell
  - windowing
  - file dialogs
  - native integration
- **Application Layer**
  - project services
  - material services
  - import services
  - nesting orchestration
  - reporting services
- **Domain Layer**
  - materials
  - projects
  - parts
  - nesting results
  - validation rules
- **Infrastructure Layer**
  - local persistence
  - Excel/CSV readers
  - PDF generation
  - file export/import
- **Web UI Layer**
  - forms
  - tables
  - results dashboards
  - Three.js viewer

### 15.3 Suggested Technology Choices

#### Import
- CSV: `CsvHelper`
- XLSX: `ClosedXML` or `EPPlus`

#### Persistence
- SQLite for structured local data, or
- JSON project files plus local material library store

#### PDF
- `QuestPDF` is a strong fit for clean report generation

#### Viewer
- `Three.js` inside WebView2
- 2D orthographic-style presentation is likely sufficient even if implemented with Three.js

#### UI Framework in WebView
Possible good fits:
- plain TypeScript + component library
- React
- Vue

For an internal tool, React or Vue would both work well if you want a more app-like interface.

### 15.4 Nesting Engine Recommendation
For v1, implement a **fast heuristic rectangular nesting engine**, not an exact solver.

Good practical candidates:
- shelf/row-based heuristic
- guillotine-style heuristic
- best-area-fit / largest-first strategy

A very reasonable v1 path is:
- sort larger panels first
- attempt placement on existing sheets
- rotate when allowed
- open new sheets as needed
- track utilization and leftover regions

That will get you a strong internal tool much faster than chasing perfect optimization.

---

## 16. Risks

### Product Risks
- Users may expect near-perfect optimization
- Exact material name matching may cause import friction
- Large part quantities may stress runtime or viewer readability

### Technical Risks
- Three.js may be more than necessary for a strictly 2D sheet viewer
- PDF visual fidelity may differ from on-screen rendering
- WebView2/WPF communication complexity can grow if not structured cleanly
- XLSX import edge cases can produce confusing user errors without strong validation messaging

### Mitigation
- set expectations clearly in UX,
- make validation explicit,
- treat viewer/rendering as read-only,
- keep v1 algorithm simple and reliable,
- build clean service boundaries around nesting and reporting.

---

## 17. Future Enhancements

Potential v2+ items:
- multiple stock sheet sizes per material
- irregular polygon nesting
- grain direction
- part-level rotation overrides
- manual sheet editing
- drag/drop rearrangement
- cost rollups
- saved report templates
- cloud/shared projects
- optimization modes
- print labels / shop tickets
- remnant tracking
- sheet inventory support

---

## 18. Open Decisions Still Worth Clarifying

You’ve answered enough to write the PRD, but there are a few implementation-level choices still worth deciding before development starts:

- Should **kerf** reduce usable geometry directly, or should it be treated only as added spacing between cuts?
- Should **quantity** be expanded into individual instances immediately after import, or only during nesting execution?
- Should saved projects embed a snapshot of material definitions, or reference the current library version?
- Should the PDF include one sheet visual per page, or allow multiple smaller sheets per page?

### Recommended Defaults
- treat kerf as part of placement clearance,
- expand quantity during nesting preparation,
- snapshot materials into the project at time of use,
- allow multiple sheet visuals per page unless the sheet is dense.