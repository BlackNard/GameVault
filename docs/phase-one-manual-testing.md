# Phase one: deferred owner acceptance checklist

Status: **pending owner final pass**. These are owner-only checks deferred until the planned features are implemented. Automated tests, focused review, builds and GitHub CI remain the per-issue gates. Passing CI or the synthetic image encoder is not approval of real-game capture, audio, native dialogs, visual usability or first-version acceptance. This checklist does not authorize release publication or merging a PR.

Use the latest clean development package and record its commit/build, Windows version, GPU/driver, display arrangement/scaling and games tested. Preserve the portable Data/Profiles layout and stable profile IDs. Refresh the private vault before work resumes; create and verify the clean ZIP before importing the latest verified snapshot into only the local app directory. Close registered GameVault windows before a snapshot; never kill a game or GameVault to copy its data. Do not combine divergent histories. Keep personal libraries, keys and test evidence containing them out of Git and downloadable packages.

Record each item as pass, fail or not tested, with expected/actual behavior and reproduction steps. Report failures and requested changes as new GitHub issues; remove personal paths, keys and game content from shared evidence.

## Library, sessions and saved data

- [ ] Same-window navigation in Midnight and Crimson; keyboard focus, long paths/text, resizing and display scaling remain usable. Settings and selected profile survive restart.
- [ ] Add/edit games using each native executable/folder/save-file/cover picker; cancellation retains prior values. Validate missing/invalid paths and duplicate executables. Search, status filters and removed-profile recovery preserve profile IDs, artwork and statistics.
- [ ] Launch a real game, follow a child process where applicable, observe start/end time and accumulated playtime; relaunch and restart GameVault without duplicated sessions. Check externally launched games and stopping observation while a game stays open.
- [ ] Add/edit/persist notes and status. Confirm selection changes and closing during saves retain the latest completed edit without mixing profiles.
- [ ] Discover known saves and configure manual files/folders. Create a first save, exit, and inspect the dated verified backup; unchanged exit and manual repeat backup show the expected distinct status. Prior snapshots survive missing/locked/changed sources and copy failures. Removal preserves notes, statistics and backups.
- [ ] Close/reopen with active or interrupted work using normal app controls; recovery information and incomplete work are retained, completed sessions are not counted twice, and another instance cannot alter the same library.

## Capture, audio and shortcuts

Use an allowed test game and content the owner is comfortable recording. Check the actual output files and replay completed videos, not only the success message.

- [ ] Take a screenshot and record/stop a video for the tracked game window. Verify correct game/profile/session association, readable image, expected size/frame-rate settings, playable finalized MP4, media entries and persistence after restart.
- [ ] Test windowed and borderless modes, focus changes, minimizing/restoring, resizing, game exit and process/window replacement. Unsupported or inaccessible targets produce clear errors without silently capturing another desktop/window.
- [ ] Explicitly select a monitor fallback where needed; verify the selected monitor is used across multiple monitors and scaling. Without explicit fallback, failure does not silently choose a monitor.
- [ ] Record with audio disabled, then optional game audio enabled on supported Windows. Replay to confirm silence versus intended game audio; unrelated applications/microphone audio are excluded. Check unsupported process-audio capability gives a clear result.
- [ ] Exercise screenshot, recording and overlay shortcuts with the game focused; customize and persist bindings. Reserved/duplicate/occupied shortcuts give clear feedback and retain working bindings. Verify overlay visibility, theme readability and that it does not steal game input.
- [ ] Check encoder fallback/failure and target loss; incomplete recordings remain clearly incomplete and earlier completed media stays playable. Stopping and rapidly repeating commands does not overlap recordings.
- [ ] Close GameVault while recording, while starting/stopping capture and while the game exits; the app drains/finalizes work or reports incomplete work without hanging or falsely marking a partial video complete. Reopen and confirm recovery/media status.

## Optional online artwork

- [ ] Local cover selection works with no network or key. Restart retains the chosen cover; invalid or unsupported files produce useful errors.
- [ ] If desired, use the owner's personal provider key to search, distinguish same-title results, preview and select a cover. Never include that key in screenshots, logs, packages or Git. Verify settings do not reveal the saved key and allow its removal.
- [ ] Cancel searches and switch profiles during requests; a late response cannot change the wrong profile. Offline, invalid key, timeout and rate-limit results are readable and preserve existing covers. Online artwork is optional; record live-provider checks as not tested if no key is supplied.

## Installer, package and performance

- [ ] Inspect the clean ZIP before local seeding: no personal Data/Profiles or keys. Extract the full package to a writable location; confirm self-contained launch and correct version.
- [ ] On the owner's chosen test environment, exercise setup dialogs, cancellation, default/custom paths, shortcuts, upgrade and preserving uninstall. Reinstall preserves the intended library; removal of personal data requires the owner's explicit choice. Check a running app produces clear guidance. CI's disposable-runner lifecycle checks do not approve these dialogs.
- [ ] With representative 500/1000-game libraries, assess actual WPF cold launch, scrolling, search/filter responsiveness, artwork loading and memory during extended use in both themes. Include real covers/session histories and record machine details. The CI harness measures service reads and in-memory query timings only; it does not establish UI/frame-time or final performance acceptance.

Owner sign-off: build/commit ______; date ______; outstanding issues ______. Real-game validation, visual usability, performance and first-version acceptance remain pending until explicitly recorded by the owner.
