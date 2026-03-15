---
name: "per-user-wix-msi-packaging"
description: "Package a WPF desktop app as a repo-buildable per-user MSI with WiX SDK"
domain: "desktop-packaging"
confidence: "high"
source: "bishop-per-user-msi"
---

## Context

Use this when a Windows desktop app needs a real `.msi` artifact, must install without admin elevation, and the repo does not already have an installer project.

## Patterns

1. Add a dedicated WiX SDK project to the repo and wire it into the solution so MSI generation is a first-class build path.
2. Keep packaging self-contained in the installer project: build any web assets, publish the desktop project to a staging folder, and harvest that staged payload instead of hand-copying binaries.
3. For WPF/WebView2 apps that ship placeholder web content in `WebApp`, overwrite the published `WebApp` folder with the real built `dist` output before packaging so installed bits do not fall back to placeholder UI.
4. Set the WiX package scope to `perUser` and install under a user-writable path such as `%LocalAppData%\Programs\{Product}`.
5. Add only user-scoped shell integration (for example, Start Menu shortcuts backed by `HKCU` key paths) so the installer stays non-admin.
6. Expect legacy MSI ICE validation noise when harvesting many files into a user-profile root; document and suppress the specific ICEs you intentionally accept rather than moving the install to a machine-wide folder.

## Examples

- `installer\PanelNester.Installer\PanelNester.Installer.wixproj`
- `installer\PanelNester.Installer\Product.wxs`
- `dotnet build .\installer\PanelNester.Installer\PanelNester.Installer.wixproj -c Release -nologo`

## Anti-Patterns

- Requiring a manual Visual Studio publish step before the MSI can be built
- Packaging the desktop publish output without replacing placeholder `WebApp` content with the real web build
- Using `Program Files` for a supposedly per-user MSI
- Treating WiX ICE suppression as a default instead of a documented, intentional tradeoff tied to per-user harvested payloads
