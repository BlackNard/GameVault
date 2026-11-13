# Backup history and retention - final manual checks deferred

Browse backups opens in the same main window. Completed copies are listed newest first by manifest creation timestamp, with stable IDs to distinguish equal timestamps, local dates including seconds and offset, recorded save-data size, file count and pin status. These dates describe copying, not progress in a game. Historical listing reads manifests; it does not advertise a new integrity verification of every payload. Restore is a later ticket.

The total is recorded completed payload bytes, not filesystem allocated bytes. Manifest overhead, incomplete/unknown/quarantined folders are not included. Recovery-folder counts and protected-document warnings are shown separately. Unknown pin state is labeled unavailable; mutation is disabled until protected data is recovered.

Per-game retention uses existing profile settings. The default is the latest 20 unpinned backups plus all pinned backups. Keep everything disables pruning. Changing fields only previews a policy; Apply retention persists it and applies safe cleanup. Pin/unpin alone does not trigger cleanup. Failed/unchanged backups do not trigger it either. A new verified copy triggers cleanup after it is safely committed. The latest verified copy and just-created copy are protected even if the system clock moves backward.

Cleanup validates all deletion candidates, their hashes and exact manifest-owned trees before removing any. Unexpected data, corrupted/future manifests, protected pins and links stop cleanup. Old eligible copies are moved into a .prune-* recovery folder before individual known-file deletion; no generic recursive deletion is used. Failure/cancellation leaves the remaining recovery folder protected and never automatically retried. Backup success is independent of cleanup success. A failed cleanup may leave the chosen retention settings saved; the user can retry Apply after resolving warnings.

## Deferred owner final pass
- Midnight and Crimson, small/large windows: readable list, visible selection, wrapped paths, fixed Back navigation and no extra window.
- Browse dates, sizes and pin status across restarts; equal timestamp copies remain distinct. Confirm pinned copies remain in addition to the unpinned limit.
- Adjust per-game limit, preview removals, Apply. Verify the latest copies remain and unrelated profile data/statistics/notes are unchanged.
- Keep everything keeps all completed copies; unpin does not delete immediately. Apply or a new verified backup later enforces the current policy.
- Failed/unchanged backup leaves history untouched. Cleanup failures preserve the latest verified copy and show a separate warning.
- Recovery, protected/unknown and linked folders are preserved. Their excluded size is stated clearly.
- Active game/backup/history work refuses conflicting changes. Navigation and close wait for current history work; reads never trigger cleanup.

No manual UI/native-dialog/real-game checks are requested per issue. Automated verification uses temporary synthetic snapshot trees and fake process state only.

Implementation reference: .NET Directory.Delete has reparse-point behavior; this app additionally rejects links and deletes only an explicit file allowlist and empty directories: https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.delete?view=net-10.0