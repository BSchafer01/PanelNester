# Persist Import Configuration Without Workbook Snapshots

OptiFab will parse an immutable Import Snapshot during an active Import Session but will not embed the source `.xlsx` or `.xlsm` contents in the saved project. The project will instead retain the Import Configuration, derived parts, provenance, source metadata, and content fingerprints, balancing auditability and future re-import support against project-file growth and retention of sensitive workbook or macro content.

## Consequences

Changes to the external file cannot affect an active Import Session. A future re-import captures a new snapshot, compares fingerprints, and requires explicit Worksheet relinking when names no longer match; it cannot reproduce the original Workbook solely from the saved project.
