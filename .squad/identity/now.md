---
updated_at: 2026-03-17T20:27:28Z
focus_area: import page performance
active_issues: []
---

# What We're Focused On

The current active slice is large-dataset responsiveness on the import/edit page after loading thousands of rows. The immediate goal is to keep navigation to and from the import tab responsive, avoid unnecessary full-row scans on related pages, and preserve the existing import/edit workflow while scaling better for stress cases like the 7,500-row workbook.

The grouped import/nesting/results work remains complete and validated. Validation for the performance slice continues to rely on the existing .NET regression suites plus the WebUI production build, with manual confirmation in the live UI for perceived responsiveness on very large imports.
