# OptiFab

OptiFab prepares parts from tabular sources for either sheet-layout or stock-length optimization. Each project chooses exactly one of those optimization kinds.

## Projects

**Project Kind**:
The persistent classification that determines whether a project manages two-dimensional sheet layouts or one-dimensional stock lengths. It may be changed only while the project contains no sheet parts or Required Pieces.
_Avoid_: Project mode, optimization mode

**Sheet Project**:
A project that optimizes rectangular parts on two-dimensional sheet material.
_Avoid_: 2D project, nesting project

**Stock-Length Project**:
A project that arranges Required Piece lengths cut from one-dimensional stock. It cannot contain sheet parts.
_Avoid_: 1D project, linear project

## Workbook imports

**Import Source**:
The single external file from which a project imports sheet parts or Required Pieces. A project may also contain manually created entries of its Project Kind.
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
A visible, nonempty grid within a Workbook that a user may select as a source of sheet parts or Required Pieces. Its saved identity consists of its name and original workbook position.
_Avoid_: Tab, sheet

**Heading Range**:
A contiguous set of cells within one Worksheet row whose values name the importable columns.
_Avoid_: Header row, headings row

**Table Region**:
The single tabular area imported from a Worksheet, beginning below its Heading Range and continuing through its last used row within the Heading Range's columns.
_Avoid_: Table, data range

**Optimization Group**:
An ordered, stably identified collection of imported and manually created sheet parts or Required Pieces that are processed together and whose results remain isolated from every other Optimization Group. Its display name is unique and editable; entries imported from a Workbook belong through their Worksheet, and a selected Worksheet belongs to exactly one Optimization Group per import.
_Avoid_: Sheet group, combined import

**Part Group**:
An optional classification of Sheet Project parts within an Optimization Group that controls sequencing and domain-specific reporting or layout behavior without creating an optimization boundary. It does not apply to Stock-Length Projects.
_Avoid_: Group, Optimization Group

**Optimization Result**:
The saved output produced for one Optimization Group. A Sheet Project result contains material-specific panel layouts, while a Stock-Length Project result contains one Cut Plan per Stock Group; a change invalidates only the results of groups whose optimization inputs it affects.
_Avoid_: Workbook result, project result

**Column Mapping**:
The association between a Worksheet column and a property of an imported sheet part or Required Piece. Column Mappings belong to an individual Worksheet even when Worksheets share an Optimization Group.
_Avoid_: Workbook mapping, group mapping

**Source Column**:
A Worksheet column identified by its column address and displayed with its Heading Range value. Its address distinguishes columns that have the same heading text.
_Avoid_: Header name, column name

**Material Resolution**:
The association between a material label found in a Sheet Project Workbook and an OptiFab material. A distinct label has one Material Resolution across the entire import; Stock-Length Projects do not resolve materials.
_Avoid_: Worksheet material mapping

**Source Reference**:
The Worksheet and physical row from which an imported sheet part or Required Piece originated, displayed as `Worksheet!Row`. An entry combined from compatible rows retains every contributing Source Reference.
_Avoid_: Row ID, source ID

**Import Configuration**:
The saved selection of Worksheets, Heading Ranges, Column Mappings, Optimization Groups, and excluded source rows used to derive imported sheet parts or Required Pieces from a Workbook.
_Avoid_: Import settings, mapping session

**Grouping Field**:
A mapped text property whose normalized values create Optimization Groups for imported Required Pieces across selected Worksheets. Blank values belong to one explicitly named unspecified group.
_Avoid_: Group column, Worksheet group

**Excluded Source Row**:
A source data row that the user explicitly chose not to import after validation identified an error.
_Avoid_: Skipped row, ignored row

**Source Fingerprint**:
A saved representation of a source row's values used with its Source Reference to recognize the same row during re-import.
_Avoid_: Row hash, row identity

**Part Override**:
A user-authored change to an imported sheet part or Required Piece that retains both its imported values and Source References.
_Avoid_: Source edit, workbook edit

## Stock-length optimization

**Required Piece**:
A quantity of identical lengths that must be cut from stock and that carries a Profile Number, Finish, Part Number, and Part Name. It belongs to exactly one Optimization Group; only its quantity, length, and Profile Number are required to have values.
_Avoid_: Part row, stock part

**Piece Instance**:
One physical cut represented by a Required Piece's quantity. It retains the Required Piece's metadata and Source References and has a deterministic ordinal within that quantity.
_Avoid_: Imported row, engine placement

**Stock Length**:
The fixed usable length of every Stock Item within one Optimization Group. All Worksheets assigned to that Optimization Group use the same Stock Length.
_Avoid_: Sheet length, bar size

**Oversized Stock Length**:
An optional length greater than an Optimization Group's Stock Length that may receive overlong Piece Instances after its Cut Plan is generated. It does not participate in optimization.
_Avoid_: Alternate Stock Length, second optimization length

**Stock Item**:
One consumed piece of stock in an Optimization Result. Its human-readable number is regenerated deterministically within its Stock Group whenever the result is recomputed and is not an inventory identity.
_Avoid_: Sheet, inventory bar

**Oversized Stock Item**:
A Stock Item created after Cut Plan generation for exactly one formerly unplaced Piece Instance that fits the Oversized Stock Length.
_Avoid_: Optimized oversized bar, alternate Stock Item

**Cut Plan**:
The generated arrangement of Piece Instances across ordered Stock Items for one Stock Group. It is a deterministic heuristic result and does not claim to minimize the number of Stock Items.
_Avoid_: Optimal solution, stock optimization

**Cut Plan Status**:
The completeness of a generated Cut Plan: Complete when every Required Piece is placed, Partial when some are placed and some are unplaced, or Failed when none are placed.
_Avoid_: Engine success, freshness

**Needs Generation**:
The state of an Optimization Group that has no current Optimization Result, either because none has been generated or a geometry or compatibility input invalidated its previous result.
_Avoid_: Failed, stale result

**Empty Optimization Group**:
An Optimization Group that contains no Required Pieces and therefore cannot generate an Optimization Result.
_Avoid_: Needs Generation, Failed

**Cut Sequence**:
The recommended ordering of Piece Instances from the origin of one Stock Item outward. It is not a machine-ready cutting program.
_Avoid_: Saw program, machine instructions

**Saw Loss**:
The Stock Length consumed by kerfs between adjacent Piece Instances in one Stock Item. A Stock Item containing `n` Piece Instances has `n - 1` kerfs and includes no end-trim allowance.
_Avoid_: Reusable waste, remainder

**Remainder**:
The Stock Length left after subtracting Required Piece lengths and Saw Loss from one Stock Item. Values within the optimizer's fit tolerance of zero are treated as zero.
_Avoid_: Saw loss, scrap area

**Profile Number**:
Imported text that identifies the physical profile required by a piece without referring to an OptiFab Extrusion record.
_Avoid_: Die number, extrusion number

**Stock Group**:
The collection of Required Pieces within an Optimization Group whose trimmed, case-insensitive Profile Number and Finish match and that therefore may be cut from the same Stock Items. Pieces from different Stock Groups may not share stock; blank Finishes match one another.
_Avoid_: Material, Optimization Group
