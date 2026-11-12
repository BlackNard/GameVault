# Session History Implementation Plan

> Implement inline with superpowers:executing-plans. Owner approved the visual layout; preserve the existing low-token workflow and perform one focused final review.

**Goal:** Add same-window session history linked to notes, accurate aggregate time, date-grouped activity and auditable manual corrections.
**Architecture:** Core derives immutable history/activity from statistics and correction records. Existing shared storage gate commits append-only correction sidecars then idempotently projects schema-1 adjustments. Evolve NotesView rather than add another browser.
**Tech stack:** .NET 10 WPF, existing strict atomic JSON, Windows local timezone rules. No new dependencies.
**Spec:** docs/superpowers/specs/2026-11-12-session-history-design.md (owner approved).

## Global constraints
- Preserve measured sessions, note IDs/drafts, recovery journals, legacy adjustments and real saves.
- Corrections require a verified Completed journal and no active/uncertain session for that game. Interrupted/legacy unknown ends remain visible but cannot be corrected until verified. No mutation of start/end instants.
- Project cumulative adjustments before exposing writable stats. Protect invalid/future/recovered records, conflicting projections and stale corrections.
- Day allocations are proportional estimates, deterministic and sum-conserving; timezone rules and undated legacy corrections are visible.
- Owner alone tests UI/native dialogs/games. Package clean before importing preserved private data.

## Review focus
- Partial correction/statistics commit and repeated reconciliation must neither double apply nor drop deltas.
- Sleep/gaps and midnight/DST/week/month boundaries must not inflate total or silently lose seconds.
- Stale edits, active/recovered data, integer overflow and legacy unlinked adjustments must stay protected.
- Refresh, live progress, correction and status changes must preserve note drafts and stable selection.
- Parent close and sidebar navigation must await work and protect both note and correction drafts.

## Task 1: Core history and allocation
Files: Core/HistoryModels.cs, Core/SessionHistory.cs; tests/HistoryTests.cs.
Interfaces: HistoryData(profile,statistics,journals,corrections,warnings); HistorySummary with per-session durations/end state, longest ID, totals, daily buckets/unallocated; SessionHistory.Build(data,zone); PeriodStart(date,group).
- [x] Write failing tests for midnight/DST/rounding, zero/missing intervals, corrections vs measured, ordering/ranking/active exclusion and legacy undated totals.
- [x] Run tests RED, implement date allocation (BigInteger cumulative floors) and derived summaries, run GREEN; commit.

## Task 2: Correction persistence and recovery
Files: Infrastructure/Storage/AppDataStore.History.cs; existing AppDataStore partial integration; tests/CorrectionTests.cs.
Interfaces: LoadHistoryAsync(Guid), CorrectSessionAsync(game,session,expectedSeconds,newSeconds,reason); SetGameStatusAsync(game,status). TimeCorrection schema includes stable ID/sequence, measured baseline, previous/new adjusted durations, reason, created UTC.
- [x] Test RED persistence, stale rejection, immutable source files, linked note preservation, invalid/active/uncertain input, failed projection/restart idempotence, protected/removed sidecars and overflow.
- [x] Implement shared-gate append-only commit/reconcile; validate projections at all statistics writers, return protected diagnostics on failure; run full tests and commit.

## Task 3: Approved WPF history
Files: NotesView.xaml(.cs), new partial NotesView.History.cs for activity/corrections/status; MainWindow integration and library refresh.
- [x] Replace session picker with sortable table above existing note editor; summary/longest navigation; activity table/history; inline correction form; status selector; retain reviews/exports and themes.
- [x] Keep all navigation draft guards, snapshot updates without overwriting editor text, block correction/status during game launch/run, and update library totals after changes. Test meaningful draft state helper behavior.
- [x] Full tests, focused code review/regression fixes; owner checklist and commit.

## Task 4: Handoff
- [ ] Run full locked verify/build/tests/publish and GitHub CI. Create draft PR; do not merge.
- [ ] Verify clean ZIP then import latest private test snapshot only into new local build. Provide executable, PR and focused owner checklist; preserve older builds.
## Execution evidence
- History/allocation, correction persistence/projection and UI delivered. Meaningful regressions observed RED before implementation: five history cases, five correction transaction cases, input validation, stale inactive display totals, and an activity overflow boundary.
- Current full suite: 136 passing (112 Core/Infrastructure,24 Windows/headless). No desktop/native-dialog/real-game tests performed.
- Focused review found no important outstanding issue; Activity now retains editor identity so review exports cannot acquire another session's metadata. Friendly status labels and disabled correction explanation added.
- Corrections deliberately require a confirmed Completed journal and no active tracked session for that game. Original timestamps, measured durations and notes are preserved. Date splits are proportional estimates; undated legacy amounts remain separate.

- Final locked verification on 2026-11-13 local time: zero-warning Release build, all 136 tests passed, self-contained win-x64 publish verified (run 3db44f34461e480999d9a467193585bc). Owner UI checks remain pending. Clean package/import and remote CI are next.
