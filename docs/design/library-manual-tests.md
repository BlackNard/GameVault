# Issue #5 — owner manual checks

This build implements local game management, not launching games, session tracking, or save-file backups. Save locations are configuration only. Automated tests do not claim visual or native-dialog approval.

Use the **issue-5-library** development package. The earlier settings-only package is not suitable for these checks. Extract the entire ZIP to a writable folder and run GameVault.exe alongside its supporting files. Do not extract it over a running app.

1. **First launch:** Library and Settings navigation, Add game, search, status filter, and Removed profiles are visible. Empty library contains no invented statistics. Check Midnight and Crimson, readable focus and text, resizing, and Windows display scaling.
2. **Add a real game:** Add game > executable Browse opens the native Windows file picker initially filtered to .exe. Select a game such as Resident Evil 2; the working folder defaults to its parent. Give it a name, leave optional save paths empty, and save. The game appears with Not started status and no recorded playtime.
3. **Pickers and cancellation:** Browse working folder, save folder, individual save file, and PNG/JPEG cover. Full paths remain visible and editable (cover is selected through Browse). Cancel each picker and confirm existing form values remain. Customize the working folder, browse another executable, and confirm the custom working folder is kept. Cancel the editor without saving and confirm no profile is added/changed.
4. **Validation:** Blank name, missing executable, non-.exe pasted path, invalid working folder, and duplicate executable produce readable errors while retaining form values. A damaged cover image shows an error instead of closing the app.
5. **Persistence and editing:** Add a cover, change the title/status/save locations, close and reopen. Confirm values and artwork persist. Search by name and filter by each relevant status. Switch themes and navigation while filtered; selection and filters remain. Editing retains filters; adding a new game resets filters so it is visible.
6. **Removal and recovery:** Cancel a removal first, then confirm it. The entry disappears from the active library. Check Removed profiles, select it, and Restore to library. Uncheck Removed profiles; the same entry and artwork return. While removed, trying to add its executable again directs you to restore its retained profile.
7. **Missing game:** Use a disposable copy of an executable for this check; do not move or rename an installed game's files. Add the copy, close GameVault, rename the copy, and reopen. The entry reports Game not found. Editing its name/status still works. Browse to the renamed .exe and repair its location.
8. **Keyboard and pending saves:** Tab through controls; Escape cancels the editor when idle. Rapidly clicking Save must not create duplicates. Closing during a save waits for it to finish, retaining the data and the app's single-instance lock.

Report the failed step, expected/actual behavior, and screenshot if useful. Do not mark this issue complete until the owner has reviewed the actual build.
