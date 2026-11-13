# System Tray Implementation Plan

Execute inline on codex/issue-13-system-tray, one final focused review. Owner delegated routine decisions, manual checks deferred.
Goal: Hide GameVault while timing/backups remain alive, and distinguish deliberate full quit.
Spec: docs/superpowers/specs/2026-11-13-system-tray-design.md
Stack: Existing .NET10/WPF plus Windows desktop NotifyIcon; no new NuGet packages.

- [x] RED lifetime tests: hiding never prepares/quits; unavailable tray never hides; canceled/coalesced quit. Implement independent lifecycle coordinator; GREEN.
- [x] RED alert policy tests: dedupe, play suppression, rate limits, disable/re-enable, success reset and bounds. Implement bounded notification state; GREEN.
- [x] RED optional setting compatibility/persistence and checkpoint/hidden-session backup tests. Implement settings and LaunchService checkpoint API; GREEN.
- [x] Thin native tray adapter, same-window settings/sidebar/reopen/alert actions, explicit active-session quit/checkpoint warnings and safe drain/disposal integration. No desktop tests.
- [x] Focused review and fixes confirmed. Locked restore/build, 239 tests (215+24), zero warnings/errors and self-contained publishing passed. Clean ZIP verified including bundled tray framework before private library refresh/import into separate local build. Prepare draft PR; GitHub CI is the final external gate. Owner merges; manual native/UI tests remain deferred.

Review focus: hide cannot tear down runtime or lease; quit confirmation/state races; canceled/deferred quit retains intent; no notifications interrupt active play; native resources disposed once; no startup registration; late callbacks after shutdown.
Execution evidence: lifecycle/alert API tests observed RED before implementation, then GREEN. Legacy settings, fresh checkpoint and hidden-session confirmed-exit backup tests likewise preceded interfaces. Changed-failure acknowledgement regression RED->GREEN. Final focused review found canceled quit detached the embedded view and a new Starting session reused inactive completed progress; both fixes confirmed, with stale-checkpoint regression RED->GREEN. Current full239 tests (215 core/infrastructure +24 Windows/headless) pass. All native shell/manual checks deferred.

Ruling: lifecycle awaits preserve the UI synchronization context. Quit preparation validates views without detaching; NotesHost is disabled during preparation and restored on cancellation. Confirmed-exit backups drain before tray disposal/lease release; stopping observation does not synthesize a game exit. Failed native tray operation restores the visible main window. No Windows startup registration added.