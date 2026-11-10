# Settings and game profiles implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement issue #4 with versioned settings, independently recoverable game profiles/statistics, and remembered theme selection.

**Architecture:** Immutable versioned Core records; an Infrastructure JSON repository guarded by the existing application-folder lease at startup and an in-process root write lock. Writes stage/flush/replace on the same volume with a previous-version .bak. The starter window exposes only the theme preference; adding games and native browse dialogs remain issue #5.

**Tech stack:** Existing .NET 10/WPF/xUnit; System.Text.Json and filesystem APIs; no additional packages.

**Spec:** Approved foundation design; issue #4 acceptance criteria and approved UI design/theme feedback.

## Constraints and decisions

- Data/settings.json and Profiles/<GUID>/profile.json, statistics.json, Backups stay beside the executable; game/save paths are configuration only and never created or deleted by this issue.
- Schema 1 requires all constructor fields; future/older schemas are reported read-only, not migrated or overwritten. Malformed data is never silently reset.
- A validated .bak may be displayed as recovery data, read-only; automatic destructive repair is excluded. Original bytes remain intact.
- Create statistics before the new profile commit marker. Preserve already-present valid statistics after an interrupted creation. Updates/renames touch only profile.json.
- Build the library directly from independent profile folders; no central index is needed until ordering/search features require one. Profile visibility belongs in its own record so an index can be reconstructed later.
- Serialize writes per application root (stronger than per-profile ordering). Retain the existing cross-process exclusive lease. Reject links/reparse points inside managed Data/Profiles paths.
- Manual UI testing belongs to the owner; no desktop/browser automation.

## Review focus

1. Truncated/missing/future-schema documents must preserve bytes and prohibit unsafe replacement.
2. Rename and interrupted creation must preserve statistics and backups.
3. An unwritable/locked target must not damage the previous document or leave staged files.
4. Unsafe profile IDs, mismatched stored IDs, and managed junctions must not escape the root.
5. Concurrent saves, default Midnight, persisted Crimson, and startup error reporting must work across repository instances/restarts.

### Task 1: Versioned repository

Files: Core/PersistenceModels.cs; Infrastructure/Storage/{JsonDocuments,ManagedPaths,AppDataStore,StorageValidation}.cs; Tests/PersistenceTests.cs.

Interfaces: `AppDataStore(ApplicationPaths)`; `InitializeAsync()` -> `AppDataSnapshot`; `SaveSettingsAsync(AppSettings)`; `SaveProfileAsync(GameProfile)`; `LoadProfileAsync(Guid)` -> `StoredGame`; `SaveStatisticsAsync(GameStatistics)`. Load results expose Value/State/Message and read-only status. Stable game IDs use GUIDs independent of names.

- [ ] Write meaningful repository tests and API stubs; observe expected failures before implementation.
- [ ] Cover defaults/round trips, rename/history preservation, independent enumeration, concurrent settings writes, previous-version backups, corrupt/future/missing-schema protection, read-only recovery, interrupted staging/creation, unsafe paths, links, and invalid records.
- [ ] Implement the smallest repository satisfying these cases with strict JSON validation and atomic replacement. Keep unsafe read errors distinct from a genuinely missing first-run file.
- [ ] Run the test suite and commit verified persistence.

### Task 2: Startup/settings integration

Files: App/App.xaml.cs, MainWindow.xaml(.cs), App/ThemePalette.cs, docs/development.md, docs/persistence.md, README.md.

- [ ] Initialize storage asynchronously after acquiring the workspace lease; surface recovery/schema problems without reset.
- [ ] Load the stored theme at startup and provide Midnight/Crimson selection. Save off the UI thread, prevent overlapping preference changes, and roll back the visible selection on failure.
- [ ] Keep library/native-picker functionality in later tickets. Update the design's approval status and document schema/safety/recovery limitations.
- [ ] Run the existing full verification script (tests/build/self-contained publish). Owner handles visual/restart checks.

### Task 3: Review and handoff

- [ ] Obtain one focused independent code review of data safety and startup integration. Fix significant findings and rerun affected checks.
- [ ] Push a dedicated feature branch and create a PR referencing #4. Verify Windows CI. Record completed checks and explicitly leave manual UI checks with the owner. Do not merge automatically.
