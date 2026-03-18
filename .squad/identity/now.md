---
updated_at: 2026-03-18T16:49:33Z
focus_area: material library location management
active_issues: []
---

# What We're Focused On

The current active slice is making the reusable material library relocatable. The immediate goal is to let the user repoint the library to a different file location, persist that selection across app restarts, and provide a restore-default action that safely recreates the default `materials.json` file when it has been deleted or moved.

The grouped import/nesting/results work and recent import-page UI polish remain complete. Validation for this slice should continue to rely on the existing .NET regression suites plus the WebUI production build, with manual confirmation in the live UI for file-picker behavior, persisted location recovery, and restore-default recovery.
