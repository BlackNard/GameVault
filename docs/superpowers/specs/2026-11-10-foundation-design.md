# GameVault Windows foundation

Status: approved by the owner on 2026-11-10. Implementation and verification are still required to close issue #2.

Scope: [foundation issue #2](https://github.com/Zaid-Alhmouz/GameVault/issues/2), with architectural boundaries for the agreed first version.

## Purpose and agreed requirements

GameVault is an offline Windows desktop library for manually added games. The first real-game validation target is a directly launched Resident Evil 2 Remake installation. No store integration or account is needed.

The owner has agreed to launching games through GameVault, recording their running lifetime including menus/pauses/time away, excluding sleep, per-game folders beside the executable, automatic changed-save backups after exit, manual backups, 20 unpinned backups plus all pinned backups by default, configurable retention, safe restoration, and manual library statuses. A successful first launch changes Not started to Playing. Existing tickets also cover statistics, manual corrections, tray behavior, and UI quality.

The app must preserve profiles and backups when games are removed. Installer upgrades and uninstallation must preserve Data and Profiles. The repository remains private. Public distribution, licensing, and installer signing have not been approved.

## Proposed engineering choices

The owner approved these engineering recommendations on 2026-11-10.

| Choice | Recommendation | Rationale |
|---|---|---|
| Language and UI | C# and WPF using .NET 10 LTS | Windows integration, flexible XAML styling, mature tooling, and ordinary desktop deployment fit the project. |
| Initial supported platform | Supported Windows 11 versions on x64 | A focused initial release and test matrix. Windows 10, native ARM64, macOS, and Linux are outside the first release's support commitment. |
| Runtime distribution | Self-contained win-x64 folder publish | Bundle the runtime with the executable and dependencies so users do not need a separate .NET installation. Runtime servicing requires rebuilt app releases. |
| UI structure | WPF views and view models; independent core/platform services | Keep presentation, timing, storage, and Windows integration separately testable. |
| Data | Versioned UTF-8 JSON per game and for global settings | Fits the requested independent profile folders without requiring a central database. |
| Backups | Verified, uncompressed full snapshots initially | Simple independent recovery; compression and incremental storage can be evaluated later. |
| Installer | Decide tooling in issue #15 | Preserve the ordinary writable folder distribution contract; build the app before its wizard. |

Alternatives considered: WinUI 3 is Microsoft's recommended framework for new Windows apps and offers modern native controls; its Windows App SDK deployment adds another layer to this project's initial setup. Electron fits a web-oriented team and flexible styling but introduces a Chromium runtime. WPF is the recommended fit for this focused Windows utility, not a claim that it is universally faster or more modern than either alternative.

## Installation and portable directory contract

Proposed installer default: `%LOCALAPPDATA%\Programs\GameVault`. Users can select another writable folder, such as `D:\GameVault`. Ordinary app operations do not require administrator privileges. Do not silently redirect profile data to an unrelated directory.

```text
GameVault/
  GameVault.exe
  [runtime and application dependencies]
  Data/
    settings.json
    library.json
  Profiles/
    <stable-game-id>/
      profile.json
      statistics.json
      cover.jpg
      Backups/
        <UTC-date-and-unique-suffix>/
          manifest.json
          files/
```

Resolve the application root from the executable's directory, not the current working directory. If it is unwritable, show the actual folder and instructions to move/reinstall into a writable location before accepting profile changes. A copied folder carries records and backups; external executable/save paths can need repair. A single-instance lock prevents simultaneous writers to one application root. Multiple separate portable copies are allowed, but each must prevent duplicate active operations within its own data root.

Data and Profiles are created at runtime, ignored by Git, excluded from built application artifacts, and never owned as removable program files by the installer. No real save files, game binaries, or play histories belong in source control or releases.

## Project boundaries

- `GameVault.App`: WPF startup, views/view models, asynchronous commands, UI state, and composition of services.
- `GameVault.Core`: game/session models, statuses, duration rules, backup policies, contracts, and validation with no WPF dependency.
- `GameVault.Infrastructure`: Windows process/power integration, filesystem persistence, backup operations, and runtime path resolution.
- Test project(s): meaningful checks for path/data safety and core behavior; later feature tickets introduce their own tests.

Do not introduce a web server, cloud service, plugin architecture, or elaborate framework merely to launch the foundation shell. Package/library versions and exact namespace layout are builder decisions. Record and lock versions when implementation begins.

## Storage and safety rules

Every persistent document has a schema version. Each profile has a stable identifier independent of its display name. The global library index contains identifiers/order/visibility, not the sole copy of game records; it can be reconstructed from profiles if damaged. Profile documents hold configured executable paths, working directory, save-source mappings, status, and preferences. Session/adjustment records retain enough detail to recompute totals.

Writes are serialized per profile. Write a temporary file on the same volume, flush and close it, then replace the destination atomically where supported. Keep a recoverable previous version. An initial write uses a non-overwriting move into place. Never replace unreadable data with an empty default. Unknown newer schema versions open in a clearly explained non-writing state; migrations preserve the original and are verified before activation. Multi-file changes require recoverable ordering or a journal; file replacement alone is not a multi-file transaction.

Validate paths at the storage boundary. Relative manifest paths cannot escape a profile or a configured save destination. Explicitly reject or safely handle links/junctions to avoid copying unrelated directories. Do not store credentials. Diagnostic logs must not contain save contents or unnecessary personal paths.

## Sessions and backups

Launch success means the configured game process is actually observed, not merely that Play was clicked. Each session has a stable ID, observed start/end, measured duration, checkpoint, and recovery status. Use an injectable elapsed-time source with explicit suspend handling; do not calculate duration solely by subtracting wall-clock timestamps. Record UTC instants and render in local time. Checkpoint no less frequently than every 30 seconds during an active session; after interruption do not invent time past the last trustworthy observation. Manual corrections remain distinguishable from measured sessions. Detailed process handoff and sleep/resume implementations belong to #6 and #7.

Save sources stay in their original locations. After confirmed game exit, or a safe manual request while it is stopped, serialize backup/restore operations for that game. Build a staging snapshot, hash and verify its contents, write its manifest, and only then publish it as a completed backup. Missing sources are actionable failures rather than an instruction to replace good backups with an empty snapshot. Compare contents to the last completed backup to skip unchanged automatic snapshots. Keep partial failures separate from valid restore choices.

After a successful verified backup, retain the latest 20 unpinned completed backups plus all pinned backups, unless configured otherwise. Before restoration, verify source integrity and preserve current destination saves in a recovery snapshot; abort before replacement if preservation fails. That recovery snapshot cannot be pruned during the transaction. Define file-set replacement and rollback carefully in #12 so unrelated files remain untouched.

## Responsiveness contract

No copying, hashing, scanning, large JSON loading, or compression may block the UI thread. Work runs asynchronously with cancellation only at safe boundaries and bounded progress updates. Do not redraw continuously while idle or hidden. Load artwork on demand and use virtualized list presentation where necessary. The selected framework does not itself guarantee a smooth interface.

Proposed reference configuration for performance verification: supported Windows 11 x64, four modern logical CPU cores or more, 16 GB RAM, SSD storage, and a 60 Hz display. Record exact hardware/build and test data with each measured run. This is a benchmark configuration, not a minimum system requirement. Initial budgets for #18: feedback within 100 ms at the 95th percentile for routine actions; interactive cold start within 3 seconds with 500 fixture games; responsive scrolling with 1,000 fixture games; no sustained animation stutter at 60 Hz. CPU/memory/disk budgets are set from a measured foundation baseline before feature implementation and reviewed again against representative lower-spec hardware. These are acceptance targets, not measured achievements.

Issue #3 owns visual design and owner-reviewed interaction mockups. Issue #2's minimal shell is a development baseline, not the final interface. It must not establish an unreviewed theme or imply that later game features already work.

## What completing issue #2 requires

1. Obtain approval of this foundation design, including the proposed platform and directory policy.
2. Install the official .NET 10 SDK into an approved writable development location; the current machine has .NET 8 runtimes but no SDK. Do not replace existing runtimes or require a global system change.
3. Pin an available stable SDK feature band and dependency versions for repeatable builds.
4. Establish the small project layout and a minimal WPF window that launches, resizes, and closes cleanly. It should identify itself as a foundation build; game features are not included yet.
5. Document fresh-checkout restore/build/run and self-contained folder-publish instructions.
6. Add a least-privilege Windows CI build check on pushes/pull requests. It must restore/build/test and validate that a publish produces GameVault.exe plus runtime dependencies, with no Data or Profiles. No installer, production release, or public publishing is triggered.
7. Validate restore/build and self-contained publish locally, smoke launch the published application, and record actual results. Add focused tests only for production logic introduced in this issue; do not create empty test projects or trivial mirror tests.
8. Record licensing/signing as unresolved and update #2 with concrete evidence. Do not mark #2 complete on documentation alone.

Git changes will use a dedicated branch and pull request; implementation does not authorize automatic merging or later issues. UI work, game management, timer logic, backup engines, and the wizard remain separate tickets.

## Sources checked

- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [WPF in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [WPF threading model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)
- [WinUI 3 overview](https://learn.microsoft.com/en-us/windows/apps/winui/)
