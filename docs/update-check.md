# Update check and in-place update

GameVault can tell you when a newer version has been published. It **never downloads or installs anything**: it adds a quiet notice to the **bell** in the title bar (a badge, no pop-up) and a status line in Settings > Updates, each with a button that opens the release page in your browser. The notice stays in the bell until you dismiss it (that version stays dismissed after a restart) or you update; the last result is kept in `Cache/update-notice.json`.

## The one request
- When it starts (a few seconds after the window is up, and at most once every 12 hours) GameVault sends **one anonymous GET** to `https://api.github.com/repos/BlackNard/GameVault/releases?per_page=20` through the shared polite HTTP client: fixed HTTPS address, 10 s timeout, 512 KB limit, no redirects, no cookies, no account.
- Nothing about you or your PC is sent: only a generic `GameVault/1 (desktop game library)` user agent. GitHub sees the usual network address of any web request.
- The time of the last check is kept in `Cache/update-check.txt`.
- Failures (offline, rate limit, odd answer) are quiet at start-up; **Check now** shows them in plain words.
- Turn it off in Settings > Updates ("Check for new versions when GameVault starts"). **Check now** still works. Stored as `checkForUpdates` in `Data/settings.json` (default on).

## Update now (in place)
For the **installed** copy (the one the installer put on this PC; a portable ZIP or a development build only gets the release-page link) the notice and Settings > Updates offer **Update now** when the release carries its installer.
1. You confirm in a prompt that says what will happen.
2. GameVault downloads `GameVault-Setup-<version>-win-x64.exe` from this repository's release (HTTPS; GitHub redirects release downloads to `release-assets.githubusercontent.com`, `objects.githubusercontent.com` or `github-releases.githubusercontent.com`, and only those, at most three hops), into `%LOCALAPPDATA%\GameVault-Updates` (outside the app folder). The size must equal what the release lists, and the file's **SHA-256 must match** the digest GitHub computed for the asset and, when the release publishes one, the `.sha256` file (if both exist they must agree). A file that fails any check, is not a Windows program, or stops arriving for 60 s is deleted.
3. GameVault quits normally (the usual prompts for a running game or backup apply; if it stays open nothing is started).
4. After GameVault released its application-folder lock, the installer runs **over the same folder** with `/SILENT /NORESTART /RELAUNCH=1` (a progress window, no wizard, no administrator rights) and opens the new version when it finishes. The installer never touches `Data` or `Profiles` (checked by the installer lifecycle test in CI), so your games, saves, backups, notes and settings stay as they are.
5. Any failure (offline, mismatch, installer problem) leaves the current version in place and says so; old downloads are cleaned after two days.

What the check proves: the file is the one this release published, byte for byte. It is **not a publisher signature** (the installer is unsigned, as before), so it protects against a damaged or swapped download, not against a compromised GitHub account. A file GameVault downloads itself carries no browser download mark, so Windows does not show the SmartScreen warning again for the update.

## How versions are compared
`GameVault.Core/ReleaseVersion.cs` follows semantic versioning with an optional leading `v` and a fourth number: `0.1.0-preview.3` is newer than `0.1.0-preview.2`, `preview.10` is newer than `preview.9`, and `0.1.0` is newer than every `0.1.0-preview.N`. Draft releases and entries without a valid tag are ignored. A link is only accepted (and only opened, when you click it) if it points at `https://github.com/BlackNard/GameVault/releases/...`.

## When you prepare a release
The app's own version is `<GameVaultVersion>-preview.<GameVaultPreview>` from `Directory.Build.props` (shown by `AppVersionInfo.Current`). **Set `GameVaultPreview` to the number of the preview you are about to publish** (the draft tag is `v<version>-preview.N`, pass it with `-Tag` to `create-draft-release.ps1`) before the build the release is made from. Builds made after publishing a preview and before the next bump report that same preview and are not offered an update by themselves.

If the repository is renamed again, update `UpdateChecker.DefaultRepository`.
