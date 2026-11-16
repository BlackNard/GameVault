# Store games and re-adding removed profiles

GameVault remains the place to add a game and press Play. Recognized Steam/Epic installations are started using their installed client, while GameVault observes the actual game for sessions, notes, capture and save-backup triggers. Store clients may open for sign-in, ownership checks or updates. GameVault does not replace platform authentication/DRM or collect account credentials.

Steam can explicitly restart a directly invoked executable through its client; this explains why merely tracking the first process misses a handoff. Epic provides local protocol activation by application identity or encoded installation path. The implementation reads bounded local installation metadata and constructs supported local activations; no store account/API key is added to the profile.

References checked2026-11-15: [Valve SteamAPI_RestartAppIfNecessary](https://partner.steamgames.com/doc/api/steam_api#SteamAPI_RestartAppIfNecessary), [Steam DRM/client behavior](https://partner.steamgames.com/doc/features/drm), [Epic protocol activation](https://dev.epicgames.com/docs/epic-games-store/protocol-activation).

## Target process and timing

The actual process is an explicit Launch settings target when configured, otherwise the selected executable or an unambiguous detected game executable. TEKKEN8 uses a root bootstrapper and a separate Polaris Win64 shipping executable. The tracker waits for a newly created exact-path game process; it does not count the store client as playing time. A long-running Steam/Epic client can already be open before Play. Existing games and unrelated processes must not be attached as a new session. An ambiguous process match or failed/slow platform launch produces guidance rather than fabricated playtime. A manual actual-game-executable override remains available for launchers whose target cannot be inferred.

Stopping GameVault observation leaves the game/client open and retains checkpoints. Recovery verifies the recorded actual executable/PID/creation identity and excludes unobserved time. Confirmed game exit, rather than store-client lifetime, remains the basis for automatic save backups. Save-location discovery still needs its supported game rules or an explicit source; store launch support does not imply universal save discovery.

## Restore through Add game

Removing a game from the library keeps its profile. Adding the same executable again offers an in-place restore of its saved profile, retaining stable ID, configuration, artwork, playtime, sessions, notes, journals and backups. Empty Add form defaults must not reset retained settings. Cancelling leaves the archive and Add form intact. Existing active duplicates and protected profiles remain guarded. No profile-folder deletion or duplicated history is needed.

## Pending owner checks

- Remove a disposable profile with history/notes/cover and add its executable again. Cancel the restore first, then accept. Confirm the original profile and all retained data return without a new ID or empty statistics.
- With Steam already open, Play TEKKEN8 from GameVault. Repeat with Steam initially closed when convenient. Complete any client prompts, confirm tracking begins for the game, and confirm it ends when the game closes while Steam remains open. Check a short recorded session and its backup outcome.
- Repeat with an installed Epic game. Check normal sign-in/update prompts and exact game-exit tracking. Never expect the required platform client to be bypassed.
- For a separate launcher with an unfamiliar target, configure Actual game executable and verify handoff. Check timeout/retry and already-running-game messages, then restart GameVault during a disposable session to verify supported recovery.

All real client/game/native/UI checks are owner-owned and pending. Automated coverage uses synthetic manifests and fake activation/process/session signals only. Keep complete private profiles out of downloadable packages.
