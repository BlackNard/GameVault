# Synthetic library performance harness

Run from repository root using the SDK pinned by `global.json`:

```powershell
dotnet restore tests/PerformanceHarness/PerformanceHarness.csproj --locked-mode
dotnet run --project tests/PerformanceHarness/PerformanceHarness.csproj -c Release --no-restore
```

The Windows workflow runs these commands as its own parallel `performance-harness` job when a release is prepared (not on pull requests) and retains `artifacts/performance-harness.txt` as the `performance-harness-evidence` artifact. The harness is deliberately separate from the solution and its locked restore is explicit. The repository-local SDK helper can replace `dotnet` for a local run.

Creates and removes uniquely named test-owned temporary libraries; never uses portable app Data/Profiles or owner test data. Seeds 500 and 1000 profiles five times each before measurement. Each repetition reports first storage/service read, 15 warm list samples and 100 name-query samples (median and nearest-rank p95), managed live bytes after collection and process working set. Query work filters the returned snapshot in memory. Seeding is outside timings; runs are sequential and use a single process. Environment/runtime/CPU count/bitness are printed for interpretation.

First read does not flush OS caches and is not a full-app cold-launch measurement. No covers or long session histories are seeded. No UI, games, capture or secrets are used. Timing is diagnostic, with no machine-dependent pass threshold. This is storage/service and in-memory query evidence; it does not validate WPF startup, scrolling, frame rate, cover rendering, UI latency, peak memory or the final performance acceptance of issue #18. Those owner checks remain pending in [the final manual checklist](../../docs/phase-one-manual-testing.md). Core cache/recycling/freshness correctness is covered by normal xUnit suites.
