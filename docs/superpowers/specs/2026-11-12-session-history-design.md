# Issue #8: Session history and playtime statistics

Status: proposed layout awaiting owner review. Branch: codex/issue-8-session-history, based on merged PR #30. Existing test library refreshed and verified; no app UI or real games launched.

## Goals and owner requirements

Display each game's individual sessions, start/end dates and times, duration, longest session, last played, and daily/weekly/monthly activity. Open each session's existing linked note in the same main window. Manual corrections retain a clear measured-versus-adjusted audit history. Keep manual game status controls, existing reviews/exports, Midnight default and Crimson option. No separate note window.

## Proposed layout

Evolve the existing NotesView into **Sessions & notes** rather than add a competing session browser. Keep the existing main shell and Back to library, with game title/status and Export access at the top.

A compact summary shows total playtime, session count, longest finished session (click to select it), and last played. Tabs: **Sessions & notes**, **Activity**, **Game review**.

Sessions & notes shows a sortable table: date, start, end, playing duration, note access. Newest first by default; longest first and oldest first available. A selected row shows its note below the table using existing Read/Edit/save behavior. Notes remain selectable text and tied to stable game/session IDs. Label interrupted ends as Last checkpoint, actual exit unknown; never fabricate a verified end from a recovery checkpoint. Show Active for ongoing sessions, not a final end. Identify unavailable/legacy recovery metadata explicitly. Keep the table bounded/virtualized and note area usable at the app's minimum size.

Correct time expands an inline form for the selected finished session: corrected hours/minutes/seconds plus required reason. Show original measured duration and current adjusted duration. Save adjusts derived totals/rankings/activity while preserving the measured session and note link. Cancel keeps both original data and any unsaved note draft. Corrections never edit start/end times or recovery journals. Do not allow correction of an actively observed or uncertain session.

Activity shows Today / This week / This month, plus a daily/weekly/monthly grouped table and correction history with date, original/current values, delta, target session and reason. Table is sufficient for this issue; chart work is not required. Show measured and manually adjusted amounts distinctly where a correction exists. Existing reviews and exports continue through the same page; saved review survives later status changes.

Status choices remain Not started, Playing, Completed, On hold. Changing status is independent from note saving and preserves drafts; the first review still requires Completed. Existing read-only profile rules and launch restrictions apply. Save/Discard/Cancel protects Back/sidebar/tab/session/app-close navigation; pending writes complete before leaving. Existing export saved-versus-draft behavior stays intact. No automatic popup steals foreground after play.

## Statistics and timezone defaults (builder decisions)

Store existing UTC/offset instants unchanged. Display and group using the current Windows local timezone and show its name/offset. Weeks start Monday, months are calendar months, reporting boundaries use actual timezone rules including daylight saving. Changing system timezone may regroup dates, never change measured seconds. Stable sort tie-breaker is session ID; active sessions are excluded from longest-finished ranking. Last played means latest recorded start; total includes measured recorded checkpoints and valid adjustments, with active tracking distinguished from finished sessions.

Current schema stores cumulative awake playing time and start/end/checkpoint instants, not a complete timeline of sleep and observation gaps. For a session spanning reporting boundaries, allocate its recorded/corrected seconds in proportion to wall-clock overlap with each timezone-aware period. Use deterministic integer rounding preserving the exact whole-session sum. Clearly label split allocations as estimates when gaps cannot be located; do not claim exact awake activity by date. A zero-length valid interval allocates to its start period. Bad/missing time ranges remain visible with diagnostics and unallocated duration; do not invent dates or silently drop time. Tests cover midnight, week/month/year, DST transitions and integer-sum conservation.

A session's displayed adjusted duration includes only its linked correction deltas. Longest ranking uses adjusted duration and marks corrected entries. Preserve original measured value beside every corrected duration. Total equals measured sessions plus linked corrections plus existing legacy unlinked adjustments. Legacy adjustments have no trustworthy date/session metadata: preserve them in all-time totals and display them separately as unallocated; do not assign them to today's activity. Active progress updates may refresh duration summaries without replacing the editor/draft.

## Storage and correction transaction

Retain schema-1 statistics and recovery files unchanged in shape. Add separate versioned append-only correction records in each profile (stable correction ID, game/session ID, previous adjusted seconds, new adjusted seconds/delta, reason, created UTC). Project each correction into the existing GameStatistics.Adjustments collection using its stable correction ID, idempotently, under the shared AppDataStore gate. Commit the correction record before its statistics projection, as with existing session journals; reconcile incomplete projections before exposing writable statistics or accepting another correction. If reconciliation fails, show protected/read-only statistics rather than inconsistent totals. SessionRecorder/LibraryService must continue seeing projected all-time totals. Do not project twice or drop legacy adjustments, recorded sessions, or notes. Check expected previous adjusted value to reject stale edits.

Reject negative/non-integer/overflow values, empty reason, unknown/wrong-game/active/protected targets, corrected session or all-time totals below zero, corrupt/future/recovered correction docs and conflicting projections. Corrections may be superseded through a new correction, never by silently deleting audit entries. Profiles/notes/backups retain identity through rename/archive/restore. All data remains local/offline.

## Validation and delivery

Automated tests use synthetic profiles only: totals/ranking/tie/empty/history order; correct display of unknown/active/interrupted endpoints; timezone period allocation and sum conservation; correction validation/overflow/stale edits; interrupted commit/reconciliation/idempotence and legacy adjustments; stable note identity; draft retention while live progress or corrections refresh the page. Full locked Windows build/test/publish and one focused final review precede owner handoff. Owner tests actual UI/native dialogs/themes/keyboard/resizing/game sessions. Preserve latest test data, package clean ZIP first, seed only new local build, draft PR with CI, no automatic merge.

The interactive preview uses invented sample sessions/notes to show multiple rows and a midnight crossing; it is not imported data and does not change actual profiles.
Builder check: no important unanswered questions; ready for owner layout review. No production code changed yet.
