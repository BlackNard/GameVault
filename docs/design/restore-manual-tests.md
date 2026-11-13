# Safe restore and undo - manual testing deferred

From Browse backups, select a completed copy and Restore selected. The restore preview stays in the main window. Review destination mappings; native folder/file browsers or absolute text paths support moved/reinstalled games and missing destinations. Choices affect this operation. Use Save sources separately if future automatic backup locations must change.

Preview verifies the selected manifest, payload hashes and current destinations. Review the exact create/replace/remove list, then confirm it explicitly. Changes to paths, selected extra removals, profile identity or current files require a fresh preview. The entire selected set is checked before reporting success, including files whose bytes initially needed no replacement.

Files absent in the selected set are removed automatically only if previous snapshots tracked them for that mapped game source. Unrecognized files default to kept. The user may explicitly select individual extra files for removal and preview again. No live destination tree is recursively deleted, and a colliding file/directory path is rejected.

Before replacement, both selected data and the full current destination trees are copied, hashed, rechecked and held stable under Profiles/<id>/RestoreRecovery/<operation-id>. recovery.json records the exact before-state, destinations and change set. The before copy may contain unrelated files in those narrow folders as additional protection; restore/undo only modifies the planned paths. Recovery copies are independent of ordinary retention and remain available after restart.

A prepared journal is committed before the first replacement. On error/cancellation, the app rolls back its attempted changes if their current state can be verified safely. It does not overwrite unexpected external changes or keep writing while a game has started externally. It reports Failed before replacement, RolledBack when its changes were reversed, or RecoveryRequired with an exact recovery path. A protected/future journal or a prepared/recovery-required operation blocks launch, backup and retention mutations until resolved.

Undo using recovery creates another before-state copy first. Failed or interrupted undo retains the original required recovery target in the journal; it cannot clear the original guard by reverting to an unresolved partial state. The UI and backend only permit that authoritative recovery target while blocked. Completed undo clears the guard only after verification. Historical recovery copies remain selectable; no automatic recovery-folder cleanup is installed.

Limits are bounded: 32 mappings, 4096 files and 4 GiB in each selected/before payload set, 8192 observed tree entries and depth6, and bounded recovery metadata/history. Missing/empty current destinations can be preserved as absent states. Source/destination junctions, traversal/device aliases, broad/protected paths, overlapping mappings and unsafe manifest relative paths are rejected. Restore does not alter statistics, journals for playing time, notes or ordinary backup payloads. There is no filesystem-wide transaction against external programs; failures are reported conservatively with recovery kept.

## Owner final pass - deferred until all planned issues are finished
- Midnight and Crimson, small/large windows: readable destinations, exact changes, other-file checkboxes, results and fixed Back/Cancel controls; restore/undo stays in the same window.
- Restore a prior game save after reviewing mappings, preserving current progress first; undo it after restarting the app.
- Create newer tracked files and an unrelated file. Confirm tracked absent files are removed; unrelated files stay unless explicitly selected for removal, and recovery contains their pre-operation contents.
- Missing/new destinations and moved/reinstalled games: browse/edit file/folder mappings; source copies and unrelated installation files stay unchanged.
- Editing inputs invalidates confirmation. Current saves changing after preview require a new preview. Native picker cancellation makes no change.
- Cancel a long operation or simulate a denied/locked/disk-full destination: clear Failed/RolledBack/RecoveryRequired result, durable recovery copy and correct launch/backup/history guards.
- An interrupted restore and a failed/interrupted undo keep the original required recovery target active across restart; resolving that target clears it.
- Ordinary retention never removes pre-restore recovery copies. Sleep/playtime statistics, notes, covers and stable profile IDs remain unchanged.

No UI/native-dialog/real-game tests were performed or requested per issue. Automated coverage uses temporary synthetic saves, fake process state and fault injection only.