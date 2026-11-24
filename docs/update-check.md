# Update check (notify only)

GameVault can tell you when a newer version has been published. It **never downloads or installs anything**: it shows a toast and a status line in Settings > Updates with a button that opens the release page in your browser.

## The one request
- When it starts (a few seconds after the window is up, and at most once every 12 hours) GameVault sends **one anonymous GET** to `https://api.github.com/repos/BlackNard/GameVault/releases?per_page=20` through the shared polite HTTP client: fixed HTTPS address, 10 s timeout, 512 KB limit, no redirects, no cookies, no account.
- Nothing about you or your PC is sent: only a generic `GameVault/1 (desktop game library)` user agent. GitHub sees the usual network address of any web request.
- The time of the last check is kept in `Cache/update-check.txt`.
- Failures (offline, rate limit, odd answer) are quiet at start-up; **Check now** shows them in plain words.
- Turn it off in Settings > Updates ("Check for new versions when GameVault starts"). **Check now** still works. Stored as `checkForUpdates` in `Data/settings.json` (default on).

## How versions are compared
`GameVault.Core/ReleaseVersion.cs` follows semantic versioning with an optional leading `v` and a fourth number: `0.1.0-preview.3` is newer than `0.1.0-preview.2`, `preview.10` is newer than `preview.9`, and `0.1.0` is newer than every `0.1.0-preview.N`. Draft releases and entries without a valid tag are ignored. A link is only accepted (and only opened, when you click it) if it points at `https://github.com/BlackNard/GameVault/releases/...`.

## When you prepare a release
The app's own version is `<GameVaultVersion>-preview.<GameVaultPreview>` from `Directory.Build.props` (shown by `AppVersionInfo.Current`). **Set `GameVaultPreview` to the number of the preview you are about to publish** (the draft tag is `v<version>-preview.N`, pass it with `-Tag` to `create-draft-release.ps1`) before the build the release is made from. Builds made after publishing a preview and before the next bump report that same preview and are not offered an update by themselves.

If the repository is renamed again, update `UpdateChecker.DefaultRepository`.
