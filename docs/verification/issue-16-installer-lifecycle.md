# Issue #16 installer lifecycle checks

The automated gate combines local headless fixture tests, payload/lease checks, and actual silent Inno setup/uninstall execution on a disposable GitHub-hosted Windows runner. `scripts/verify-installer-lifecycle.ps1` refuses local and self-hosted execution before creating directories or starting an installer. It also refuses pre-existing GameVault default installations, registration and shortcuts.

## Scenarios

- Fresh default per-user install and custom folder containing spaces; explicit payload hashes, shortcut targets/working directories and per-user version registration.
- Synthetic prior version 0.0.1 built from the current source, upgraded to the current product version. This is packaging lifecycle coverage, not a historical release compatibility claim.
- Two stable profiles, two completed sessions (7,200 total seconds), two session notes, covers, unknown retained files, and four backup payloads (two pinned, two unpinned).
- Windowless fixture starts its apphost inside the installed folder. Only fixture entry files are copied; runtime and GameVault infrastructure are loaded from the actual installed payload and their locations are asserted. This shows bundled runtime use without launching a game or GameVault UI. It is not a substitute for a development-tools-free Windows image.
- Actual setup and uninstaller refuse an active application-directory lease while the windowless fixture continues running. Existing payload and complete library hashes remain intact.
- A real installed assembly is locked during upgrade, forcing an installation failure; Data/Profiles fingerprints must remain unchanged. No disk-filling operation is performed or claimed.
- Successful upgrade reads the library through installed storage code and preserves byte-identical relative file/directory names and SHA-256 hashes across complete Data/Profiles.
- Actual uninstall removes installer-owned files/shortcuts/registration while retaining the complete library; reinstall reuses it and reads notes, sessions, pins and verified backup bytes.
- Data-directory and junction targets are refused without installing payload or changing existing data.

Each run writes `artifacts/installer-lifecycle.json` and a uniquely named evidence directory with scenario observations, exact Windows/architecture information, setup logs and probe diagnostics. CI retains only synthetic evidence as an artifact for seven days. `automatedPassed` must be true; a failed run keeps partial evidence and fails CI. Installer/probe waits are bounded. Only test-created child processes may be stopped on a timeout. No owner app/game is launched or stopped.

Local verification adds four synthetic fixture tests and five PowerShell guard/fingerprint/cleanup-wait checks to the existing suite. Full restore/build/test/publish and installer compilation remain required. The linked fixture source and entry points are test-only and excluded from clean publish, ZIP and setup payloads.

## Still pending before closing #16 / release

- Clean supported Windows client machines without development tools, and the supported Windows 10/11 matrix (the hosted Windows runner records its own OS, not client coverage).
- Interactive wizard cancellation before/during installation, optional launch, visual behavior, and real GameVault launch on that clean machine.
- Final owner functionality/UI/game checks and performance acceptance remain governed by #14/#18 and the existing deferred-manual workflow.

Issue #16 stays open for this final acceptance. Passing automated lifecycle checks does not approve a public release or signing.

Command and exit-code behavior follow the official [setup parameters](https://jrsoftware.org/inno-setup/documentation/topic_setupcmdline.htm), [uninstall parameters](https://jrsoftware.org/inno-setup/documentation/topic_uninstcmdline.htm), and [setup exit codes](https://jrsoftware.org/inno-setup/documentation/topic_setupexitcodes.htm).

The initial real lifecycle run caught an immediate-reinstall race in the test driver: Inno's original uninstall process exits before the cleanup process calls DeinitializeUninstall and releases the application lock. The driver now waits for actual lease release with a bounded, regression-tested poll; it never deletes the coordination file or weakens mutual exclusion. This follows the [official Inno 7.1.1 uninstall source](https://github.com/jrsoftware/issrc/blob/is-7_1_1/Projects/Src/Setup.Uninstall.pas).
