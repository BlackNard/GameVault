# GameVault

A Windows desktop game library for launching manually added games, tracking playing time, and preserving local save backups.

## Project status

Planning stage. The application and installer have not been built yet. The GitHub repository is intended to be private during development.

## First-version scope

- Add games by selecting their executable and configuring their save files or folders.
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
      cover.jpg
      Backups/
        <dated-backup>/
```

The structure is illustrative; the storage format and technology stack are not finalized. Stable profile identifiers are proposed so renaming a game does not disconnect its records. Games keep their original save locations; GameVault stores independent backup copies.

## Installation and releases

The first completed version will provide a downloadable Windows setup wizard through GitHub Releases. The wizard will install GameVault's executable and required application files. Updates and uninstallation must preserve user profiles, statistics, and backups by default.

See [installer and release requirements](docs/installer-and-releases.md).

## Privacy

Actual game saves, local profiles, play histories, credentials, game executables, and game installation files must never be committed to this repository.

## Decisions still open

- Desktop framework and installer tooling.
- Supported Windows versions and CPU architectures.
- Final interface design and storage schema.
- Licensing and signing of release installers.

No open-source license has been selected.
