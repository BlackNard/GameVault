# Shell: history, command palette, toasts, now playing

Part of the UI overhaul (ticket #90, phases 2 and 3). The logic lives in `GameVault.Core/ShellModels.cs` (tested without WPF); `MainWindow.Shell.cs` wires it to the window.

## What it does
- **Back and Forward** in the title bar (also Alt+Left, Alt+Right and the mouse back/forward buttons) across Library, Settings and each game page and tab. The breadcrumb next to the buttons shows where you are (for example `Library › TEKKEN 8 › Sessions & notes`). Going back to a game that no longer exists skips it. If a screen cannot be left (unsaved note, open editor, a running operation) the move is undone and the history stays as it was.
- **Command palette (Ctrl+K or the title bar search pill):** type to jump to a game or Settings, add a game, play a game, back up a game, or switch theme. Matching ranks prefix, word start, substring, then loose in-order matches. Up and Down move, Enter runs, Esc closes.
- **Toasts:** every message the app used to write to the bottom status line now appears as a toast at the bottom right. The first sentence is the title. Messages that describe a problem stay until dismissed; others disappear after about five seconds (eight with an action). The old status line is hidden but still the single place code writes to, so no caller changed.
- **Now playing chip:** while a game runs, the title bar shows its name and elapsed time; clicking it opens the game page.
- **Design tokens:** shape tokens (`AppRadius`, `AppRadiusSmall`) and the title bar button styles (`TitleIconButton`, `TitlePillButton`) are in `Styles.xaml`. Colours stay in `ThemePalette`.

## Not in this change
Library cards and list, game page hero, drawer for editing, settings sub-navigation and the other screens follow in later phases.

## Manual checks (owner)
1. Open a game, switch tabs, press Back and Forward: each returns to the right game and tab; Back is disabled at the start.
2. Open Sessions & notes, type a note without saving, press Back: you are asked about unsaved changes and cancelling keeps you where you were.
3. Ctrl+K opens the palette; type part of a game name, press Enter; try Settings, Add game, Play, Back up now; Esc closes.
4. Do something that reports a result (back up, save): a toast appears; a failure stays until dismissed.
5. Launch a game: the chip with its running time appears in the title bar.
6. Drag the title bar and use the window buttons: the new title bar controls must not block moving or maximizing the window.
