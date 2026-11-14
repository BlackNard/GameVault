# Issue #17 release packaging verification

The release gate requires full app verification, clean installer payload checks, matching real lifecycle evidence, a clean reachable-source-history audit and exact installer/payload hashes. Synthetic regressions cover mismatched run IDs, installer hashes, failed lifecycle reports, changed publish files, a modified/rechecksummed bundle differing from its originating CI artifact, forbidden source paths/credential markers, corrupted checksums, extra release files, exact runtime-notice copying and missing notice refusal.

CI emits a strictly allowlisted private draft bundle and sanitized provenance manifest; it does not publish. `create-draft-release.ps1` requires a private repository and successful matching originating build, creates only an unpublished preview draft, uploads known assets and re-downloads them for checksum verification. The manually dispatched disposable-runner smoke installs the exact downloaded draft asset and checks installed hashes/runtime/storage and preservation after uninstall.

Reports, logs, and the specific successful build/smoke run are linked from the PR and retained as CI artifacts. Local checks never execute the draft smoke or installed wizard.

Final owner UI/native-dialog/real-game, clean Windows client/interactive cancellation and performance acceptance remain pending. Project license selection, signing/trust approach, public channel and release publication require explicit owner decisions. No project license, repository visibility change, automatic merge or release publication is authorized by this work. #17 stays open for those final distribution decisions.
