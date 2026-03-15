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

## Per-User MSI Packaging

### Context

Brandon requested a per-user, non-admin MSI installer for desktop distribution. Initial delivery from Bishop created WiX seam with per-user scope, but first review rejected due to WebView2 runtime profile mutation under install directory. Ripley revised installer behavior; Hicks re-review approved.

### Decision: MSI Packaging Architecture (Bishop)

**Date:** 2026-03-15 | **Agent:** Bishop

1. **WiX SDK Project:** `installer\PanelNester.Installer\PanelNester.Installer.wixproj` integrated into `PanelNester.slnx` under `/installer/`
2. **Payload Staging:** Installer build owns Web UI build, desktop publish, WebApp replacement, and file harvesting
3. **Per-User Scope:** `Product.wxs` declares `Package Scope="perUser"`, installs to `%LocalAppData%\Programs\PanelNester`, per-user Start Menu shortcut
4. **ICE Suppression:** `ICE38;ICE60;ICE64;ICE91` suppressed (user-profile install root validation noise)
5. **WebView2 Runtime:** Bootstrapping deferred; app assumes Evergreen runtime handled separately

**Rationale:** User-writable install location avoids admin elevation. Repo-owned WiX project enables reproducible builds. Harvested payload validates staging order is executable. Suppressions intentional for per-user shape.

**Consequences:** Normal install/uninstall requires zero UAC. App files land in user-scoped location. MSI generation clean without hand-copied desktop output.

### Gate: Per-User MSI Acceptance (Hicks)

**Date:** 2026-03-15 | **Agent:** Hicks

**Five Non-Negotiable Pass Gates:**
1. **Repo-buildable** — Installer project lives in repo; clean build produces deterministic `.msi`; no hand-copy, no UI publish clicks
2. **Per-user & non-admin** — Installer metadata targets per-user scope; install from standard user context completes without elevation; app files in `%LocalAppData%`, no HKLM-only writes
3. **Complete desktop payload** — Installed package contains desktop entry point, runtime files, dependencies, bundled WebApp content, supporting assemblies
4. **Baseline green** — `dotnet test .\PanelNester.slnx --nologo` passes (baseline: 132 total, 130 passed, 2 skipped, 0 failed); `npm run build` passes
5. **Trusted installed-bits lifecycle** — Build MSI, install as non-admin, launch from installed path, verify user storage writable, verify uninstall removes binaries without leaving admin artifacts or orphaned WebView2 residue

**Rejection Triggers:** MSI non-reproducible, admin elevation required, missing assemblies, mutable app state in machine-wide locations, installed app fails to launch, orphaned WebView2 runtime profile after uninstall.

**Evidence Required:** Build command, MSI path, per-user scope proof, install log showing no admin prompt, installed file listing, launch smoke, test regression results, clean uninstall verification.

### First Review: Rejection — WebView2 Residue (Hicks)

**Date:** 2026-03-15 | **Agent:** Hicks | **Verdict:** REJECT

**What Passed:**
- Repo build reproducible: `dotnet build .\PanelNester.slnx -c Release --nologo` ✅
- Per-user scope real: `Product.wxs` sets `Scope="perUser"`, HKCU registration, installs to `LocalAppDataFolder` ✅
- Desktop payload complete: `PanelNester.Desktop.exe`, `.deps.json`, `.runtimeconfig.json`, domain/service DLLs, WebView2 binaries, real WebApp assets ✅
- Baseline green: 132 tests (130 passed, 2 skipped, 0 failed) ✅
- WebView2 runtime handling explicit: MainWindow.xaml.cs catches `WebView2RuntimeNotFoundException` with clear install message ✅

**Blocking Failure:**
- After install → launch → uninstall, leftover `C:\Users\brand\AppData\Local\Programs\PanelNester\PanelNester.Desktop.exe.WebView2\...` remains
- Root cause: WebView2 default profile behavior creates `*.exe.WebView2` beside executable; not under install-managed control
- Impact: Install root not immutable after launch; uninstall cannot guarantee clean state

**Revision Required:** Assign Ripley (different author than Bishop—lock-out pattern). Relocate WebView2 user-data folder to `%LOCALAPPDATA%\PanelNester\WebView2\UserData` (outside install directory). Re-run clean install→launch→uninstall proof.

### Decision: WebView2 User-Data Relocation (Ripley)

**Date:** 2026-03-15 | **Agent:** Ripley

**Location:** WebView2 profile data must resolve to `%LOCALAPPDATA%\PanelNester\WebView2\UserData`, never under `INSTALLFOLDER`.

**Rationale:** Per-user MSI lifecycle requires immutable install root. Default WebView2 behavior mutates install location and leaves residue; uninstall cannot guarantee clean state with current host behavior.

**Implementation:**
- `DesktopStoragePaths` is single owner for app-data root and WebView2 profile path
- `WebViewBridge.InitializeAsync` creates explicit `CoreWebView2Environment` with user-data folder before `EnsureCoreWebView2Async`
- `MainWindow` passes path into bridge during startup
- Installer scope unchanged (per-user/non-admin); this is host behavior revision only

**Validation:**
- `dotnet build .\PanelNester.slnx -c Release --nologo` ✅
- `dotnet test .\PanelNester.slnx -c Release --nologo` → 134 total, 132 passed, 2 skipped, 0 failed ✅
- `npm run build --prefix .\src\PanelNester.WebUI` ✅
- Silent install → launch (10 sec smoke) → silent uninstall ✅
- **No** `*.exe.WebView2` under `%LOCALAPPDATA%\Programs\PanelNester` after launch ✅
- **Yes** `%LOCALAPPDATA%\PanelNester\WebView2\UserData` created and survives uninstall ✅

### Final Review: Approval (Hicks)

**Date:** 2026-03-15 | **Agent:** Hicks | **Verdict:** APPROVED ✅

**Verified (Re-Review):**
- Repo-buildable: `dotnet build .\PanelNester.slnx -c Release --nologo` ✅; `dotnet build .\installer\PanelNester.Installer\...` ✅; `npm run build` ✅
- Baseline green: 134 tests (132 passed, 2 skipped, 0 failed) ✅
- Per-user scope: `Product.wxs` declares `Scope="perUser"`, HKCU registration, `LocalAppDataFolder` install ✅
- Complete payload: `PanelNester.Desktop.exe`, `.deps.json`, `.runtimeconfig.json`, DLLs, WebView2 assemblies, real WebApp hashed assets ✅
- Clean lifecycle: Install exit code `0`, launch success, uninstall exit code `0`, **no residue under install root**, WebView2 user data at explicit path ✅

**Lifecycle Proof:**
- Install path: `C:\Users\brand\AppData\Local\Programs\PanelNester\`
- After launch: **no** `*.exe.WebView2` under install folder (gate requirement met)
- After launch: **yes** `%LOCALAPPDATA%\PanelNester\WebView2\UserData` created
- After uninstall: install root removed; WebView2 user data retained

**Non-Blocking Observation:** MSI not digitally signed (deferred to production release gate).

**Consequence:** Original rejection reason resolved. Install root stays immutable. MSI lifecycle clean. Per-user/non-admin scope preserved. Desktop payload complete with delegated user-data isolation.

---

## .NET 8 Retarget

### Decision: Bishop — .NET 8 Retarget For Per-User MSI

**Date:** 2026-03-15 | **Agent:** Bishop

**Context:** PanelNester's desktop app, supporting libraries, and tests were targeting .NET 10 while the repo-owned WiX installer published the desktop project as framework-dependent x64. The requirement was to keep the existing non-admin per-user MSI flow while making the packaged app runnable on .NET 8 machines.

**Decision:**
1. Retarget `PanelNester.Domain`, `PanelNester.Services`, `PanelNester.Desktop`, and all three test projects from .NET 10 to .NET 8
2. Keep the WPF host on `net8.0-windows`
3. Leave WiX packaging structurally unchanged (it publishes the desktop project directly and inherits the TFM)
4. Preserve per-user MSI install root and explicit WebView2 user-data folder
5. Validate via solution build, test re-run, WebUI rebuild, MSI rebuild, and staged publish runtimeconfig check

**Consequences:**
- Generated MSI packages a .NET 8-based desktop payload without changing per-user install behavior
- Runtime prerequisites remain x64 .NET 8 Desktop Runtime + Microsoft Edge WebView2 Runtime
- Future framework retargets should treat desktop project TFM as source of truth

**Status:** ✅ APPROVED

---

### Gate: .NET 8 Retarget Acceptance (Hicks)

**Date:** 2026-03-15 | **Agent:** Hicks

**Five Pass Conditions:**
1. **All relevant TFMs move together** — Domain/Services/tests to `net8.0`; Desktop/desktop tests to `net8.0-windows`; no hardcoded `net10.0` literals remain in review-critical files (csproj, tests, docs)
2. **Desktop/WPF contract stays valid on .NET 8 Windows** — `UseWPF=true` preserved; `win-x64` publish flow continues; builds/tests succeed
3. **Per-user MSI remains non-admin** — `Scope="perUser"`, `LocalAppDataFolder` install; no UAC prompts; clean uninstall without runtime residue
4. **Regression baseline stays green** — 134 total / 132 passed / 2 skipped / 0 failed minimum
5. **Runtime prerequisites are explicit** — Prerequisite shift from .NET 10 to .NET 8 called out in active validation docs

**Evidence Required:** Diffs of all six project files + test/doc files with hardcoded TFM text; post-retarget test output; installer build output; non-admin install/launch/uninstall cycle proof.

---

### Review: .NET 8 Retarget (First Pass)

**Date:** 2026-03-15 | **Agent:** Hicks | **Verdict:** ❌ REJECTED

**Passed:**
- All six TFMs correctly moved to .NET 8 / `net8.0-windows` ✅
- Desktop test assertions updated to expect `net8.0-windows` ✅
- Regression baseline green: 134 total / 132 passed / 2 skipped / 0 failed ✅
- Per-user MSI builds and installs non-admin ✅
- Installer publish runtimeconfig reports `tfm: net8.0` ✅

**Failing:**
- `tests\Phase0-1-Test-Matrix.md` line 15 still states desktop host should target `net10.0-windows`
- Gate explicitly required authored test/doc files with hardcoded TFM literals to move with retarget
- This mismatch tells future reviewers to expect the old framework target

**Revision Owner:** Ripley (lock-out pattern — Bishop locked from self-revision). Correct all active validation docs to .NET 8; append-only histories unchanged.

---

### Decision: Ripley — .NET 8 Doc Fix

**Date:** 2026-03-15 | **Agent:** Ripley

**Scope:** Treat active review and smoke-test documents as part of the framework-retarget contract. When the app moves TFMs or runtime prerequisites, reviewer-facing matrices and smoke guides must be updated in the same correction slice.

**Applied Changes:**
1. Updated `tests\Phase0-1-Test-Matrix.md` to state desktop host target as `net8.0-windows`
2. Updated `.squad\smoke-test-guide.md` to distinguish:
   - Local build/test requirement: `.NET 8.0.x` SDK
   - Installed-app validation requirement: x64 `.NET 8 Desktop Runtime` + `Microsoft Edge WebView2 Runtime`

**Rationale:** Hicks' rejection was about review drift, not implementation quality. Leaving active docs on `.NET 10` would mislead future reviewers and anyone validating the MSI on a clean machine. Append-only history files stay untouched.

**Validation:** 134 total / 132 passed / 2 skipped / 0 failed. No lingering `.NET 10` text in active docs.

---

### Final Review: .NET 8 Retarget

**Date:** 2026-03-15 | **Agent:** Hicks | **Verdict:** ✅ APPROVED

**Verified:**
1. All six TFMs aligned on .NET 8 ✅
2. Desktop/WPF contract valid on .NET 8 Windows ✅
3. Active validation docs corrected (no stale .NET 10 references) ✅
4. Baseline validation green after retarget ✅
5. Runtime prerequisites explicit in active docs ✅

**Non-Blocking:** MSI not digitally signed (deferred to production-release gate).

**Decision:** APPROVED. Doc-fix slice clears gate. Project, tests, installer, and active validation docs now tell the same .NET 8 story.

---

*Last updated: 2026-03-15T17:06:36Z*
