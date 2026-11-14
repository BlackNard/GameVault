# GameVault UI/UX redesign implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver the approved library-first redesign with same-window workflows, key-free covers, configurable backup storage and session-scoped capture shortcuts.
**Architecture:** Retain WPF/domain services; compose a main navigation shell, reusable custom templates and inline child controls. Add destination-aware backup storage with legacy location discovery; no silent data migration.
**Tech Stack:** C#/WPF .NET10, existing test/installer stack.
**Spec:** docs/superpowers/specs/2026-11-14-ui-ux-redesign-design.md (owner approved 2026-11-14).

## Global constraints
- Preserve Midnight default/Crimson, stable IDs, complete profile data and existing safety gates.
- No owner-PC UI/native-dialog/game/capture testing. No auto merge/publication.
- Default backups remain portable. External roots are explicit; missing drives fail visibly. Old backups remain available.
- OS file/folder dialogs, explicit browser/Explorer/media opening and in-game panel are intentional external surfaces; all other app flows stay within main window.
- Existing repository feature-branch workflow retained; no relocation of owner's source/build/vault directories.

## Review focus
- Back navigation with unsaved notes/editor/export work: prompt inline; never drop drafts.
- Changed/offline backup destinations: retain old snapshots, reject unsafe overlaps, preserve restore/retention safety.
- Several running games/shortcut conflicts: target actual saved session; never silently capture another game/display.
- Large library refresh: don't navigate to a game accidentally; preserve selection/filter/scroll and bounded cover resources.
- Theme/scaling and editor shutdown: consistent controls, asynchronous guards, no orphan dialogs or hanging deferred quit.

### Task 1: Destination-aware backups
**Files:** Core new backup destination models; Infrastructure/Saves/{BackupStorage,BackupHistoryService,RestoreStorage,RestoreService*}.cs as required; new BackupDestinationService; dedicated tests.
**Interfaces:** BackupDestinationService(ApplicationPaths); ReadAsync(Guid? gameId=null), SetDefaultAsync(string? root), SetGameAsync(Guid,string?root), ResolveCurrentAsync(Guid). Read returns default/override/effective directory and historical roots; provide exact final contract before app integration.
- [x] Write failing tests for custom root, per-game override, portable default, legacy history after destination changes, offline/unsafe roots, restore across locations and shared-store coordination.
- [x] Implement persisted versioned mappings and validated root resolution under stable IDs, retaining historical locations and existing manifest/hash/restore/retention boundaries. No moving/deleting owner backups.
- [x] Run focused backup/restore suites and commit only owned source/tests when requested.

### Task 2: Inline forms and key-free covers
**Files:** GameEditor.*, LaunchSettingsWindow.*, NoteExportWindow.*, NotesView*.cs, CoverSearchView.* as needed; new focused helpers/tests.
**Interfaces:** Retain class/constructor names, convert dialog classes to UserControl. Expose IsBusy, Completed event EventHandler<bool> (true saved/export finished, false cancelled), Task<bool> CanLeaveAsync(). Root handles mounting/unmounting and awaits completion; no Owner/ShowDialog/DialogResult calls remain. Notes export mounts inline inside NotesView and restores draft/read view on return.
- [x] Test title-only browser query encoding, path rejection, drag/drop image validation and completion/navigation behavior using headless/fake boundaries.
- [x] Replace cover API-key panel with explicit browser search plus local image import/preview/drop. Retain pending cover and all form values on cancellation. Existing saved artwork unaffected.
- [x] Convert form/export dialogs and note/correction confirmations to inline workflows using InlinePrompts.ShowAsync(owner,message,title,buttons,defaultResult).
- [x] Run focused artwork/notes/app tests/build; no owner dialogs or actual browser launch.

### Task 3: Shared control system, chrome and inline prompts
**Files:** Styles.xaml, ThemePalette.cs; new WindowChromeBehavior.cs and InlinePrompts.cs; focused app tests.
**Interfaces:** WindowChromeBehavior.Apply(Window), root provides custom titlebar buttons via SystemCommands; InlinePrompts.ShowAsync(FrameworkElement,string,string,MessageBoxButton,MessageBoxResult=Cancel) -> Task<MessageBoxResult>. Attach within current Window root Grid, no Window creation. All prompt buttons labelled and keyboard accessible; serialize concurrent requests.
- [x] Implement consistent 40px controls/6px corners, custom ComboBox/CheckBox/RadioButton/Expander/scrollbar/list templates with full states and contrast. Don't overwrite PrimaryButton references consumed by existing screens.
- [x] Implement integrated window chrome retaining resize/snap/system commands; high contrast and theme resources preserved.
- [x] Build/test headlessly; owner validates actual visual/native behavior later.

### Task 4: Main shell and capture integration (parent)
**Files:** MainWindow.xaml and partials, CapturePreferences.cs, CaptureSettingsView.cs and tests.
- [x] Write failing navigation/session-shortcut tests where behavior is extracted into small pure helpers.
- [x] Rebuild library as cover grid without permanent detail pane. Explicit card activation opens dedicated game page; restore library state on back. Group details into Overview/Sessions & notes/Saves/Media and use one game-options menu.
- [x] Mount inline forms and existing notes/source/history/restore controls within the shell. Keep actions/confirmations inline and preserve in-flight work and deferred quit. Retain all existing functionality.
- [x] Wire global/per-game backup destination controls plus Open backup folder/Open selected backup location through validated service outputs.
- [x] Alt+Z default panel shortcut, customizable/conflict-aware, registered only during tracked sessions. Preserve existing saved shortcuts; screenshot/record bindings remain available with clear UI labels.
- [x] Add persistent settings layout and custom integrated titlebar; explicit OS pickers remain native.
- [x] Build, run full locked verification and installer/package guard tests; fix integration regressions.

### Task 5: Review, CI and handoff
- [x] Fresh focused whole-branch review of data safety/navigation/capture; fix important findings and verify.
- [ ] Create reviewable PR linked to redesign issue(s), push and await native encoder/installer lifecycle/clean-package CI.
- [ ] Clean publish/ZIP/setup verified before private Import into a new local test build. Latest private snapshot 20261114T145517760Z-d109e4de.
- [ ] Deliver PR, setup/build paths and updated manual checklist; owner approves/merges and tests. No automatic release publication.
