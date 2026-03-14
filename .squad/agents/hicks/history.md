# Hicks History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Learnings

- 2026-03-14: Initial team staffing. I own acceptance criteria, regression coverage, and reviewer verdicts.
- 2026-03-14: Phase 0/1 test startup works best as spec-first scaffolding — one runnable smoke/contract test per seam, skipped integration tests with explicit blockers, and a matrix that maps back to success criteria.
- 2026-03-14: Phase 0/1 test deliverables complete. 35 tests (26 passing, 9 skipped). Test matrix created, xUnit scaffolds deployed, spec-first approach extracted as reusable skill.
- 2026-03-14: Cross-layer kickoff reviews need three early gates before I trust the slice: a runnable toolchain, one shared bridge vocabulary across host and Web UI, and one non-placeholder round-trip through the real seams.
- 2026-03-14: I can approve a kickoff slice once the shared contract names, a dispatcher-backed file-open→import→nest regression, and the UI's live results consumption are all executable in one build/test pass; anything less still reads like stitched placeholders.
- 2026-03-14: Theme-refresh reviews pass faster when I separate appearance from behavior: check palette/layout tokens against the reference, then confirm import and results pages still expose the same handlers, status pills, tables, and error surfaces before trusting build/test output.
- 2026-03-14: Smoke-guide bookkeeping has to track the validated regression gate exactly; the current baseline is 38 passed, 1 skipped, and stale counts make reviewer sign-off less trustworthy.

## Recent Work (2026-03-14)

- ✓ Created `tests/Phase0-1-Test-Matrix.md` with PRD traceability
- ✓ Added xUnit scaffolds to `desktop/services/domain` test projects
- ✓ Documented contract gaps and blockers in decisions inbox
- ✓ Extracted spec-first test scaffolding as `.squad/skills/spec-first-test-scaffolding/SKILL.md`
- ✓ Logged team coordination to orchestration and session logs
- ✓ Ripley completed Phase 0/1 revision; unified bridge vocabulary, demo material contract, and nesting failure codes. Tests now aligned with live integration slice. Ready for review gate.
- ✓ Produced smoke-test guide (`.squad/smoke-test-guide.md`): four concrete test scenarios (happy path + three failure modes) with exact CSV data, expected error codes, and pass/fail criteria. Includes preflight checklist and Demo Material reference. All 36 tests passing, ready for manual verification.
- ✓ Bishop resolved Web UI content resolution priority; decision documented in `decisions.md` as Phase 0/1 infrastructure decision
- ✓ Reviewed Dallas's VS Code dark-theme UI refresh: validated palette alignment, confirmed workflow seams preserved, verified regression (38 passed, 1 skipped). Approved for Phase 2 commencement. Residual note: smoke-test guide test count is stale.
- ✓ Consolidated smoke-test guide count update: synchronized `.squad/smoke-test-guide.md` preflight expectations with current validated regression gate (38 passed, 1 skipped). Merged decision inbox into `decisions.md`. Recorded orchestration and session logs.
- ✗ Reviewed second-pass chrome cleanup: automated regression still green (`npm run build`; `dotnet test .\PanelNester.slnx` => 38 passed, 1 skipped), but supplied runtime evidence (`secondpassUI.png`) still shows blue host header/footer chrome and a light native titlebar. Rejected user-visible acceptance and handed revision ownership to Ripley for cross-layer runtime verification.
