# Installer and releases

GameVault uses **Inno Setup 7.1.1 (x64 compiler)** to package the self-contained .NET 10 Windows x64 application. The setup wizard is implemented for issue #15; installation lifecycle acceptance remains in #16 and the owner's final manual pass. No public release is approved by a successful compile.

## Wizard behavior

- Per-user installation, without requesting administrator access; default `%LOCALAPPDATA%\Programs\GameVault`. A custom writable local folder can be selected. Drive roots, paths inside Data/Profiles, and redirected/reparse paths are rejected.
- Windows x64 only; the installer enforces Windows build 19041 or later. The supported/tested Windows matrix is still pending issue #16. ARM64/native ARM distribution is not provided by this package.
- All published app files and the .NET/Windows Desktop runtimes are included. No separate runtime download is needed at installation time.
- Start menu shortcut selected by default; desktop shortcut optional. Launch after installation is optional, unchecked by default, and skipped in silent mode.
- Stable AppId registers per-user version/uninstaller information and allows reinstall/upgrade into the previous folder.
- `Data` and `Profiles` remain beside the executable, including stable profile IDs, statistics, sessions, notes, covers, recovery state and backups. Neither directory is packaged or registered for uninstall deletion. There is no recursive cleanup of the installation folder. Other files not installed by setup are also left alone. Reinstall into the same folder to reuse retained data.
- Upgrade/uninstall do not close GameVault automatically. Quit from the tray menu first; closing the window may leave it running. The wizard acquires the same exclusive `.gamevault.lock` used by ApplicationWorkspace. It holds this lease through file replacement/removal and releases it before optional post-install launch. A busy/unwritable folder blocks changes with a retry message. The lock file remains in place to prevent two independent leases.
- Windows or file permission errors are reported by setup; no reboot-based replacement is requested. No personal-data removal option exists.

The installer does not move an existing portable library into a different installation directory. Keep a complete separate backup before moving libraries; uninstall/reinstall alone is not a library migration tool. A custom install directory must be writable for the same Windows user who will run GameVault.

## Repeatable development build

On Windows x64 with the repository's pinned .NET SDK and PowerShell 7:

```powershell
./scripts/verify.ps1
./scripts/test-installer.ps1
$verified = Get-Content artifacts/verification.json -Raw | ConvertFrom-Json
./scripts/build-installer.ps1 -PublishDirectory $verified.publishDirectory
Get-Content artifacts/installer.json
```

`build-installer.ps1` validates the complete publish before generating an explicit source/destination list. It rejects Data/Profiles/Backups, save files, local lock/probe files, fixture executables, credential-like files, unsafe filenames, and reparse points. It does not consume a personal seeded test build. Assembly product version determines the setup filename/version.

The compiler bootstrap downloads the official pinned installer into ignored `work/tools/inno-7.1.1`, verifies its SHA-256 and valid Pyrsys B.V. Authenticode signature, and invokes the official **portable** mode silently with no shortcuts or uninstall registration. The ISCC executable is also signature/hash checked. No global compiler installation is required. Network access is needed for the first tool download; existing verified tools can be reused. Inno Setup's own noncommercial/commercial licensing terms apply to the build tool; review them before commercial use.

Each compile creates a fresh `artifacts/installer-<id>` directory with:

- `GameVault-Setup-<version>-win-x64.exe` and a `.sha256` checksum;
- `manifest.json` containing version, compiler identity, installer checksum, and per-file payload hashes;
- generated explicit file entries and destination guards for audit.

`artifacts/installer.json` points at the latest successful compile. Manifests contain local build paths; they are build evidence, not intended public release assets. CI runs the full app verification, synthetic installer boundary/Win32 lease checks, actual compiler, and silent install/upgrade/uninstall on a disposable GitHub-hosted Windows runner. Setup is never executed by this workflow on the owner's machine. Tests are automated evidence only; they do not claim end-to-end installation success.

## Release and signing

The current installer is an **unsigned development artifact**. Signing and a certificate are a separate release decision. Do not bypass Windows security controls. No setup executable, personal library, private vault, or local handoff manifest belongs in Git.

Once all release acceptance is complete, GitHub Releases can distribute the versioned installer, SHA-256, release notes, and supported Windows matrix. The repository is private, so downloads require repository access; public distribution will require a visibility/distribution decision. Release publishing/automation belongs to #17. Clean downloadable ZIPs and installers must be produced before a separate local development directory is seeded from the private test-data vault.

## Installation lifecycle acceptance (#16)

[Automated lifecycle coverage and evidence limits](verification/issue-16-installer-lifecycle.md) describe the real setup/uninstall CI scenarios. The lifecycle command refuses local/self-hosted execution. Interactive cancellation, clean client-machine and owner UI acceptance remain pending; #16 stays open for those checks.

Use a disposable Windows environment for automated lifecycle execution and the owner's final UI pass:

- clean install into default/custom writable folders, shortcut choices, optional launch and version registration;
- runtime independence on a clean supported Windows machine;
- running/tray-hidden app blocks upgrade/uninstall, and an app cannot start while replacement/removal holds the lease;
- upgrades, canceled/failed upgrades, and uninstall preserve byte-identical complete Data/Profiles, stable IDs and recovery state;
- uninstall removes only installer-owned files/shortcuts/registration; reinstall into retained data works;
- unwritable, redirected, invalid, and insufficient-space targets fail safely; no real game is killed;
- final source/package inspection and owner real-game/visual acceptance (#14), plus performance acceptance (#18).

## Tool references

- [Official Inno Setup download and license information](https://jrsoftware.org/inno-setup/downloads)
- [Official portable compiler mode](https://jrsoftware.org/inno-setup/documentation/topic_technotes.htm)
- [Official 7.1.1 compiler release](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_1)
- [Setup/uninstall event hooks](https://jrsoftware.org/inno-setup/documentation/topic_scriptevents.htm)
- [Per-user privilege behavior](https://jrsoftware.org/inno-setup/documentation/topic_setup_privilegesrequired.htm)
