# Workbook import safety policy

OptiFab protects the desktop process using measured Open XML package characteristics, not an ordinary-use row limit. A row count is a poor proxy for allocation risk: a wide Worksheet, thousands of package parts, or extreme ZIP expansion can consume much more memory than a long narrow Table Region.

## Desktop thresholds

| Characteristic | Guidance warning | Safety ceiling |
| --- | ---: | ---: |
| Compressed Import Source | 32 MiB | 128 MiB |
| Expanded package | 64 MiB | 256 MiB |
| Largest package part | 32 MiB | 192 MiB |
| Package entries | 5,000 | 20,000 |
| Compression ratio | 100× | 500× |

The warning band tells users that import may take several minutes and recommends closing memory-intensive applications, removing unused formatting/content, or splitting the Workbook. Crossing any ceiling returns `workbook-safety-ceiling-exceeded` before the Import Snapshot is copied into memory or ClosedXML opens the package.

These ceilings reserve headroom for ClosedXML's expanded object graph and the simultaneous immutable Import Snapshot on the supported 64-bit Windows desktop target. They deliberately constrain package expansion rather than imposing an arbitrary row maximum. Cancellation remains available at package discovery, Worksheet, row-reading, validation, combination, and finalization checkpoints.

## Baseline evidence

The full harness was run on Windows 10.0.26200, x64, .NET 8.0.30. The results below are the baseline for this policy:

| Profile | Package | Expanded | Entries / ratio | Elapsed | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| 75,000 rows | 1.6 MiB | 20.5 MiB | 10 / 13.1× | 5.3 s | Accepted |
| 5,000 rows × 200 columns | 2.7 MiB | 45.7 MiB | 10 / 17.2× | 5.5 s | Accepted |
| 200 Worksheets × 100 rows | 0.6 MiB | 5.4 MiB | 209 / 9.2× | 108.8 s | Accepted |
| Highly compressed valid package | 1.1 MiB | 129.0 MiB | 11 / 114.0× | 0.004 s preflight | Accepted with guidance |
| Pathological package | 0.6 MiB | 600.0 MiB | 11 / 1,018.2× | 0.003 s preflight | Blocked |

The wide profile reached 45.7 MiB expanded while remaining usable, so guidance begins at 64 MiB. Its object graph and the 75,000-row profile produced substantially more working-set growth than ZIP size alone (the large profile established a new peak about 240 MiB above its pre-import peak), so the expanded ceiling is limited to 256 MiB rather than extrapolating to an ordinary row count. Compressed bytes are capped at 128 MiB to bound snapshot ownership; the largest-part ceiling is 192 MiB so the measured 129 MiB compressed-package case remains importable with guidance while still bounding a single expanded part. The 100× warning admits the measured highly compressed case with explicit guidance; the 500× ceiling separates it from the pathological 1,018× package. Entry limits provide roughly 24× warning and 96× ceiling headroom over the 209-entry, 200-Worksheet profile.

## Reproducing the benchmark evidence

Run the checked-in harness on the supported desktop target:

```powershell
dotnet run --project benchmarks/PanelNester.WorkbookImportBenchmarks/PanelNester.WorkbookImportBenchmarks.csproj -c Release
```

The profiles cover a 75,000-row Workbook, a 5,000-row/200-column Workbook, 200 Worksheets, a valid highly compressed package, and a pathological expansion package. `--quick` uses smaller dimensions for CI/smoke verification while retaining all five shapes. The harness records compressed and expanded size, package entries, compression ratio, elapsed time, incremental peak working set, and whether preflight accepted or blocked the package.

Re-run the full profile before raising a ceiling or when changing ClosedXML, target architecture, or snapshot ownership. A ceiling change should include the CSV output and the machine's Windows, architecture, .NET, and physical-memory details in the change record.
