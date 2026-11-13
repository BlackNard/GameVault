# Issue #9 — Save source discovery and configuration

Owner workflow: implement using automated gates; defer UI/native/game UAT to the final test pass. Existing first-launch requirements and same-window/themed UI remain authoritative. No additional manual-test gate is introduced.

## Approach and scope

Use offline known-location rules plus bounded game-specific suggestions. A directory-change notification cannot identify the writing process reliably, and FileSystemWatcher may lose events; use periodic bounded rescans during a confirmed session plus a scan after observation ends. Do not add full-disk scanning, injection, kernel tracing, administrator privileges, or store-login dependencies. Registry-based saves remain deferred.

Initial exact catalog covers Resident Evil 2 (2019) and the original/remake Dead Space layouts, plus common per-game install-folder, Documents/Saved Games and Unreal SaveGames candidates. Known RE2 identity requires re2.exe and a matching title/install folder. Dead Space versions without a clear year/remake identity remain suggestions. Unknown/generic-name matches require confirmation; never label guesses verified automatically. A read-only scoped machine probe confirmed the known non-store RE2 public-documents layout exists; no save contents were read/copied and no game was launched.

Known sources are narrow game/app-ID directories. Scan only the relevant Documents, Saved Games, AppData, Public Documents and selected install-folder candidates. Bounded Steam userdata account enumeration is optional and does not assume ownership/store installation. Resolve OS known Documents location, keeping redirected local folders supported. No user-wide/disk-wide recursive search. Original files stay in place.

## Persistent model

Independent schema-1 save-discovery.json per profile stores stable game ID, identity fingerprint (executable path + working directory + normalized manual sources; friendly title renames retain existing mappings), automatic enabled flag, candidate mappings with stable source ID, resolved absolute path, file/folder kind, origin/confidence, selection (automatic/confirmed), access state and diagnostics, last scan and overall status. Existing profile.SaveSources are explicit manual overrides, preserved as-is and preferred while nonempty; adding a game with none keeps discovery enabled. Settings and profile version stay unchanged.

Source mappings retain stable IDs when rechecked, including temporarily missing sources; no deletion of history/backups. Manual edits do not overwrite discovery sidecars or unrelated profile fields. Candidate confirmation revalidates against the current candidate set/path safety and current profile fingerprint. Editing game identity invalidates stale automatic/confirmed associations until rediscovered/reconfirmed; manual paths remain explicit overrides. Corrupt/recovered/future sidecars stay protected, with actionable diagnostics; launching remains available.

## States and first launch

Distinguish Waiting for first save (known path absent/empty), Searching, Sources found (verified readable game-specific files), Needs confirmation, Unresolved, Missing, Unreadable, Unsafe and Disabled. Show resolved paths and source origin. Existence alone does not prove a successful backup: explicitly say No verified backup yet; backup creation belongs to #10.

Look up when game is saved/selected/startup and on Refresh. While a confirmed game session runs, rescan every 10 seconds (one operation per game; coalesce requests). A nonexistent folder remains pending and is retried; never create it just to watch it. A final scan observes saves created late in the session. Observation shutdown is not confirmed exit; add a lightweight confirmed-session-ended signal for #10 without changing measured timing endpoints. Discovery failure must not change launch success or measured playtime. Track and drain background discovery work before releasing the app lease; no game is killed.

## Filesystem safety and access

Shared source validator accepts absolute local file/folder paths, rejects UNC/device/root/broad system/profile roots, app-managed Data/Profiles overlap in either direction, traversal supplied by untrusted relative candidates, and any link/junction/reparse point in path or descendants. Candidate templates contain fixed narrow components; sanitize names and reject generic/unsafe name components. A manually selected exact source can be missing initially but must remain safe. Reject overlapping/nested selected sources and recursive/unsafe backup mappings. Never recurse through a link, even during discovery or status probing.

Read accessibility via bounded metadata plus a one-byte shared read of at least one candidate save file (manual file can be any user-selected file). Use bounded tree depth/file/entry counts, cancellation checks and deterministic ordering. A locked/unreadable/partial/linked/too-large tree cannot be silently declared fully verified. Surface partial/limit diagnostics. Do not inspect file contents beyond the accessibility byte or copy data. Filename/extension signals on heuristic candidates provide suggestions only.

## UI and integration

Add a same-window Save sources panel reachable from game details, with Refresh, Automatic discovery toggle, candidate list/details, explicit Confirm this source for suggestions, and native manual file/folder selection through existing Edit game. Multiple manual sources remain supported. Show pending first-save guidance, invalid/locked/missing paths and No verified backup yet. Keep Midnight/Crimson and existing native pickers. Add-game save fields explain they are optional overrides, not prerequisites.

Use separate AppDataStore partial persistence, shared safe-source validation for LibraryService/manual paths, testable SaveDiscoveryService with injected local environment/known roots, and a tracked lifecycle coordinator subscribed to launch state (no synchronous filesystem work in launch event handlers). Existing playtime journals, notes, corrections, covers and backups are untouched. #10 consumes verified source mappings through the service rather than trusting arbitrary strings.

## Verification

Synthetic fixtures cover no initial folder then first-save creation while tracked, post-exit discovery, matching/ambiguous identities, manual override and missing/unreadable distinctions, stable mapping IDs, atomic/protected sidecars, identity changes, unsafe overlap/traversal/links, bounded recursion/entry limits, coalesced scans/cancellation/disposal and independent launch/time behavior. Add a headless launch/discovery integration fixture, never a real game/UI. Run full locked build/test/publish, focused review, clean ZIP check then private test-data import; draft PR with CI, owner merges.

References checked 2026-11-13:
- Microsoft FileSystemWatcher docs (events may be duplicated/lost; events are not process attribution): https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher
- Ludusavi manifest format and exact game entries (MIT): https://github.com/mtkennerly/ludusavi-manifest and data/manifest.yaml. Reuse only reviewed initial path facts, preserve attribution; no full manifest or external updater/dependency in this ticket.