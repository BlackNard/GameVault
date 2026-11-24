# Manual acceptance checklist

Use the build folder you were given (private test data already imported). Quit GameVault with its **Quit** button when you finish so test data can be refreshed. Tick a box only after you saw it work. Anything that does not match: write the exact step and what you saw.

`verify.ps1` passing means the automated tests, publish and source audit passed. It does not replace this list.

## 0. Quick smoke test (5 minutes)

- [ ] GameVault opens maximized, Library shows your four games with covers.
- [ ] Left rail: Library, Backups, Captures; Settings and Quit at the bottom. Back/Forward arrows work.
- [ ] Ctrl+K opens the command palette; typing a game name finds it.
- [ ] Settings opens without the old left menu or search box.

## 1. Data backup and restore (#89)

Settings → **GameVault data**

- [ ] Untick *Include game backups and recorded media*, press **Back up GameVault data…**: it shows the number of files and size, then asks for a folder. A `GameVault-data-<date>.zip` appears and the toast has *Show in folder*.
- [ ] Repeat with the box ticked: the zip is bigger.
- [ ] Choosing a folder inside GameVault's own `Data` or `Profiles` folder is refused.
- [ ] Start a game, then press **Back up** or **Restore**: refused with a message (games running).
- [ ] **Restore from a backup…** with the zip: it shows the date and file count and warns that the library is replaced. After **Yes**, GameVault closes. Reopen it: your games are all there. A `Replaced-<time>` folder next to `GameVault.exe` holds the library from before.
- [ ] Pick a zip that is not a GameVault backup (any other zip): "cannot be used… nothing was changed".
- [ ] (Optional) Open a backup zip in Explorer, change a file inside, save, try to restore it: rejected, library untouched.

## 2. Diagnostics package (#86)

Settings → **Diagnostics**

- [ ] **Create diagnostics package…** first shows what is inside (environment, settings, counts, log) and your Windows/GPU/NVIDIA details.
- [ ] Choose a folder: a zip is created. Open it: no user name, no `C:\Users\<you>`, no game names (they appear as `<game 1>`…), no notes text.
- [ ] Tick *Include game names*: only then `games.txt` is added.
- [ ] `Logs\gamevault.log` exists next to `GameVault.exe` and has lines.

## 3. Tags, categories, sort and saved views (#88)

- [ ] Open a game → Overview → **Edit tags…**, enter `RPG, Co-op, Finish soon`, Save. Tags show on the page.
- [ ] The library now has a **Tags ▾** chip. Tick `RPG`: only tagged games remain. Tick a second tag: only games with both. **Clear tag filter** resets.
- [ ] **Sort: Name ▾** switches between Name, Recently played, Most played; order changes and makes sense.
- [ ] Set a status chip + a tag + a sort, then **Views ▾ → Save view**. The view appears as a quick button next to the chips. Change filters, click the button: search, status, tags and sort come back exactly.
- [ ] Settings → **Saved views**: rename a view (Enter), move it up/down, delete it. The quick buttons follow.
- [ ] Rename a game (Edit game), archive it (Remove from library) and restore it: its tags are still there.
- [ ] Close and reopen GameVault: tags, sort-less filters and saved views are still there.

## 4. Play actions, arguments and hooks (#87)

Game → ⋯ → **Launch settings**

- [ ] **Add play action**: name, program (.exe), arguments, optional working folder, administrator tick, *Play button runs this action*. Save.
- [ ] A **▾** button appears next to **Play** for that game; its menu lists *Game executable* and your actions.
- [ ] Play with an action that starts the game with an argument (for example a launcher or a second shortcut). The game starts; playtime is recorded as usual.
- [ ] Choose *Game executable* in the menu: the game itself starts, ignoring the default.
- [ ] With a mod manager as an action: it opens, you start the game from it, playtime is tracked once the game process appears.
- [ ] An action with *Run as administrator*: Windows asks for approval; cancelling it says so and nothing starts.
- [ ] **Before launch** hook with *Wait* ticked (for example a small program): it runs before the game; a failing or missing hook shows a toast, and the game still starts.
- [ ] **After exit** hook: runs after you close the game. A hook that exceeds its timeout is stopped and reported.
- [ ] A game with no actions or hooks behaves exactly as before (no ▾ button).
- [ ] Invalid input is refused with a clear message (action without a name, a relative path, two actions with one name).

## 5. Add games from Steam and Epic (#85)

Library → **Add from Steam / Epic**

- [ ] The drawer lists your installed Steam games (all library folders) and Epic games with store, size and a program path.
- [ ] No Steamworks/Proton/redistributable entries, no DLC.
- [ ] Games already in your library say *already in your library* and cannot be ticked. A game you removed earlier says it can be restored.
- [ ] Tick two new games, check or change the program (list, Browse…), **Add selected games**: both appear in the library, with the right executable; saves discovery runs.
- [ ] A game where no program was found shows a warning and cannot be added until you pick an .exe.

## 6. Game information (#83)

- [ ] A game's Overview has **About this game** with *Refresh info* and *Edit description…*.
- [ ] **Refresh info** on a Steam game fills description, release date, developer, publisher, genres (Steam store) and install size. On a non-Steam game only install size appears.
- [ ] **Edit description…**, save, then **Refresh info** again: your text is kept.
- [ ] Settings → **Game information**: untick a field (for example Genres) and refresh: it is not filled. *Only fill fields that are empty* on/off behaves as described.
- [ ] **Fetch missing info for all games** shows progress, can be cancelled with **Cancel**, and ends with a summary.
- [ ] With the PC offline: Refresh shows a short message, nothing crashes.

## 7. Save-location database (#84)

Settings → **Save-location database**

- [ ] The text explains source and licence (Ludusavi manifest, PCGamingWiki, CC BY-NC-SA) and nothing downloads on its own.
- [ ] **Download the save-location database** (about 18 MB): status shows the number of games and the date.
- [ ] Open a game whose saves GameVault could not find before (for example TEKKEN 8) → Saves → refresh discovery: new candidates mention *Save-location database* and need **Confirm**; nothing is selected automatically.
- [ ] Disconnect from the internet, restart GameVault: the status still shows the games and discovery still works.

## 8. Earlier UI work (overhaul parts one and two)

- [ ] Right-click a game card: menu has even spacing, no stray lines. The card's **⋯** button sits inside the cover border.
- [ ] Continue-playing row shows recently played games (when any played).
- [ ] **Backups** page: one row per game with *Back up now* and *Open game*.
- [ ] **Captures** page: grouped per game, newest first, *Open* on a capture and *Open game* on a group.
- [ ] Add game / Edit game open as a side drawer; the cover search takes the whole window.
- [ ] Add game button hover is readable (no black text).

## 8b. First-run welcome (issue #26)

Use a clean copy of the app (extract the clean ZIP from `outputs/Archives` into a new folder) so the library is empty.

- [ ] On first start the library shows "Welcome to GameVault" with four actions: Add a game, Import from Steam, Epic or GOG, Choose a theme, Get save locations (optional).
- [ ] Each action opens the right screen (add-game form, store import, theme picker) or starts the download.
- [ ] Tab/Enter reach every button; the focus ring is visible; it looks right in two or three themes.
- [ ] **Skip for now** hides it; restart the app: it stays hidden. Settings still load.
- [ ] Add a game: the welcome disappears; remove it again: the welcome does not come back (the library has had a game).
- [ ] With your own library: Ctrl+K, "Show the welcome" opens it as a drawer; Close works and an action runs after it closes.

## 8c. Startup splash (issue #28)

- [ ] Starting GameVault shows a small centred window with the logo, "GameVault" and "Loading your library…" right away, then the main window appears; the splash is not in the taskbar and does not take focus from what you were doing.
- [ ] It looks right in two or three themes (switch the theme, restart).
- [ ] It stays about a second even on a fast start, and no longer than the real start-up on a slow one.
- [ ] Settings > Background behaviour: untick "Show a small startup window…", restart: no splash. Tick it again: it returns.
- [ ] With Windows "Show animations" off or reduce motion set, the bar does not animate.
- [ ] Start a second copy while one is running: the splash closes before the "could not open its application folder" message.

## 8d. Drag and drop a game (issue #35)

- [ ] Drag a game's `.exe` from Explorer over the library: the library shows "Drop a game here to add it"; drop it: the add-game form opens with the path, name and working folder filled in (nothing was run).
- [ ] Drop a Desktop/Start-menu **shortcut** to a game: same result using the shortcut's target.
- [ ] Drop a game **folder** (for example `...\Steam\steamapps\common\SomeGame`): the main game exe is picked, not the installer, uninstaller or crash handler.
- [ ] Drop a game that is already in the library: you are told and taken to its page; if it is in Removed profiles you are told where to restore it.
- [ ] Drop a text file, a broken shortcut or an empty folder: a clear message, no form.
- [ ] Drop several items: the first game opens the form and the rest are listed in a toast.
- [ ] While a drawer, prompt or operation is open, dropping is refused with a short message. Works in two or three themes and on the welcome screen of an empty library.

## 8e. Update check and the notification bell (issues #38 and #40)

- [ ] The title bar has a bell left of the window buttons; with nothing to show it has no badge and its list says "Nothing needs your attention."
- [ ] Settings > Updates shows "You have version 0.1.0-preview.N" and a **Check now** button.
- [ ] With a newer published release (the "old simulated" test build, or lower `GameVaultPreview` in `Directory.Build.props`): **no toast**; the bell gets a badge "1"; opening it lists "GameVault ... is available" with **Open release page** (opens the GitHub release page; nothing is downloaded) and a dismiss button; the badge clears after opening, the entry stays.
- [ ] Dismiss it and restart: it does not come back for that version. With the current version: "You have the latest version" and no entry.
- [ ] Close and reopen the app: the notice is still in the bell even though the start-up check is skipped (at most every 12 hours).
- [ ] End the app from Task Manager and start it again: the bell shows "GameVault did not close normally last time" with **Save diagnostics**.
- [ ] A failed backup (for example a read-only backup folder) adds "Backup needs attention: <game>" with **Review**; reviewing it, or a later successful backup, removes it.
- [ ] Esc and a click outside close the list; keyboard Tab reaches the buttons; it looks right in two or three themes. Offline: a quiet start-up (no message), **Check now** explains.
- [ ] Untick "Check for new versions when GameVault starts": restart: no check runs (cached notices still show). Tick it again: it returns.

## 8f. Automatic updates (issue #42)

Needs an **installed** older copy whose version is lower than the newest published release (the release must carry its installer and `.sha256`). `v0.1.0-preview.2` has no updater: install the first release that has it by hand once; the next release is the first real test.

- [ ] Installed copy, automatic updates on (the default): a few seconds after start the Settings status shows "Downloading GameVault X in the background... N%"; when it finishes the bell gets "GameVault X is ready to install" (**Restart and update now**) and Settings says it installs when you close GameVault. No prompt appears.
- [ ] Close GameVault normally (tray Quit): the installer progress window appears and finishes; start GameVault again: it is the new version with all games, covers, playtime, notes, backups and settings intact; the bell has no update notice.
- [ ] **Restart and update now**: GameVault closes, the installer runs, and the new version opens by itself.
- [ ] With a game running: no download starts and nothing interrupts the game; the update installs only after GameVault is closed. A running game or backup still gets the usual prompt when you quit.
- [ ] GameVault left in the tray: at the next start the waiting update installs first, then the new version opens.
- [ ] Untick "Download and install updates automatically": a found update only shows in the bell with **Update now** and nothing downloads on its own; **Update now** downloads, closes, updates and reopens.
- [ ] Portable/ZIP or development copy: only **Open release page** is offered, nothing is downloaded.
- [ ] Offline during the download: a quiet message in Settings, the current version keeps working, it tries again later. (A tampered download cannot be tested by hand; the unit tests cover it.)

## 8g. Data safety during updates (issue #44)

Same setup as 8f. Use a library that has some games, saves and backups.

- [ ] After an automatic or **Restart and update now** update, the new version opens straight away with no data warning, and `%LOCALAPPDATA%\GameVault-Updates\Safety` holds a `GameVault-data-....zip` plus a `fingerprint-....txt` (at most two of each).
- [ ] `Logs/gamevault.log` says a safety copy was made before the update and that all files were checked and are unchanged after it.
- [ ] Delete or edit one game's `profile.json` between the safety copy and the first start of the new version (close GameVault, wait for the installer, then change the file before reopening): the new version shows "your library is not exactly as it was", names the file and the safety copy; **No** closes it, **Yes** opens it.
- [ ] Settings > Restore a data backup accepts that safety copy.
- [ ] With the system drive nearly full (or the `Safety` folder made read-only) **Update now** says the safety copy cannot be made and nothing was changed; the app keeps running.

## 9. Existing acceptance issues (cannot be closed by tests)

These need real games and real hardware. Tick them as you go; close the issue yourself when satisfied.

- [ ] **#1 First Windows version:** the whole product works for your real games (add, play, track time, back up, restore, notes, capture).
- [ ] **#3 Library, details and backup screens:** the three screens are clear and usable (sections 0, 3 and 8 above).
- [ ] **#14 Complete play-and-restore workflow:** play a real game for a few minutes, quit it, check the session is recorded and a backup was made, change/delete a save, **restore** the backup and confirm the game loads the restored save.
- [ ] **#18 Responsiveness and performance:** with all your games, scrolling the library, switching pages and typing in search stay smooth; no freezes during a recording or while a game runs.
- [ ] **#16 Installer: clean install, upgrade, uninstall keep data.** *Not done yet.* Needs your go-ahead to build the installer (release script). Then: install on a clean state, upgrade over an older version, uninstall; profiles and data must survive.
- [ ] **#17 Release packaging:** *Not done yet.* Depends on #16 and on your direction (release publication is yours).
