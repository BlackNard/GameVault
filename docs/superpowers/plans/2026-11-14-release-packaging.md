# Private draft release packaging plan

**Goal:** Produce versioned, integrity-checked release assets from matching automated evidence, prepare an unpublished private draft, and smoke-test its downloaded installer only on a disposable GitHub Windows runner.

**Architecture:** Keep ordinary CI read-only. After app verification, clean installer compilation and real installer lifecycle checks, require matching run IDs and installer/payload hashes before packaging. Audit reachable fetched Git history and clean assets for private file types and recognizable credentials. Package a flat allowlisted bundle with setup, checksums, instructions, notes, notices and a sanitized provenance manifest. Upload CI artifacts; a separate manual draft-smoke job may read draft assets and install them silently on a disposable runner. No script publishes releases or changes repository visibility.

**Files:** runtime notice copier plus required payload notices; `ReleasePackaging.psm1` for binding/audit/bundle checks; `test-release.ps1` synthetic regressions; `audit-release-source.ps1`; `package-release.ps1`; `smoke-draft-release.ps1`; existing build workflow and lifecycle evidence binding; release documentation/templates.

**Sequence:**
1. Write failing evidence-binding/source-path/secret/checksum tests; implement the module and runtime notice inclusion.
2. Implement history audit and packaging, bind lifecycle output to the verified run and exact installer; add CI tests/artifact export.
3. Implement guarded downloaded-draft-asset smoke and a manual workflow input/job. Preserve local/UI restrictions.
4. Focused review, full local headless checks, draft PR and green CI. Download the clean CI bundle; create only an unpublished private draft and upload its allowlisted files. Download assets again, verify checksum, and dispatch the exact-asset smoke on the issue branch.
5. Deliver clean ZIP/setup before seeding a separate local build under organized outputs folders. Owner merges; no release publication.

**Distribution policy:** Current build remains unsigned, private and unpublished. No project source license is granted by this change; owner license/signing/public-channel decisions and final #14/#16/#18 acceptance are required before any public distribution. Bundled dependency notices are retained, including licenses from the actual pinned runtime packs. #17 remains open for final distribution decisions if necessary; do not invent approval or license terms.
