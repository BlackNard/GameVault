# Foundation verification — issue #2

Dates: 2026-11-10 to 2026-11-11 (Asia/Amman). This is a development starter build, not the finished library or installer.

## Verified locally

- Installed official SDK 10.0.404 in the workspace after verifying its SHA-512 checksum against Microsoft's release metadata. Existing machine-wide .NET runtimes were not replaced.
- Ran the 12 foundation tests against unimplemented APIs and observed failures. Implemented root normalization, then observed six passing path cases and six remaining workspace failures. Implemented the workspace lease and observed all 12 tests passing.
- Ran `scripts/verify.ps1` successfully: locked restore, Release build, 12 passing tests, self-contained win-x64 publish, required runtime-file checks, and no Data/Profiles in the publish.
- Release build reported zero warnings and zero errors.
- Tests exercise relative/blank roots, spaces/trailing separators, path normalization, missing/unusable directories, exclusive access, release/reopen, independent portable roots, and no prematurely created profile data/probe leftovers.
- On 2026-11-11, launched the self-contained executable and inspected its actual window. Text and profile-path binding rendered correctly. Maximizing and restoring the window preserved a readable layout.
- Launched a second process against the same application root. It displayed the actionable startup warning; dismissing it left the original window open.
- Closed the main window, confirmed no GameVault windows remained, restarted successfully, and closed it again. No Data or Profiles directories were created.
- Independent read-only code review found no critical or important issues. Corrected its one minor README inconsistency about the approved stack and profile identifiers.

## Initial resource baseline

One informal warm restart took 505 ms from the desktop automation launch request until its window was enumerated. This includes automation overhead and is not a cold-start benchmark or a percentile measurement.

On the test machine (Windows build 26300, AMD Ryzen 9 9900X3D, 24 logical processors, approximately 31.1 GiB usable RAM), a single idle sample reported 160.0 MiB working set and 0 ms accumulated CPU over a 2,025 ms interval. Resource counts are a development baseline, not a guaranteed budget across machines. Library-scale benchmarks and representative lower-spec testing remain in #18.

## Remote verification

- [Windows foundation CI](https://github.com/BlackNard/GameVault-archive/actions/runs/42911923448) passed for implementation commit `7b8225c`: the remote Windows runner restored locked dependencies, built the solution, passed the tests, and verified the self-contained publish.
- [Pull request #19](https://github.com/BlackNard/GameVault-archive/pull/19) holds the change for review. Check its current checks for any subsequent documentation-only commits. No merge or release has been performed.

## Scope and limitations

No claim is made about the 500-game startup or 1,000-game scrolling targets, which belong to issue #18 after real library functionality exists. Minimum-window sizing and the complete display-scaling/accessibility matrix belong to the actual interface work in #3.

The application has no game library, timer, persistence engine, backup engine, restore flow, tray behavior, final visual design, or setup wizard yet. Architecture rules for these features are documented; their implementations remain in later tickets.

The development publish contains the application and bundled runtime. It is not a signed installer, public release, or validated compatibility claim for a clean Windows machine.
