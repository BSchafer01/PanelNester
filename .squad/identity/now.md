---
updated_at: 2026-03-15T16:48:18Z
focus_area: .NET 8 retarget
active_issues: []
---

# What We're Focused On

The current active slice is a framework retarget from .NET 10 to .NET 8 across the app, tests, and installer flow. The team moved the relevant TFMs, kept the per-user MSI seam working, and updated active validation docs so review/smoke guidance matches the new runtime target.

This work preserves the existing non-admin installer behavior and WebView2 cleanup improvements while making the package viable on machines that do not have .NET 10 installed.
