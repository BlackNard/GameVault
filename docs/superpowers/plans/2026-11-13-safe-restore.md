# Safe Restore Implementation Plan

Execute inline on codex/issue-12-safe-restore with one focused final safety review. Owner delegated routine decisions and deferred manual UI/native/game testing.
Goal: Preview and restore prior saves without losing current data, with durable recovery and undo.
Spec: docs/superpowers/specs/2026-11-13-safe-restore-design.md
Stack: Existing .NET10/WPF, protected JSON, SHA256, same-window controls and shared game leases; no new packages.

- [x] RED tests for preview ownership/hash/mapping validation, exact apply/unknown preservation, stale preview, absence and file mappings. Implement restore models, bounded destination inventory, strict payload/recovery storage and core planning; GREEN.
- [x] RED tests for preservation and copy failures, partial rollback, rollback failure, journal guard and undo after restart; implement durable restore transaction and recovery snapshots; GREEN.
- [x] RED tests for active game/conflicts/protected journal, cancellation/links/unsafe manifests and independent retention. Integrate guard into launch/backup/history mutations; GREEN.
- [x] Same-window restore/undo previews and editable native-browse mappings, explicit confirmation and clear recovery states; update deferred checklist/docs.
- [x] Focused review and corrections confirmed. Locked restore/build, 226 tests (202+24), zero warnings/errors and self-contained publish passed. Clean ZIP verified before private data refresh/import into the separate local build. Prepare draft PR; GitHub CI is the final external gate. Owner merges; manual tests remain deferred.

Review focus: recovery verified before first destructive write, exact ownership and independent recovery retention, stale preview/profile/path checks, crash and rollback guards, no conflicting game launch, no false success after journal failure, unaffected playtime/notes/statistics.

Ruling: unknown files default to kept; explicit extra-removal selection is part of the preview fingerprint. Only tracked absent paths default to removal. Undo reverses the exact original change set and creates its own before copy; no recursive live-folder replacement/deletion.
Evidence: interfaces preceded failing tests. RED->GREEN regressions cover late corruption of the before copy, blocked pending-restore launch/backup/history, parent-file collisions, the review finding that a failed/interrupted undo cleared the original guard, and unchanged selected files changing before final verification. Final suite currently226 tests (202 core/infrastructure +24 Windows/headless). Review corrections and narrow follow-ups are confirmed. No manual UI/native/game actions.

Ruling: reverify and hold read locks on recovery metadata and all before/selected files throughout transaction/rollback. Track attempted writes conservatively and recognize already-before states during rollback. Recheck the full destination inventory before Prepared and all selected file hashes before success. RequiredRecoveryId preserves the original authoritative recovery through nested failures and restart.