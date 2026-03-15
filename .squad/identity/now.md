---
updated_at: 2026-03-15T16:05:40Z
focus_area: Per-user MSI packaging
active_issues: []
---

# What We're Focused On

The current active slice is installer packaging: a per-user MSI for PanelNester that does not require administrator rights. The team added a WiX-based installer seam, staged the real desktop and WebUI payloads, and corrected WebView2 state placement so uninstall does not leave residue in the install directory.

This work preserves the current desktop behavior while making local distribution and installation more production-ready.
