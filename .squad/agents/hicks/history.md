# Hicks History

## Project Context

- **Requested by:** Brandon Schafer
- **Project:** PanelNester
- **Stack:** C#/.NET desktop host, WPF shell, WebView2 UI, React + TypeScript web app, Three.js viewer, local JSON/SQLite persistence, CsvHelper/ClosedXML import, QuestPDF reporting
- **Description:** Local desktop tool for importing rectangular parts, nesting them by material, visualizing sheet layouts, and exporting PDF summaries.

## Core Context — Phases 0–6 Complete

I own acceptance criteria, regression coverage, and reviewer verdicts for the full product. Spec-first test scaffolding strategy (one runnable smoke/contract test per seam, skipped integration tests with explicit blockers) has been applied across all phases. Cross-layer review gates require shared contract names, a dispatcher-backed round-trip through real seams, and live results consumption.

**Phase Progression:**
- **Phase 0–1:** Test infrastructure and spec-first scaffolding (35 tests)
- **Phase 2:** Material library CRUD gates; 61 tests; bridge contracts validated
- **Phase 3:** Project persistence gates; 80 tests; snapshot-first validation
- **Phase 4:** Import pipeline gates (regression safety, format parity, edit persistence, failure clarity); 93 tests
- **Phase 5:** Results viewer & PDF reporting gates (rendering fidelity, PDF accuracy, multi-material determinism, export reliability); 110 tests (after Ripley revision + follow-up + bugfix batch)
- **Phase 6:** Hardening & smoke verification (empty-result export, dense-layout readability, viewer edge cases, bridge error surfaces); 127 tests (125 passed, 2 skipped)
- **Recent Batches:** FlatBuffers migration, UI cleanup, Import page cleanup, Material/Results page cleanup, Maximize clipping fix, Per-user MSI packaging

**Key Learnings:**
- Spec-first scaffolding works with one runnable smoke/contract test per seam and explicit blockers for skipped tests
- Theme reviews benefit from separating appearance validation from behavior validation
- Maximize-state fixes need paired-state validation (restored vs maximized return trip); safest approach is host-side inset with restored mode reset to zero
- Per-user MSI trust requires repo-root reproducibility, per-user scope, complete payload, non-admin install/launch/uninstall cycle, and proof mutable state stays in LocalAppData
- WebView2 desktop hosts need explicit user-data folder outside install root; default `*.exe.WebView2` behavior breaks per-user MSI uninstall cleanliness
- Debug/validation UI duplicating primary state clutters workspace; better removed or surfaced as transient notifications
- Hardening slices need explicit evidence artifacts (screenshots/recordings), not verbal claims
- Bridge error code-to-message mapping better than per-handler duplication; centralize in `BridgeError.Create`
- Dense-layout callout strategy (numbered badges + legend) avoids contract widening while keeping panels identifiable
- Viewer `resetViewToken` cleanly separates "same sheet, resize" from "different sheet, re-center"
- Dialog serialization via SemaphoreSlim prevents rapid cancel/retry race conditions
- Bridge proposal reversal: replaced proposed `update-window-title` message with WebView2's native `DocumentTitleChanged` event (smaller vocabulary, existing infrastructure, single source of truth)

## Recent Work (2026-03-15)

- ✓ **FLATBUFFERS MIGRATION COMPLETE** (2026-03-15T00:52:22Z) — Parker delivered FlatBuffers schema, dual-read JSON/binary persistence, crash fix. Test results: 110/112 passing, 2 skipped. Zero regressions.

- ✓ **MATERIAL/RESULTS UI CLEANUP APPROVED** (2026-03-15T15:22:42Z) — Dallas removed validation-heavy controls, implemented two-column split on Results page with resizable boundary (360px min workspace, 420px min viewer). All gates satisfied.

- ✓ **UI CLEANUP BATCH APPROVED** (2026-03-15T02:40:13Z) — Sidebar removal, nav indicator 320–1440px visible, File menu quiet, titlebar sync via WebView2 `DocumentTitleChanged` event. All gates cleared: 132 tests (130 passed, 2 skipped, 0 failures).

- ✓ **MAXIMIZE CLIPPING FIX APPROVED** (2026-03-15T15:49:17Z) — Bishop maximize-only WebView inset using `SystemParameters.WindowResizeBorderThickness`. All four gates met: nav accent visibility, no clipping, restored baseline, titlebar/resize intact. Zero regressions.

- ✓ **PER-USER MSI PACKAGING & RE-REVIEW APPROVED** (2026-03-15T16:39:48Z) — Gate authored: five non-negotiable pass conditions. Bishop delivered first MSI with WiX, per-user scope, complete payload. **First review: REJECTED** (WebView2 residue under install). Ripley revised: relocated WebView2 user-data to `%LOCALAPPDATA%\PanelNester\WebView2\UserData`. **Final re-review: APPROVED ✅** — Clean install→launch→uninstall; no residue; install root immutable. 134 tests (132 passed, 2 skipped, 0 failed).

- ✓ **SINGLE-FILE MSI EMBEDDING APPROVED** (2026-03-15T17:24:18Z) — Bishop changed WiX media authoring to `<MediaTemplate EmbedCab="yes" />` to embed cabinet inside MSI, eliminating external `cab1.cab` dependency. Per-user scope, .NET 8 pipeline, WebView2 lifecycle unchanged. Release output: `PanelNester-PerUser.msi` only (no external CAB). All validation green: installer build, solution tests (134/132), WebUI build. Hicks review gate passed all four must-pass checks. Artifact ready for distribution.

### Per-User MSI Cycle Summary (Full Lifecycle)

**Agents:** Bishop (delivery), Hicks (gate + reviews), Ripley (revision)

**Phase 1 — Initial MSI Delivery (2026-03-15T16:39:48Z)**

Gate Definition (Hicks): Five non-negotiable pass conditions — repo-buildable, per-user/non-admin scope, complete desktop payload, baseline regression safe, clean install→launch→uninstall lifecycle with no orphaned runtime profiles.

First Review (Hicks): REJECTED — WebView2 default profile behavior created `*.exe.WebView2` under install folder on first launch, left as orphaned residue after uninstall. Root cause: no explicit user-data folder configuration. Revision assigned to Ripley.

Revision (Ripley): COMPLETE — Made WebView2 profile location explicit contract at `%LOCALAPPDATA%\PanelNester\WebView2\UserData` via `CoreWebView2Environment.CreateAsync(userDataFolder: ...)`. Installer scope unchanged (per-user/non-admin); revision is host behavior only. Validation: 134 tests (132 passed, 2 skipped, 0 failed); clean lifecycle proven (no `*.exe.WebView2` under install root, explicit path created and survives uninstall).

Re-Review (Hicks): APPROVED ✅ — All five gates re-verified: repo-buildable, per-user scope, complete payload, baseline green, clean lifecycle. Non-blocking observation: MSI not digitally signed (deferred to production-release gate).

**Phase 2 — Single-File MSI Embedding (2026-03-15T17:24:18Z)**

Gate Definition (Hicks): Four must-pass checks — per-user/non-admin contract, single-file Release output (no external CAB), payload completeness, existing validation green.

Decision (Bishop): Change WiX media template from external CAB to embedded: `<MediaTemplate EmbedCab="yes" />`. Preserves all existing contracts (per-user scope, .NET 8 publish pipeline, WebView2 user-data location). Enables single-file distribution.

Verification:
- **Per-user / non-admin:** `Scope="perUser"` unchanged, installs under `%LOCALAPPDATA%\Programs\PanelNester`
- **Single-file output:** Release build produces `PanelNester-PerUser.msi` only; no sibling external `.cab`
- **Embedded cabinet:** MSI `Media.Cabinet` resolves to `#cab1.cab` (embedded)
- **Payload complete:** Desktop exe, runtime files, WebView2 dependencies, web assets present
- **Validation:** `dotnet build .\installer\...wixproj`, `dotnet test .\PanelNester.slnx`, `npm run build` all green
- **Lifecycle clean:** Install/launch/uninstall cycle clean; WebView2 user data in correct location

Final Review (Hicks): APPROVED ✅ — All four gates satisfied. Artifact ready for distribution.

**Key Learning:** Lock-out pattern (rejecting agent cannot revise own work) forces fresh perspective. Ripley's revision narrowly focused on WebView2 bootstrap, not installer plumbing, reducing regression risk.

---

*Old gate definitions and detailed review checklists archived to `hicks\history-archive.md`.*

## Recent Work (2026-03-15T17:06:36Z)

- ✓ **NET 8 RETARGET GATE & REVIEWS COMPLETE.** Authored five-condition acceptance gate for .NET 8 downgrade: all six TFMs move together, Desktop/WPF contract stays valid, per-user MSI remains non-admin, baseline regression green, runtime prerequisites explicit. First review rejected on stale validation docs (`tests\Phase0-1-Test-Matrix.md` and `.squad\smoke-test-guide.md` still asserted .NET 10 when executable checks had moved to .NET 8). Locked Bishop from self-revision (pattern enforcement). Final re-review approved after Ripley corrected active validation docs to .NET 8. **APPROVED 2026-03-15T17:06:36Z**. Decisions merged; inbox files deleted; agent histories updated.

## Learnings

- .NET retarget review is not just six csproj edits: hardcoded TFM assertions, `bin\Debug\netX...` path literals, and framework-dependent installer runtime prerequisites must move in the same slice or the review will miss user-visible breakage.
- Acceptance matrices that restate target frameworks are review-critical artifacts, not commentary; if executable checks move to the new TFM but the matrix still tells reviewers to expect the old one, the retarget stays incomplete.
- Framework retarget re-review should separate three checks: executable targeting (`TargetFramework`/WPF/runtimeconfig), active reviewer docs, and user-facing prerequisite callouts. Approval is earned only when all three agree on the same runtime story.
- Single-file MSI review needs two separate proofs: the Release output shape must collapse to one `.msi` with no external `.cab`, and the installed payload must still be complete enough to launch. Either half missing means the packaging story is still untrusted.
- Single-file MSI approval is stronger when the embedded-cab story is proven three ways at once: WiX metadata requests embedding, the built Release folder has no sibling `.cab`, and a real install/launch/uninstall cycle still preserves a complete payload and clean install root.
