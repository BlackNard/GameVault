# Windows setup wizard implementation plan

> Execute inline with one focused final review; retain the owner workflow and defer manual UI checks.

**Goal:** Build a repeatable, versioned Windows installer without risking portable user data.

**Architecture:** Inno Setup 7.1.1 packages a clean self-contained win-x64 publish. A generated explicit file list excludes personal data. Setup and uninstall hold the same exclusive `.gamevault.lock` as the app while changing files. The per-user wizard supplies folder selection, shortcut tasks, optional launch, version registration, and a preserving uninstaller.

**Files:** `installer/GameVault.iss` owns the wizard and directory/lease guards; `scripts/InstallerPayload.psm1` validates input and emits the owned-file list; `scripts/build-installer.ps1` compiles/checksums; `scripts/get-installer-compiler.ps1` bootstraps a pinned signed portable compiler; `scripts/test-installer.ps1` tests payload boundaries without executing setup. Update build CI and installer documentation.

- [x] Write and observe failing synthetic payload tests for complete runtime, private directories, fixture content, unsafe names, and reparse points.
- [x] Implement payload validation and explicit file entries; rerun tests.
- [x] Implement wizard guards, portable compiler bootstrap and compile script. Compile the actual installer; do not execute it on the owner's machine.
- [x] Add CI packaging checks and document reproducible commands, preservation contract, signing/release limits, and deferred lifecycle checks.
- [x] Obtain one focused read-only review; fix findings, run locked verification and compile/package.
- [x] Create clean ZIP and installer before seeding a separate local build through the private helper; commit, draft PR and await CI.

Lifecycle acceptance (clean install/upgrade/uninstall and UI) belongs to issue #16 and the owner's final pass. This build does not approve a public release.
