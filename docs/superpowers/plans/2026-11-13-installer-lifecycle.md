# Installer lifecycle verification plan

**Goal:** Run the real setup/uninstaller against disposable synthetic libraries on GitHub-hosted Windows, without touching the owner's installs or requiring manual checks now.

**Architecture:** Extend the existing excluded windowless fixture with token-guarded seed/inspect commands. Publish its apphost self-contained, but copy only fixture entry files into the installed folder, so its runtime and GameVault infrastructure assembly come from the installed payload. A PowerShell lifecycle runner refuses local/self-hosted execution and pre-existing GameVault registration/default folders/shortcuts. It compiles a synthetic prior-version payload and drives silent setup/uninstall with bounded waits and logs. Data/Profiles hashes and application storage reads prove preservation across real install, blocked/failed upgrade, upgrade, uninstall and reinstall.

**Tasks:**
- [ ] Add failing tests for guarded synthetic library seeding/inspection and byte-stable reopen; implement the fixture and rerun tests.
- [ ] Add/test fail-closed local runner guard; implement actual installer orchestration only for disposable GitHub Windows runners.
- [ ] Cover default/custom spaced folders, shortcuts and per-user registration, bundled runtime, prior-version upgrade, exclusive lease refusal, locked-file failed upgrade, full data preservation, uninstall/reinstall and redirected targets. Record reports/logs.
- [ ] Add CI lifecycle step, run existing full verification and focused review, fix actual CI failures.
- [ ] Deliver clean packages before private local build seeding; commit and open PR for owner merge.

**Evidence limits:** Previous version is generated from current source with an older numeric version, not a historical released binary. Storage/runtime probe is windowless test code excluded from downloads, not interactive UI validation or proof of a development-tools-free Windows image. Interactive cancellation, optional post-install UI launch, clean-machine supported-OS matrix and the owner's final UI/game pass remain pending. Do not close #16 merely for partial acceptance, and do not claim release readiness.
