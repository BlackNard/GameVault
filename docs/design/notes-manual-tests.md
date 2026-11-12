# Issue #25 owner checks: Notes, reviews and exports

Use the new local development build seeded with the latest preserved profiles. The assistant does not launch the UI, game or native dialogs. Close older GameVault builds first so test edits have one authoritative home.

1. Select Resident Evil 2, choose **Notes & review**, and find the recorded session by its date and 50m29s duration. Write a title and multiline note, including Unicode if wanted. Save: it should return to Read mode. Select/copy the readable text, then Edit it again.
2. Preview an unsaved edit through Read; return to Edit and confirm the draft remains. Switch session/tab and close with unsaved text: check Save, Discard and Cancel. A save failure must keep the draft, show an error and keep the window open.
3. Restart GameVault and confirm saved text persists. Test Dead Space's existing session too. No recorded times should change.
4. Through Edit game, mark a game Completed, then write its game review. Change status back to Playing and confirm the review remains editable. A game without a review and without Completed status should explain how to enable the first review.
5. Export a selected session note and a review in both Markdown and PDF. Export this game and the entire library with both **combined** and **separate** packaging, in both formats. Choose a folder outside the app folder. Check titles, text, paragraphs, dates, duration and context. No executable/save-source paths should appear.
6. Cancel the Save As and folder pickers; notes and drafts should remain. Repeat a separate export to the same folder: existing files must remain and new names must be used. Confirm combined-file overwrite requires the native prompt. Empty scopes must explain there are no saved notes.
7. Remove/restore or rename a game; its notes and review should survive. Entire-library export includes removed profiles. If warnings occur, read them and confirm excluded entries are acknowledged rather than claimed as exported.
8. Check Midnight and Crimson, resizing, scrolling long text, keyboard focus, Ctrl+S, and closing during a save/export. While a game runs, its active session must not accept notes; refresh after it exits. No automatic notes popup should steal game focus.

## PDF export implementation boundary

PDFs are generated offline with WPF text shaping/pagination and 200-DPI page images in PDFsharp 6.2.3. This preserves visually shaped Unicode and fallback fonts without a printer/browser dependency. PDF text is currently image-based: search/copy/accessibility text layers are not included. Markdown provides the editable/searchable text alternative. Synthetic multi-page Unicode PDFs are visually checked during development; the owner should review real text/font coverage on their machine.

## Data safety

Notes are separate versioned files in `Profiles/<game-id>/Notes/Sessions/<session-id>.json` and `Notes/review.json`. Playtime statistics, session recovery journals and actual game saves are untouched. Corrupt/recovered/future notes remain read-only with diagnostics. New whitespace-only notes and clearing an existing body are refused; deletion is outside this ticket. Exports are independent copies, and the app refuses export destinations inside its own app/profile folder.