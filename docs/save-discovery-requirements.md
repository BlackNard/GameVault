# Automatic save discovery, including first launch

GameVault's product definition is a local desktop game launcher, similar to Steam's library/launching experience, without a store or cloud synchronization in the first versions. Launching, playtime tracking, automatic save discovery, and dated local save preservation are core behavior; it is not only a catalog that requires the player to configure every save path manually.

Owner requirement, 2026-11-11: a player may launch a game for the first time through GameVault. Adding the executable must be enough to begin; manually selecting an existing save folder must not be a prerequisite. The game may not have created any saves yet. GameVault is expected to discover them and preserve independent copies inside its game profile.

## Required experience

1. Adding the game creates its stable GameVault profile immediately, even with no configured save sources.
2. Launching starts the game and session tracking without requiring an existing save folder.
3. Automatic discovery handles save locations that appear only after first launch or the first in-game save. It must not conclude that no save source exists simply because the initial lookup was empty.
4. Associate verified save sources with the correct game and retain those mappings for subsequent sessions and restores. Local installations outside stores must be supported; Resident Evil 2 is the initial validation case, and its actual installation must be checked rather than assuming Steam directories.
5. After confirmed game exit, create a verified dated backup under `Profiles/<stable-game-id>/Backups/<dated-backup>/`, including saves first created during that session. Apply the existing retention requirements.
6. Keep the game's live saves in their original locations. Launching an arbitrary executable does not itself change where that game writes saves. Profile copies provide independent preservation when the game is removed.
7. Show clear states for searching, waiting for first save, discovered, backed up, unresolved, and failed. Do not show protected until a backup has been verified. An unresolved source must never overwrite a previous successful backup with an empty snapshot.
8. Retain native manual selection as a fallback/override. Automatic attribution cannot be assumed reliable for every executable; ambiguous candidates need confirmation and unsupported games need actionable feedback. Do not silently copy unrelated personal files.

## Implementation boundary

This changes the earlier decision to defer automatic discovery. Discovery belongs to issue #9, coordinated with process lifetime in #6 and verified backup creation in #10. The specific discovery mechanism still needs investigation; neither a full-disk watcher nor universal process-to-save attribution is promised. First-run discovery and subsequent restoration need end-to-end owner validation in #14.

The issue #5 development build stores manually configured source paths only. It does not implement discovery, game launching, or actual save backups.
