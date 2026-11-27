# Save finder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GameVault finds any game's save folder by itself (before play from the game's identity, during play from file activity), selects confident finds and backs them up with a notice, and asks about unsure ones.

**Architecture:** Pure scoring, save-root lifting and noise rules live in `GameVault.Core`. `GameVault.Infrastructure/Saves` gains an identity reader, a before-play finder, a play-time watcher and a `save-finds.json` store; `SaveDiscoveryService.Build` merges all candidates, scores them and selects High ones. The App reacts to a new discovery event with toasts/notices and an immediate backup.

**Tech Stack:** C# / .NET 10, WPF, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-27-save-finder-design.md` (issue #111).

## Global Constraints

- No new dependency, service or network call; nothing about saves or paths leaves the PC.
- No new fields or enum values in existing documents (`save-discovery.json`, `profile.json`, settings). Automatic selections use origin `KnownRule`; new data goes in `Profiles/<game>/save-finds.json` and the marker `Data/save-watch-off.marker`.
- Every candidate path goes through `SaveSourcePolicy.Validate` and `SaveSourceInspector` (no links/junctions followed, no broad roots, entry/depth limits unchanged).
- Watchers exist only while a game GameVault started is running; the callback never opens files.
- A manual path (`profile.SaveSources`) always wins; a rejected path is never selected automatically again.
- On-screen text short; explanations in `local:SettingHelp.Text`. Toast text: title "<Game>: saves found", detail "In <path> · backed up".
- Never print Steam account ids or file contents in logs.
- Tests use synthetic temp folders only (never the real user folders), headless, no windows.

## Review Focus

1. A game that writes settings only (e.g. `11bitstudios\Frostpunk\config.bin`) must not be auto-selected → test in Task 1 (`SettingsOnlyFolderIsLow`) and Task 4.
2. Two games running at once both see the same writes → the shared folder must not become High for either → Task 1 (`WrittenWhileOtherGameRanIsPenalised`) and Task 5.
3. Watcher buffer overflow or a burst of 10,000 events → bounded memory, evidence marked partial, no crash → Task 5 (`BurstStaysBounded`).
4. A rejected folder found again by every route (store number, activity, database) stays unselected → Task 4 (`RejectedPathIsNeverSelected`).
5. A `save-discovery.json` written by the new code must load in the current validator, and `save-finds.json` with an unknown field is read-only → Task 3 and Task 4.

---

### Task 1: Scoring, save-root lifting and noise rules (Core)

**Files:**
- Create: `src/GameVault.Core/SaveFinding.cs`
- Test: `tests/GameVault.Tests/SaveFindingTests.cs`

**Interfaces:**
- Produces:
  - `enum SaveCandidateSource { KnownRule, StoreNumber, Database, PublisherFolder, GameNamed, InstallFolder, NameSearch, Activity }`
  - `enum SaveConfidence { Low, Medium, High }`
  - `record SaveCandidateFacts(string Path, SaveCandidateSource Source, bool NamedForGame, bool HasSaveFiles, bool OnlySettingsFiles, bool WrittenDuringPlay, int SessionsSeen, bool WrittenWhileOtherGameRan)`
  - `static class SaveCandidateScore { int Points(SaveCandidateFacts f); SaveConfidence Of(SaveCandidateFacts f); }`
  - `static class SaveFileNames { bool IsSettings(string fileName); bool IsSave(string path); }` (`IsSave` = `SaveCandidateRules`-style save extensions or Rockstar names, and not `IsSettings`)
  - `static class SaveActivityFilter { bool Ignore(string fullPath, IReadOnlyList<string> roots); }`
  - `static class SaveRootFinder { IReadOnlyList<string> Lift(IEnumerable<string> writtenFiles, IReadOnlyList<string> protectedRoots); }`
  - `static class GameNameTokens { IReadOnlySet<string> For(IEnumerable<string?> names); bool Matches(string folderName, IReadOnlySet<string> tokens); }` (normalised: letters/digits lower-case, ≥ 3 chars; app numbers kept as digits)

- [ ] **Step 1: Write the failing tests** in `SaveFindingTests`:
  - `FrostpunkStoreNumberFolderIsHigh`: Source=StoreNumber, NamedForGame, HasSaveFiles → `High`.
  - `TekkenUnrealFolderIsHigh`: Source=GameNamed, NamedForGame, HasSaveFiles → `High`.
  - `SettingsOnlyFolderIsLow`: Source=PublisherFolder, NamedForGame, OnlySettingsFiles → `Low`.
  - `UnnamedActivityFolderIsMedium`: Source=Activity, WrittenDuringPlay, HasSaveFiles → `Medium`.
  - `NamedActivityFolderIsHigh`: Activity + NamedForGame + WrittenDuringPlay + HasSaveFiles → `High`.
  - `WrittenWhileOtherGameRanIsPenalised`: the High case above plus WrittenWhileOtherGameRan → not `High`.
  - `NoSaveFilesIsNeverHigh`: KnownRule + NamedForGame + WrittenDuringPlay, HasSaveFiles=false → not `High`.
  - `SettingsNames`: `config.bin`, `GfxConfig.ini`, `keybindings.settings`, `settings.json` are settings; `PLAYTHROUGH1.save`, `autosave.save`, `ProfileData.bin`, `SGTA400`, `slot1.sav` are saves.
  - `LiftsToCommonFolder`: writes `R\HOODLUM\323190\remote\saves\a.save`, `R\HOODLUM\323190\remote\ProfileData.bin` with protected root `R` → `[R\HOODLUM\323190\remote]`.
  - `LiftSplitsPerTopFolder`: writes under `R\A\x\1.sav` and `R\B\y\2.sav` → two roots `R\A\x`, `R\B\y`.
  - `LiftNeverReturnsProtectedRoot`: a file directly in `R` → nothing.
  - `IgnoresNoise`: `Local\Temp\…`, `Local\Microsoft\…`, `Roaming\discord\…`, `Local\Google\Chrome\…`, `Local\NVIDIA\DXCache\…`, `Local\D3DSCache\…`, `x.log`, `x.tmp`, `x.dmp`, `x.etl`, `db-journal` → ignored; `Roaming\HOODLUM\323190\remote\saves\a.save` → kept.
  - `TokensMatch`: tokens from `["TEKKEN 8","Frostpunk","323190"]` match folder names `Tekken8`, `FrostPunk`, `323190`, not `Saves`.

- [ ] **Step 2: Run** `dotnet test tests/GameVault.Tests -c Release --filter "FullyQualifiedName~SaveFindingTests"` → FAIL (types missing).

- [ ] **Step 3: Implement `SaveFinding.cs`.** Points:

  | Fact | Points |
  |---|---|
  | Source KnownRule 4, StoreNumber 3, Database 3, PublisherFolder 2, GameNamed 2, InstallFolder 1, NameSearch 1, Activity 0 | as listed |
  | NamedForGame | +2 |
  | WrittenDuringPlay | +3 |
  | SessionsSeen ≥ 2 | +1 |
  | HasSaveFiles | +2 |
  | OnlySettingsFiles | −3 |
  | WrittenWhileOtherGameRan | −4 |

  `High` = points ≥ 6 **and** HasSaveFiles; `Medium` = points ≥ 3 and HasSaveFiles; otherwise `Low`. `Lift`: drop files not under a protected root; group by the first folder below their root; per group the root is the longest common directory of the files; discard a result equal to a protected root. Noise list: folder names `Temp`, `Microsoft`, `Packages`, `CrashDumps`, `D3DSCache`, `NVIDIA`, `AMD`, `Google`, `Mozilla`, `BraveSoftware`, `Opera Software`, `discord`, `Steam` (except a path containing `\userdata\`), `GameVault`, `INetCache`, `ShaderCache`, `GPUCache`; extensions `.log .tmp .etl .dmp .lock .pdb .pf`; names ending `-journal`.

- [ ] **Step 4: Run** the same filter → PASS.

- [ ] **Step 5: Commit** `feat(saves): score save candidates and lift save roots (#111)`.

### Task 2: Game identity reader

**Files:**
- Create: `src/GameVault.Infrastructure/Saves/GameSaveIdentity.cs`
- Test: `tests/GameVault.Tests/GameSaveIdentityTests.cs`

**Interfaces:**
- Produces: `record GameSaveIdentity(string? SteamAppId, string? GogId, string? EpicApp, string? Company, string? Product, IReadOnlySet<string> Tokens)` and `static GameSaveIdentity GameSaveIdentity.Read(GameProfile profile)`. `Tokens` = `GameNameTokens.For([profile.Name, install folder name, exe name, Product, SteamAppId])`.

- [ ] **Step 1: Failing tests** on a temp install folder: `steam_appid.txt` "323190" → SteamAppId; `hlm.ini` `[GameSettings]\nAppId=323190` (no txt) → SteamAppId; `steam_settings\steam_appid.txt` → SteamAppId; `goggame-1648559910.info` → GogId; `.egstore\*.mancpn` with `"AppName": "Fennec"` → EpicApp; a non-numeric appid file → null; a 2 MB `steam_appid.txt` → null (files over 64 KB are skipped); `Tokens` contains `frostpunk` and `323190`. Version info: use the test assembly's own path as the exe → `Product` is its product name.
- [ ] **Step 2: Run** filter `GameSaveIdentityTests` → FAIL.
- [ ] **Step 3: Implement.** Search the exe folder and its parent install root (depth ≤ 2), small files only; `FileVersionInfo.GetVersionInfo` inside try/catch; the existing `StoreGameLauncher().Resolve` supplies the Steam id when it finds one. Values are never logged.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(saves): read a game's store numbers and publisher (#111)`.

### Task 3: `save-finds.json` store

**Files:**
- Create: `src/GameVault.Core/SaveFindsModels.cs`, `src/GameVault.Infrastructure/Storage/AppDataStore.SaveFinds.cs`
- Test: `tests/GameVault.Tests/SaveFindsPersistenceTests.cs`

**Interfaces:**
- Produces: `record SaveFind(string Path, int Files, int Sessions, DateTimeOffset LastSeen, bool Shared)`; `record SaveFindsDocument(int SchemaVersion, Guid GameId, SaveFind[] Finds, string[] Rejected, long Revision = 1)`; `AppDataStore.LoadSaveFindsAsync(Guid) : Task<StoredDocument<SaveFindsDocument>>`; `AppDataStore.SaveFindsAsync(SaveFindsDocument) : Task`. Limits: ≤ 64 finds, ≤ 64 rejected, paths fully qualified, no duplicates (case-insensitive canonical).

- [ ] **Step 1: Failing tests:** round trip; unknown field → `IsReadOnly`; schemaVersion 2 → read-only; 65 finds → write throws `ArgumentException`; missing file → `DocumentState.Missing`; file lives at `ProfileFolders.Resolve(paths,id)/save-finds.json`.
- [ ] **Step 2: Run** filter `SaveFindsPersistenceTests` → FAIL.
- [ ] **Step 3: Implement** following `AppDataStore.SaveDiscovery.cs` (same `documents.Read/Write`, validator style).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(saves): keep save finds and rejected folders per game (#111)`.

### Task 4: Before-play finder and scored selection in discovery

**Files:**
- Create: `src/GameVault.Infrastructure/Saves/SaveFinder.cs`
- Modify: `src/GameVault.Infrastructure/Saves/SaveDiscoveryService.cs` (`PerformAsync`, `Build`, new `RejectAsync`, `RestoreAsync`, event), `src/GameVault.Infrastructure/Saves/SaveLocationDatabase.cs` (`Candidates` takes the identity's Steam id), `src/GameVault.Infrastructure/Saves/SaveCandidateRules.cs` (candidates carry a `SaveCandidateSource`)
- Test: `tests/GameVault.Tests/SaveFinderTests.cs`, existing `SaveDiscoveryTests.cs`, `SaveConfirmationTests.cs`, `SaveLocationDatabaseTests.cs`

**Interfaces:**
- Consumes: Task 1 types, `GameSaveIdentity.Read`, `LoadSaveFindsAsync/SaveFindsAsync`.
- Produces:
  - `static IReadOnlyList<SaveCandidate> SaveFinder.Candidates(GameProfile profile, GameSaveIdentity identity, SaveDiscoveryEnvironment env, SaveFindsDocument? finds, CancellationToken token)`
  - `SaveCandidate` gains `SaveCandidateSource Source = SaveCandidateSource.GameNamed` (positional record default at the end).
  - `SaveDiscoveryEnvironment` gains `string LocalLow` and `string ProgramData` (computed in `Current()`; test constructors updated).
  - `SaveDiscoveryService.SelectionChanged : EventHandler<SaveSelectionChanged>`, `record SaveSelectionChanged(Guid GameId, string GameName, SaveSourceMapping[] NewlySelected, SaveSourceMapping[] NewlyToConfirm, bool NothingFound)`. Raised after a successful save only when the selected/confirmable sets differ from the previous document's, or when a scan after a finished session found no candidate at all.
  - `Task<SaveDiscoveryResult> RejectAsync(Guid gameId, Guid sourceId, CancellationToken)`, `Task<SaveDiscoveryResult> RestoreAsync(Guid gameId, string path, CancellationToken)` (undo).

- [ ] **Step 1: Failing tests** (`SaveFinderTests`, temp env):
  - `FindsHoodlumFolderByAppNumber`: install has `steam_appid.txt`=323190, `roaming\HOODLUM\323190\remote\saves\PLAYTHROUGH1.save` exists → after `ScanAsync` that folder `…\323190\remote` is selected, origin `KnownRule`, evidence starts `Found automatically`.
  - Store-number table: one `[Theory]` row per entry (`Goldberg SteamEmu Saves\N\remote`, `GSE Saves\N\remote`, `HOODLUM\N\remote`, `EMPRESS\N\remote`, `FLT\N`, `OnlineFix\N`, public `Steam\CODEX\N\remote`, public `Steam\RUNE\N\remote`, public `OnlineFix\N`, public `CPY_SAVES\N`, programdata `Steam\RLD!\N`, Steam `userdata\<acct>\N\remote`) → found.
  - `FindsUnrealFolderAndSelectsIt` (TEKKEN 8): `local\TEKKEN 8\Saved\SaveGames\slot.sav` → selected.
  - `PublisherSettingsFolderNotSelected`: exe version company/product set via identity stub, `roaming\11bitstudios\Frostpunk\config.bin` only → not selected and not confirmable.
  - `NameSearchFindsDeepFolder`: `docs\My Games\Studio\Frostpunk\save1.sav` (depth 3) → candidate, Medium → `CanConfirm` true, not selected.
  - `NameSearchIsBounded`: 3,000 sibling folders in `roaming` → scan finishes and visits ≤ 2,000 per root (expose a counter internal to tests).
  - `RejectedPathIsNeverSelected`: reject the HOODLUM source → rescans keep it unselected, `CanConfirm` false, message `Not this game's saves`; `RestoreAsync` brings it back.
  - `ManualPathWins`: profile with `SaveSources` set → no automatic candidates selected.
  - `SelectionChangedRaisedOnce`: first scan raises `NewlySelected` with the HOODLUM folder; a second identical scan raises nothing.
  - `WrittenDocumentLoadsInCurrentValidator`: the document written after auto-selection loads with `DocumentState.Ready`.
  - Database: `SaveLocationDatabaseTests` new case — a non-Steam-path install whose identity has app 323190 gets the manifest's `<winAppData>/11bitstudios/Frostpunk/Default/saves` candidate when that folder exists.
- [ ] **Step 2: Run** filter `SaveFinderTests|SaveDiscoveryTests|SaveConfirmationTests|SaveLocationDatabaseTests` → new tests FAIL.
- [ ] **Step 3: Implement.** `SaveFinder` builds, in order: existing `SaveCandidateRules.For` results (source KnownRule/GameNamed/InstallFolder), store-number table for `identity.SteamAppId`, database (via `env.Database`, passing the Steam id), publisher/product folders `<root>\<Company>\<Product>` and `<root>\<Product>` for Roaming, Local, LocalLow, Documents, My Games, Saved Games, then the name search (breadth-first, depth ≤ 3, ≤ 2,000 folders per root, 1 s total, skipping `SaveActivityFilter.Ignore` folders and reparse points, matching `GameNameTokens.Matches`), then `finds.Finds` (source Activity). `Build` inspects each candidate twice (any file; `SaveFileNames.IsSave`) to fill `HasSaveFiles`/`OnlySettingsFiles`, computes `NamedForGame` from path segments vs `identity.Tokens`, `WrittenDuringPlay/SessionsSeen/WrittenWhileOtherGameRan` from `finds`, then: High → origin `KnownRule`, selected, evidence `Found automatically · <source words>`; Medium → `Suggestion`, `CanConfirm`; Low → `Suggestion`, not confirmable. Overlapping High candidates: keep the higher score (tie → the narrower path). Rejected paths: never selected or confirmable. The old "exactly one ready KnownRule" rule is replaced by this. Keep `Confirmed` and manual behaviour unchanged.
- [ ] **Step 4: Run** the filter → PASS; then `dotnet test tests/GameVault.Tests -c Release` → all PASS (update older expectations only where the new selection is the intended behaviour, and say which in the commit).
- [ ] **Step 5: Commit** `feat(saves): find saves by store number, publisher and name, select confident finds (#111)`.

### Task 5: Watch while playing

**Files:**
- Create: `src/GameVault.Infrastructure/Saves/SaveActivityWatcher.cs`
- Modify: `src/GameVault.App/MainWindow.xaml.cs` (construct, dispose in the same place as `discoveryCoordinator`), `src/GameVault.App/MainWindow.Tray.cs` (dispose on quit)
- Test: `tests/GameVault.Tests/SaveActivityWatcherTests.cs`

**Interfaces:**
- Consumes: `IGameSessionSignals`, `AppDataStore` finds methods, `SaveRootFinder.Lift`, `SaveActivityFilter.Ignore`, `SaveDiscoveryService.ScanAsync`.
- Produces: `sealed class SaveActivityWatcher(IGameSessionSignals signals, AppDataStore store, ISaveDiscoveryScanner scanner, Func<Guid, IReadOnlyList<string>> rootsFor, Func<bool> enabled, TimeSpan? settle = null) : IAsyncDisposable`; `int ActiveWatcherCount { get; }` (tests); `Task FlushAsync(Guid gameId)`.
  `rootsFor` in the App returns Roaming, Local, LocalLow, Documents, Saved Games, Public Documents, each Steam `userdata`, and the game's install folder (existing ones only).

- [ ] **Step 1: Failing tests** (temp roots, a fake `IGameSessionSignals`):
  - `RecordsWritesAndStoresFind`: Running → write `r\Studio\Game\saves\1.sav` → SessionEnded(confirmed) → after `FlushAsync`, `save-finds.json` has `r\Studio\Game\saves` with Sessions=1 and the scanner was called once.
  - `NoWatchersWhenNoGameRuns`: before Running and after flush `ActiveWatcherCount == 0`.
  - `DisabledDoesNothing`: `enabled()` false → no watcher, no document.
  - `NoiseIgnored`: writes in `r\Temp\x.sav` and `r\Studio\Game\run.log` only → no find.
  - `TwoGamesMarkShared`: games A and B both Running, one write → both finds have `Shared=true`.
  - `BurstStaysBounded`: 10,000 writes → at most 5,000 recorded events, no exception, find stored.
  - `SecondSessionCountsUp`: two sessions writing the same folder → Sessions=2.
- [ ] **Step 2: Run** filter `SaveActivityWatcherTests` → FAIL.
- [ ] **Step 3: Implement.** One `FileSystemWatcher` per existing root per running game (`IncludeSubdirectories`, `InternalBufferSize = 64 KB`, Created|Changed|Renamed); callback enqueues the full path only if `!Ignore`; `Error` (overflow) marks the session partial (still stored). Stop `settle` (default 30 s, tests 0) after confirmed exit or at once on unconfirmed end, lift roots, merge into `save-finds.json` (Files, Sessions+1, LastSeen, Shared if another game was Running at any time during the session), drop finds whose path is rejected, then `scanner.ScanAsync(gameId)`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(saves): watch for save writes while a game runs (#111)`.

### Task 6: Notices, immediate backup, plain failure text, background pass

**Files:**
- Modify: `src/GameVault.App/MainWindow.Notices.cs` (new prefix `saves:`), create `src/GameVault.App/MainWindow.SaveFinder.cs`, modify `src/GameVault.Core/NoticeCenter.cs` (`NoticeKind.SaveFound`), `src/GameVault.Infrastructure/Saves/SaveDiscoveryService.cs` (`RequireVerifiedSourcesAsync` throws `SaveSourceMissingException` when nothing is selected), `src/GameVault.Infrastructure/Saves/BackupService.cs` (message for that exception), `src/GameVault.Core/BackupAlertPolicy.cs` if it matches on text
- Test: `tests/GameVault.Tests/BackupTests.cs`, `tests/GameVault.App.Tests/MainWindowStartupTests.cs`

**Interfaces:**
- Consumes: `SaveDiscoveryService.SelectionChanged`, `RejectAsync`, `BackupService.BackUpAsync`, `ShowToast`, `PostNotice`.
- Produces: `sealed class SaveSourceMissingException(string gameName) : IOException`; backup failure text `No save folder found yet. Choose the folder in the game's Saves tab.`

- [ ] **Step 1: Failing tests:** `BackupTests.MissingSourceSaysSoPlainly` — game with no candidates → `BackupPhase.Failed`, message equals the text above and does not contain `not fully verified`. `MainWindowStartupTests` source-contract checks: `MainWindow.SaveFinder.cs` subscribes `discovery.SelectionChanged`, posts `NoticeKind.SaveFound`, calls `backups.BackUpAsync(`, and the startup pass `ScanGamesWithoutSavesAsync` is started after the window is shown (not in `OnStartup`).
- [ ] **Step 2: Run** filters `BackupTests` and `MainWindowStartupTests` → FAIL.
- [ ] **Step 3: Implement.**
  - NewlySelected → toast "<Game>: saves found" / "In <path> · backed up" (or "· backs up when you close the game" while it runs) with action **Not this folder** (calls `RejectAsync`, toast "Got it. Frostpunk won't use that folder."), notice `saves:<id>` with action **Show** (opens the game's Saves tab); run `BackUpAsync(id, true)` when `launches.GetState(id).IsActive` is false.
  - NewlyToConfirm → notice "Is this where <Game> saves?" detail = path, action **Yes, back up** (`ConfirmAsync` then `BackUpAsync`); stays until answered or resolved.
  - NothingFound → notice "Couldn't find <Game>'s saves" action **Choose folder…** (native folder picker via `NativePickers.Folder`, saved as a manual path with the same store call `GameEditor` uses, then `ScanAsync`).
  - `ScanGamesWithoutSavesAsync`: after the main window is shown, one game at a time with 2 s gaps, for non-archived games whose discovery has no selected source and automatic discovery on.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(saves): tell the player where saves were found and back them up (#111)`.

### Task 7: Saves tab actions and the setting

**Files:**
- Modify: `src/GameVault.App/SaveSourcesView.xaml(.cs)` (origin words, **Not this folder** / **Use this folder again**), `src/GameVault.App/MainWindow.xaml` (Backups settings page switch `WatchSavesSwitch`), `src/GameVault.App/MainWindow.Settings.cs`, `src/GameVault.Core/SettingsCatalog.cs`, `docs/save-location-database.md` → rename nothing, add a section in `docs/persistence.md` for `save-finds.json` and the marker, `CLAUDE.md` Saves bullet
- Test: `tests/GameVault.App.Tests` (`HoverCoverageTests`, settings catalog test), `tests/GameVault.Tests/SaveWatchSettingTests.cs`

**Interfaces:**
- Produces: `static class SaveWatchSetting { bool IsOn(ApplicationPaths p); void Set(ApplicationPaths p, bool on); }` in Infrastructure (marker `Data/save-watch-off.marker`); the watcher's `enabled` uses it.

- [ ] **Step 1: Failing tests:** `SaveWatchSettingTests` (default on, off writes the marker, on deletes it); catalog test finds `WatchSavesSwitch`; hover coverage passes with help "Notes which folders a game writes to while it runs, to find its saves. Nothing leaves your PC."; SaveSourcesView row text: origin `KnownRule` with evidence `Found automatically` shows "Found automatically", rejected shows "Not this game's saves".
- [ ] **Step 2: Run** the filters → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(saves): Saves tab actions and the watch setting (#111)`.

### Task 8: Verify, What's new, PR, test build

- [ ] Add the What's new line to the next version's entry in `Core/ReleaseNotes.cs` ("GameVault now finds where a game saves, backs it up and tells you."), after rebasing on `main` once PR #110 is merged.
- [ ] `pwsh -NoProfile -File ./scripts/verify.ps1` → passes; `pwsh -NoProfile -File ./scripts/audit-release-source.ps1` → passes.
- [ ] Push, open the PR (`Closes #111`, notes that it touches performance-sensitive code: the watcher), bind it.
- [ ] Test build with the owner's data: GameVault closed, then `pwsh -NoProfile -File work/deliver-build.ps1 -Name save-finder` → `D:\Projects\DesktopApps\GameVault\outputs\Builds\GameVault-save-finder`. Manual checks for the owner: Frostpunk and TEKKEN 8 found and backed up with a notice; GTA IV unchanged; Not this folder sticks; restoring Frostpunk works.
