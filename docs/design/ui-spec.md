# GameVault interface design — issue #3

Status: proposed, awaiting owner review. The owner will perform manual UI testing. This issue delivers a design and an offline interactive prototype, not production game operations.

## Direction

A quiet dark desktop library, with game covers as the main visual feature. Keep Play and save protection visible. Avoid store promotions, social feeds, decorative dashboards, and continuously animated backgrounds. This visual direction is the designer's proposal, not yet an approved preference.

The prototype is `prototype.html` in this directory. Open it directly in a modern browser; no server or dependencies are required. All records and operations are simulations held in memory and reset on refresh. It does not launch games or read/write save files.

## Design tokens

| Role | Value |
|---|---|
| Background | `#131b26` |
| Surface | `#1c2838` |
| Raised surface | `#26364a` |
| Primary text | `#edf2f8` |
| Secondary text | `#b5c3d6` |
| Primary action | `#b6ceff`, dark text `#131b26` |
| Protected/success | `#a8dfcc` |
| Attention/error | `#ffc6ab` |

Use Segoe UI Variable with Segoe UI fallback, tabular figures for time, 32/24/18/14 px type hierarchy, and 8 px spacing steps. Controls have 8 px corners; cover artwork has 12 px corners; avoid shadows except to separate a modal from the background. Buttons have at least 36 px height. Secondary text remains readable rather than using low-opacity gray.

## Structure and navigation

Left navigation: Library, Activity, Settings. The library includes search, All/Playing/Completed/On hold/Not started filters, and Add game. Selecting a cover opens a game detail view. Back returns to the same search/filter/scroll position.

Game details contain cover art/title, status selector, Play/current session, total time/last played, and Overview/Backups tabs. Overview shows session history and save protection status. Backups shows newest-first date/size/pin rows, Back up now, and Restore. Keep the last successful backup visible even after a later failure.

Activity shows daily/weekly/monthly summaries and an accessible labeled chart. Settings covers the local profile root, default retention (20 plus pinned), keep everything, and reduced motion. Per-game settings expose executable, working directory, save sources, and retention override. Global settings do not silently override an existing per-game choice.

Default real-app window: 1200 × 800 device-independent pixels; minimum 900 × 600. At narrower prototype viewports, navigation moves above content and cards collapse into fewer columns. At high Windows display scaling the real app must reflow/scroll and keep every action reachable; it must not force a larger-than-screen window. The earlier foundation window dimensions are a temporary baseline.

## Key flows

1. **Add game:** Name and executable are required; working directory defaults to the executable's folder in the real app. Save location is optional so a game can be launched before saves exist. Validate inline; on success, show the game details with Not started status and Save location needed if unconfigured. No store login.
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

The prototype includes a Preview state selector to explore these situations; it is a design-review control, not part of the shipped interface.

## Interaction, accessibility, and smoothness

- Use actual labeled buttons/inputs, visible keyboard focus, predictable tab order, and accessible names for icon-only controls. Convey protected/warning/running states in words as well as colors.
- Modal dialogs contain focus; Escape cancels; closing restores focus to the opener. Restore is never the default accidental action from an unrelated Enter key.
- Keep content/navigation selection stable after asynchronous updates. Use polite status announcements for completion and an alert for actionable failures. Do not steal foreground focus for backup notifications.
- Only animate user-triggered transitions, normally 120–160 ms opacity or color. Respect Windows/browser reduced motion and the explicit setting. No timer when idle; only update an active session once per second.
- In WPF, use theme resources and reusable templates, async commands, background file services, virtualized lists, and lazy artwork loading. This HTML prototype is a design reference, not a proposed embedded browser stack.
- Targets and resource checks remain in #18; visual appearance alone is not evidence of performance.

## Review and completion

Owner review is required before production interface implementation. Manual checks are intentionally assigned to the owner in `manual-review.md`: appearance, resizing/scaling, keyboard/dialog behavior, and the simulated flows. Automated checks cover prototype syntax and structural invariants; they do not claim visual approval or native Windows accessibility compliance.

Issue #3 stays open until the owner approves the design. Actual WPF screenshots and the final scaling matrix will be reviewed by the owner as those screens are implemented in later tickets.
