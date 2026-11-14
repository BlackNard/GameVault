# First-version app validation status

Issue #14 is **partially complete: automated workflows pass; owner acceptance is pending**. Do not close #14 or infer first-version acceptance from a green test run alone. The owner deferred manual UI/native-dialog/real-game testing until remaining implementation work is finished. That work can continue under the agreed workflow; this record does not approve app, installer or release acceptance.

## Repeatable automated evidence

Run scripts/verify.ps1 on Windows with the pinned .NET SDK. It performs locked restore, build, all tests and a clean self-contained publish. The verifier requires full-workflow.json and failure-boundaries.json from that exact run, writes artifacts/validation.json with firstVersionAccepted=false, and records matching run IDs in validation.json/verification.json. Scenario observations are also printed to CI logs. TRX files and per-run evidence remain under artifacts; no private saves or machine/user paths are placed in the scenario reports.

Current functional suite: 242 tests (215 core/infrastructure +27 Windows/headless). This is functional evidence, not a #18 performance or desktop-usability measurement.

| #14 criterion | Evidence | Status |
| --- | --- | --- |
| Add, launch, tracking, discovery, backup, retention, restore | CompletePlayBackupRetentionRestoreUndoAndControlledUninstall | Automated pass |
| First save with no manual path | Toy game creates a versioned save in an injected synthetic known layout; real discovery and post-exit backup services consume it | Automated pass |
| Restored progress is loadable | New windowless fixture process parses the restored save format and loads chapter-a; undo loads chapter-c and later-slot | Fixture semantic pass; Resident Evil 2 pending |
| Profile survives game installation removal | Delete only the checked copied dummy installation; all profile/statistics/backup/recovery hashes remain identical | Automated pass; no actual game removed |
| Missing save, invalid launch, locked source | FailureBoundariesPreserveSavesAndVerifiedHistory | Automated pass |
| Insufficient space, failed restore | Injected disk-space IOException; failed mutation rolls back, recovery remains and fixture loads current progress | Automated fault-injection pass; no disk was filled |
| Crash/interrupted-session recovery | Replay an abandoned Active checkpoint while the fixture remains running; recover the same session and exclude downtime; broader SessionTests cover journal/statistics reconciliation | Controlled simulation pass; no user process killed |
| Sleep/resume | Controlled awake/wall clock excludes600 simulated sleep seconds; SessionTests exercise sleep/wall-clock behavior | Simulation pass; actual OS suspension not exercised |
| Actual Resident Evil 2 executable/save mapping and restored save loading | Owner real-game final pass | Pending, deliberately not run |
| Visual/usability/native tray/dialog checks | Owner final pass and #18 | Pending, deliberately not run |
| #18 reference-machine performance targets | Separate performance work and final desktop measurement | Pending |
| First-version acceptance before installer/release approval | Requires remaining app features and explicit owner acceptance | Not accepted yet |

## Scenario observations

The complete workflow records five distinct sessions with27 simulated awake seconds. It excludes600 simulated sleep seconds and300 simulated observer downtime seconds, keeps one pinned plus one newest unpinned backup, restores chapter-a and undoes to chapter-c, keeps an unrelated file, and preserves all profile hashes after removing the copied fixture installation. Recovery copies remain available after ordinary retention.

The failure workflow records missing saves without an empty backup, a failed executable without a session, locked-save and injected-space failures preserving the prior completed snapshot, and failed restore rolling back to a format that a fresh fixture process loads as current progress.

FixtureProtocolLoadsProgressAndRejectsTraversalAndReplayedSequence checks the native protocol independently. Each fixture is opt-in, windowless, bounded to a unique temporary sandbox and30-second lifetime. Immutable session/request-specific command and response files avoid the reproduced Windows80070005 shared-mailbox replacement race. Cleanup signals only copied fixture processes and validates absolute temporary targets before recursive deletion. Test fixture binaries are excluded from published app packages.

## Remaining work and owner final pass

The combined first-phase candidate implements capture (#26), online covers (#27) and responsiveness improvements (#18), alongside installer/release work (#15-#17). Automated gates and the synthetic measurements are documented separately; full app timing, capture compatibility, actual game behavior and visual acceptance remain pending. Use [the consolidated checklist](../phase-one-manual-testing.md) for the final pass. At the agreed final pass, the owner will confirm actual game paths, preserve current saves, restore and load representative real-game backups, and report bugs or requested changes as separate tickets.

Use the existing [restore](../design/restore-manual-tests.md), [backup](../design/backups-manual-tests.md), [history](../design/backup-history-manual-tests.md), [discovery](../design/save-discovery-manual-tests.md) and [tray](../design/system-tray-manual-tests.md) checklists then. No manual testing is requested for this PR. No production behavior changed in the automated-validation work.
