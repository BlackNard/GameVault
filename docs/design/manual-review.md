# Manual interface review

Open [prototype.html](prototype.html) locally in your browser. All values are examples and reset on refresh. This is the interface proposal; no game is launched and no save file is touched.

Please review:

- [ ] Overall appearance: readable text, comfortable spacing, cover size, colors, and clear Play/backup status.
- [ ] Search and filter the library; open a game and return without losing the filter.
- [ ] Add a game, use Browse for its executable and working/save folders, and then edit it. These preview pickers offer sample paths; the Windows app will use actual native dialogs.
- [ ] Cancel a picker and confirm that typed fields are preserved. Choosing an executable should fill an empty/default working directory while preserving an explicitly customized directory.
- [ ] Start a demo session, switch screens, and end it. Check the session and simulated backup result.
- [ ] Open Backups, pin a backup, and walk through Restore; cancel once and confirm once.
- [ ] Try Preview state options for empty/loading/missing executable/no saves/running/backup progress/failure.
- [ ] Inspect Activity and Settings. Change the retention/reduced-motion options.
- [ ] In Settings > Appearance and comfort, switch from Midnight (the original default) to Crimson (red/black), inspect the library/dialogs/activity, and switch back. Refresh returns the preview to Midnight; the real app will remember the selected theme.
- [ ] Use Tab/Shift+Tab/Enter/Space and Escape. Check focus visibility, modal focus containment, and focus return.
- [ ] Resize the window and try browser zoom 100%, 125%, 150%, and 200%. Every action should remain reachable.

Please report any confusing action, clipped text, uncomfortable colors, or behavior you would change. Approval is for this design direction; native WPF scaling/accessibility will need another check once implemented.
