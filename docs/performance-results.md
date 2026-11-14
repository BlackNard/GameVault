# Synthetic library performance report — 2026-11-14

Reference: Windows NT 10.0.26300 x64, AMD Ryzen 9 9900X3D (24 logical processors), 33,435,799,552 bytes physical RAM, .NET 10.0.15, Release configuration. Other issue workers/builds were active; these are indicative local measurements rather than isolated hardware certification.

Run: `dotnet run --project tests/PerformanceHarness -c Release`.

The harness creates five independent 500-game and five independent 1,000-game libraries in test-owned temporary folders through real AppDataStore writes, then removes only those folders. Seeding is excluded from timing. It measures real LibraryService.ListAsync, including fresh JSON validation and filesystem safety checks. Each fixture has profile/statistics and an inert executable; it has no covers, journals, backups or real game processes. Query is an ordinal-ignore-case name substring query plus array materialization, not WPF rendering. Fifteen warm lists and 100 queries are measured per library; p95 uses nearest rank (15/15 and 95/100). Each first read is from a newly constructed store/service after seeding. OS file caches are not flushed: **first read is storage/service cold, not physical-disk cold or app cold launch**.

| Games / repetition | First read ms | Warm median ms | Warm p95 ms | Query median ms | Query p95 ms |
|---|---:|---:|---:|---:|---:|
| 500 / 1 | 188.84 | 162.67 | 176.41 | 0.0092 | 0.0097 |
| 500 / 2 | 161.74 | 156.95 | 160.97 | 0.0113 | 0.0137 |
| 500 / 3 | 163.28 | 167.62 | 221.07 | 0.0033 | 0.0036 |
| 500 / 4 | 162.87 | 158.31 | 169.55 | 0.0032 | 0.0035 |
| 500 / 5 | 174.59 | 161.03 | 178.81 | 0.0032 | 0.0035 |
| 1000 / 1 | 332.64 | 320.67 | 363.23 | 0.0058 | 0.0061 |
| 1000 / 2 | 359.33 | 318.27 | 339.75 | 0.0060 | 0.0065 |
| 1000 / 3 | 338.37 | 319.31 | 345.96 | 0.0059 | 0.0063 |
| 1000 / 4 | 327.16 | 326.30 | 583.89 | 0.0060 | 0.0064 |
| 1000 / 5 | 336.33 | 317.92 | 335.20 | 0.0061 | 0.0065 |

First-read median/p95 across five fixtures: 500 games **163.28 / 188.84 ms**; 1000 games **336.33 / 359.33 ms**. Warm and query distributions are shown per independent fixture to avoid mislabelling medians of medians as pooled percentiles. An earlier baseline before thumbnail changes was 500: first 191.57, warm median/p95 167.32/180.12 ms; 1000: first 322.81, warm 316.08/328.74 ms. LibraryService has deliberately not changed: measured reads are already off the UI thread, and avoiding a parsed-JSON cache preserves external-edit freshness and corruption/recovery behavior. Name query cost alone does not justify additional filter infrastructure; parent owns debounce/UI application and stable rows.

After a full GC per fixture, harness managed live memory ranged 1,037,632–1,062,792 bytes at 500 games and 1,470,744–1,479,176 at 1000. Process working set ranged 92,028,928–97,026,048 bytes at 500 and 92,028,928–92,622,848 at 1000. These measure the console workload, not the full WPF app, thumbnail storage or peak allocation. The thumbnail cache independently enforces 32 MiB decoded pixel retention (4 bytes/pixel) and two concurrent decodes. Visible rows and at most two active decodes add memory outside that retained-cache budget. Every decoded image remains frozen, source-size/dimension limits remain enforced, oversized values are not cached, same-key loads deduplicate, last cancelled waiters skip queued decode, released rows clear references and reject late assignment. RefreshGame invalidates old cover content even if path/mtime/length are unchanged.

Pending owner checks: full app cold/warm launch and time to interaction, scroll/selection/filter frame pacing at 500/1000 with artwork, long-history libraries, Midnight/Crimson scaling, idle CPU/working set, and real-game CPU/GPU/frame-time impact. No WPF windows, native dialogs, desktop capture or real games were used. No hard timing assertion is added to standard tests; CI can collect the harness results as a diagnostic artifact with machine details.
