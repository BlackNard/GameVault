# Developing GameVault

## Prerequisites

Use supported Windows 11 x64, PowerShell, Git, and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). `global.json` pins SDK 10.0.404 and permits newer patches in that feature band, excluding previews. An installed .NET runtime alone cannot build the project.

The development SDK may be unpacked into a private writable tools folder. Add that folder to the current terminal's PATH and set DOTNET_ROOT to it; no machine-wide SDK installation is required. The SDK and NuGet cache are development tools and must not be committed. The distributed app bundles its own runtime.

## Fresh checkout

Run these commands from the repository root:

```powershell
dotnet --version
./scripts/verify.ps1
```

Verification restores dependencies in locked mode, builds all projects with warnings treated as errors, runs the test suite, publishes a self-contained win-x64 application, and checks that runtime files are present and user profiles are absent. Each run publishes to a new `artifacts/publish-<id>` folder to avoid accidentally including stale files. The latest successful output path is recorded in `artifacts/verification.json`. A failed run exits with an error; do not treat an older verification.json as evidence of the failed run succeeding.

To run the development app:

```powershell
dotnet run --project src/GameVault.App/GameVault.App.csproj
```

To launch the latest verified standalone build:

```powershell
$build = Get-Content -Raw artifacts/verification.json | ConvertFrom-Json
& $build.executable
```

The entire publish folder is required. Copying only GameVault.exe is insufficient. A zip of the complete folder is a possible future portable distribution; the installer comes later in issue #15. Updating bundled runtimes requires rebuilding and distributing GameVault.

## Dependencies and CI

Package references use exact versions and committed `packages.lock.json` files, including the win-x64 restore graph. Update references and run `dotnet restore GameVault.slnx --force-evaluate` deliberately when changing dependencies or framework settings. Review and commit resulting lock changes. Ordinary verification must not rewrite them.

The Windows foundation GitHub Actions workflow runs the same script on feature-branch pushes and pull requests targeting main. Action revisions are pinned. Its token can read source only; the workflow does not publish releases, merge changes, or build an installer.

## Boundaries and data

- `GameVault.App`: WPF presentation/startup; receives its root from AppContext.BaseDirectory.
- `GameVault.Core`: layout model and future domain logic without WPF dependencies.
- `GameVault.Infrastructure`: exclusive application-folder lease and writeability checks; later filesystem/Windows services.
- `GameVault.Tests`: path safety and real filesystem concurrency tests.

The foundation window deliberately contains no game-management functionality or final theme. It opens the executable's own folder asynchronously, verifies actual write access, and keeps an exclusive `.gamevault.lock` handle until exit. The empty coordination file remains after exit and does not indicate an active instance; only the OS-held handle does. Different portable directories can run independently. Missing/busy/unwritable folders produce a startup message rather than redirecting user data elsewhere.

Data and Profiles are reserved below the app root and are not created by this foundation build. Future issue #4 implements their versioned persistence. Preserve both across program updates/uninstallation. Never commit profiles, saves, game files, logs containing personal information, credentials, installers, or published binaries.

## Design and unresolved distribution decisions

The owner approved the [foundation design](superpowers/specs/2026-11-10-foundation-design.md) on 2026-11-10. It documents storage safety, versioning, timing, backup/restore boundaries, and performance targets for later tickets. These architectural rules do not imply those features are already implemented.

No repository license has been selected. Installer tooling, signing identity/costs, public distribution, and minimum hardware requirements remain open. Do not infer public-release authorization from the private development repository.
