# Dated Backups Implementation Plan

Execute inline on codex/issue-10-dated-backups; the owner delegated routine implementation decisions and deferred manual testing. Retain the established checkout/build workflow.

Goal: Verified independent snapshots on confirmed exit and demand without risking existing saves/backups.
Spec: docs/superpowers/specs/2026-11-13-dated-backups-design.md
Stack: Existing .NET10/WPF/JSON/SHA256, no new packages.

- [x] RED: synthetic engine tests for first snapshot/manifest, changed and deleted files, unchanged automatic skip, failure preserving last success, missing/locked/corrupt/unsafe data and operation conflicts. Implement models, leases, bounded snapshot engine and atomic status. GREEN.
- [x] RED: lifecycle tests for confirmed exit only, first-session discovery, cancellation/drain and external-running probe. Integrate shared launch lease and background coordinator. GREEN.
- [x] Existing-window Back up now/status integration with Midnight/Crimson resources, no native/real-game automation. Update deferred checklist and honest discovery copy.
- [x] Full suite and focused review/regressions: 181 tests (157 core/infrastructure +24 Windows/headless), zero build warnings/errors, locked restore and self-contained publish. Clean ZIP verified before private test-library import. Prepare draft PR; GitHub CI is the final external check before handoff. Owner merges; manual testing stays deferred.

Review focus: no false success from partial inventories; no prior-backup overwrite; protected/linked destination; race with relaunch; canceled shutdown doesn't publish; backup failure never affects recorded playtime.

Ruling: snapshot folders are authoritative; last-success UI verifies them rather than trusting the mutable status record. Pending staging remains separate and is preserved on failure. No automatic retention deletion in this ticket.
Execution evidence: synthetic RED tests preceded engine and lifecycle interfaces. Later RED regressions reproduced late inventory publication, stale success after removed snapshots, Win32 process-inspection failure, one emptied mapping hidden by another, and last-success loss during Working. Each was fixed and observed GREEN. Final focused reviewer found no remaining important issues and confirmed the Working-state fix. Windows/game/manual UI checks remain deferred. Completed status-write failure is covered independently from manifest success.

Ruling: shared leases include recovered sessions; confirmed-exit work awaits launch completion to avoid colliding with its still-held lease. Verification/copy loops check cancellation per buffer. Any source must retain nonempty expected payload, not just the combined inventory. UI ignores obsolete history reads when newer backup events arrive.