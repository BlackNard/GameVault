# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

GameVault is a Windows desktop game launcher (C#/WPF, .NET 10, win-x64 self-contained) for manually added games: it tracks playtime, auto-discovers save locations, keeps dated local backups and session notes, and handles screenshots/recordings. No cloud sync. The repository is private; the owner approves/merges PRs and owns release publication.

## Commands

The SDK is pinned by `global.json` (10.0.404, latestPatch roll-forward). A .NET *runtime* alone cannot build this repo; the matching SDK must be on `PATH`. Repository scripts need PowerShell 7 (`pwsh`).

```powershell
dotnet --version
dotnet restore GameVault.slnx --locked-mode             # do not let this rewrite packages.lock.json
dotnet build GameVault.slnx -c Release --no-restore     # warnings are errors: this is the lint gate
dotnet test GameVault.slnx -c Release --no-build --no-restore

dotnet test tests/GameVault.Tests --filter "FullyQualifiedName~StoreLaunch"      # focused
dotnet test tests/GameVault.App.Tests --filter "FullyQualifiedName~Notes"

pwsh -NoProfile -File ./scripts/verify.ps1              # the real acceptance gate
```

`verify.ps1` does a locked restore, Release build, full tests (plus two workflow-evidence JSON files), a fresh self-contained publish to `artifacts/publish-<guid>`, a runtime-file check, and a check that the output contains no `Data`/`Profiles`. A stale `artifacts/verification.json` is not evidence; re-run. Run one build/test/publish at a time. Installer/release scripts (`test-installer`, `test-capture-packaging`, `test-release`, `audit-release-source`, `build-installer`, `package-release`, `create-draft-release`, `smoke-draft-release`) are only for release work and need the owner's direction. CI (`.github/workflows/build.yml`) runs `verify.ps1` for PRs to `main` and pushes to `main`/`feature/**`; other branch names do not trigger it by themselves.

Dependencies use exact versions with committed `packages.lock.json`; change them only deliberately (`dotnet restore GameVault.slnx --force-evaluate`) and review the lock diff.

## Architecture

- `GameVault.Core`: plain models/contracts, no WPF.
- `GameVault.Infrastructure`: `Storage/` (versioned JSON repository `AppDataStore*`, `ProfileFolders`, `ManagedPaths`), `Library/`, `Launching/`, `Playtime/`, `Saves/` (discovery, backup, restore, destinations), `Notes/`, `Artwork/`, `Capture/` (`CaptureCoordinator`, `CaptureMediaStore`, `NvidiaApp`, `NvidiaCaptureImporter`), `ApplicationWorkspace` (exclusive `.gamevault.lock` handle).
- `GameVault.App`: WPF shell; `MainWindow` partials (`.Navigation`, `.Tray`, `.Capture`, `.Performance`); same-window views (`NotesView`, `BackupsView`, `RestoreView`, `SaveSourcesView`, cover search, `GameEditor`). A type named `*Window` may be hosted inline: check its base type. Capture UI in `Capture/`.
- `GameVault.Capture`: lazy native capture backend (ScreenRecorderLib 7.0.1). Missing prerequisites must degrade gracefully and never block startup.

### Persistence

The data root is always `AppContext.BaseDirectory` (never the working directory or a game folder).

```
GameVault.exe
Data/settings.json  Data/backup-destinations.json  Data/capture-preferences.json
Profiles/<game name>/          (older GUID-named folders are renamed once at startup; the ID lives in profile.json and .gamevault-id)
  profile.json  statistics.json  launch.json?  save-discovery.json  capture-source.json?  cover.png
  Sessions/  Corrections/  Notes/Sessions/  Notes/review.json  Media/<session-id>/  Backups/<dated-backup>/
```

Every document is `schemaVersion`-tagged and rejects unknown fields; an unrecognised version is read-only (no auto-migration). Writes: validate, temp file in the same folder, flush, atomic replace, keep `.bak`. A corrupt primary with a valid `.bak` surfaces as Recovered/read-only and is never auto-promoted. Managed paths reject symlinks/junctions. **Always resolve a game's folder with `ProfileFolders.Resolve(paths, id)`; never `Path.Combine(Profiles, guid)`.** Backup retention default: latest 20 unpinned plus all pinned; external destinations are `<root>/<stable-id>/Backups`.

### Launch and elevation

`MainWindow` runs at normal integrity; an administrator game is started by a one-shot elevated helper (`ElevationBroker`/`BrokerPipe`) that hands observation handles back. Steam/Epic games launch through the store protocol and are tracked via detached-process handoff; GameVault does not bypass authentication or DRM.

### Capture

Per game the recorder is Auto (NVIDIA App when installed), GameVault, or NVIDIA App (`capture-source.json`). GameVault's recorder is registered with global shortcuts (default Ctrl+Shift+F9/F10/F11) only while a game using it runs. NVIDIA App has no API for starting recordings or setting folders, so GameVault only detects it read-only and copies its `.mp4`/`.png` files from the game's recorded sessions into the profile (see `docs/nvidia-app-capture.md`). `RecordingWatchdog` flags a recording whose video frames stop while audio continues.

## Testing

xUnit throughout. `tests/GameVault.Tests` (real filesystem, process fixtures), `tests/GameVault.App.Tests` (headless WPF/contract tests; passing is not proof of real pixel/input behaviour), `tests/Fixtures/GameProcessFixture`, `tests/BrowserDependencyProbe`, `tests/PerformanceHarness` (synthetic only).

## Working conventions

- A person verifies UI, native dialogs, real games and installers. Do not launch the owner's apps, stores, games, browsers or installers to "test", and never kill a GameVault or game process. A green `verify.ps1` does not close the manual-acceptance issues (#1, #3, #14, #16, #17, #18).
- Never commit real saves, profiles, credentials, installers or published binaries (`.gitignore` excludes `/Data/`, `/Profiles/`, `**/Backups/`, `artifacts/`, `work/`, `outputs/`). Never print or store tokens.
- No open-source licence is selected and installers are unsigned; never infer release authorisation. The owner approves merges and owns releases, repository settings and collaborator changes.
- `licenses/windows-sdk/LICENSE.rtf` must stay byte-for-byte as shipped (`.gitattributes`).
- Keep the same-window UI direction and Midnight/Crimson themes; resolve routine implementation choices yourself and ask only when a decision is load-bearing.
- For a reported bug: record exact build/steps/expected/actual, find the failing boundary, add a meaningful regression, fix narrowly, run focused tests, then one full `verify.ps1` for the proposed revision.
- Start from `README.md`, `docs/development.md`, `docs/persistence.md`, `docs/store-game-launching.md`, `docs/portrait-cover-search.md`, `docs/nvidia-app-capture.md`; read other `docs/*` only as needed (some describe superseded milestones).
