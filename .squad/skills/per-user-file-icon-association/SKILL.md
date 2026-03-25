---
name: "per-user-file-icon-association"
description: "Register a custom Explorer icon for a per-user file type without shipping a broken open command"
domain: "desktop-packaging"
confidence: "high"
source: "bishop-pnest-icon"
---

## Context

Use this when a per-user Windows installer needs a custom Explorer icon for an app-owned file extension, but startup file-open behavior is either missing or not yet trustworthy.

## Patterns

1. Keep the registration in `HKCU\Software\Classes` for per-user installers; do not write machine-wide `HKCR` state.
2. Create a dedicated ProgID (for example, `PanelNester.Project`) and point the extension's default value at that ProgID.
3. Reuse the shipped executable's embedded icon by setting `ProgID\DefaultIcon` to `"[INSTALLFOLDER]App.exe",0` instead of copying a second icon asset into the install.
4. Never write `FileExts\.{ext}\UserChoice`; Windows treats that as user-owned default app state.
5. If the host cannot yet open a passed-in file path on startup, omit `shell\open\command` entirely rather than registering a broken association command.
6. Once startup support exists, gate it with a strict resolver that accepts only fully qualified, existing `.{ext}` paths, then hand the path back into the existing UI/bridge open flow after the WebView host handshake completes.
7. Register the shell command with fully quoted executable and file placeholders (for example, `"[INSTALLFOLDER]App.exe" "%1"`), and keep it under the same per-user ProgID branch.
8. Add regression coverage for both sides of the seam: installer authoring should assert the command key and no forbidden `UserChoice`, while desktop tests should prove an explicit file path reuses the normal open-project route without falling back to a dialog.

## Examples

- `installer\PanelNester.Installer\Product.wxs`
- `tests\PanelNester.Desktop.Tests\ProjectConfiguration\InstallerFileAssociationSpecs.cs`
- `src\PanelNester.Desktop\StartupProjectPathResolver.cs`
- `src\PanelNester.Desktop\Bridge\WebViewBridge.cs`
- `src\PanelNester.WebUI\src\App.tsx`

## Anti-Patterns

- Writing `UserChoice` in WiX or any installer authoring
- Registering `shell\open\command` before the app can reliably consume startup file arguments
- Trusting relative, missing, or wrong-extension startup paths just because Explorer supplied them
- Shipping a separate icon file when the executable already embeds the canonical product icon
