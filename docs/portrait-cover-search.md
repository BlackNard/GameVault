# In-app portrait cover search — issue #51

GameVault searches by title and displays matching portrait covers in the existing game editor. No account, personal API key, Steam client or browser window is required. Only an explicit title search contacts the catalogue; executable paths, profiles, notes and play history are never included. Local Browse/drop remains available, including offline.

The first adapter uses Steam's anonymous store search and per-game metadata, then fixed Steam CDN library-capsule URLs. Metadata excludes non-game results such as DLC where identified. Results can still include different editions: the user chooses the intended game. Search examines at most twenty unique app IDs and returns at most ten game candidates, with at most two metadata/image operations at once, and does not perform bulk collection or background refresh.

Steam documents library capsules as 600 × 900 pixels. GameVault accepts online artwork only after decoding and checking a near-2:3 aspect ratio and a minimum 600 × 900 resolution. The selected download prefers 1200 × 1800, falling back to the base asset only when the higher-resolution file is missing. Images are proportionally reduced to at most 1200 × 1800 and never enlarged. Library and game-header frames use the same 2:3 ratio. Existing covers are retained; local landscape/square imports remain possible with guidance about empty space.

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
3. Try a local landscape image. Confirm the preview explains its shape and the app neither stretches it nor silently replaces it. Browse/drop still works without online access.
4. Navigate by keyboard, switch Midnight/Crimson, resize the editor/library, and check gallery scrolling with multiple results. Observe that opening the picker/search never opens an external browser.
