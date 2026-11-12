# Issue #25: Session notes, reviews, and exports

Status: owner approved the interactive layout, including Read/Edit, on 2026-11-12. Branch: codex/issue-25-notes.

## Purpose and agreed scope

Players can remember what happened in each recorded session, write a final review after marking a game Completed, and keep independent Markdown/PDF copies. The owner requested both combined and separate-file bulk exports. This feature precedes #8; later history/statistics work must reuse its session picker.

Keep all work offline, within the existing Windows WPF app and portable profile layout. Preserve stable game/session IDs, the default Midnight theme, optional Crimson theme, recorded times, recovery journals, and original game saves. User performs UI/native-dialog/real-game testing. No automatic editor popups or foreground stealing after play.

## Proposed owner-facing layout

Add **Notes & review** to the selected game's details, below playtime. It opens a resizable owned window using the current theme. The window has two tabs: **Session notes** and **Game review**, plus **Export...** and **Close** actions.

Session notes: a left-hand list shows recorded finished/interrupted sessions, newest first, with local start date/time, duration, and interruption/recovery indication. A note indicator distinguishes entries already written. The right-hand editor shows the selected session context, an optional Title field, a large multiline plain-text Note field, a visible Saved/Unsaved/Saving/error status, and Save. Select the most recent eligible session initially. The game title remains visible in the window heading. An active session is identified but cannot be edited until tracking ends; no history charts or time corrections are added here. Where legacy statistics lack recovery metadata, label that metadata unavailable rather than guessing a clean ending. Missing/corrupt statistics/journals must not turn ongoing sessions into editable finished sessions.

Game review: the same title/body/save editor. First creation is available when the player marks the game Completed through Edit game. Otherwise explain how to mark it Completed. An existing review remains viewable/editable after status changes, replay, rename, archive, or restore. No stars, rich-text toolbar, attachments, posting, or automatic completion detection.

The review is one entry per game; session notes are one entry per recorded session. Empty Title is allowed. New whitespace-only bodies are not saved or exported; clearing an existing body is blocked with an explanation so it does not silently delete a note. Explicit note deletion is outside this ticket.

Save is explicit; Ctrl+S saves. Switching session/tab or closing with unsaved text offers Save / Discard / Cancel. A failed save preserves editor text and keeps the window open. Canceling export or a destination picker preserves all editor text. Exporting with unsaved changes first offers Save / Export saved version / Cancel, with Save required to export a new unsaved entry itself. Choosing the saved version may still export other saved game/library entries while preserving a new draft; selected-entry export stays unavailable for that draft. No implicit background save or silent loss.

Small windows keep the session list bounded and scrollable, editor usable, and actions visible. Follow existing focus indicators, keyboard navigation, contrast/high-contrast support, and theme resources.

## Export flow

Export dialog explicitly selects:
- Scope: selected saved entry, all saved entries for this game, or all saved entries in the library.
- Format: Markdown (.md) or PDF (.pdf).
- Bulk packaging: one combined document or one file per entry. Individual export uses one file.

Library scope includes archived profiles (they still belong to the retained library); label this explicitly. Only saved nonempty notes/reviews are included. Show entry count before choosing a destination. Empty scopes explain why there is nothing to export. Unreadable/protected documents are reported as excluded with diagnostics; do not silently call an incomplete library export complete. Recovered readable notes can be viewed and individually exported with an explicit recovery warning, but cannot be overwritten in place. Future/corrupt unreadable documents remain preserved.

Use native Save As for individual/combined export, with overwrite confirmation. Use the native folder picker for separate files; sanitize game/title components, append stable entry identifiers, and allocate a numbered suffix instead of overwriting existing files. A game review has a deterministic review identifier based on its game ID. Sort bulk output deterministically by case-insensitive game name, game ID, then session start/entry type and entry ID. Include a clear export summary with written paths, skipped entries, and failures. Partial failure cannot report total success. Separate exports may retain completed files if later files fail; explain this and allow a retry without replacing them.

Each entry includes game name, entry kind, optional note title, note body, creation/update dates, and session start/end/date/duration where applicable. Display timezone offsets in exports. Exclude executable paths, save-source paths, process IDs and unrelated private profile data. Markdown uses UTF-8 and preserves paragraphs, with metadata/title escaped so user content cannot corrupt surrounding headings. PDF uses readable headings/body text, margins, page numbering, wrapping and automatic pagination. User text is plain content, not executable HTML or external-resource markup. Export builds independent copies without changing source documents.

## Storage and integration decisions (builder defaults)

Use schema-1 independent note documents at Profiles/<game-id>/Notes/Sessions/<session-id>.json and Profiles/<game-id>/Notes/review.json. Each includes game ID, optional session ID, entry kind, title, body, created UTC and updated UTC. Identity and creation date cannot be rewritten during normal edits. Use the existing AppDataStore gate, managed-path validation, atomic write/backup and strict schema checks. A note must reference its own existing profile/session; new reviews require Completed, retained reviews do not. Never change statistics, adjustments, session journals or actual game-save files during note operations.

Missing notes are ordinary empty editors. Corrupt, unavailable, future-version or recovered documents show actionable read-only diagnostics; never replace them with blanks. Archived valid profiles retain notes and allow note access. Refresh session metadata when opening the notes window; do not cache stale active/completed state. Main-window closure must await outstanding note writes and respect dirty-editor cancellation before releasing the application lease.

Put note/storage and export-selection/content models in Core/Infrastructure; keep WPF dialog/native destination logic in App. Prefer an offline PDF library with a verified permissive license for its exact pinned version, embedding required fonts with the appropriate notices. PDFsharp/MigraDoc v6.2.3 is an initial MIT-licensed candidate (verified tagged LICENSE); current releases have different licensing, so do not silently upgrade to them. Choose the renderer during implementation after testing Unicode/paragraphs/pagination; if it cannot render a script correctly, solve that before claiming support. No online conversion, printer-driver dependency, embedded store credentials or paid service is authorized.

Sources checked 2026-11-12:
- Exact MIT license: https://github.com/empira/PDFsharp/blob/v6.2.3/LICENSE
- Current licensing differs: https://docs.pdfsharp.net/LICENSE.html

## Implementation and acceptance boundaries

Meaningful automated checks cover independent persistence/restart, stable IDs across rename/archive, review creation gating and retention, invalid references, protected/corrupt/future/recovered documents, interrupted atomic writes, created-date preservation, dirty-navigation/export state, deterministic export context/order, safe names, destination collisions, empty scopes and partial failures. Use synthetic fixture data for all automated tests.

Verify synthetic long/Unicode Markdown content and visually inspect rendered multi-page PDFs for legible text, paragraph breaks, no clipping, and sensible page breaks. Verify source data remains unchanged after all export paths, including failures. Run the repository's full locked build/test/publish checks and one focused final review.

Build handoff: refresh private test-data vault first (done; latest snapshot verified), create and inspect the clean ZIP before importing personal data, then seed only the new local testing folder with the verified latest profile snapshot. Never commit or package owner profiles. Draft PR and Windows CI precede owner manual testing; no automatic merge.

Owner checks: notes on the existing RE2/Dead Space sessions; edit/reopen/restart; dirty navigation/cancellation; Completed review and retention after switching back to Playing; individual/game/library scopes in both formats and both bulk packaging modes; exported long/Unicode content; both themes, keyboard access and resizing. No save discovery/backups, capture overlay, cover API, installer, or #8 charts are added in this issue.
## Owner addition: Read and Edit modes

Both session notes and game reviews provide Read and Edit controls within the entry pane. Existing saved entries initially open in Read mode, showing the title and selectable, wrapped paragraphs without editor fields. A new empty entry opens in Edit mode. Saving successfully returns to Read mode. Read mode may also preview the current unsaved draft; label it clearly as unsaved, preserve that draft when returning to Edit, and keep the existing Save/Discard/Cancel protection when leaving the entry. Failed saves keep the draft in Edit mode. This is a plain-text reading presentation, not a new rich-text or Markdown-authoring feature. Read mode supports long text, Unicode, keyboard navigation and both themes. Export controls remain available in either mode; exporting a draft still follows the explicit saved-version/Save/Cancel rules above.