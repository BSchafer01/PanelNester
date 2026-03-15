---
name: "per-user-msi-review-gate"
description: "How to gate a Windows MSI installer that must install without admin access"
domain: "testing"
confidence: "medium"
source: "hicks-per-user-msi-gate"
---

## Context

Use this when a desktop app adds an MSI installer and the slice specifically requires standard-user, non-admin installation.

## Pattern

1. Require one repo-root build command that produces the MSI artifact deterministically.
2. Verify installer metadata really declares per-user scope and limited privileges for the packaging tool in use.
3. Treat the installed payload as the product: confirm the installed directory contains the app exe, runtime files, bundled content, and supporting assemblies.
4. Run one install/launch/uninstall cycle from a non-elevated user context.
5. Re-run the existing app regression baseline so packaging work does not quietly break the app itself.
6. Check that mutable app data remains user-scoped and is not removed accidentally on uninstall.
7. After first launch, inspect the install root for app-created runtime caches (especially WebView2 `*.exe.WebView2` folders). If launch repopulates the install directory, the lifecycle is not clean enough to trust.

## Good signs

- No UAC prompt during install or uninstall
- Install location is under a user profile path rather than `Program Files`
- Installed app launches successfully from the installed path or shortcut
- Existing solution tests and Web UI build still pass
- First launch does not create runtime profile/cache data under the install folder

## Anti-patterns

- Trusting an MSI only because the file exists
- Building the installer from hand-staged binaries outside the repo workflow
- Verifying the build output directory but never launching the installed copy
- Allowing uninstall to remove user data without an explicit, documented choice
- Ignoring framework-default runtime caches that respawn inside the install folder after launch
