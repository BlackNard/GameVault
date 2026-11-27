# Shell: history, command palette, toasts, now playing

Part of the UI overhaul (ticket #90, phases 2 and 3). The logic lives in `GameVault.Core/ShellModels.cs` (tested without WPF); `MainWindow.Shell.cs` wires it to the window.

## What it does
- **Back and Forward** in the title bar (also Alt+Left, Alt+Right and the mouse back/forward buttons) across Library, Settings and each game page and tab. The breadcrumb next to the buttons shows where you are (for example `Library › TEKKEN 8 › Journal`). Going back to a game that no longer exists skips it. If a screen cannot be left (unsaved note, open editor, a running operation) the move is undone and the history stays as it was.
- **Command palette (Ctrl+K or the title bar search pill):** type to jump to a game or Settings, add a game, play a game, back up a game, or switch theme. Matching ranks prefix, word start, substring, then loose in-order matches. Up and Down move, Enter runs, Esc closes.
- **Toasts:** every message the app used to write to the bottom status line now appears as a toast at the bottom right. The first sentence is the title. Messages that describe a problem stay until dismissed; others disappear after about five seconds (eight with an action). The old status line is hidden but still the single place code writes to, so no caller changed.
- **Now playing chip:** while a game runs, the title bar shows its name and elapsed time; clicking it opens the game page.
- **Design tokens:** shape tokens (`AppRadius`, `AppRadiusSmall`) and the title bar button styles (`TitleIconButton`, `TitlePillButton`) are in `Styles.xaml`. Colours stay in `ThemePalette`.

## Notifications (the bell, issue #40)
- The bell in the title bar (left of the window buttons) shows a badge with the number of notices you have not looked at. Click it (or press Esc / click elsewhere to close) to see the list: title, detail, an action button, a dismiss button and **Clear all**.
- It holds notices that must not vanish like a toast: a newer GameVault version (Open release page), "GameVault did not close normally last time" (Save diagnostics) and backup-needs-attention alerts (Review). Logic: `GameVault.Core/NoticeCenter.cs`; window code: `MainWindow.Notices.cs`. Toasts are still used for passing messages.

## Library, game page, rail, settings and drawer (UI overhaul, same PR)
- **Library:** search box with placeholder, status chips (All, Playing, Completed, On hold, Not started) and a Removed chip, a cover-grid / compact-list switch, cover cards with a status badge, and hover actions (Play and a more-actions menu). Right-click a card or row for Play, Back up now, Open install folder, Edit, Journal, Remove. The chips drive the original (now hidden) status filter and removed checkbox, so filtering behaves as before. Artwork is still shown whole (Stretch Uniform).
- **Game page:** a hero banner (soft backdrop from the cover), large Play button, icon buttons for Edit, Open install folder, Back up now and More (launch settings, remove), a facts row (playtime, latest session) and underline tabs. The separate Back button is gone: use the title bar history or the breadcrumb.
- **Left rail:** icons, a Recent list (the last four games you opened this session) and highlighting of the current section.
- **Settings:** a section menu (Appearance, Backups, Background, Capture, Storage) that scrolls to the section.
- **Edit game, Launch settings and Add game** open in a right-hand drawer over the page instead of replacing it. The page behind is disabled while the drawer is open.

## Not in this change
Continue-playing row, saved filter views, settings search, and the global Backups and Captures pages from the prototype are not built yet.



## Manual checks (owner)
1. Open a game, switch tabs, press Back and Forward: each returns to the right game and tab; Back is disabled at the start.
2. Open a game's Journal, type a note without saving, press Back: you are asked about unsaved changes and cancelling keeps you where you were.
3. Ctrl+K opens the palette; type part of a game name, press Enter; try Settings, Add game, Play, Back up now; Esc closes.
4. Do something that reports a result (back up, save): a toast appears; a failure stays until dismissed.
5. Launch a game: the chip with its running time appears in the title bar.
6. Drag the title bar and use the window buttons: the new title bar controls must not block moving or maximizing the window.
