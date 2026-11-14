# GameVault UI/UX redesign — approved design

Status: approved by the owner on 2026-11-14, including the interactive preview and browser-assisted key-free cover route.

## Intent
Make GameVault feel like a focused desktop game launcher. Use Steam/Epic as references for a cover-led library and dedicated game pages, while keeping GameVault's own identity and existing offline sessions, notes, saves and captures. Remove the permanent overloaded detail sidebar and generic native-looking controls. Full manual acceptance resumes after the redesign candidate is delivered.

## Approach
Keep C#/WPF and the existing domain/storage services. Rebuild presentation as a single-window navigation shell with reusable themed control templates. This preserves mature tracking/backup safeguards and avoids introducing a second UI runtime. A superficial reskin would retain the crowded navigation the owner rejected; a wholesale framework rewrite would add migration risk without improving the requested flows. Routine implementation choices are delegated; the layout remains subject to owner review.

The proposed preview opens with a cover grid. A game card navigates to its dedicated page in the same window. Midnight stays the default and Crimson remains selectable. No new store, community, account, cloud or promotional content.

## Shell and visual system
- Integrated title bar with subtle app identity and matching minimize/maximize/close controls. Preserve drag, resize, double-click maximize, keyboard commands, taskbar behavior, Windows snap, high-contrast and close-to-tray preferences. Do not remove normal window accessibility.
- Compact permanent navigation: Library and Settings. Back navigation restores library search, filter, scroll and focus.
- One family of consistent custom WPF buttons, inputs, selectors, checkboxes, tabs, menus, scrollbars and lists. Normal action height 40px, 6px control corners, 8px spacing rhythm. Hover, pressed, selected, disabled, validation and keyboard focus states are designed explicitly.
- Segoe UI Variable/Segoe UI, 14px body, 12px supporting copy, 20px section titles, 30–34px page titles. Clear sentence-case action names. Icons supplement labels; uncommon icon-only actions have accessible names and tooltips.
- Preserve Midnight palette (#131b26 background, #111822 chrome, #1c2838 surface, #edf2f8 text, #adbdcf secondary, #b6ceff accent); Crimson uses #120e11/#160f14/#21171d, #f8eef2/#d0bbc5 and #cb3c54. Contrast verified during implementation. Reduced motion and quiet hidden/tray mode remain requirements.

## Navigation and content
Library: cover grid, search, one filter control containing game status and removed games, Add game. Cards show title, concise status and playtime. No permanent details column or repeated action buttons.

Game page: Back to library, cover/title, Play, current session state and one labelled/accessibly named game-options menu for edit/launch settings/removal. Four local sections:
1. Overview: playtime, recent session/activity and concise backup health. Relevant failures offer a specific next action.
2. Sessions & notes: individual start/end/duration, longest session, read/edit notes, completed-game review, existing corrections and all existing export choices. Editors and export options stay in the same window.
3. Saves: detected live save sources, automatic discovery status, backup destination, backup history/retention/pins and reviewed restore/undo. Show technical paths here rather than the overview.
4. Media: session-linked screenshots/recordings, previews/open action, capture settings entry.

Add/edit game, launch settings, cover selection, export configuration, restore review and app confirmations become inline pages/panels in the main shell. Preserve unsaved drafts and async-operation guards on navigation. Native file/folder pickers remain OS dialogs; explicit Open folder, Open media and Find in browser intentionally open the associated external application. Those are clear user-requested external actions, not additional GameVault windows. The requested in-game capture panel is the other intentional separate surface.

## Backup destination selection
Expose a default backup destination in Settings and an override in a game's Saves section. Existing portable profile-based backup folders remain the default; do not silently relocate anything on upgrade. External layout is <chosen-root>/<stable-profile-id>/Backups/<dated-backup>. Profile metadata, sessions, notes and statistics remain beside GameVault as before.

Choose folder validates writeability, no reparse traversal, no overlap with any live save source/game installation or managed Data/Profiles, and adequate space. Do not weaken existing manifest/hash/restore/retention safeguards. Changing a destination affects future backups; existing snapshots remain indexed and available at their original locations. Moving existing backups is a separate reviewed copy/verify/commit operation (may be deferred from initial delivery; no silent move/delete). Shared destinations require exclusive backup-store coordination; stable IDs prevent same-title games colliding. A missing/unwritable external disk produces a clear failed/skipped backup status, never a false success or silent destination fallback.

Provide Open backup folder and Open selected backup location. A selected snapshot opens its actual folder, including older locations. Restore continues to target the original live saves through the existing review/recovery flow.

## In-game capture
Capture actions remain available through customizable screenshot/recording shortcuts. Propose Alt+Z as the capture-panel toggle, enabled for active GameVault-tracked sessions; detect conflicts (including Nvidia) and offer another binding rather than claiming the shortcut. Existing screenshot/record shortcuts can remain Ctrl+Alt+F9/F10. Launcher Media/settings pages show the exact active bindings.

The lightweight panel shows game, recording state/time, Take screenshot and Start/Stop recording. It does not inject into games, automatically capture another window/monitor, or promise exclusive-fullscreen/elevated/protected-content compatibility. Game-window capture and audio-off remain defaults; monitor and process-only game audio require explicit choice. Existing session identity, finalize-on-exit/quit and incomplete-file protection remain intact.

## Key-free cover selection
Remove the personal API-key requirement from the normal flow. The inspected xlenore/ps2-covers repository is a static PS2 artwork collection resolved by disc serial, not a general PC-title search service. Copying its URL template cannot match arbitrary Windows executables/game titles.

Proposed first route: Find images in browser opens an explicit title-only image search in the default browser. User selects artwork and imports through drag/drop or Browse, previews/crops if needed, then confirms. The saved image is validated, normalized and copied into the existing stable profile so it works offline. No account/API key is required by GameVault. No scraping search-engine pages, app-owned shared secret, background browsing or automatic unverified artwork choice. Keep provider/creator/source information when available. A richer built-in free catalogue needs a separately verified PC catalogue/provider; do not depict it as implemented or promise universal coverage. Existing downloaded covers remain unchanged. Existing Credential Manager keys are not copied into profile data; any optional advanced provider integration is excluded from the primary UI, with explicit key removal available during migration if retained.

## Evidence and implementation boundaries
Automated tests cover navigation/draft preservation, reusable control states where practical, backup path/mapping changes and unavailable drives, catalogue/open-folder target resolution, shortcut registration/conflicts/session scoping and existing domain regressions. Keep locked builds, full suite, clean package checks, focused review and GitHub installer/native encode CI. No native UI/dialog/game/capture tests on the owner's PC. Owner checks visual/layout, scaling, keyboard access, real-game overlay/capture/audio and full workflows after delivery.

Private data refresh/import safeguards continue. Deliver clean ZIP/setup before seeding only a separate local test directory. Preserve every profile/session ID and existing backup/source folder. Owner merges PRs; no auto merge or release publication.

## Approved preview
The interactive preview is presentation only: game pages, tabs, notes, inline game/cover pages, backup-location controls, themes and capture-panel state can be explored without touching files or launching games. Cover artwork is deliberately illustrative placeholder artwork. The owner approved this layout and the browser-assisted key-free cover route. The builder check found no essential unanswered questions.

## References
- https://store.steampowered.com/libraryupdate/
- https://launcher.epicgames.com/
- https://github.com/xlenore/ps2-covers
