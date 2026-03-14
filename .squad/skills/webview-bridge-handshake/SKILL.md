---
name: "webview-bridge-handshake"
description: "Contract-first browser preview pattern for WebView2-hosted React apps"
domain: "frontend"
confidence: "high"
source: "phase-0-ui"
---

## Context

Use this when a React UI has to ship before the desktop host or native bridge is fully wired.

## Pattern

1. Define one shared `BridgeMessage` envelope and keep request payloads serializable.
2. Expose `window.hostBridge.receive(message)` early so the host can inject messages later without changing React code.
3. Let the browser-preview mode resolve to an explicit "host pending" handshake snapshot instead of throwing or inventing fake success states.
4. Keep page components read-only until real host capabilities are available; render bridge status and activity so integration work is observable.

## Why it helps

- Frontend and host work can proceed in parallel.
- Placeholder UI still compiles and stays honest about missing capabilities.
- The host team gets one narrow seam to target instead of scattered component callbacks.
