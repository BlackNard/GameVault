# GameVault

A Windows desktop game launcher for manually added games, tracking playing time, automatically discovering saves, and preserving dated local backups. The first versions have no store or cloud synchronization.

## Project status

The Windows app supports a local game library, native executable browsing, key-free in-app portrait cover search, reversible removal, launching/exit detection, live playtime, and checkpoint-based interrupted-session recovery. Sleep/hibernate and unobserved downtime are excluded from recorded durations. Versioned local storage includes safe previous copies, diagnostics, and a remembered Midnight/Crimson theme. Session history now includes date/time, longest-session navigation, date-grouped activity and audited manual corrections. Bounded automatic save-source discovery is available, including first-session retries and confirmation for ambiguous candidates. Changed saves now receive verified dated backups after confirmed exit, and Back up now is available while the game is closed. Backup history supports pins and per-game retention: keep the latest 20 unpinned copies by default, plus every pinned copy, or keep everything. Restore now supports reviewed destinations, verified pre-restore recovery, rollback and undo. GameVault can stay in the system tray while tracking and backups continue, with configurable close behavior and quiet backup alerts. A development setup wizard and clean portable package are available through the private review workflow. The repository remains private. Session notes and game reviews now support Read/Edit modes and offline Markdown/PDF exports; see the [notes test checklist](docs/design/notes-manual-tests.md).

Play automatically requests Windows administrator approval when a game requires elevation. GameVault's main window stays at normal permissions; a one-shot helper starts the game and transfers observation handles. Cancelling approval leaves the game unstarted and allows retry.

See [cover search and its limits](docs/portrait-cover-search.md), [development instructions](docs/development.md), [foundation verification](docs/verification/foundation.md), and the [issue roadmap](https://github.com/Zaid-Alhmouz/GameVault/issues/1).

See [persistence and recovery behavior](docs/persistence.md) for the schema, write safeguards, and owner manual checks.

## First-version scope

- Add games by selecting their executable and configuring their save files or folders.
- Discover save locations automatically, including saves created during the first launch; keep manual selection as a fallback. See [first-launch save requirements](docs/save-discovery-requirements.md).
- Launch games from GameVault and record sessions, total playing time, and last played.
- Count menus, pauses, and time away while the game runs; exclude computer sleep time.
- Support manual playtime corrections and daily, weekly, and monthly statistics.
- Organize games as Not started, Playing, Completed, or On hold. Change Not started to Playing only after a successful first launch.
- Store each game's settings, statistics, and dated save backups in its own profile folder alongside the application's files and data.
- Back up changed saves after the game exits and provide a manual backup command.
- Keep the latest 20 unpinned backups per game by default, plus all pinned backups. Make retention configurable.
- Restore an older backup only after preserving the current saves.
- Keep profiles and backups when the game is uninstalled or removed from the library unless the user explicitly chooses to delete profile data.

Resident Evil 2 Remake, launched directly without a store launcher, is the initial validation game. Its actual save location must be confirmed on the test installation.

## Local folder structure

```text
GameVault/
  GameVault.exe
  Data/
    settings.json
  Profiles/
    <stable-game-id>/
      profile.json
      statistics.json
      launch.json (optional actual-process target)
      Sessions/<session-id>.json
      save-discovery.json (independent source mappings)
      Corrections/<correction-id>.json
      Notes/Sessions/<session-id>.json
      Notes/review.json
      Media/<session-id>/<dated-capture>.png or .mp4 (with metadata)
      cover.png
      Backups/
        <dated-backup>/
```

The approved foundation uses C#/WPF on .NET 10 with Data and Profiles beside the executable. Each game uses a stable profile identifier so renaming it preserves its records. See [persistence](docs/persistence.md) for the implemented storage schema. Games keep their original save locations; verified independent copies are stored under each profile folder in Backups. Retention runs only after a new verified copy or explicit Apply retention; pinned, latest and recovery copies are protected.

## Installation and releases

The first completed version will provide a downloadable Windows setup wizard through GitHub Releases. The wizard will install GameVault's executable and required application files. Updates and uninstallation must preserve user profiles, statistics, and backups by default.

See [installer and release requirements](docs/installer-and-releases.md) and the [private draft release procedure](docs/release-process.md).

## Privacy

Actual game saves, local profiles, play histories, credentials, game executables, and game installation files must never be committed to this repository.

## License

GameVault is **source-available, all rights reserved**: you can read the code and use the official installer for yourself, free of charge, but you may not copy, modify or redistribute it without permission. See [LICENSE](LICENSE). Third-party components keep their own licenses ([THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)).

## Decisions still open

- Final owner functionality/UI acceptance.
- Code signing of release installers (the installer is unsigned, so Windows shows an unknown-publisher warning).

See the [session history test checklist](docs/design/session-history-manual-tests.md) for issue #8, including timezone estimates and correction safeguards.

See [save discovery scope and deferred UAT](docs/design/save-discovery-manual-tests.md). Source detection leaves live saves in place; finding a source alone is not a verified backup.

See [dated backup scope and deferred checks](docs/design/backups-manual-tests.md) for limits, failure behavior and the final owner testing checklist.

See [backup history and retention](docs/design/backup-history-manual-tests.md) for browsing, storage totals and the deferred final checks.

See [safe restore and undo](docs/design/restore-manual-tests.md) for file ownership, recovery behavior and the deferred final checks.

See [system tray behavior](docs/design/system-tray-manual-tests.md) for close versus quit, checkpoint warnings and the deferred final checks.

The [app validation record](docs/validation/app-acceptance.md) separates automated fixture evidence from pending owner gameplay/UI acceptance. Run scripts/verify.ps1 to regenerate the per-run workflow reports; a green automated run does not close #14 or approve first-version acceptance.

The first-phase candidate adds PNG screenshots, H.264 MP4 recording, configurable capture shortcuts and a floating control panel. The redesigned cover flow uses explicit browser search and local image import without a personal API key; local covers and launching work offline. Captures belong to the actual tracked session in its profile. Recording is silent unless game-only audio is explicitly enabled. The panel works with windowed/borderless games; exclusive fullscreen, elevated or protected content can limit capture. Entire-monitor capture requires explicit selection. See [the final manual checklist](docs/phase-one-manual-testing.md), [provider decisions](docs/artwork-provider.md) and [performance measurements](docs/performance-results.md). These features await owner manual acceptance.

The approved launcher redesign uses a library-only cover grid and dedicated same-window game pages. Backup destinations can be set globally or per game, retaining earlier locations. Cover selection now uses explicit browser image search and local preview/import without a user API key. See the [redesign manual checklist](docs/redesign-manual-testing.md).
