# Folder-based game-name autofill — issue #28

When Add game > Browse selects D:\Games\Resident Evil 2\re2.exe with an empty Name field, the suggested name is Resident Evil 2. An existing typed name is kept. Cancelling Browse leaves all fields unchanged, and working-folder behavior is unchanged. Existing saved profiles are not renamed automatically.

Automated verification: scripts/verify.ps1 passed locked restore, Release build with zero warnings/errors, all 109 tests, and self-contained Windows x64 publish checks. Six new regression cases cover the RE2 example, whitespace-only names, custom names, quoted paths, forward separators, and root-level executable fallback. A focused read-only review found no material issues. No manual UI/native-dialog checks were performed by the assistant.

Owner checks:
1. In Add game leave Name empty, browse to RE2's executable, and confirm Resident Evil 2 appears.
2. Type a custom name, browse again, and confirm the custom name stays.
3. Cancel Browse and confirm no values change.
4. Confirm working-folder defaults and customized working folders still behave as before.

The capture panel and online cover-search tickets are planned features; they are not part of this bug-fix build.
