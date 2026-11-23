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

The entire publish folder is required. Copying only GameVault.exe is insufficient. Clean ZIP and Inno Setup packages are created by the verification/release scripts. Updating bundled runtimes requires rebuilding and distributing GameVault.

## Dependencies and CI

Package references use exact versions and committed `packages.lock.json` files, including the win-x64 restore graph. Update references and run `dotnet restore GameVault.slnx --force-evaluate` deliberately when changing dependencies or framework settings. Review and commit resulting lock changes. Ordinary verification must not rewrite them.

The Windows foundation GitHub Actions workflow runs the same script on feature-branch pushes and pull requests targeting main. Action revisions are pinned by commit SHA. The `build` job reads source, builds the installer, verifies its lifecycle on a disposable runner and prepares private assets without publishing or merging. The slow, independent synthetic capture-encode probe and performance harness run as their own parallel jobs. The `verify` job is the single PR check: it passes only when all three succeeded. The NuGet package cache is restored by `actions/setup-dotnet` (keyed on every `packages.lock.json` and `global.json`). The optional exact-draft smoke job has narrowly scoped write access required by GitHub to read private drafts.

### App version

The app version is defined once, as `<GameVaultVersion>` in the root `Directory.Build.props` (which also feeds `<Version>`). `scripts/AppVersion.psm1` reads it; `build-installer.ps1` refuses a publish folder built from a different version and passes the value to `installer/GameVault.iss`, and the release scripts take the version from the installer manifest. `scripts/test-version.ps1` (run by `verify.ps1`) checks that no project or the installer script sets its own version and that the published `GameVault.exe` matches. To release a new version, change only that one line.

### Dependency updates (Dependabot)

`.github/dependabot.yml` opens at most one grouped weekly pull request for NuGet and one for GitHub Actions. Dependabot edits the exact version in the project files but does not reliably regenerate `packages.lock.json`, and CI restores in locked mode, so a NuGet update PR is expected to fail until the locks are refreshed. Review step for the owner (or the person working for them):

1. Read the changelog of each bumped package; be wary of major versions and of packages that ship native or notice files (WebView2, ScreenRecorderLib, PDFsharp). Check the licence of every new version too: PDFsharp 6.5.0 is AGPL-3.0/commercial, so PDFsharp is pinned at the MIT release 6.2.3 and ignored by Dependabot.
2. Check out the PR branch and run `dotnet restore GameVault.slnx --force-evaluate`, plus the same for `tests/PerformanceHarness` (it is outside the solution); commit the lock changes to the PR branch and review the lock diff.
3. Run `./scripts/verify.ps1`; update `THIRD-PARTY-NOTICES.txt` if a package or its licence changed.
4. Merge only when CI is green. Actions PRs keep the 40-character SHA and the `# vN` comment; check that both changed and that the new SHA belongs to the release tag.


## Boundaries and data

- `GameVault.App`: WPF presentation/startup; receives its root from AppContext.BaseDirectory.
- `GameVault.Core`: layout model and future domain logic without WPF dependencies.
- `GameVault.Infrastructure`: exclusive application-folder lease and writeability checks; later filesystem/Windows services.
- `GameVault.Capture`: lazy Windows x64 native capture adapter; missing capture prerequisites do not block library startup.
- `GameVault.Tests`: domain, path safety and real filesystem concurrency tests.

The development window opens the executable's folder asynchronously, holds the exclusive `.gamevault.lock` handle until exit, initializes versioned settings/profile storage, and restores the saved theme. Closing during a settings save waits for that operation. The empty coordination file remains after exit and does not indicate an active instance; only the OS-held handle does. Different portable directories can run independently. Missing/busy/unwritable folders produce a startup message rather than redirecting user data elsewhere.

Data and Profiles are initialized below the app root. See [persistence.md](persistence.md) for schema 1, read-only recovery, and write safety. Preserve both across program updates/uninstallation. Never commit profiles, saves, game files, logs containing personal information, credentials, installers, or published binaries.

## Design and unresolved distribution decisions

The owner approved the [foundation design](superpowers/specs/2026-11-10-foundation-design.md) on 2026-11-10. It documents storage safety, versioning, timing, backup/restore boundaries, and performance targets for later tickets. These architectural rules do not imply those features are already implemented.

No repository license has been selected. Inno Setup is pinned by the build scripts. Signing identity/costs, public distribution, and minimum hardware requirements remain open. Do not infer public-release authorization from the private development repository.
