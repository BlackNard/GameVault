# Save source discovery — deferred owner UAT

Manual UI/native-dialog/real-game testing is intentionally deferred until the final feature pass. These items are pending and do not gate the issue's automated handoff.

- Add a game with no source paths; launch its first session. A missing known save folder should stay pending, be rechecked during play and after observation ends, and resolve once actual saves appear. Game launching and playtime must remain independent of discovery errors.
- Open Save sources inside the main window. Check resolved paths, selected/readable state, origin, pending-source filtering, refresh, confirmation and automatic on/off. Disabling automatic discovery stops automatic source probes; explicit manual paths remain usable. No page should claim a verified backup in this ticket.
- For the local RE2 installation, compare the resolved source with actual saves during the final real-game pass. A scoped metadata-only development probe found a non-store public-documents layout; no game or save contents were used for automated testing.
- Confirm ambiguous/generic candidates deliberately. Unknown titles and unclear Dead Space versions must not silently select a guessed folder. Initial catalog scope is RE2 (2019), Dead Space original/remake known layouts plus game-named and common engine/install-folder suggestions, not universal game coverage.
- Through Edit game, choose one or more narrow local save files/folders. Empty paths are optional; missing first-run paths are allowed. Reject managed-data overlap, nested selections, drive/profile/system roots, junctions/links, network/device paths and Windows name aliases. Unreadable/locked/oversized sources should remain unverified with diagnostics.
- Rename a game and verify stable mappings remain; change executable/working folder/manual overrides and verify stale selections are invalidated/rechecked. Corrupt/future/recovered discovery documents should stay protected and launching should remain available.
- Check Midnight/Crimson, keyboard/focus, narrow windows, closing during refresh and shutdown while a game remains alive. Discovery work must finish/cancel before releasing the app's lease; the game is never killed.

## Scope and storage

Profiles/<game-id>/save-discovery.json stores independent schema-1 mappings, fingerprint, revision, preference, last check and access diagnostics. Selected means intended source; only Ready/readable selections from a fresh successful full check can feed backup code. Missing/unsafe/unreadable selections never authorize copies. Revision checks protect newer settings from stale scans. Existing profile.SaveSources remain explicit manual overrides; original saves are never moved, redirected, copied or deleted by discovery.

The scanner checks narrow game-specific paths only, with limits of 6 folder levels and 4,096 entries per source. It skips links and distinguishes limits from success. It reads at most one accessibility byte per file, not save contents for analysis. Checks run every 10 seconds during a confirmed observed session and once after observation ends; confirmed exit has a separate signal for #10. Registry saves, full-disk/process-write tracing, online catalog updates and actual dated backup creation are outside #9. #10 consumes the verified-source API and must stage/verify copy manifests before reporting protection.

## Research sources

- Microsoft FileSystemWatcher event-loss/duplication limitations: https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher
- Microsoft local/device path and reserved-name semantics: https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file and https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats
- Initial reviewed save-location facts and MIT attribution: https://github.com/mtkennerly/ludusavi-manifest and data/manifest.yaml. No full third-party manifest or extra runtime dependency is shipped.