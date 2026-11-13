# Workflow Validation Implementation Plan

Execute inline on codex/issue-14-workflow-validation, one focused final review. Owner manual/native-game checks deferred; #14 acceptance stays open.
Goal: Repeatable headless fixture evidence for add/play/track/discover/backup/retain/restore/load/undo and failure boundaries.
Spec: docs/superpowers/specs/2026-11-13-workflow-validation-design.md
Stack: Existing .NET10/xUnit/windowless fixture and real native process adapter. No packages.

- [x] RED native protocol/semantic save-load tests. Extend opt-in fixture with strict sandbox, session tokens, bounded atomic command/results; GREEN.
- [x] End-to-end successful workflow: first-session discovery, hide/checkpoint/recovery/simulated sleep, multiple backups/retention, semantic restore/undo load, dummy-install removal and data hash preservation.
- [x] Failure workflow: missing save, failed launch, locked source, injected space failure, failed restore/rollback and semantic load of preserved progress.
- [x] Machine-readable evidence in full verifier, coverage/results and explicit pending owner/#18 acceptance matrix. Do not close #14.
- [x] Focused review found no material issues. Locked restore/build, 242 tests (215+27), required fresh scenario reports and self-contained publish passed with zero warnings/errors. Clean ZIP verified before private refresh/import. Prepare draft PR refs #14; CI is the final external gate. Owner acceptance stays pending.

Safety focus: all fixture paths under unique temporary working/install/app folders; no user saves or actual installation changes, no UI/native dialogs, no process killing, bounded fixture lifetime and graceful signal-based cleanup, no stale protocol replay, no false real-game/UI acceptance claims.
Evidence: protocol test observed RED timeout against the old fixture, then GREEN with opt-in commands. Combined workflows reproduced a shared mutable command-file replacement race (Windows80070005); immutable per-session/request command/results resolved it. Three workflow tests and full242 test suite pass. No production changes were required. Owner #14 real-game/UI acceptance and #18 remain pending; PR must reference #14 without closing it.