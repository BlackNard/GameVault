# Local settings and game profiles

Issue #4 adds a versioned local JSON repository. The executable remains the anchor for all managed data; the current working directory and game installation directory are never used as app-data roots.

```text
GameVault/
  GameVault.exe
  Data/settings.json
  Profiles/<game name>/
    profile.json
    statistics.json
    capture-source.json   (optional: which recorder the game uses, see nvidia-app-capture.md)
    .gamevault-id
    Backups/
```

Profile folders are named after the game (for example `Profiles/TEKKEN 8/`); a second game with the same name becomes `TEKKEN 8 (2)`. The stable game ID is stored in `profile.json` and repeated in the small `.gamevault-id` marker so a folder is still identified if profile.json is damaged. Folders created by earlier versions were named by the GUID; they are still recognised and each is renamed once at startup. Renaming a game renames its folder. A folder that is in use (for example open in Explorer) keeps its current name and is renamed on a later start or save; it is never deleted or replaced. Folders GameVault cannot identify, or that repeat another folder's ID, are reported and left unchanged. Code never builds a profile path from the name or the GUID directly: use `ProfileFolders.Resolve`. External backup destinations keep `<chosen-root>/<stable-id>/Backups`.

Only the app's own managed files are written. Configuring executable/working/save paths does not create, move, read, or delete the actual game or its saves in this issue.

## Schema 1

Issue #6 adds an optional per-profile `launch.json` containing `schemaVersion`, matching `gameId`, and nullable `targetExecutablePath`. Missing means track the main executable from profile.json. It uses the same validation, atomic replacement, previous-copy, link, and read-only recovery protections. A separate target must be a full .exe path. Process identities and runtime states are transient in #6; persistent session timing and interrupted-session recovery remain #7. A confirmed launch changes only Not started to Playing, preserving profile identity, preferences, save sources, and recorded statistics.

All serialized documents require `schemaVersion` and all of their constructor fields. Unknown fields, malformed values, missing fields, mismatched game IDs, invalid enum values, unsafe game paths, or invalid statistics are rejected without resetting data. Unsupported older/newer schema versions are read-only; no migration is currently required or implemented.

- **settings.json:** `schemaVersion`, `theme` (the id of a theme in `ThemeCatalog`, for example Midnight), `defaultBackupLimit` (positive integer), `keepAllBackups`, `reduceMotion`, optional `closeToTray`, `notifyBackupFailures` and `welcomeDismissed` (true once the first-run welcome was skipped; missing means false). Optional `showSplash` (the small startup window; missing means true). New installations default to Midnight and 20 unpinned backups. These are defaults/configuration; actual backup retention is implemented in its own ticket.
- **profile.json:** `schemaVersion`, stable `id`, `name`, absolute `executablePath` and `workingDirectory`, absolute `saveSources`, `status` (NotStarted/Playing/Completed/OnHold), `backupLimit`, `keepAllBackups`, and `isArchived`.
- **statistics.json:** `schemaVersion`, matching `gameId`, `sessions` (ID/start/end/duration seconds) and `adjustments` (ID/signed seconds/reason). Timing and correction UI remain future features; this issue preserves their records without inventing playtime.

The library is reconstructed directly from independent profile folders. A central library index is deliberately not needed yet; profile visibility survives through `isArchived`, and a future ordering index can be rebuilt. Renaming changes profile.json and renames the folder, preserving stable identity, statistics, and existing backups. Removing a game installation therefore does not remove app-owned records.

## Write safety and recovery

Issue #7 adds versioned `Sessions/<session-id>.json` journals. Each holds the matching game/session IDs, start and last-observed timestamps, awake duration in 100 ns ticks, Active/Completed/Interrupted state, executable/process identity, optional process-group name, and recovery count. Journals commit before their schema-1 statistics projection; startup reconciles the same session ID rather than adding it twice. Completed journals retain recovery metadata. A journal cannot move a completed record back to active, reduce duration, or regress its observation timestamp. A resumed Interrupted session retains its ID and prior ticks but starts a new awake-clock baseline, excluding the unobserved gap. Backup-only/future/corrupt journals stay read-only with diagnostics. See [manual timing/recovery checks](design/playtime-manual-tests.md) for the explicit limits.

The application acquires its existing exclusive workspace-file handle before opening the repository. In-process operations across repository instances are serialized per normalized app root; disk work runs on worker tasks. Closing the window during a preference save waits for that operation before releasing the workspace handle.

Writes validate the model/current document, serialize to a uniquely named same-directory temporary file, flush to disk, recheck readability/version, and atomically replace the destination with a `.bak` of its previous version. A new document uses a non-overwriting move. Ordinary failure removes its own staged file and preserves existing committed data. An abrupt process termination may leave an ignored temporary file, which is never treated as a committed document. No copy-over fallback weakens replacement semantics on unsupported filesystems.

For a new profile, statistics are committed before profile.json. The profile document is the inventory commit marker. An interrupted creation remains an incomplete preserved folder; retrying the same identity retains existing valid statistics rather than resetting them. Updating a profile never rewrites its statistics. There is no claim of a multi-file transaction covering unrelated profile/settings updates.

A malformed or missing primary file with a valid `.bak` exposes that previous value as **Recovered/read-only**. It does not replace the primary automatically. An unsupported primary schema never falls back to an older copy. Bad/unreadable primaries and unrecognized folders remain intact with warnings; other independent profiles can still be inspected.

Recovery is deliberately non-destructive in this version: preserve the whole app folder and use an appropriate compatible build or a later explicit recovery workflow. Do not manually overwrite unknown-schema data or assume that a recovered previous version is the newest data. A missing statistics file in an already committed profile is reported rather than silently recreated during startup.

Managed Data/Profiles files and intermediate directories reject links/junctions. GUID identities determine profile paths rather than game names. The filesystem checks protect normal configured paths; they are not a security boundary against another privileged process maliciously swapping files during an operation.

## What the current window supports

Startup initializes local directories/settings, loads the profile inventory, restores the saved theme, and displays storage warnings. The theme picker saves asynchronously and retains the previous selection on failure. Recovery/unsupported settings disable preference writes. The window shows a count of stored profiles; the approved full library UI and native game/path pickers are still issue #5 work.

## Verification

Automated tests cover defaults, persisted themes/preferences, profile round-trips, rename/history preservation, independent discovery, previous copies, malformed and future schemas, read-only recovery, concurrent saves, invalid records, locked files, interrupted staging/creation, and linked-directory rejection. Tests use disposable fixtures rather than real game saves. Run `scripts/verify.ps1` for locked restore, Release build, tests, and standalone publish checks.

The owner performs manual UI checks:

- Change to Crimson, close GameVault, reopen it, and confirm the selection/colors return.
- Switch back to Midnight and repeat.
- Close immediately after a theme change; the pending save should finish before shutdown.
- Confirm that Data/settings.json and Profiles are beside the executable.

No assistant desktop/UI testing is performed for this issue. A development package is not a public release or installer.

References: [File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace), [required JSON properties/constructor parameters](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties).


## Backup destinations (redesign)

Data/backup-destinations.json stores schema1 global/per-game roots and prior roots. Missing settings preserve the portable Profiles/<stable-id>/Backups layout. Explicit external stores use <chosen-root>/<stable-id>/Backups; profile/statistics/session/notes data remain beside the app. Changes select future destinations without moving old snapshots. History reads portable and registered prior stores; restore/retention keep existing manifest/hash/recovery rules.

Operations hold the per-app backup-store lock and each registered external store lock. Offline or corrupt historical stores block mutations conservatively until resolved; available history can still be listed with warnings. Reparse paths, overlaps with games/live saves or the app folder, and unavailable destinations are rejected. An external disk is never silently replaced by portable storage. Data/Profiles import retains the mappings while external files remain at their chosen absolute locations.
