# Backup History Implementation Plan

Execute inline on codex/issue-11-backup-history with one focused final review. Owner delegates routine choices; no further per-issue manual UI gate.
Goal: Browse/pin snapshots and safely retain newest N unpinned plus all pinned copies.
Spec: docs/superpowers/specs/2026-11-13-backup-history-design.md
Stack: Existing .NET10/WPF, protected atomic JSON and operation leases; no new packages.

- [x] RED: metadata listing/order/totals, atomic pin persistence/protected docs, policy updates preserving profile fields. Implement Core history models, BackupStorage catalog and AppDataStore pin/policy methods; GREEN.
- [x] RED: default/custom/keep-all retention, pinned/latest/pending safety, strict exact-tree/link/extra-file protection, canceled/partial cleanup, conflicts; implement BackupHistoryService under shared leases; GREEN.
- [x] RED: only completed new captures trigger cleanup, failures/unchanged do not. Integrate BackupService without changing successful backup semantics; GREEN.
- [x] Same-window Backups page, pin controls, retention summary/apply and pending final manual checklist. Reuse app styles and safe navigation/close behavior.
- [x] Focused final review complete with no important findings. Locked restore/build, 201 tests (177+24) and self-contained publish passed with zero warnings/errors. Clean ZIP verified before private library refresh/import. Prepare draft PR; GitHub CI is the final external gate before handoff. Owner merges; manual checks remain deferred.

Review focus: no deletion outside exact completed snapshots; no protected pin overwrite; new backup verified before pruning; failure leaves latest intact; UI warning/size/date honesty; no automatic deletion on read/unpin/failed backup.

Ruling: cleanup quarantines a fully validated old snapshot and removes only manifest-owned files/empty directories. Recovery/unknown folders are preserved indefinitely and excluded from completed payload totals. No recursive deletion of arbitrary trees.
Execution evidence: engine/list/pin/policy interfaces were preceded by failing synthetic tests. RED regressions reproduced last-moment pin/future-manifest changes still being pruned, then GREEN after state/manifest rechecks. New capture integration was RED (three retained instead of one), then GREEN; failed/unchanged captures preserved their existing snapshots. Cancellation, future/recovered pins, strict extra-file/tree, junction preservation, partial prune quarantine, timestamp ties and clock adjustments are covered. Current full suite: 201 tests (177 core/infrastructure +24 Windows/headless). Owner manual checks remain deferred.