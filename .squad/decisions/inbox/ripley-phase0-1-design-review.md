# Design Review: Phase 0 + Phase 1 Kickoff

**Author:** Ripley  
**Date:** 2026-03-14  
**Status:** Active  
**Scope:** Foundation scaffold + vertical slice handoff

---

## 1. Solution/Project Structure

Current repo has a bare WPF project. Recommended structure for Phase 0+1:

```
PanelNester/
├── PanelNester.slnx
├── src/
│   ├── PanelNester.Desktop/           # WPF shell + WebView2 host
│   │   ├── PanelNester.Desktop.csproj
│   │   ├── App.xaml(.cs)
│   │   ├── MainWindow.xaml(.cs)
│   │   └── Bridge/
│   │       └── WebViewBridge.cs       # Message dispatch to/from JS
│   │
│   ├── PanelNester.Domain/            # Pure domain models & interfaces
│   │   ├── PanelNester.Domain.csproj
│   │   ├── Models/
│   │   │   ├── Material.cs
│   │   │   ├── PartRow.cs
│   │   │   ├── ExpandedPart.cs
│   │   │   ├── NestSheet.cs
│   │   │   ├── NestPlacement.cs
│   │   │   └── NestResult.cs
│   │   └── Contracts/
│   │       ├── INestingService.cs
│   │       └── IImportService.cs
│   │
│   ├── PanelNester.Services/          # Application services
│   │   ├── PanelNester.Services.csproj
│   │   ├── Import/
│   │   │   └── CsvImportService.cs
│   │   └── Nesting/
│   │       └── ShelfNestingService.cs
│   │
│   └── PanelNester.WebUI/             # React + TypeScript frontend
│       ├── package.json
│       ├── tsconfig.json
│       ├── vite.config.ts
│       ├── src/
│       │   ├── main.tsx
│       │   ├── App.tsx
│       │   ├── bridge/
│       │   │   └── hostBridge.ts      # JS↔.NET message layer
│       │   ├── components/
│       │   ├── pages/
│       │   └── types/
│       │       └── contracts.ts       # Shared DTO types
│       └── dist/                      # Build output (embedded in WPF)
│
├── tests/
│   ├── PanelNester.Domain.Tests/
│   └── PanelNester.Services.Tests/
│
└── docs/
    └── architecture.md
```

**Key points:**
- Move existing WPF code to `src/PanelNester.Desktop/`
- Domain is its own project—no WPF/WebView dependencies
- Services depend on Domain only
- WebUI is a separate npm project; dist output embedded in Desktop at build

---

## 2. Seam Definitions

### 2.1 WPF Shell ↔ WebView2 Bridge

**Direction:** Bidirectional JSON-over-postMessage

**WPF → JS:**
```csharp
// WebViewBridge.cs
public async Task SendToWebAsync(string messageType, object payload)
{
    var json = JsonSerializer.Serialize(new { type = messageType, payload });
    await webView.ExecuteScriptAsync($"window.hostBridge.receive({json})");
}
```

**JS → WPF:**
```typescript
// hostBridge.ts
export function sendToHost(type: string, payload: unknown): void {
  window.chrome.webview.postMessage({ type, payload });
}
```

**WPF receives:**
```csharp
webView.WebMessageReceived += (s, e) => {
    var msg = JsonSerializer.Deserialize<BridgeMessage>(e.WebMessageAsJson);
    dispatcher.Dispatch(msg.Type, msg.Payload);
};
```

### 2.2 WebView2 Bridge ↔ Domain Services

Bridge calls domain services directly via DI-injected handlers:

```csharp
// BridgeDispatcher.cs
handlers.Register("import-csv", async (payload) => {
    var request = Deserialize<ImportRequest>(payload);
    var result = await importService.ImportAsync(request.FilePath);
    return new ImportResponse(result);
});

handlers.Register("run-nesting", async (payload) => {
    var request = Deserialize<NestRequest>(payload);
    var result = await nestingService.NestAsync(request.Parts, request.Materials);
    return new NestResponse(result);
});
```

### 2.3 Web UI ↔ Three.js Viewer

React owns the viewer component. Data flows down as props:

```typescript
// ResultsPage.tsx
<SheetViewer 
  sheets={nestResult.sheets} 
  placements={nestResult.placements}
  onPartClick={handlePartClick}
/>
```

Viewer is read-only in Phase 1—no drag/drop.

---

## 3. First-Batch Implementation Split

| Agent   | Phase 0 Work                              | Phase 1 Work                                    |
|---------|-------------------------------------------|-------------------------------------------------|
| **Bishop** | WPF→WebView2 host setup, WebViewBridge.cs, verify round-trip ping/pong | Expand bridge handlers for import/nest requests |
| **Dallas** | React scaffold, hostBridge.ts, layout shell, navigation stub | Import UI (file picker trigger, validation display), Results summary cards |
| **Parker** | Domain project structure, Material/PartRow/NestSheet models | CsvImportService, ShelfNestingService (basic heuristic) |
| **Hicks**  | WebView2 init test, bridge round-trip test | CSV import edge cases, nesting boundary tests |

**Parallel execution notes:**
- Bishop and Dallas can work in parallel—Bishop on .NET side, Dallas on JS side of the bridge.
- Parker can work on domain/services independently.
- Hicks shadows all three, writing tests as interfaces stabilize.

---

## 4. Key Contracts / DTOs (Must Stay Stable)

### 4.1 Bridge Message Envelope

```typescript
// contracts.ts
interface BridgeMessage {
  type: string;
  requestId?: string;  // For request/response correlation
  payload: unknown;
}
```

### 4.2 Import Contracts

```typescript
interface ImportRequest {
  filePath: string;
}

interface ImportResponse {
  success: boolean;
  parts: PartRow[];
  errors: ValidationError[];
  warnings: ValidationWarning[];
}

interface PartRow {
  rowId: string;
  importedId: string;
  length: number;
  width: number;
  quantity: number;
  materialName: string;
  validationStatus: 'valid' | 'warning' | 'error';
  validationMessages: string[];
}
```

### 4.3 Nesting Contracts

```typescript
interface NestRequest {
  parts: PartRow[];
  material: Material;  // Single material for Phase 1
  kerfWidth: number;
}

interface NestResponse {
  success: boolean;
  sheets: NestSheet[];
  placements: NestPlacement[];
  unplacedItems: UnplacedItem[];
  summary: MaterialSummary;
}

interface NestSheet {
  sheetId: string;
  sheetNumber: number;
  materialName: string;
  sheetLength: number;
  sheetWidth: number;
  utilizationPercent: number;
}

interface NestPlacement {
  placementId: string;
  sheetId: string;
  partId: string;
  x: number;
  y: number;
  width: number;
  height: number;
  rotated90: boolean;
}

interface UnplacedItem {
  partId: string;
  reasonCode: string;
  reasonDescription: string;
}

interface MaterialSummary {
  totalSheets: number;
  totalPlaced: number;
  totalUnplaced: number;
  overallUtilization: number;
}
```

### 4.4 Material (Hardcoded for Phase 1)

```typescript
interface Material {
  materialId: string;
  name: string;
  sheetLength: number;
  sheetWidth: number;
  allowRotation: boolean;
  defaultSpacing: number;
  defaultEdgeMargin: number;
}
```

---

## 5. Risks & Edge Cases for Hicks

### Immediate Test Targets

1. **WebView2 Initialization**
   - Test: WebView2 loads without error on clean Windows machine
   - Risk: Missing WebView2 runtime; need graceful error message

2. **Bridge Round-Trip**
   - Test: JS calls .NET, receives response, handles timeout
   - Risk: Message deserialization failures, async deadlocks

3. **CSV Import Edge Cases**
   - Empty file
   - Missing required columns
   - Wrong column order
   - Non-numeric values in numeric fields
   - Negative/zero dimensions
   - Empty material name
   - Extremely large quantities (>10,000)
   - Unicode in part IDs

4. **Nesting Boundary Cases**
   - Part larger than sheet
   - Part exactly sheet size (with margins → won't fit)
   - All parts identical (stress utilization calc)
   - Empty parts list
   - Single part
   - Rotation makes the difference between fit/unfit

5. **Numeric Precision**
   - Test: Floating point comparisons for "fits on sheet" logic
   - Risk: 0.0001" rounding errors causing false negatives

### Test Infrastructure Needed

```
tests/
├── PanelNester.Domain.Tests/
│   └── Models/
│       └── PartRowTests.cs
├── PanelNester.Services.Tests/
│   ├── Import/
│   │   └── CsvImportServiceTests.cs
│   └── Nesting/
│       └── ShelfNestingServiceTests.cs
└── PanelNester.Desktop.Tests/
    └── Bridge/
        └── BridgeDispatcherTests.cs
```

---

## 6. Architecture Decisions

### Decision 6.1: Message Bridge Pattern

**Choice:** JSON-over-postMessage with type/payload envelope.

**Rationale:** 
- WebView2's `postMessage` is the blessed path
- JSON keeps both sides language-agnostic
- Envelope pattern allows routing without deserializing payload first

**Consequences:**
- No binary payloads (acceptable for v1 data sizes)
- Must handle serialization errors gracefully

### Decision 6.2: Domain Project Isolation

**Choice:** Domain has zero dependencies on WPF, WebView2, or persistence.

**Rationale:**
- Testability—domain logic runs in pure unit tests
- Portability—if we ever need CLI or different UI
- Forces clean contracts at service boundaries

**Consequences:**
- More projects in solution
- DTOs may be duplicated across layers (acceptable)

### Decision 6.3: Shelf Heuristic for V1 Nesting

**Choice:** Implement shelf/row-based packing, not guillotine or optimal solver.

**Rationale:**
- Simplest correct implementation
- Good-enough results for rectangular parts
- Easy to reason about and debug
- PRD explicitly says "practical" over "optimal"

**Consequences:**
- May leave ~5-10% efficiency on the table vs advanced algorithms
- Can swap algorithm later without changing contracts

### Decision 6.4: Hardcoded Material for Phase 1

**Choice:** Phase 1 uses a single hardcoded material (e.g., 96" × 48" sheet, rotation allowed).

**Rationale:**
- Eliminates material CRUD as a blocking dependency
- Proves data flow without persistence complexity
- Material library comes in Phase 2

**Consequences:**
- Phase 1 UI shows "Demo Material" only
- Material dropdown disabled/hidden until Phase 2

### Decision 6.5: Kerf = Additional Spacing

**Choice:** Kerf is added to part spacing, not subtracted from part dimensions.

**Rationale:**
- Simpler placement math
- Matches PRD recommendation
- Easier to explain to users

**Consequences:**
- Parts render at actual dimensions
- Spacing between parts = (partSpacing + kerfWidth)

### Decision 6.6: Quantity Expansion at Nest Time

**Choice:** PartRow holds quantity; expansion into ExpandedPart instances happens in NestingService.

**Rationale:**
- Import/validation works on compact rows
- Only nesting needs individual instances
- Avoids memory bloat on 10,000-qty rows until needed

**Consequences:**
- Import response is small
- Nesting service handles expansion logic

---

## 7. Open Items / Parking Lot

- **File dialog integration:** Bishop to expose `OpenFileDialog` via bridge in Phase 1. JS requests dialog, .NET opens native picker, returns path.
- **WebUI build embedding:** Need MSBuild task to copy `dist/` into Desktop resources. Bishop owns.
- **Error reporting UX:** Dallas to propose toast/snackbar pattern. Keep simple for Phase 1.

---

## 8. Success Criteria for Phase 0 + 1 Combined

Phase 0 + 1 is complete when:

1. WPF app launches and displays React UI in WebView2
2. User can trigger CSV file open (hardcoded path OK for Phase 0, native dialog for Phase 1)
3. CSV parses, validation errors display in UI
4. User can click "Run Nesting" 
5. Nesting executes, results display: sheet count, utilization, unplaced items
6. At least one Hicks test covers each: bridge round-trip, import validation, nesting placement

---

*— Ripley, keeping the seams tight and the scope honest.*
