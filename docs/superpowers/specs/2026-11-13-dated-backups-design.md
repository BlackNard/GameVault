# Dated save backups — issue #10

The player wants multiple independent local snapshots, created automatically after confirmed exit or using Back up now. Existing saves remain in place; missing, empty, ambiguous or unreadable discovery is pending/failure, never an empty successful backup.

## Approach
Use immutable dated directories in Profiles/<stable-id>/Backups, with an explicit manifest committed inside a private staging directory before an atomic directory rename. This gives independently inspectable copies and keeps older backups untouched. A rolling mirror loses history; a shared archive/index introduces unnecessary overwrite/recovery coupling. No retention deletion or restore operation in #10 (separate tickets).

## Safety and lifecycle
Fresh discovery supplies selected readable mappings. Sources are revalidated with the existing path/link policy. Enumerate a bounded tree, hold read-only handles that deny writers/deletion, copy with hashes, verify destination hashes, and compare a second source inventory/hashes before committing. Concurrent launch and backup for the same game share an operation lease; additionally probe the configured executable/target for already-running external processes. Refuse conflicts immediately. No elevated capture or original-save writes. A disappearing/changed/locked source, limit, disk-full or denied destination fails retryably and retains the last success.

Use unique UTC timestamp plus random-ID directories; .pending-* is never complete. Manifest schema 1 records source IDs/paths/kinds, relative files, sizes, SHA256 and deleted entries relative to the last completed snapshot. Reject malformed/unsupported manifests and linked destinations; verify prior payload before using it as comparison. Skip unchanged automatic snapshots; manual snapshots may repeat content. Never delete source files, backups or abandoned staging folders on startup. Failures preserve staged evidence for later recovery and do not advertise it as success.

Post-exit coordinator only responds to ConfirmedExit=true. It runs off the launch timing path, performs fresh discovery itself, and drains/cancels safely on close. Launch cancellation/observation stop does not create automatic backups. UI shows current result and last verified backup independently of playtime, with Back up now in the existing library details. Status persistence is a separate atomic JSON sidecar; completed manifests remain authoritative for last success if status persistence fails.

## Bounds and consistency
4096 files/8192 entries, depth 6, 32 sources, 4 GiB total copied payload; exceed limits with actionable failure rather than truncation. A completed snapshot guarantees verified bytes and a stable observed source inventory, not a filesystem-wide transactional instant. Recheck identity/source mapping and external-running status immediately before publication. Known discovery limitations remain visible. Restores/retention/export and backup browsing are separate issues.

## Verification
Synthetic sources only: first-session creation, changed/unchanged/deleted files, multiple mappings, integrity, corruption/future manifests, failure injection, locked/missing/empty/changed sources, unsafe/junction destinations, active/external game conflicts, concurrent requests, cancellation, coordinator confirmed exit and drain. Full locked verification and one final focused review, clean ZIP before private test-library import, draft PR and green CI. Owner manual UI/game checks deferred until all planned issues finish.