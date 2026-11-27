# Save finder: find any game's saves and back them up

Date: 2026-11-27. Owner request: "smart search across the PC to find the save data for a game … track any game's save data location". Backups and restores are a core promise of GameVault and today they often fail before they start.

## Why (evidence from the owner's installed GameVault, 2026-11-27)

None of the three games in the owner's library has a backup:

| Game | What happened | Root cause |
|---|---|---|
| Frostpunk (`D:\Games\Frostpunk`) | "Backup could not finish … No verified source yet" | Saves are in `AppData\Roaming\HOODLUM\323190\remote`. Discovery only tries 10 folders built from the game's name; the save-location database lists only the official folders (`11bitstudios\Frostpunk\Default\saves`, Steam `userdata`). Nothing ever looks where the game actually writes. |
| TEKKEN 8 (Steam) | No backup, no prompt | `AppData\Local\TEKKEN 8\Saved\SaveGames` was found, readable and full of saves, but it is only a *Suggestion*: it waits for a confirmation nobody is asked for. |
| GTA IV | Works | A known rule (Goldberg SocialClub folder) is selected. |

Two failures: the search is too narrow, and finds that are not certain are never used or shown.

## Goals

1. Find the save folder of almost any Windows game without the player typing a path, including games set up outside stores (Steam replacement setups that store saves under the Steam app number).
2. When GameVault is confident, select the folder and back it up straight away, and **tell the player** where it found the saves, with a one-click "Not this folder" (owner decision, 2026-11-27).
3. When it is not confident, ask in a notice the player cannot miss (not a silent Suggestion).
4. Plain messages: "Couldn't find Frostpunk's saves yet. Choose the folder" instead of the current failure text.

## Non-goals

- Saves kept in the registry or in the cloud only.
- A whole-disk scan, or watching other drives' entire contents.
- Attributing writes to a process (needs administrator rights or kernel tracing). The watcher sees file changes, not who made them; scoring handles that.
- Changing backup storage, retention or restore.

## Anti-goals (a failure even if it "works")

- Silently backing up unrelated personal files (requirement 8 in `save-discovery-requirements.md`): every automatic selection is announced and can be undone.
- Slowing the PC or GameVault: watching runs only while a game GameVault started is running; searches are bounded in folders, entries and time.
- Sending anything anywhere: everything stays on the PC.
- Older GameVault versions unable to read profiles: no new fields or enum values in existing documents.

## Approaches considered

1. **Watch while playing only.** Watch the usual user folders and the game folder while the game runs, then pick the folder where save-like files were written. Finds any layout, but only after a first session, and needs noise filtering.
2. **Smarter search before playing only.** Use the game's identity (Steam app number from `steam_appid.txt` and similar files, publisher and product from the `.exe`) to check more folders, plus a bounded name search. Works before the first session, but misses layouts nobody predicted.
3. **Both, with one score (chosen).** The search runs whenever discovery runs today (add, play start, during play, exit); the watcher adds evidence during sessions. Every candidate gets a confidence; high confidence is selected and backed up with a notice, medium is asked about.

A full-disk "recently modified files" scan was dropped: slow, and it reads far more of the player's files than needed.

## Design

### 1. Game identity (`Core/GameSaveIdentity`, Infrastructure reader)

Read once per discovery, from the install folder (depth ≤ 2, small files only, values never logged):
- Steam app number: `steam_appid.txt`; `AppId=` in `hlm.ini`, `steam_emu.ini`, `steam_settings/steam_appid.txt`, `cream_api.ini`-style files; the Steam path (existing `StoreGameLauncher`).
- GOG id: `goggame-<id>.info`. Epic: `.egstore` manifest app name.
- `.exe` version info: `CompanyName`, `ProductName`, `FileDescription` (e.g. "11 bit studios", "Frostpunk").
- Names: profile name, install folder name, exe name (already used), each normalised by `GameTitles` rules so "TEKKEN 8" / "Tekken8" match.

### 2. Before-play search (`SaveFinder`, Infrastructure)

Candidate folders, all checked with the existing `SaveSourceInspector` (payload must exist):
- **Store number folders** for the app number N: `Steam\userdata\<account>\N\remote`, and the folders Steam replacement setups use: `%AppData%\{Goldberg SteamEmu Saves, GSE Saves, HOODLUM, EMPRESS, FLT, CODEX, RUNE, TENOKE, OnlineFix}\N` (and `\remote`), `%Public%\Documents\{Steam\CODEX, Steam\RUNE, OnlineFix, CPY_SAVES}\N`, `%ProgramData%\Steam\{RLD!, …}\N`. The list lives in one table with a test per entry; adding a new one is one line.
- **Database by number:** the Ludusavi lookup also uses the app number from step 1 (today it only gets one for real Steam installs), so Frostpunk's official folders are also tried.
- **Publisher/product folders:** `<root>\<Company>\<Product>` and `<root>\<Product>` for Roaming, Local, LocalLow, Documents, Documents\My Games, Saved Games.
- **Bounded name search:** in Roaming, Local, LocalLow, Documents, My Games, Saved Games, Public Documents: folders up to depth 3 whose normalised name equals a game name/product/app number; at most 2,000 folders visited per root and 1 s per discovery; noisy trees skipped (see exclusions).
- Existing rules (`SaveCandidateRules`, known titles) stay and keep priority.

### 3. Watch while playing (`SaveActivityWatcher`, Infrastructure)

- Starts when a game GameVault launched reaches Running, stops 30 s after its confirmed exit. Only one watcher set per running game; none when no game runs.
- `FileSystemWatcher` (created, changed, renamed; subfolders) on: Roaming, Local, LocalLow, Documents, Saved Games, Public Documents, Steam `userdata`, and the game's install folder. Large internal buffer; on overflow the session's evidence is marked partial, never wrong.
- The callback only records `(folder, file name, extension, time)` into a bounded in-memory set (≤ 5,000 events); no file is opened during play.
- Exclusions (one tested table): `Temp`, `Microsoft`, `Packages` (except the game's own package), `CrashDumps`, browsers, Discord, NVIDIA/AMD caches, shader caches, `D3DSCache`, GameVault's own folders, Steam outside `userdata\<account>\<N>`; extensions `.log`, `.tmp`, `.etl`, `.dmp`, `.lock`, `-journal`, `.pdb`, shader cache files.
- At exit the events become folder candidates: each written file is lifted to its **save root** = the narrowest folder that holds all writes in that branch, not above the first folder named after the game/product/app number, never a protected broad root (existing `SaveSourcePolicy`). E.g. writes in `HOODLUM\323190\remote\saves` and `…\remote\ProfileData.bin` give `HOODLUM\323190\remote`.
- The result is stored in a new per-game document `save-finds.json` (see Persistence) so it survives restarts and feeds later discoveries.

### 4. One score per candidate (`SaveCandidateScore`, Core, pure)

Points, summed:
- path segment equals the game name / product / app number: strong;
- written during a GameVault session: strong; written repeatedly across sessions: stronger;
- contains save-like files (`.sav`, `.save`, `.savegame`, `.dat`, `.bin`, Rockstar names, …): medium; only settings-like files (`.ini`, `.cfg`, `config.*`, `keybindings.*`): weak;
- from a known rule or the database: strong; from the name search only: weak;
- penalties: very large folder, folder also written while another game ran, broad/shared folder names (`Saves`, `Settings` directly under a root).
- **High** = selected automatically. **Medium** = ask. **Low** = listed in the Saves tab only.
- Several high candidates that do not overlap are all selected (a game may keep saves and profile in two places, within the existing limit of 32). Known rules and manual paths keep their current behaviour; a manual path always wins.

### 5. What the player sees

- **Found automatically:** toast "Frostpunk: saves found" / "In AppData\Roaming\HOODLUM\323190\remote · backed up", actions **Show** (opens the Saves tab) and **Not this folder**. The first backup runs at once when the game is not running, otherwise at exit as today. The bell keeps the notice.
- **Not sure:** toast "Is this where Frostpunk saves?" with the path, **Yes, back up** and **Choose folder…**; it stays in the bell until answered.
- **Nothing found:** after a session with no candidate, "Couldn't find Frostpunk's saves. Choose the folder" with **Choose folder…** (native picker; writes the manual path as Edit game does).
- Saves tab: each source shows how it was found ("Found while you played", "Steam app 323190 folder", "Database", "Chosen by you") and a **Not this folder** action.
- Backup status text: the current "Backup could not finish … Save sources are not fully verified/readable …" for a missing source becomes "No save folder found yet" with the same action; real storage or read errors keep their own wording.
- Settings > Backups: switch **Watch for saves while playing** (on by default), with hover help and a `SettingsCatalog` entry.

### 6. Persistence (no change to existing documents)

- New `Profiles/<game>/save-finds.json` (`schemaVersion` 1, strict): candidates seen while playing (path, files seen, sessions seen, last seen), and **rejected paths** from "Not this folder". Written atomically like the others.
- Automatic selections are stored in `save-discovery.json` with the existing origin `KnownRule` and evidence text such as "Found while you played"; no new enum value, so older versions still read it. The store validator already allows selected `KnownRule` sources.
- A rejected path is never selected again automatically and is shown as "Not this game's saves" in the Saves tab, with an undo.

### 7. When discovery runs

As today (add/edit, play start, every 10 s during play, exit) plus: once in the background, one game at a time after start-up, for games with no selected source (never blocking start-up), and when the watcher finishes a session.

## Testing

- Unit (Core): score rules, save-root lifting, exclusions, identity parsing (synthetic `steam_appid.txt`, `hlm.ini`, `goggame-*.info`, version info from a test exe).
- Infrastructure on synthetic temp folders: each store-number layout; Frostpunk-like and TEKKEN-like trees end up selected; a folder written only with settings stays "ask"; a rejected path stays unselected; overlapping finds collapse to one root; watcher with a fixture process writing into a temp root (no real user folders).
- Document tests: `save-finds.json` round trip, unknown field → read-only; `save-discovery.json` written by the new code is accepted by the current validator.
- App contract tests: toast actions wired, Settings entry and hover help (`HoverCoverageTests`, `SettingsCatalog` test).
- Performance: watcher idle cost with no game running is zero (no watchers exist); a synthetic burst of 10,000 events stays bounded.
- Manual (owner, test build with the owner's data): Frostpunk and TEKKEN 8 are found and backed up with a notice; GTA IV unchanged; "Not this folder" sticks; a restore of Frostpunk works.

## Left to the builder

Exact score weights and thresholds (tuned with the tests above), the toast wording within the owner's short-text rule, and how the bounded name search orders its roots.
