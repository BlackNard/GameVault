# Manual UI findings #44–#49 batch plan

**Goal:** Fix all owner-reported findings together without changing stored game data or existing workflows.
**Base:** d6dffeb (merged PR43). Branch codex/manual-ui-fixes-44-49.
**Requirements:** GitHub issues44–49 and screenshots in the project chat; owner explicitly authorized one combined fix batch.

## Constraints
- No owner-PC UI/window/native-dialog/real-game testing. Headless template/layout tests and fixture/CI checks only.
- Preserve profile IDs, sessions/notes/backups, Midnight/Crimson and same-window navigation.
- Private snapshot refreshed after confirmed Quit:20261114T162949549Z-b372da2b. Clean packages first, then import into fresh local build; preserve earlier builds.
- One reviewable PR; owner merges. No release publication.

## Work
1. Parent: #44 literal corrupted encoded glyphs in Styles.xaml are replaced by geometry-based indicators for expanders and any affected shared menu/check indicators. Add headless tests for expanded/collapsed geometry and absence of malformed glyph literals. #45 fit complete covers proportionally in library and hero, preserve normalization/cache. #47 constrain Add game height40/centered to prevent DockPanel stretch. #49 remove Advanced privacy/legacy key button and unused handler while keeping Local profile storage. Automated headless regressions before fixes where behavior warrants.
2. Caption worker owns WindowChromeBehavior.cs/new caption helper and focused tests: investigate #46 actual native/client hit-test/event chain; fix all caption actions/hit areas without duplicating clicks or losing drag/resize/close-to-tray/accessibility/snap. Parent integrates named control and geometry visuals in MainWindow.xaml from worker's exact contract.
3. Sessions worker owns NotesView.xaml (and minimal NotesView*.cs if needed), focused tests: #48 compact consistent header/status/export/navigation/sort layout; remove longest-session border while retaining selectable navigation and keyboard access. Preserve existing names/handlers and note/correction/export behavior.
4. Parent integrates, runs locked full verify, packaging safeguards and focused fresh review. Resolve important findings; compile setup, open PR closing44–49, await exact-head green CI. Verify ZIP/setup before private Import into fresh local test folder; return PR/build/checklist paths.

## Review focus
Caption coordinate scaling/nonclient routing, no global shared-style sizing regression, whole-image aspect ratio, longest-session selection accessibility, missing legacy controls/resource references, preservation of notes/session data and active-work navigation guards.
