# OptiFab

OptiFab prepares rectangular parts from tabular sources for sheet-layout optimization.

## Workbook imports

**Import Source**:
The single external file from which a project imports parts. A project may also contain manually created parts.
_Avoid_: Source file, uploaded file

**Import Snapshot**:
The immutable contents captured from an Import Source for the active import session. The saved project retains its content fingerprint and derived data, not the source file's contents.
_Avoid_: Upload, cached file

**Import Session**:
The unfinalized Worksheet selection, configuration, validation state, and previews derived from one Import Snapshot. Cancelling it leaves the finalized project unchanged.
_Avoid_: Import wizard state, mapping session

**Workbook**:
An Excel import source containing one or more ordered Worksheets.
_Avoid_: Spreadsheet, Excel file

**Worksheet**:
A visible, nonempty grid within a Workbook that a user may select as a source of parts. Its saved identity consists of its name and original workbook position.
_Avoid_: Tab, sheet

**Heading Range**:
A contiguous set of cells within one Worksheet row whose values name the importable columns.
_Avoid_: Header row, headings row

**Table Region**:
The single tabular area imported from a Worksheet, beginning below its Heading Range and continuing through its last used row within the Heading Range's columns.
_Avoid_: Table, data range

**Optimization Group**:
An ordered, stably identified collection of imported and manually created parts that are optimized together and whose results remain isolated from every other Optimization Group. Its display name is unique and editable; parts imported from a Workbook belong through their Worksheet, and a selected Worksheet belongs to exactly one Optimization Group per import.
_Avoid_: Sheet group, combined import

**Part Group**:
An optional classification of parts within an Optimization Group that controls sequencing and domain-specific reporting or layout behavior without creating an optimization boundary.
_Avoid_: Group, Optimization Group

**Optimization Result**:
The material-specific panel layouts produced for one Optimization Group. Results follow the hierarchy Optimization Group, then Material, then Panels; a change invalidates only the results of groups whose inputs it affects.
_Avoid_: Workbook result, project result

**Column Mapping**:
The association between a Worksheet column and a property of an imported part. Column Mappings belong to an individual Worksheet even when Worksheets share an Optimization Group.
_Avoid_: Workbook mapping, group mapping

**Source Column**:
A Worksheet column identified by its column address and displayed with its Heading Range value. Its address distinguishes columns that have the same heading text.
_Avoid_: Header name, column name

**Material Resolution**:
The association between a material label found in a Workbook and an OptiFab material. A distinct label has one Material Resolution across the entire import.
_Avoid_: Worksheet material mapping

**Source Reference**:
The Worksheet and physical row from which an imported part originated, displayed as `Worksheet!Row`. A part combined from compatible rows retains every contributing Source Reference.
_Avoid_: Row ID, source ID

**Import Configuration**:
The saved selection of Worksheets, Heading Ranges, Column Mappings, Optimization Groups, and excluded source rows used to derive imported parts from a Workbook.
_Avoid_: Import settings, mapping session

**Excluded Source Row**:
A source data row that the user explicitly chose not to import after validation identified an error.
_Avoid_: Skipped row, ignored row

**Source Fingerprint**:
A saved representation of a source row's values used with its Source Reference to recognize the same row during re-import.
_Avoid_: Row hash, row identity

**Part Override**:
A user-authored change to an imported part that retains both its imported values and Source References.
_Avoid_: Source edit, workbook edit
