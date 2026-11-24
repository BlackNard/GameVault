# Private draft release procedure

GameVault releases are prepared as **unpublished drafts** and published only by the owner. The repository is public and the license is the root `LICENSE` (source-available, all rights reserved). Packaging does not publish anything, buy a signing certificate or change repository visibility. Releases are unsigned previews until the owner decides on signing.

## Automated build and evidence

The Windows workflow runs locked restore/build/tests and a clean self-contained publish, includes the exact restored runtime licenses/notices, checks payload/lease boundaries, audits fetched reachable Git history, compiles setup, and performs real silent installer lifecycle checks on a disposable GitHub-hosted runner. It then runs `scripts/package-release.ps1`.

Packaging requires identical verification run IDs across app and lifecycle reports, a matching clean source audit, the exact lifecycle-tested installer SHA-256, and matching per-file publish/installer hashes. A stale or modified report/payload fails packaging. Every bundle has a fresh directory; no seeded local build is used.

The `private-draft-assets` CI artifact contains only these nine files:

- versioned `GameVault-Setup-<version>-win-x64.exe` and its `.sha256`;
- `SHA256SUMS.txt` covering every other asset;
- `INSTALL.md` and `RELEASE-NOTES.md`;
- `THIRD-PARTY-NOTICES.txt`, `DOTNET-LICENSE.txt`, `DOTNET-THIRD-PARTY-NOTICES.txt`;
- `RELEASE-MANIFEST.json` with source/head commits, build ID, relative payload hashes and pending acceptance/distribution status. It contains no local build paths.

Only allowlisted flat files are accepted. Symlinks, personal Data/Profiles, games and fixture executables are rejected before packaging. Git source auditing covers all reachable fetched historical paths and text blobs for forbidden private/build file types and recognizable GitHub/AWS/private-key markers; it does not claim universal detection of every possible secret or audit unreachable Git objects. A failed audit prints only paths/object IDs, never credential values or blob contents. Owner privacy/license review remains part of final public release acceptance.

## Prepare an unpublished draft

After CI succeeds, download the complete `private-draft-assets` artifact, extract it into a fresh directory, and verify it with `Assert-ReleaseBundle` from `scripts/ReleasePackaging.psm1`. Obtain a token with authorized repository push/release access in the current process as `GH_TOKEN`; never store it in Git, a build asset, an instruction file, or logs.

```powershell
./scripts/create-draft-release.ps1 -BundleDirectory '<verified bundle folder>'
```

This tool checks that the manifest names a successful matching CI run and source commit, and the preview tag is new. Every candidate file must additionally match the named run's authenticated `private-draft-assets` artifact, including its GitHub-provided ZIP digest and expected workflow identity. An expired/missing origin artifact requires a fresh verified build; recomputed local checksums alone are insufficient. On a public repository it also requires `GAMEVAULT_PUBLIC_REPOSITORY_APPROVED=true` in the environment, as the owner's confirmation (the draft smoke workflow sets it). It creates **only a draft prerelease** (default `v<version>-preview.1`), uploads the nine checked assets, downloads them again and verifies their checksums. Existing releases are not replaced. A later preview can use `-Tag 'v<version>-preview.2'`. The result is recorded in ignored `artifacts/draft-release.json`; partial failures leave an unpublished draft for inspection, never a published release.

GitHub may display an untagged URL for a draft. This is normal; the tool does not explicitly push tags. Private draft downloads require repository access/push privileges. Signing checksums provide integrity checks, not publisher authentication.

## What in-place updates need from a release

The automatic in-place update ([docs/update-check.md](update-check.md)) downloads the release's `GameVault-Setup-<version>-win-x64.exe` and verifies it against the digest GitHub computes for the asset and the `GameVault-Setup-<version>-win-x64.exe.sha256` asset; both are already part of the nine-file bundle. Keep those two names exactly (the installer name must match the tag's version) and never remove them from a published release, or "Update now" falls back to the release-page link.

## Preview number (update check)

Before building the commit a preview release is made from, set `GameVaultPreview` in `Directory.Build.props` to that preview's number and create the draft with the matching tag (`-Tag v<version>-preview.N`). The app reports `<version>-preview.N` and the notify-only update check ([docs/update-check.md](update-check.md)) compares it with the GitHub release tags.

## Test the exact downloaded asset

Manually dispatch the existing Windows build workflow on the intended reviewed branch with `draft_release_id` set to that draft's numeric ID. The ordinary verification job remains read-only. The separate `draft-asset-smoke` job gets contents-write scope because GitHub draft visibility requires push access; its script only reads release/asset/originating-CI metadata and does not create, update, publish or delete releases.

The smoke script:

1. refuses local/self-hosted execution, a public repository, a published release, missing/extra assets or inconsistent provenance;
2. downloads every asset again, verifies all checksums and compares every file with the digest-verified originating CI artifact from the expected successful workflow;
3. removes the API token from child-process environment, then silently installs that exact downloaded setup into an isolated test directory and verifies installed payload hashes;
4. uses the excluded windowless fixture to read/write synthetic profiles, sessions, notes and backups through the installed runtime/infrastructure;
5. silently uninstalls, waits for actual cleanup lease release and verifies synthetic data remains.

`exact-draft-asset-smoke-evidence` retains reports/logs for seven days. No owner GameVault window, native wizard interaction or real game is started. This does not replace final Windows client/interactive testing. Smoke failure blocks acceptance; it does not publish or silently modify the draft.

## License and signing policy before public distribution

- The project license is the root `LICENSE`: **source-available, all rights reserved**. Anyone may read the code and download and use the official installer for personal, non-commercial purposes; copying, modifying and redistributing need the owner's written permission. It is not an open-source license, and the owner is the only person who can change it. Dependency notices apply to those dependencies only. Dependency notices apply to those dependencies only. See [GitHub's licensing guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository).
- PDFsharp and existing catalog/dependency notices remain included. Runtime licenses/notices are copied byte-for-byte from the exact restored .NET/Windows Desktop packs into installed `licenses/` and relevant release assets. Inno Setup is a pinned build tool; its current noncommercial/commercial terms must be reviewed before commercial use.
- Builds are currently unsigned development previews. No certificate is purchased or identity asserted. The owner must approve the signing/trust approach before a public release. Do not disable Windows security controls. SHA-256 is an integrity check and does not authenticate the publisher.
- Public download access requires a future owner decision: make this repository public, or use a separately approved public download repository. No workflow changes visibility or publishes a release automatically. Private releases require repository access.
- #14/#16/#18 and the owner's deferred UI/native-dialog/real-game/clean-client acceptance remain pending. #17 retains final license/signing/public-channel acceptance until those decisions are made.

## Local owner builds

Keep clean setup downloads under `outputs/Installers`, clean ZIPs under `outputs/Archives`, and local seeded apps under `outputs/Builds`. Generate/verify the clean ZIP first, then import the complete private test-data snapshot only into a separate fresh local app directory. Private vault metadata, local instructions, profiles and helper state must not enter source, CI artifacts or draft assets.

The API operations follow [GitHub Releases](https://docs.github.com/en/rest/releases/releases) and [release assets](https://docs.github.com/en/rest/releases/assets). These tools intentionally have no publish command.
