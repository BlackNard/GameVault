# Save Discovery Implementation Plan

> Execute inline; owner chose automated acceptance, with final manual UAT deferred. Use existing isolated issue branch and one focused final review.

**Goal:** Discover and retain safe local save mappings, including first-session-created saves, without requiring manual paths or claiming backup protection.
**Architecture:** Core source/discovery models; strict independent per-game sidecar; safe bounded source inspector and offline candidate catalog; tracked lifecycle worker and same-window panel.
**Spec:** docs/superpowers/specs/2026-11-13-save-discovery-design.md
**Tech:** Existing .NET10/WPF/atomic JSON; no new packages, online conversion or elevated monitoring.

## Constraints/review focus
- No mutation of original saves, statistics, journals, notes, corrections or successful backups.
- No attribution from timestamps alone; ambiguous/generic matches require confirmation.
- Missing first-save directories remain pending. Partial scans/links/locks/limits must never become false verified sources.
- Background scans drain on close; launch timing remains independent. Protected sidecars and stale identity cannot be overwritten.
- Manual source paths are local, narrow, overlap/link safe; existing unsafe data remains visible and protected for repair.

## Tasks
- [x] Core models and shared safe path/tree inspector. RED tests: local/manual file/folder, missing/empty/unreadable, app/profile overlap, roots/links/nesting/limits. Implement; GREEN suite.
- [x] Atomic save-discovery sidecar methods with stable source IDs and fingerprint validation. RED tests: restart, protected docs, stale identity, failed commit, unrelated data preservation. Implement; GREEN suite.
- [x] Offline candidate catalog and SaveDiscoveryService. RED tests: RE2 non-store rules, version ambiguity, generic suggestions, absent then new saves, manual precedence, retained missing mappings, confirm revalidation/disabled mode. Implement; GREEN suite.
- [x] Tracked lifecycle coordinator and confirmed-end signal. RED synthetic first-launch discovery/exit/cancel/disposal integration; implement without blocking measured time or requiring game monitoring privileges; GREEN suite.
- [x] Same-window Save sources panel and library/add-game integration, deferred owner checklist. Full suite + focused review/regression fixes.
- [ ] Full locked verify/publish, clean ZIP, private data import, draft PR and green CI. Do not merge; manual UAT stays pending for the final pass.
## Execution rulings and evidence
- Friendly title rename is excluded from identity; executable/working path/manual overrides form the fingerprint. Revision CAS rejects stale scans. Payload markers persist so a recheck cannot silently substitute config-only files for expected save files.
- Selected is an intended mapping, including missing/unsafe diagnostic state; backup consumers require a fresh successful result and every selected source Ready. Known ambiguity and generic guesses require explicit confirmation. Disabled and stale identity mappings are not probed.
- Tests observed RED before implementation for path safety, persistence, first-save/confirmation/manual precedence, lifecycle drain, confirmed-exit separation, manual library validation, Windows device/alias rejection and disabled checks. Full locked verification passed: 158 tests (134 infrastructure/core + 24 Windows/headless), zero warnings/errors, and self-contained publish. Focused review found and verified the ancestor-root rejection fix; regression tests also confirm disabled periodic checks do not rewrite records. No UI or real game testing performed.
