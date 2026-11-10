# Manual interface review

Open [prototype.html](prototype.html) locally in your browser. All values are examples and reset on refresh. This is the interface proposal; no game is launched and no save file is touched.

Please review:

- [ ] Overall appearance: readable text, comfortable spacing, cover size, colors, and clear Play/backup status.
- [ ] Search and filter the library; open a game and return without losing the filter.
- [ ] Add a game using sample paths, then edit it. Required fields explain what is missing.
- [ ] Start a demo session, switch screens, and end it. Check the session and simulated backup result.
- [ ] Open Backups, pin a backup, and walk through Restore; cancel once and confirm once.
- [ ] Try Preview state options for empty/loading/missing executable/no saves/running/backup progress/failure.
- [ ] Inspect Activity and Settings. Change the retention/reduced-motion options.
- [ ] Use Tab/Shift+Tab/Enter/Space and Escape. Check focus visibility, modal focus containment, and focus return.
- [ ] Resize the window and try browser zoom 100%, 125%, 150%, and 200%. Every action should remain reachable.

Please report any confusing action, clipped text, uncomfortable colors, or behavior you would change. Approval is for this design direction; native WPF scaling/accessibility will need another check once implemented.
