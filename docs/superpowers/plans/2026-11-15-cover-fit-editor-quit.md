# Cover fitting and inline-editor Quit follow-up

Owner reported issue53 (undersized Steam previews) and issue54 (tray Quit from Add game), then approved issue55 crop-to-fill with preview. Update the still-open PR52 with these corrections; PR50 remains its base. No merge/publication or owner-PC UI/native/game testing.

## Confirmed causes and design

- Resident Evil2 base library_600x900.jpg decodes to300x450; _2x decodes to600x900. The gallery rejected the base image correctly but requested the wrong asset. Prefer _2x for gallery and selection; keep404-only fallback, actualdecodedvalidation and existinglimits. Mock request regression first.
- PrepareToQuitAsync deferred on editorOpen without asking the active inline form to leave. Register a testable active-editor session, route Quit through its CanLeave guard, finish the existing completion/cleanup sequence, then perform normal checkpoint/capture/backup shutdown. Cancel/discard refusal preserves drafts; real save/export work cannot be interrupted. Cover searches may cancel before draft confirmation.
- Center-crop sufficiently large landscape/square artwork to2:3, then proportionally reduce to at most1200x1800 without enlargement/stretch. Require at least600x900 usable cropped pixels for online images. Preview the converted result before Use. Local undersized originals remain an explicit fallback with guidance, not silent enlargement. Retain source credit and all byte/pixel limits.

## Execution and gates

Parent owns provider/request tests/docs/delivery. Independent workers own inlineeditorquit and image/pickerconversion respectively. Add meaningful mocked/headless regressions; no Window/native/real-game runs. Fresh review, full automated verification and exact-head Windows CI before ready. Verify clean ZIP/setup before importing latest verified private library into a fresh local build; preserve previous builds/snapshots.

Owner subsequently approved issue56: Google Images displayed inside GameVault with selection followed by crop preview. Use the official WebView2 composition control and native image context menu, isolated InPrivate cache, no scraping/host bridge, and bounded explicit public-HTTPS image download. Missing runtime has honest local fallback and explicit Microsoft guidance. SDK and exact notices are included; no runtime/browser launch during automated owner-PC checks. Background: an embedded Google Images page is possible, but displaying a browser page and providing a stable native result gallery are distinct. Google Custom Search JSON API is closed to new customers; Bing Search APIs retired2025-08-11. Do not claim those APIs provide a free no-key native gallery. Keep title-only privacy and local image import; document source availability honestly.

## Local evidence

- Provider regression red→green; all35 mocked provider tests pass. Actual public catalogue→preview request for Resident Evil2 verified600x900 with a windowless CLI probe.
- Crop/import focused37pass; quit/session guard focused8pass; explicit web-image crop/credit confirmation regression red→green; web policy/download tests50pass without starting a browser.
- Full locked restore/Release build/test/publish:325Core+183App tests passed, one native encoder test reserved for the disposable runner, zero warnings.
- Exact browser SDK LICENSE/NOTICE included; incomplete SDK payload and notice cases reproduced red, then installer29/capture12/release29 packaging checks passed.
- Fresh read-only review found no important actionable bug. Browser/native menus/Google page/tray interactions remain owner manual checks.
- Final exact-head Windows CI and clean/local-seeded delivery remain handoff gates. No merge or release publication.
