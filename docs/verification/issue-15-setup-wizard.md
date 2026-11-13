# Issue #15 automated verification

- Locked .NET restore, Release build, full tests and clean self-contained win-x64 publish passed.
- 243 app tests passed (215 core/infrastructure, 28 headless Windows app tests), zero failures/skips.
- 15 installer boundary/Win32 exclusive-lease checks passed. These reject personal/reserved content, unsafe names, reparse directories and incomplete runtime payloads, and verify mutual exclusion between managed and native leases without changing lease contents.
- Official Inno Setup 7.1.1 compiled the actual versioned development installer, with SHA-256 and explicit payload hashes. Compiler package and ISCC binary identities are pinned and Authenticode validated.
- Clean portable ZIP inspected for expected file count and absence of Data/Profiles/Backups and test fixtures. Installer copied with checksum verification before local-only private library seeding.
- One focused independent final review found no actionable defects.
- CI now repeats full app verification, installer boundary/lease checks, and actual setup compilation.

No setup/uninstall, native wizard UI, or real game was executed on the owner's machine. Clean installation, upgrade, uninstall, shortcut/UI behavior and supported-Windows acceptance remain pending issue #16 and the owner's deferred final pass. Public release approval, signing/distribution, real-game/UI acceptance (#14), and performance acceptance (#18) remain separate.

Icon polish: custom multi-size ICO embedded in the app host and WPF resource, used by the main window, tray and setup. Headless resource/tray decoding passed; extracted published executable icon matches the source asset pixel-for-pixel at 32px. Persistent Close/Quit sidebar note removed. Focused review found no actionable defects.
