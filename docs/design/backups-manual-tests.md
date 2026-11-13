# Dated backups: scope and deferred manual checks

Issue #10 adds Back up now in the library detail panel and automatic backups after confirmed game exit. Each completed folder contains manifest.json and Files/<source-id>/<relative-file>. The manifest records local source mappings, file sizes and SHA256 hashes, plus files absent relative to the last snapshot. Live saves remain in their original locations. New snapshots never replace older folders.

The UI shows current work/failure and the last verified success separately. Unchanged automatic backups are skipped; manual copies can repeat content. Discovery runs fresh before capture, so saves created during the first session can be included without manual configuration when a known source is verified. Unresolved discovery remains pending/needs attention, without an empty successful backup.

Incomplete .pending-* folders are preserved and never treated as successful backups. Failed copies, hashes, reads, disk/permission problems and source changes produce retryable diagnostics; prior completed folders remain intact. A failed status-sidecar write cannot undo a completed manifest. Corrupt/future manifests or status files are protected, with no silent reset.

Limits: 32 nonoverlapping sources, 4096 total files, 8192 entries, directory depth 6 and 4 GiB total payload. Narrow the source if these are exceeded. Capture holds read-only source handles that deny writes/deletes, verifies copied hashes, and checks inventory/source hashes before publication. This observes a stable tree; it is not a filesystem-wide transaction. Games launched externally must be closed; GameVault refuses detected active games and concurrent launch/backup operations. Backup logic does not change playtime.

No retention deletion, restore UI or history browser in this issue. Existing retention fields do not cause cleanup yet. Complete manifests are the basis for later restore/retention tickets. Keep personal profiles and snapshots out of downloadable ZIPs/Git.

## Owner final testing pass - deferred, not a per-issue gate
- Both Midnight and Crimson: readable status, wrapped long paths, responsive controls at smaller window sizes.
- Play a known game with no manual source, create its first save, exit; verify a completed dated backup with expected files.
- Exit without changes: report unchanged and retain the prior success timestamp. Back up now while closed creates another snapshot.
- Modify/delete a save while leaving a nonempty source; the new manifest identifies deleted entries and earlier snapshots retain their original files.
- While playing/starting/backing up: conflicting play/edit/backup actions are refused. Detect external game launches without creating a snapshot.
- Locked/missing/empty source or denied/full destination: retryable failure and prior success remains visible. Closing drains/cancels work; stopping observation while the game stays open does not trigger a backup.
- Restart: latest verified snapshot and persisted failure/unchanged state are shown separately from playtime. Removal keeps profiles and snapshots.

Automated coverage uses synthetic saves, fake process/session signals and temporary paths only; no real-game/UI/native-dialog testing was performed.