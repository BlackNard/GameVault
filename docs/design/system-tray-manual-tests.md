# System tray lifetime - final manual checks deferred

GameVault now provides a Windows tray icon with Open GameVault, backup-alert review and Quit GameVault. Left-click opens the same main window. Close-to-tray defaults on and is explained in the sidebar and Settings; turning it off makes the window Close action quit instead. A permanently visible Quit GameVault button always fully exits. If native tray initialization fails, the app stays accessible in its window rather than hiding without a reopen path.

Hiding leaves the window/view state, application workspace lease, process observation, playtime sampling, discovery and post-exit backups alive. Reopening does not repeat startup initialization/recovery. This is background operation only while GameVault remains running; it is not tracking after the process has quit.

Quit resolves dirty-note and current-operation guards, warns for active/starting sessions, saves fresh checkpoints and leaves games open. If a current checkpoint is unavailable or cannot be saved, the user must explicitly choose Quit anyway; default is stay. Canceled quit preserves the current embedded page. A starting new session cannot reuse completed-session progress as a fresh checkpoint. Busy editors defer the explicit quit intent until finished. Shutdown stops observation and drains already-confirmed post-exit backup tasks before removing the tray icon and releasing the workspace. OS forced termination/shutdown/crash still relies on the latest successful checkpoint and existing interrupted-session policy.

Backup failures remain actionable through tray and sidebar alerts. The native notification is suppressed while any tracked game is active, deferred until play ends, deduplicated for the same failure until success, and limited to one notification every two minutes. A genuinely changed diagnostic can start a new notification episode. Preference disabling balloons keeps diagnostics/review controls available. Reviewing a balloon respects unsaved notes before navigating to the game's library details for Save sources or Back up now. Windows may suppress native notifications; persisted backup status remains authoritative.

Alert memory is bounded to128 game episodes in the running app and is not a replacement for persisted status. Tray menus show up to20 game actions plus an additional-review action. Queue and dedupe state reset on application restart. No Windows startup task, registry entry or background automation is installed.

Settings add optional CloseToTray and NotifyBackupFailures flags to schema1, default true for existing data. Preferences persist alongside the selected theme, with corrupt/future settings still protected. No new NuGet dependencies; NotifyIcon uses the bundled Windows desktop framework.

## Owner final pass - deferred until all planned issues finish
- Midnight and Crimson: settings/sidebar explanation is clear, checkbox choices persist, Quit remains accessible.
- Tray icon/tooltip/menu: left-click/Open restores the existing window, including after minimization, without creating another instance or restarting recovery.
- Close while playing: app hides, session timer and first/new save discovery continue, confirmed game exit creates its dated backup. Reopen and verify data.
- Turn close-to-tray off: Close follows full quit with active-session warnings. Choosing No leaves the same page and session intact.
- Quit with unsaved notes, a busy editor, restore or backup: existing guards still apply; completing a deferred operation continues explicit Quit rather than hiding. Check canceled quit leaves notes/backups/sources/restore pages visible.
- Active/starting game quit: fresh checkpoint, game stays open, app exits; reopening uses the existing interrupted-session recovery policy. A checkpoint-write failure produces the separate quit-anyway choice.
- Trigger a synthetic/manual backup failure: one actionable alert after play, no repeated balloon for identical retries, changed failures remain reviewable, notification-off keeps the menu/sidebar diagnostic.
- Repeated Open/Close, Explorer notification behavior and full Quit: no duplicate tray icons, no inaccessible hidden window, icon disappears only after shutdown drains work.

No manual tray/UI/native-dialog/real-game checks were performed per issue. Automated tests use lifecycle fakes, fake game processes, synthetic settings/checkpoint failures and real storage backed by temporary synthetic saves only.

Native behavior reference: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms