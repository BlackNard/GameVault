# In-app portrait cover search — approved design

Status: owner approved 2026-11-14. Implementation follows issue51, separately stacked after PR50.

## Requested experience
Enter a game title in GameVault, search, inspect matching cover thumbnails inside the same game-profile editor, select a cover, then download/preview/confirm it. No personal API key or external browser window in the normal search flow. Retain local Browse/drop as fallback. Cancel, errors and stale results must preserve the existing cover and form fields.

## Proposed approach
Use background catalogue/image HTTP requests rather than loading the user's browser. First adapter: Steam's public store catalogue search plus portrait library assets, with no Steam client, ownership check, account or personal key. A live read-only check on2026-11-14 returned Resident Evil2 matches and both portrait asset sizes with HTTP200 without credentials. This is a public website interface, not a documented external API contract: verify distribution/use constraints before shipping, bound requests, handle changes/unavailability visibly and never claim universal game coverage. A broad search-engine scraping adapter is not the first choice because its results/layout/access requirements can change independently and mix screenshots/banners with covers. A hosted paid/provider-key service would add infrastructure and is outside this first approach.

## Shape and quality
Use a consistent2:3 portrait frame for library cards and game-page covers. Prefer600x900 or1200x1800 source artwork;600x900 is also Steam's documented library-capsule size. Resize display frames to that ratio rather than changing image proportions. Verify decoded width/height/aspect before showing a suitable result; do not trust filenames/HTML metadata. Reject low-resolution/landscape results from the normal portrait gallery rather than stretching/upscaling them. Local imports may remain available with a clear preview/warning for unsuitable proportions. Existing saved images are not replaced automatically.

## Same-window flow
Search title (folder-derived editable default) → matching games/covers with title/edition context, thumbnail and pixel dimensions → choose → full portrait preview → Use this cover → existing game-profile Save. Prefer the best resolution for duplicate artwork. Exclude DLC/bundles/non-game results when metadata identifies them; require positive user choice for ambiguous editions. Show bounded loading, cancel/refine, no-results, offline/rate-limit/provider errors with retry/local import. Download/normalize/persist through existing safe cover-save logic; retain sufficient resolution and source attribution. Source page links are explicit optional external actions only.

## Boundaries and evidence
Separate catalogue and image clients, HTTPS host allowlists, bounded responses/decode dimensions/concurrency/timeouts, no executable paths/saves/notes/history in queries, no shared secret, no background bulk harvesting. Mock HTTP tests for title/edition/size filtering, cancellation, bad URLs/images and persistence. Headless layout/aspect tests; no owner-PC browser/window/native tests. Keep latest Data/Profiles handoff safeguards. Implement on a new review branch after accounting for PR50 state; no merge or release publication.

Reference: https://partner.steamgames.com/doc/store/assets/libraryassets
