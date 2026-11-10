# Issue #4 verification

Date: 2026-11-11. Manual UI testing remains with the owner.

## Automated evidence

- Before implementation: 16 new persistence cases failed with NotImplementedException while 12 original tests passed.
- Added failure-path cases for interrupted staging/creation, invalid schema types, absent primaries with previous copies, junctions, and invalid history. Two malformed schema-type cases failed; checking the JSON token type before integer conversion fixed both.
- Full suite: **35 passed, zero failed**.
- Full `scripts/verify.ps1`: locked restore, Release build (**zero warnings/errors**), tests, self-contained win-x64 publish, runtime files present, and no Data/Profiles shipped.
- Independent focused code review found no material correctness/data-loss findings. It examined recovery, version protection, file links, partial profile creation, renaming, and the pending-save shutdown path.
- Current remote build results are attached to the issue #4 pull request and branch under GitHub Actions; local evidence alone is not a claim of remote CI success.

## Owner checks, not performed by the assistant

Use the development executable to select Crimson, close/reopen, and confirm its restoration; repeat with Midnight. Check closing immediately after changing the theme. Inspect the startup storage message if testing with disposable malformed/unsupported data, and verify no existing data is reset.

See [the persistence guide](../persistence.md) for data layout and recovery limits. This build provides storage and theme settings, not the full library or native add-game flow yet. Actual game saves were not used or changed. No desktop/browser automation or manual UI check was performed by the assistant.
