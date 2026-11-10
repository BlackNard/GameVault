# Installer and release requirements

## Agreed distribution intent

- Project name: GameVault.
- GitHub visibility: private during development.
- Users download a Windows setup wizard from GitHub Releases once the first version is complete and validated.
- The wizard installs the executable and required application files.
- User profiles, statistics, and save backups live alongside the application's files and data.

Private repository releases require repository access. Public downloads will require making the repository public or agreeing on a separate public distribution repository later.

## Proposed installer behavior

These are implementation recommendations, not finalized tooling choices:

1. Offer a per-user writable installation folder and allow a custom writable location. Avoid a default location that needs administrator permission for routine profile and backup writes.
2. Install all required application files, with any runtime requirements addressed by the selected packaging approach.
3. Offer Start menu and optional desktop shortcuts.
4. Offer to launch GameVault after installation.
5. Register an uninstaller.
6. Preserve Data and Profiles during upgrades and uninstallation. Any future removal of personal data must be a separate, explicit choice.
7. Refuse an upgrade while GameVault is running, or arrange a clean application shutdown before replacing program files.

## Release assets

Publish a tested installer with a versioned filename, for example `GameVault-Setup-0.1.0.exe`, as a GitHub Release asset. Add release notes, supported operating systems, and a SHA-256 checksum. Generated installers belong in release assets, not source-control history.

The installer format, version number, architecture suffix, and signing approach remain to be selected. No installer or automated build workflow exists yet.

## Validation before the first release

- Install and launch on a clean supported Windows environment.
- Add a directly launched game and confirm session tracking, including menus, pauses, sleep/resume, and unexpected interruption.
- Confirm post-session backup, unchanged-save handling, configurable retention, and pinned backup preservation.
- Restore a known backup while retaining the previous saves and verifying that the restored game loads correctly.
- Confirm backups remain available after the game is uninstalled.
- Upgrade GameVault without losing profiles, statistics, or backups.
- Uninstall GameVault and verify that user data remains.
- Confirm source files and release assets contain no personal data or actual game files.

## Reference

[Managing releases in a repository — GitHub Docs](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
