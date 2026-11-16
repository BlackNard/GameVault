# Re-add removed profiles and launch store games — owner request

Owner wants to re-add a removed game without deleting its profile folder, and use GameVault Play for Steam/Epic games including TEKKEN8. Routine implementation proceeds under the established owner workflow; Steam/Epic clients may still be required for authentication/DRM.

## Profile re-add

When Add game encounters the same executable in an archived profile, offer a same-window restore action. Restore the existing stable ID, saved configuration, cover, sessions, notes, journals and backups; do not create a second history or silently overwrite retained configuration with blank Add form defaults. Active duplicates stay protected. Cancellation leaves the archive and form intact. The restore operation must enforce writable-profile and duplicate guards atomically, then return its saved profile to the existing editor completion flow.

## Store launch

Detect an installed Steam game from bounded appmanifest metadata in the selected executable's steamapps library. Detect Epic using bounded local installation .item manifests matched to the selected executable's install root. No account, credentials, registry changes or global game scan. Construct only supported local Steam/Epic protocol activations from validated metadata; no arbitrary URL or shell commands. Platforms handle their own authentication/DRM.

Resolve the tracked executable from an explicit existing Launch settings target first, otherwise the selected real executable or an unambiguous Unreal Binaries/Win64/*-Win64-Shipping.exe below the matched install root. TEKKEN8 root TEKKEN8.exe is a bootstrapper and Polaris/Binaries/Win64/Polaris-Win64-Shipping.exe is present on the owner installation (read-only filename check). Do not guess between multiple actual executables; retain explicit target selection as fallback.

Play through a detected store waits up to two minutes for a new exact-path game process after activation, even when the already-running client owns its process tree. Ignore the long-lived client and pre-existing/PID-reused/unrelated processes. Reserve the game/store identity, start session recording only after confirmed game detection, and observe the actual game through exit; use its real path/PID/creation identity for checkpoints/recovery. Preserve session signals, capture, confirmed-exit backups, duplicate prevention, shutdown and direct executable/UAC behavior. Never kill a game/client to attach or test. Tests inject launch/process/clock signals and synthetic metadata only.

## Ownership / contracts

WorkerA owns LibraryModels/LibraryService archive restore, GameEditor and narrow MainWindow editor restore delegate wiring plus tests. WorkerB owns store metadata/launch adapter, ProcessContracts/WindowsGameProcessSystem/LaunchService integration and focused tests. Parent owns LaunchSettingsWindow wording, docs/issues, review, full gates, PR and clean/local-seeded delivery. Workers must not revert others or run simultaneous builds.

Use a new optional injected IStoreGameLauncher in LaunchService. Proposed contract: StoreLaunchPlan? Resolve(string selectedExecutable,string? targetOverride); void RequestLaunch(StoreLaunchPlan plan). Plan carries store provider, stable store key, activation URI, install root and resolved exact game executable. Default production adapter performs bounded read-only metadata resolution and supported protocol activation; fakes never start a client. Existing LaunchSettings schema remains unchanged.

## Verification / handoff

Meaningful archive restore/data-retention and detached process/store activation tests first; compatibility/direct-launch/recovery tests; headless same-window contracts; fresh focused review; full locked restore/build/test/publish plus clean package/source audit and exact-head Windows CI. Owner native/store/real-game tests remain pending. Preserve all personal data and old builds; clean ZIP verified before helper Import into a fresh local test build. No automatic merge or release publication.

Snapshot Refresh after owner closed app:20261115T192429205Z-de3b2c16. Existing PR52 remains open on codex/issue-51-in-app-cover-search stacked after PR50; deliver the new codex/restore-profiles-store-launch branch as a stacked PR targeting codex/issue-51-in-app-cover-search, closing issues59 and60.
