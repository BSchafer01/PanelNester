# Session Log: Phase 3 Design Review

**Date:** 2026-03-14T17:41:39Z  
**Participants:** Brandon Schafer, Ripley, Team  
**Outcome:** Phase 3 narrowed to `.pnest` JSON project persistence with metadata editing and material snapshots

## Decision: Persistence Format
- **JSON file-based (`.pnest` extension)** — matches material library pattern, human-readable, no SQLite overhead
- **Schema version: 1** for forward compatibility

## Decision: Material Snapshot Behavior
- Projects snapshot materials at save time
- Opening project shows snapshotted materials even if library changed
- New imports validate against live library; existing project imports validate against snapshots
- Prevents silent corruption when library materials are renamed/deleted

## Decision: Scope Narrowing
- **In Phase 3:** Project CRUD, metadata editing, import/nesting state persistence, bridge extension
- **Deferred:** XLSX import, inline row editing, project export, revision history, multi-material

## Decision: Parallel Workstreams
- **Parker:** Domain models, `IProjectService`, `ProjectSerializer`
- **Bishop:** Desktop bridge contracts and handlers
- **Dallas:** WebUI project page and metadata form
- **Hicks:** Tests, integration review, smoke guide updates

## Next Steps
Phase 3 begins with Day 1 parallel workstreams on domain, bridge, and UI scaffolding.
