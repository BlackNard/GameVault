# In-app portrait cover search — issue #51

GameVault searches by title and displays matching portrait covers in the existing game editor. No account, personal API key or Steam client is required. The native gallery uses background catalogue requests; the optional Google Images page runs inside the same window. Only an explicit title search contacts the catalogue; executable paths, profiles, notes and play history are never included. Local Browse/drop remains available, including offline.

The first adapter uses Steam's anonymous store search and per-game metadata, then fixed Steam CDN library-capsule URLs. Metadata excludes non-game results such as DLC where identified. Results can still include different editions: the user chooses the intended game. Search examines at most twenty unique app IDs and returns at most ten game candidates, with at most two metadata/image operations at once, and does not perform bulk collection or background refresh.

Steam documents library capsules as 600 × 900 pixels. GameVault validates actual decoded pixels rather than filenames. Both gallery and selection request the larger library asset first, falling back to the base asset only on 404. Resident Evil 2 demonstrates why: the base file named 600x900 actually decodes to 300×450, while its _2x file decodes to 600×900. Larger landscape/square images can be center-cropped to a 2:3 portrait with at least 600×900 usable pixels, then proportionally reduced to at most 1200×1800 without enlargement or stretching. The converted image is previewed before Use this cover. Library and game-header frames use the same 2:3 ratio. Existing covers are retained; local landscape/square imports use the crop preview when sufficient pixels are available. Undersized local imports remain an explicit fallback with guidance about reduced quality/frame space.

Requests use separate catalogue/image clients, fixed HTTPS hosts, no cookies or automatic redirects, a 20-second timeout, JSON limited to 1 MiB/depth 24, image bodies limited to 12 MiB, decoded images limited to 8192 pixels per side/16 megapixels, and saved PNGs limited to 10 MiB. Cancellation, invalid responses, rate limits and unavailable artwork preserve the editor's previous cover. Source names and page addresses remain visible. Selecting a cover only changes the pending editor value; saving the game persists it locally for offline display. The selected PNG also retains the publisher credit and source-page address in its Description metadata.

## Availability and rights

The store-search website interface is not a documented external API contract. A limited anonymous protocol check on 2026-11-14 returned Resident Evil 2 search results and both portrait assets. Availability can change; some games have no matching catalogue entry or suitable portrait. The picker must report this honestly and retain local import. This implementation does not imply Valve affiliation, universal catalogue coverage or a licence to redistribute third-party artwork. Personal downloaded covers are excluded from source, installers and clean ZIPs.

References checked on 2026-11-14:

- [Steam library graphical assets](https://partner.steamgames.com/doc/store/assets/libraryassets) — documented 600 × 900 library capsule dimensions.
- [Steam Web API terms](https://steamcommunity.com/dev/apiterms) — these concern the documented API; they do not guarantee the website search interface.
- [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/) — content ownership and service conditions still apply.

## Pending owner checks

No app window, native dialog, owner browser or real game was used for automated acceptance. Check the following during full manual testing:

1. Search Resident Evil 2 and Dead Space by title from Add/Edit game. Confirm results appear inside GameVault with edition/context, dimensions and source. Try no-match, offline, cancellation and changing the title while loading.
2. Choose a portrait, inspect its preview, use it, and save. Confirm full-height proportional display in library/header, restart persistence and offline display. Cancel before use/save and verify the previous cover and all other form fields remain intact.
3. Try a local landscape image. Confirm a sufficiently large image previews a centered 2:3 crop; an undersized image explains the fallback. The app must not stretch, enlarge or silently replace the existing cover. Browse/drop still works without online access.
4. Navigate by keyboard, switch Midnight/Crimson, resize the editor/library, and check gallery scrolling with multiple results. Observe that opening the picker/search never opens an external browser.


## Tray Quit follow-up — issue #54

Quit from the system tray must work while Add/Edit game, cover selection or launch settings are open. Confirm discard only when needed; refusing keeps the draft. An active cover search may be cancelled, but an actual save/export must finish safely. Checkpoints, recording finalization and backup shutdown keep their existing behavior. Native tray checks remain pending owner verification.

## Google Images inside GameVault — issue #56

Choose Search Google Images from the cover picker, then search the prefilled or edited title. The embedded Google page is a normal browser page: click a result or its full image to select it, then choose Preview selected image. Right-click Preview as cover remains an alternative. GameVault downloads only that explicit selection, returns to the portrait crop preview, and waits for Use this cover and Save game. Source credit is retained in the PNG; no Google results are scraped or automatically selected.

The page uses the official Microsoft WebView2 composition control, so same-window prompts can appear above it. The SDK is locked to1.0.4314.35 and its exact LICENSE/NOTICE files accompany clean packages. A separate Evergreen WebView2 Runtime must be available on the device. If it is missing/unavailable, the app offers local Browse and an explicit link to Microsoft's download guidance; GameVault does not silently install browser software. The launcher and native cover gallery remain usable without it.

Browser storage is isolated in the Windows user's LocalAppData/GameVault/WebCoverSearch area with InPrivate mode, outside portable Data/Profiles and test-library snapshots. Searches send the entered title with cover-focused keywords and large/portrait/JPG filter hints to Google; Google and image hosts also receive normal browser network requests. No Edge profile, save files, session history or notes are passed to the page. Sign-in, external top-level pages, new windows, browser downloads, permissions and host-object/web-message bridges are disabled. Explicit image downloads use public HTTPS addresses only, validate each redirect, pin validated DNS addresses, and retain the image size/time/decode limits. Sites that prevent direct image downloads require another image or local import. Google consent/result layouts, native image menus and the missing-runtime experience require the owner's manual check.

References: [Microsoft WPF composition control](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf), [WebView2 Runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [context-menu customization](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/context-menus).
## Browser crash regression — issue57

The composition browser requires the Windows SDK .NET projection as well as WebView2 Runtime. The app targets the required Windows API version and bundles Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll; users do not install a developer SDK. Startup checks these managed components before adding the browser control. Clean publish verification independently loads the published graph without opening a window, and package validation rejects missing projection files. The SDK publisher license is included at licenses/windows-sdk/LICENSE.rtf. Owner retest: enter Google Images, search, navigate back and retry, then quit from the tray while the page is open. Confirm failures stay inside GameVault without terminating it.


## Cover-focused Google selection — issue58

Host searches append game cover unless the query already contains cover/covers/box-art wording. Google receives its current public advanced-form parameters for Large size, Tall aspect ratio and JPG. These narrow results but do not guarantee indexed dimensions; actual downloaded pixels must still provide a 600×900 portrait crop. The original Wikimedia image in the report is273×365 and must remain rejected without enlargement.

A click selects one candidate and shows a visible Preview selected image action. A bounded, user-action-only hit-test reads only the clicked image and its associated link; Google's explicit imgurl may identify its original instead of a thumbnail. No result list is scanned, no page is automatically operated and no web-message/host bridge is enabled. Search/navigation/back/refresh/disposal invalidate stale selections. Preview downloads and validates actual bytes before the existing crop/Use/Save confirmation.

Downloads identify GameVault honestly and request supported PNG/JPEG formats, while retaining public-address DNS validation, HTTPS, redirect, time and byte limits. The reported Wikimedia source returned403 without an identity and200 with GameVault's descriptive User-Agent. Blocked/missing/unsupported/oversized/redirected, rate-limited and unavailable-server failures have distinct guidance; unsuitable images report their decoded pixel dimensions. Raw response bodies, query secrets and browser cookies are not displayed or forwarded.

Pending owner checks: verify the Google query visibly includes cover wording; inspect filter behavior; click a result and its full preview, then Preview selected image; test the right-click alternative; confirm suitable images reach crop preview, undersized ones show dimensions, and cancellation/navigation never applies an older selection. Google DOM/layout and browser-native input remain owner checks.
