# GameVault @@VERSION@@ — private development preview

Draft only: this build has not been approved for public distribution.

Implemented development features include a local game library and launcher, individual play sessions and playtime history, session notes/game reviews and export, Midnight/Crimson themes, local save-source discovery, dated backups with retention and pinning, restore/recovery controls, background tray operation, and a per-user setup wizard. Automatic save discovery depends on supported rules and verified locations; it is not universal interception of every game's saves.

The build pipeline checks app behavior, clean payloads, exact installer hashes, synthetic-library upgrade/uninstall/reinstall preservation and fetched Git source history before preparing assets. A separate disposable-runner smoke check can download and install this exact draft asset. See `RELEASE-MANIFEST.json` and the linked CI evidence for provenance and scope.

Final owner UI/native-dialog/real-game testing, clean Windows client coverage, interactive cancellation and performance acceptance remain pending (#14/#16/#18). Planned capture/recording and online cover-search work is not represented as finished by this draft.

The installer is unsigned. The repository remains private; downloads require access. No project license is granted by packaging this preview, and no publication, visibility change or public distribution decision is implied.

Assets include the versioned setup, its SHA-256, `SHA256SUMS.txt` covering every other asset, installation instructions, dependency notices and a sanitized build manifest. No personal Data/Profiles or game installation files are included. Checksum validation detects changed bytes; it does not establish publisher authenticity.
