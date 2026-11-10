# GameVault Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete issue #2 with a tested Windows starter executable and reproducible build, without implementing later game features.

**Architecture:** A minimal WPF shell composes a pure path-layout model and a small filesystem workspace guard. Core and infrastructure remain separate from presentation. Data and Profiles are reserved beside the executable and are not populated by this foundation build.

**Tech Stack:** C#, WPF, .NET SDK 10.0.404, net10.0/net10.0-windows, xUnit, GitHub Actions on Windows, self-contained win-x64 publish.

**Spec:** docs/superpowers/specs/2026-11-10-foundation-design.md

## Global Constraints

- Supported Windows 11 versions on x64.
- Bundle the runtime with the executable and dependencies so users do not need a separate .NET installation.
- Resolve the application root from the executable's directory, not the current working directory.
- Data and Profiles are created at runtime, ignored by Git, excluded from built application artifacts.
- No copying, hashing, scanning, large JSON loading, or compression may block the UI thread.
- No game features, installer, production release, licensing decision, or automatic merge in this issue.

## Review Focus

- Paths containing spaces or trailing separators must resolve to the executable root consistently (task 1 tests).
- Relative/empty roots must fail before writes, never resolve against the working directory (task 1 tests).
- A second instance targeting the same root must be rejected without affecting the first (task 1 tests and task 2 smoke).
- An unusable application directory must produce an actionable startup message and must not redirect storage (task 1 tests and task 2 startup handling).
- Build artifacts must exclude Data/Profiles and include the runtime needed on a clean machine (task 3 verification).

### Task 1: Build the project foundation and workspace guard

**Files:** global.json, Directory.Build.props, GameVault.slnx; src/GameVault.Core/ApplicationPaths.cs; src/GameVault.Infrastructure/ApplicationWorkspace.cs; projects and package locks; tests/GameVault.Tests/*.

**Interfaces:** `ApplicationPaths.FromRoot(string root)` returns Root, Data, Profiles. `ApplicationWorkspace.Open(ApplicationPaths paths)` returns a disposable exclusive lease; filesystem failures throw IOException/UnauthorizedAccessException. The caller passes AppContext.BaseDirectory, never Environment.CurrentDirectory.

- [ ] Scaffold projects and pin SDK/dependencies; remove template example classes/tests.
- [ ] Write tests first: absolute root with spaces/trailing slash yields correct child directories; relative/blank roots are rejected; opening workspace creates no Data/Profiles or leftover probe files; opening twice fails; disposal permits reopening; distinct roots can coexist; nonexistent/file-as-directory roots fail without redirection.
- [ ] Run tests against API stubs and confirm expected NotImplementedException failures.
- [ ] Implement root normalization and a disposable exclusive `.gamevault.lock` lease with an actual write/flush probe in the application root. Avoid touching game files or creating feature data.
- [ ] Run the suite and confirm all tests pass. Commit the verified foundation.

### Task 2: Launch a minimal desktop shell

**Files:** src/GameVault.App/App.xaml(.cs), MainWindow.xaml(.cs), app project and references.

**Interfaces:** consumes task 1 paths/workspace; holds the lease for app lifetime and disposes on exit. Shows an informational development window identifying this as a foundation build.

- [ ] Create a neutral WPF window with a clear foundation-build explanation and reserved profile location. Do not create nonfunctional game controls or establish the final theme.
- [ ] Initialize workspace off the UI thread using AppContext.BaseDirectory. Show a useful error for unwritable/busy folders; release any partially acquired lease on failure.
- [ ] Build and publish, then smoke launch the actual published executable, inspect its window, resize, try a second instance, and close cleanly. Confirm the first process survives the second-instance rejection.
- [ ] Record actual baseline startup/process measurements, with hardware limitations, without claiming achievement of future library benchmarks.
- [ ] Commit the runnable shell.

### Task 3: Reproduce builds and publish for review

**Files:** scripts/verify.ps1, .github/workflows/build.yml, docs/development.md, docs/verification/foundation.md, README.md, .gitignore.

**Interfaces:** `scripts/verify.ps1` uses dotnet from PATH to restore locked dependencies, build/test, publish win-x64, and reject artifacts containing Data/Profiles or lacking GameVault.exe/core runtime files.

- [ ] Document development setup and commands, directory ownership, library boundaries, data safety, and unresolved licensing/signing.
- [ ] Add a read-only-permission Windows CI workflow invoking the same verification script; use pinned action revisions and dependency lock files.
- [ ] Run the full verification script, check artifact contents and staged diff, and record results. CI success is claimed only after a run actually succeeds.
- [ ] Request one fresh whole-branch review against the spec, resolve significant findings, then rerun affected checks.
- [ ] Commit/push the feature branch, open a pull request linked to #2, and post verification evidence. Leave merging to the owner.
