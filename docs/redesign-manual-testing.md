# Redesign manual acceptance

Build/commit: ______. All checks are pending owner verification. Automated checks do not establish visual, native or real-game acceptance.

- Library opens alone. Search/filter, cover grid and empty/removed states are readable. Select a game and use Back: search, filter, scroll and keyboard focus return to its card. Stress 500/1000 games with covers.
- Midnight/Crimson and high contrast: all controls have consistent sizes/states, readable focus, checked/selected/disabled/error states. Resize, scaling and long names/paths do not overlap or clip.
- Integrated titlebar: drag, resize, double-click, minimize/restore, close-to-tray, keyboard/system commands and Windows snap. Check maximize-hover Snap Layout on Windows11 and multiple monitors; narrow zones below minimum app size may be unavailable.
- Dedicated game pages: Play, Overview backup health, Sessions & notes, Saves, Media and the game-options menu are clear. A refresh or session tick does not switch pages or steal focus.
- Add/edit and launch settings stay in main window. Browse still opens the OS picker. Cancel preserves saved data; unsaved changes require an inline decision. Quit during a save waits safely.
- Notes/reviews, time corrections, read/edit and combined/separate Markdown/PDF export remain intact. Export stays in the same window, preserves drafts on return, and uses OS destination pickers only on explicit export.
- Cover picker has no required key. Browser search sends only the typed game title. Browse/drop PNG/JPEG, inspect preview, confirm or cancel. Saved cover works offline and after restart. Existing cover stays on errors/cancellation. Advanced privacy offers removal of an old provider key.
- Default/per-game backup destinations persist. Null/reset returns to portable/inherited storage. Existing snapshots remain listed/restorable at old destinations. Open backup folder and Open selected backup folder go to the actual location. No data moves automatically.
- Disconnect a configured backup disk: backup/restore/retention fail safely with guidance, never silently fall back. Reconnect and retry. Offline/corrupt historical stores conservatively block mutations until resolved, so earlier snapshots cannot be ignored by cleanup.
- Capture shortcuts activate only for tracked running sessions, then release at session end. New settings default to Alt+Z for the panel; existing saved bindings remain unchanged. Change bindings, test occupied shortcuts (including Nvidia), and ensure no silent reassignment. Screenshot/record controls target the actual game session. Audio/monitor capture remain explicit; finalize on exit/quit and preserve incomplete output.

Continue the broader [phase-one functionality checklist](phase-one-manual-testing.md) after these redesign-specific checks. Report bugs/changes as new issues. Preserve the private test-data handoff; do not upload personal profiles or captures.
