# Issue #6 — Game launching and process lifetime

> Execute inline with superpowers:executing-plans; one focused fresh-context final review. Continue in the owner's selected project folder on a separate feature branch.

**Goal:** Play launches local games and reports Starting, Running, Stopped, or Failed, observing the actual configured game process without terminating it.

**Spec:** Issue #6 and docs/design/ui-spec.md. First-launch saves remain required by docs/save-discovery-requirements.md, implemented in #9/#10; playtime collection/recovery is #7.

**Architecture:** Infrastructure LaunchService coordinates profile validation and process lifetimes through an injectable IGameProcessSystem. The Windows implementation starts an absolute executable directly with its configured working folder, uses a private Windows process group plus exact image paths/creation times to find a configured descendant target, and awaits retained process handles. An optional versioned launch.json sidecar stores a separate target executable without changing schema-1 profiles. WPF exposes Play and a dedicated launch-settings dialog.

**Global constraints:** No game/UI automation; only headless owned process fixtures. No Kill/CloseMainWindow/job-object termination. No shell commands assembled from game paths. No invented playtime or save-protection claims. Midnight/Crimson preserved. Existing corrupt/future-schema data stays read-only. Closing stops observation and waits for pending app writes while leaving games running.

**Design decisions:** A separate target defaults to the launch executable; configure it only for launcher/child handoff. Match full paths, process identity, private process-group membership, and creation time, not executable name alone. Fail clearly if a target is not found within 30 seconds; do not kill the launched process. Once attached, await its actual exit even if the starter exits earlier. Reject an already-running target rather than attach unrelated external sessions. A timeout may leave a real game running; retry must recheck process existence. Status changes Not started to Playing only after an observed running process; other manual statuses stay unchanged.

## Task 1 — Tested launch orchestration and configuration

Files: Core/LaunchModels.cs; Infrastructure/Launching/{LaunchService,LaunchSettingsStore,ProcessContracts}.cs; Library/LibraryService.cs; Tests/LaunchTests.cs.

- [x] Write API stubs and failing tests for states, duplicate concurrent clicks, missing/invalid executable and working folder, actual target after starter exits, old/unrelated/PID-reused candidates, timeout, status preservation, and cancellation without terminating processes.
- [x] Implement atomic per-game/per-path reservations and background launch/monitor work. Persist only the first-start status transition; keep tracking if that write fails, with a visible warning.
- [x] Use existing JsonDocuments protections for optional launch.json. Reject unusable settings and archived/read-only profiles; allow native target repair. Test round-trip and malformed/future data preservation.

## Task 2 — Windows backend and real headless integration

Files: Infrastructure/Launching/WindowsGameProcessSystem.cs; tests/Fixtures/GameProcessFixture; tests/GameVault.App.Tests/ProcessIntegrationTests.cs; solution/verification as necessary.

- [x] Verify documented .NET/Win32 APIs using official Microsoft sources.
- [x] Start and observe disposable windowless fixtures: spaced paths/custom working folder, launcher exits while child remains, and stop-observing leaves the fixture alive. Native snapshots use safe handles and retain root/target identities.
- [x] Run baseline and new tests; no real games or desktop windows launched.

## Task 3 — WPF integration, review, delivery

Files: App/MainWindow.xaml(.cs), LaunchSettingsWindow.xaml(.cs), LibraryItemView.cs; docs/design/launch-manual-tests.md.

- [x] Add Play with readable state and duplicate blocking; keep edit/remove/launch-settings disabled for active games. Keep filters and navigation usable.
- [x] Native executable picker for optional actual game target; independent settings writes avoid partial profile/launch saves. Surface read-only/missing target failures without crashing.
- [x] On close, cancel monitoring, await pending operations, detach handles, release app workspace; never terminate games.
- [ ] Focused final code review, full locked Release build/tests/self-contained publish, new issue-6 package, draft PR and passing Windows CI. Owner performs all manual UI and real-game checks; no automatic merge.

## Execution ledger

- Main merge verified as 9a95434. Clean feature/issue-6-launching branch created in D:\Projects\DesktopApps\GameVault.
- Ruling: maintain the owner's established in-place feature-branch workflow rather than introduce another project folder after relocation.
- Native API references: [ProcessStartInfo](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0), [WaitForExitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-10.0), [PROCESSENTRY32](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32), [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew).

- Launch orchestration: 10 initial expected NotImplemented failures observed, followed by passing implementation; expanded to 15 orchestration/configuration tests.
- Native backend: headless spaced-path/custom-working-directory, actual exit, child handoff, and detach-without-kill tests pass.
- Final review identified disappearing ancestry through exited intermediates. A three-process same-image handoff regression failed as predicted, then passed after the process-group fix. Follow-up review found no further material issues.
- Ruling: start with CreateProcessW suspended, attach a private unrestricted Windows job, then resume. No termination flags are set. This preserves descendant attribution even when intermediate helpers exit before a snapshot. Observation failure resumes the program; disposal leaves running processes alive. See https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects .
- Added fast intermediate-helper regression and tolerated exited-process path-query races. Full suite now 75 passing tests. Final scripts/verify.ps1 also passed locked restore, Release build with zero warnings/errors, all tests, and self-contained win-x64 publish verification.
- WPF launch-state updates read only the affected profile, avoiding storage initialization during close; service shutdown awaits pending app writes before the workspace lease is released.
- No real game or desktop UI was launched. Owner review remains pending; no merge or release is authorized by passing automated tests alone.
- Windows PR CI exposed a readiness-marker race in the test fixture: File.Exists could precede writer closure. The fixture now writes/closes ready.tmp and atomically publishes ready.txt. This changes test synchronization only; production launch code and the packaged app are unchanged.
