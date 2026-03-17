---
updated_at: 2026-03-17T15:13:34Z
focus_area: grouped panel nesting
active_issues: []
---

# What We're Focused On

The current active slice is optional panel grouping during import/editing and grouped nesting behavior across the app. The flow now carries an optional `group` value from import and panel editing into nesting, keeps first-seen groups together, and only allows the last partially used sheet from one group to accept parts from the next group.

If no groups are supplied, the application should continue nesting exactly like before. Validation for this slice includes .NET solution tests and the WebUI production build so the cross-layer contract stays intact.
