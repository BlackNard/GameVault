# GameVault

A Windows desktop game library for launching manually added games, tracking playing time, and preserving local save backups.

## Project status

The Windows foundation is in development: a minimal WPF starter window, application-folder safety checks, and a repeatable self-contained build. Game features and the installer are not implemented yet. The repository remains private during development.

See [development instructions](docs/development.md), [foundation verification](docs/verification/foundation.md), and the [issue roadmap](https://github.com/Zaid-Alhmouz/GameVault/issues/1).

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

The approved foundation uses C#/WPF on .NET 10 with Data and Profiles beside the executable. Each game will use a stable profile identifier so renaming it preserves its records. The detailed storage schema is still to be implemented. Games keep their original save locations; GameVault stores independent backup copies.

## Installation and releases

The first completed version will provide a downloadable Windows setup wizard through GitHub Releases. The wizard will install GameVault's executable and required application files. Updates and uninstallation must preserve user profiles, statistics, and backups by default.

See [installer and release requirements](docs/installer-and-releases.md).

## Privacy

Actual game saves, local profiles, play histories, credentials, game executables, and game installation files must never be committed to this repository.

## Decisions still open

- Installer tooling (the approved app foundation uses C#/WPF on .NET 10, targeting supported Windows 11 x64).
- Final interface design and storage schema.
- Licensing and signing of release installers.

No open-source license has been selected.
