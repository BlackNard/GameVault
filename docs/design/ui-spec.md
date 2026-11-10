# GameVault interface design — issue #3

Status: revised design approved by the owner on 2026-11-11 and merged through PR #20. The owner retains manual UI testing. The design/prototype do not themselves implement production game operations.

## Direction

A quiet dark desktop library, with game covers as the main visual feature. Keep Play and save protection visible. Avoid store promotions, social feeds, decorative dashboards, and continuously animated backgrounds. Per owner feedback, the original blue palette remains the default theme, named Midnight. An optional Crimson theme adds red and near-black colors. This revised direction was approved on 2026-11-11.

The prototype is `prototype.html` in this directory. Open it directly in a modern browser; no server or dependencies are required. All records and operations are simulations held in memory and reset on refresh. It does not launch games or read/write save files.

## Design tokens

| Role | Midnight (default) | Crimson (optional) |
|---|---|---|
| Background | `#131b26` | `#120e11` |
| Surface | `#1c2838` | `#21171d` |
| Raised surface | `#26364a` | `#35232c` |
| Primary text | `#edf2f8` | `#f8eef2` |
| Secondary text | `#b5c3d6` | `#d0bbc5` |
| Primary action | `#b6ceff`, text `#131b26` | `#b62b43`, text `#fff6f8` |
| Focus/link accent | `#b6ceff` | `#ffb2c0` |
| Protected/success | `#a8dfcc` | `#b4ddc7` |
| Attention/error | `#ffc6ab` | `#ffc5aa` |

Expose the choice in Settings > Appearance and comfort > Color theme. Apply immediately across navigation, forms/dialogs, primary actions, focus indicators, and charts; preserve library selection, filters, and active operations. Artwork retains its original colors. Persist the real app's choice in global local settings and restore it at startup; new installations default to Midnight. The offline prototype resets its theme on refresh alongside its other example state.

Use Segoe UI Variable with Segoe UI fallback, tabular figures for time, 32/24/18/14 px type hierarchy, and 8 px spacing steps. Controls have 8 px corners; cover artwork has 12 px corners; avoid shadows except to separate a modal from the background. Buttons have at least 36 px height. Secondary text remains readable rather than using low-opacity gray.

## Structure and navigation

Left navigation: Library, Activity, Settings. The library includes search, All/Playing/Completed/On hold/Not started filters, and Add game. Selecting a cover opens a game detail view. Back returns to the same search/filter/scroll position.

Game details contain cover art/title, status selector, Play/current session, total time/last played, and Overview/Backups tabs. Overview shows session history and save protection status. Backups shows newest-first date/size/pin rows, Back up now, and Restore. Keep the last successful backup visible even after a later failure.

Activity shows daily/weekly/monthly summaries and an accessible labeled chart. Settings covers the local profile root, default retention (20 plus pinned), keep everything, and reduced motion. Per-game settings expose executable, working directory, save sources, and retention override. Global settings do not silently override an existing per-game choice.

Default real-app window: 1200 × 800 device-independent pixels; minimum 900 × 600. At narrower prototype viewports, navigation moves above content and cards collapse into fewer columns. At high Windows display scaling the real app must reflow/scroll and keep every action reachable; it must not force a larger-than-screen window. The earlier foundation window dimensions are a temporary baseline.

## Key flows

1. **Add game:** Name and executable are required. Place a Browse button beside executable, working directory, and save location. The Windows app opens a native executable-file picker (initial filter *.exe) and native folder pickers for working directory/save folders. Save configuration must also support selecting individual save files as required by #9. Display the full selected path, permit manual editing/paste, and preserve the current value and other form entries on Cancel or an error. Selecting the executable defaults the working directory to its parent folder unless the user explicitly customized it. Missing/inaccessible paths receive inline feedback. Save location is optional so a game can be launched before saves exist. On success, show the game details with Not started status and Save location needed if unconfigured. No store login.
2. **Play:** Acknowledge input immediately, show Starting, and only mark Running/change Not started to Playing after process confirmation. Running shows elapsed session time and blocks duplicate launches. The prototype's End demo session substitutes for closing a real game; the real app observes exit rather than claiming its button can save/close a game safely.
3. **Backup:** After observed exit, show Backing up without blocking navigation. On success show date/time and changed-save outcome. On failure retain the prior success and explain the specific retry action. Do not promise protection before a verified backup exists.
4. **Restore:** Require a configured save destination; if it was cleared while old backups remain, direct the player to Edit game before allowing restore. Show backup date, destination, and the requirement that the game is stopped. Explain that current saves will be preserved first. Confirm with Restore backup; preservation failure aborts replacement. Success identifies the retained recovery snapshot in the backup list. Block restore during a running game or conflicting operation.
5. **Edit/remove:** Edit paths without changing the stable profile identity. Removal first preserves the profile; any permanent deletion is a separate later explicit operation. The design prototype supports editing; removal/recovery implementation belongs to its feature ticket.

## State and error contract

| State | What the player sees and can do |
|---|---|
| Empty library | Add your first game; no invented stats or protected status |
| Loading | Static loading message; controls that need data unavailable; no endless shimmer |
| Missing executable | Game not found, Edit game action, Play disabled; statistics/backups retained |
| Save path unconfigured | Save location needed, Edit game action; Play still available |
| Running | Elapsed session and running label; no duplicate Play, restore, or manual backup |
| Backing up | Clear progress/state; Play/restore/duplicate backup disabled for that game |
| Backup failed | Readable failure reason, retry, and unchanged last successful backup |
| Restore blocked | Stop the game first, with no destructive action |
| No backups | Explain that no verified backup exists; offer configuration or backup |

The prototype includes a Preview state selector to explore these situations; it is a design-review control, not part of the shipped interface. Its Browse buttons open a second modal with example files/folders; these simulate the final Windows browsing flow without accessing the real device. Closing the picker returns focus to its Browse button and retains unsaved form values. The production app must use actual native Windows dialogs rather than the example picker or a type-only path field.

## Interaction, accessibility, and smoothness

- Use actual labeled buttons/inputs, visible keyboard focus, predictable tab order, and accessible names for icon-only controls. Convey protected/warning/running states in words as well as colors.
- Modal dialogs contain focus; Escape cancels; closing restores focus to the opener. Restore is never the default accidental action from an unrelated Enter key.
- Keep content/navigation selection stable after asynchronous updates. Use polite status announcements for completion and an alert for actionable failures. Do not steal foreground focus for backup notifications.
- Only animate user-triggered transitions, normally 120–160 ms opacity or color. Respect Windows/browser reduced motion and the explicit setting. No timer when idle; only update an active session once per second.
- In WPF, use theme resources and reusable templates, async commands, background file services, virtualized lists, and lazy artwork loading. This HTML prototype is a design reference, not a proposed embedded browser stack.
- Targets and resource checks remain in #18; visual appearance alone is not evidence of performance.

## Review and completion

Owner review is required before production interface implementation. Manual checks are intentionally assigned to the owner in `manual-review.md`: appearance, resizing/scaling, keyboard/dialog behavior, and the simulated flows. Automated checks cover prototype syntax and structural invariants; they do not claim visual approval or native Windows accessibility compliance.

The owner approved issue #3's design. Actual WPF screenshots and the final scaling matrix will be reviewed by the owner as those screens are implemented in later tickets.
