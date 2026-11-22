# GameVault @@VERSION@@ — development preview

An early preview of GameVault for Windows 10 and 11 (64-bit): a game launcher for games you add yourself, with playtime tracking, automatic save discovery and dated save backups.

Features include a local game library and launcher, individual play sessions and playtime history, session notes and game reviews with export, Midnight and Crimson themes, local save-source discovery, dated backups with retention, pinning and restore, background tray operation, screenshots and recording, game information fetched from public sources (Steam, GOG, Epic, Wikidata, HowLongToBeat), and a per-user setup wizard. Automatic save discovery depends on supported rules and verified locations; it is not universal interception of every game's saves.

The build pipeline checks app behavior, clean payloads, exact installer hashes, install, upgrade, uninstall and reinstall preservation of a synthetic library, and the full Git source history before these assets were prepared. See `RELEASE-MANIFEST.json` and the linked build evidence for provenance and scope.

This is a preview: final real-game, clean-PC and performance testing by the owner is still in progress. Keep your own backups of your game saves.

The installer is unsigned, so Windows SmartScreen may show an unknown-publisher warning. GameVault is source-available and free for personal, non-commercial use under the license in the repository (`LICENSE`); all rights are reserved.

Assets include the versioned setup, its SHA-256, `SHA256SUMS.txt` covering every other asset, installation instructions, dependency notices and a sanitized build manifest. No personal Data/Profiles or game installation files are included. Checksum validation detects changed bytes; it does not establish publisher authenticity.
