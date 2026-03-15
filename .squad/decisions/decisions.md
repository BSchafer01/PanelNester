# Decisions

Records of project decisions and design choices.

## Phase 0/1 Test Strategy
- **Date:** 2026-03-14
- **Agent:** Hicks
- **Decision:** Deploy spec-first test scaffolding with one runnable smoke/contract test per seam
- **Rationale:** Ensures early acceptance coverage while blocking integration tests with explicit documented blockers
- **Impact:** Test matrix maps directly to PRD success criteria; enables iterative validation

## Phase 0/1 Design Review
- **Awaiting:** Ripley review output for cross-agent coordination

---

## Phase 6: Hardening & Smoke Verification

### 6.1 — Slice Definition (Ripley)
**Date:** 2026-03-15 | **Status:** Active

**Scope:** Bounded polish — viewer refinements, PDF fidelity edge cases, error-surface hardening, empty-result export workflows, dense-layout readability, and manual smoke validation.

**In Scope:**
1. Empty-result export hardening — PDF export with zero sheets/placements should not crash
2. Dense layout readability — 20+ panel sheets need minimum stroke/font treatment
3. Viewer edge cases — Reset-to-fit on sheet switch, zero-placement handling
4. Bridge error surface polish — User-facing messages, edge-case exceptions
5. Manual smoke test execution — Formal pass through smoke-test-guide.md

**Seam Ownership:**
- Parker: PDF export empty-result handling, dense-layout SVG refinements
- Dallas: Viewer edge cases, empty-result UI state, label overflow handling
- Bishop: Bridge error messaging, native dialog polish, reliability
- Hicks: Test coverage, smoke execution, integration gate

**Acceptance Criteria:**
- Empty-result export works (graceful, no crash)
- Dense layout readable (20+ panels, minimum visual treatment)
- Viewer state stable (reset-to-fit, zero-placement outline)
- Bridge errors user-friendly (non-technical userMessage)
- Smoke checklist complete (100% scenarios with evidence)
- No regression (112+ tests, 0 failures)

---

### 6.2 — Reviewer Gate (Hicks)
**Date:** 2026-03-15 | **Status:** Active

**Non-Negotiable Gates:**
1. **Build gate** — `dotnet test` and `npm run build` pass with zero failures
2. **Empty-result export gate** — Graceful outcome (disabled button, message, or valid "no results" report), never crash
3. **Dense-layout gate** — Readable with ≥20 placements; labels identify each panel; hover/click works
4. **Save/export stability gate** — Focus loss (Alt-Tab) during save dialog doesn't crash; cancel/fail leaves next attempt usable
5. **Pointer capture gate** — Viewer drag/zoom releases cleanly when pointer leaves bounds
6. **Precision gate** — No double-multiplied utilization, no visible floating-point drift on save/open round-trip
7. **Regression gate** — All Phase 5 test coverage remains green

**Evidence Required:** Screenshots/recordings of empty-result export, dense-layout labels, focus-loss recovery, pointer release, test summary output, build confirmation.

---

### 6.3 — Reporting Hardening (Parker)
**Date:** 2026-03-15 | **Status:** Active

**Decisions:**
1. `ReportDataService` treats report results as **renderable layouts** only when at least one sheet has at least one placement
2. `QuestPdfReportExporter` shows empty-state notice whenever no renderable layouts exist
3. Dense sheet diagrams keep **6pt minimum inline label floor**; overflow uses **numbered callout badges** with ordered placement summary as legend
4. Zero-placement sheets render explicit **"No placements"** state

**Rationale:** Empty-result exports should succeed and remain readable. Numbered callouts preserve deterministic ordering and dense-layout readability without widening contracts or changing successful Phase 5 output.

**Consequences:** Empty/zero-utilization reports now consistent "No results" state. Bridge payloads, persistence, FlatBuffers unchanged.

---

### 6.4 — Bridge Error Contract (Bishop)
**Date:** 2026-03-15 | **Status:** Active

**Decisions:**
1. Extend `BridgeError` with optional `userMessage` field
2. Preserve `BridgeError.message` for technical/detail text
3. Populate `userMessage` auto-populated for non-cancel failures (unsupported-message, invalid-payload, host-error)
4. Leave `userMessage` unset for `cancelled` responses (quiet, expected outcome)
5. Serialize native dialog entry in `NativeFileDialogService` to prevent rapid cancel/retry overlaps

**Rationale:** Keep failures user-friendly without losing actionable host diagnostics. Dialog cancel paths remain intentionally quiet.

**Consequences:** Desktop/UI contract has explicit place for stable, non-technical copy. Existing error-code behavior unchanged. Dialog retry more predictable.

---

### 6.5 — Viewer Empty-State & Fit Behavior (Dallas)
**Date:** 2026-03-15 | **Status:** Active

**Decisions:**
1. Treat **no sheets + no placements** as explicit empty-result state
2. Keep report/export surface visible during that state
3. Drive viewer reset-to-fit from active material/sheet selection token
4. Render zero-placement sheets with visible outline and in-view notice
5. Degrade dense-layout labels to truncation or compact callouts

**Rationale:** Clearer "nothing was placed" workflow. Viewer behavior more predictable on sheet switches. Dense layouts remain readable without expanding scope.

**Consequences:** Operators get clearer workflow without losing report context. Viewer behavior stays within approved 2D model. Dense layouts readable without new modes.

---

### 6.6 — Integration Review & Approval (Hicks)
**Date:** 2026-03-15 | **Status:** APPROVED

**Gate Results:**
- Empty-result export ✅ (ReportDataServiceSpecs, QuestPdfReportExporterSpecs green; PDF shows "No nesting results")
- Dense-layout readability ✅ (Build_sheet_svg_uses_minimum_strokes_and_callouts_for_dense_layouts confirms 24-panel sheet)
- Viewer reset-to-fit ✅ (resetViewToken dependency in useEffect triggers updateCameraLayout on change)
- Pointer release ✅ (window.addEventListener('pointerup') + cleanup ensures no stuck capture)
- Bridge userMessage ✅ (Phase06BridgeHardeningSpecs validates all failures return user-friendly copy)
- Cancel quiet ✅ (null userMessage for cancelled code verified)

**Test Summary:** 127 total (125 passed, 2 skipped, 0 failures) — net +15 new tests from baseline 112, no regressions.

**Residual Manual Smoke Items** (not blockers, production-release gate):
- Test Case 37: Focus-loss during native save dialog
- Test Case 38: Pointer capture release
- Test Case 39: Zoom limits
- Test Case 40: Precision after save/open

**Decision:** APPROVED. Phase 6 hardening meets all five review gates.

---

### 6.7 — FlatBuffers Migration Details (Parker)
**Date:** 2026-03-15 | **Status:** Active

**Serialization Strategy:**
- DateTime? values stored as DateTime.ToBinary() int64 with companion has_* bool; null writes 0 ticks
- FlatBuffers header: "PNST" + uint16 version (2) + uint16 flags (0); Project version remains Project.CurrentVersion
- Snapshot duplicate resolution: deterministically select last material ordered by name (for id duplicates) or materialId (for name duplicates)

**Rationale:** Append-only evolution enabled. Backward compatibility maintained with legacy JSON fallback.

---

### 6.8 — FlatBuffers Review (Hicks)
**Date:** 2026-03-15 | **Status:** APPROVED

**Evidence:** Verified 2026-03-15
- `dotnet test .\PanelNester.slnx` passed 112 total (110 passed, 2 skipped, 0 failed)
- Targeted suites: ProjectPersistenceSpecs (13/13), ProjectBridgeSpecs (2/2)
- Manual harness: PNST header verified, legacy JSON reopens correctly, duplicate-material saves pass, failed saves return proper code, temp files cleaned

**Residual Risk:** Project save-dialog cancellation/retry covered indirectly via bridge happy path and shared native dialog service behavior, not dedicated smoke.

**Decision:** APPROVED. FlatBuffers foundation solid.

---

## UI Cleanup (Post-Validation Polish)

### Context

Brandon requested five UI adjustments now that validation is stable. The current UI has redundant chrome layers (WPF header/footer duplicating WebUI header) and stale Phase 3 context on the Overview page.

### Decision

Approved seam split (Dallas WebUI 4 changes, Bishop native 3 changes) with contract definition for titlebar synchronization via WebView2's `DocumentTitleChanged` event.

**Ripley (Design):** Identified removals—WPF header/footer, Overview sections (Project File, Workflow), VS Code-style titlebar sync.

**Hicks (Gate):** Five non-negotiable gates: sidebar removal, nav indicator visibility (320px–1440px), File menu quiet behavior, titlebar dirty-indicator consistency, zero regression.

**Dallas (WebUI Implementation):**
- Remove `h1` from AppShell header
- Delete "Project file" and "Workflow" sections from OverviewPage
- Add VS Code-style File menu dropdown (New/Open/Save/Save As)
- Fix active nav indicator with `box-shadow: inset 2px 0 0` (no offset margin)
- Set `document.title` to `{projectName}{isDirty ? ' *' : ''} — PanelNester`

**Dallas (Follow-up):**
- Removed dedicated Saved snapshot + Next save panels below metadata form
- Kept saved snapshot count in summary card (outside metadata section)

**Bishop (Native Implementation):**
- Remove WPF header/footer rows from MainWindow.xaml
- Add `UpdateWindowTitle()` method in MainWindow.xaml.cs
- Listen to `CoreWebView2.DocumentTitleChanged` event
- Mirror document.title to custom titlebar TextBlock and Window.Title
- Fallback: `Untitled Project — PanelNester` if page title blank

### Contract Rationale

Initially proposed new `update-window-title` bridge message. Bishop's follow-up decision reversed this in favor of reading `document.title` from WebView2's `DocumentTitleChanged` event—smaller vocabulary, existing event infrastructure, single source of truth in Web UI.

### Validation Evidence (Hicks Review)

| Gate | Result | Evidence |
|------|--------|----------|
| 1 | Sidebar removed | AppShell has no .app-shell__sidebar CSS; grid is 48px minmax(0, 1fr) |
| 2 | Nav indicator visible 320–1440px | box-shadow inset approach, no margin offset, mobile breakpoint switches to bottom |
| 3 | File menu quiet | VS Code-style dropdown; no unexpected UI state changes |
| 4 | Titlebar dirty-indicator consistent | DocumentTitleChanged mirrors to titlebar; StatusPill reflects projectDirty |
| 5 | No regression | 132 tests (130 passed, 2 skipped, 0 failures); npm run build ✅ |

### Residual Smoke Items (Production Gate, Not Blockers)

- Resize to 320px and confirm nav abbreviations complete
- File menu keyboard nav (Tab/Arrow/Escape)
- Titlebar sync on rapid metadata edits
- Long project names in titlebar TextTrimming

### Consequences

- Chrome redundancy eliminated; single source of truth for app identity
- Overview page focused on actionable content (metadata, snapshots)
- Native titlebar provides VS Code-like project context pattern
- Bridge vocabulary unchanged (no new messages)
- Web shell owns project identity; host mirrors cleanly

**Status:** ✅ APPROVED | Date: 2026-03-15

---

*Last updated: 2026-03-15T02:40:13Z*
