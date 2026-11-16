# In-app portrait cover search implementation plan

> For agentic workers: execute tasks under the existing parallel ownership/verification workflow; tests first for meaningful behavior.

**Goal:** Implement approved issue51: key-free same-window portrait cover search, preview, download and profile assignment.
**Spec:** docs/superpowers/specs/2026-11-14-in-app-cover-search-design.md (owner approved).
**Base:** f80b91b (PR50 fixes). PR50 is open; use stacked feature PR against codex/manual-ui-fixes-44-49 until owner merges it. No automatic merge.

## Constraints and boundaries
- Existing same-window editor callback contract preserved; no personal API key, browser window or Steam login required for normal search.
- Target2:3 frames and decoded online source artwork at least600x900, max8192px/16MP/12MB source; save normalizedPNG at<=1200x1800 without upscaling, <=10MB. Local import remains optional and warns for unsuitable ratios rather than silently replacing anything.
- Explicit title-only requests, no paths/profile data/history/credentials; HTTPS host/path allowlists, separate cookie-free redirect-disabled catalogue/image clients, bounded responses/concurrency/timeouts, no background/bulk crawling.
- Steam public website endpoints are not a guaranteed documented external API. Handle upstream changes/rate limits/unavailable images honestly. Source/publication permissions and universal coverage are not inferred from anonymous availability; retain source credit/link and original publisher ownership. No downloaded artwork in packages.
- No owner-PC UI/window/browser/native/game tests. Mock HTTP/headless image/layout tests, full build/CI/package gates. Preserve private Data/Profiles handoff.

## Contracts
New Core PortraitCoverCandidate(int AppId,string Title,string Context,Uri SourcePage), IPortraitCoverCatalogue.SearchAsync(string,CancellationToken)->Task<IReadOnlyList<PortraitCoverCandidate>>, DownloadAsync(PortraitCoverCandidate,bool preview,CancellationToken)->Task<byte[]>; PortraitCoverPolicy.IsSuitable(int width,int height) min600x900 and2:3 tolerance2percent, rejecting invalid sizes.
Provider SteamPortraitCoverCatalogue(ApplicationPaths not needed): default constructor + injected(HttpClient catalogue,HttpClient images) for mocks, IDisposable. Search up to10 positive game candidates using storesearch then appdetails type=game; bounded metadata concurrency2 and positive-id/title checks. Download fixed app-id CDN paths: preview600x900, selected1200x1800 with600x900 fallback on missing file. Never trust server-supplied arbitrary URLs.
UI validates decoded dimensions before displaying a candidate or assigning it. Existing ArtworkImageNormalizer validates bytes/pixels; new portrait image helper downsamples to max1200 wide without upscaling and creates small frozen thumbnails. Provider bytes are not image-verified until this app boundary.

## Task1: catalogue/provider (worker)
Own new Core/PortraitCoverModels.cs, Infrastructure/Artwork/SteamPortraitCoverCatalogue.cs, tests/GameVault.Tests/SteamPortraitCoverTests.cs. Do not modify old SteamGridDB provider/models, existing App files or project graphs.
- [x] Write/run failing tests for title rejection/query encoding, game-vs-DLC metadata, duplicate/invalid IDs, errors/429/timeouts/cancel/size/redirect/host/path policy, primary/fallback asset requests and no-auth/no-cookie assumptions.
- [x] Implement bounded fresh anonymous read-only clients and domain policy. Return exact contracts early.
- [x] Run focused core tests; no commits until parent integration.

## Task2: inline picker and image boundary (worker)
Own CoverSearchView.xaml/.cs, new App/Artwork/PortraitCoverImages.cs and tests. Preserve CoverChosen(byte[],string), Cancelled, IsBusy, CanLeaveAsync(), ImportAsync(string) and IDisposable; GameEditor mounts same control unchanged.
- [x] Headless image tests first for decoded600x900 portrait validation, landscape/tiny/oversized rejection, <=1200x1800 normalized save and small frozen previews; no upscaling/source corruption.
- [x] Implement editable title + Search + cancellation, bounded gallery thumbnails labelled game/context/source/dimensions, selected preview + Use cover + Browse/drop fallback. Use catalogue interface injected for tests, default Steam provider.
- [x] Limit visible pending thumbnail work to2, catalogue results<=10, cancel/reject stale completions on query/navigation/selection. Avoid retaining full-sized source bitmaps/bytes for every result; keep only small previews plus selected normalized candidate. Preserve prior cover/drafts on failures/cancel and disposal.
- [x] Verify positive decoded quality before showing usable gallery result. If preferred selection fallback differs/too small/landscape, refuse assignment with clear message. Selection download must be explicit and awaitable, no outside browser in normal flow.
- [x] Run focused headless tests/build. No owner UI/browser/credential/native tests.

## Task3: frame sizing/integration (parent)
Own MainWindow.xaml cover frames, VirtualizingCoverPanel.cs/layout tests, GameEditor.xaml labels if needed (coordinate), docs/manual checklist/provider research and final branch/package workflow.
- [x] New frame contract test red: library184x276 and hero96x144, image Uniform, grid tile200x372 so labels fit; update fixed extent/scroll expectations and verify actual headless virtualized containers across1000 games/reset.
- [x] Integrate component while preserving old saved artwork/IDs and game editor fields; remove normal browser-first wording. Existing local images stay unchanged until user selects another.
- [x] Document verified provider observations/limits, privacy and owner search/result/offline checks. Full locked restore/build/test/publish and packaging safeguards.

## Task4: review and delivery (parent)
- [x] Fresh focused HTTP/security/state/image/layout review; fix important findings and verify.
- [ ] Create stacked reviewable PR closing51, attach and await exact-head green Windows CI (current workflow tests PRs only targetingmain, so dispatch build workflow on stacked branch explicitly).
- [ ] Compileclean setup/ZIP, verify all payload hashes and no Data/Profiles; import verified latest private library only into newlocal test folder. Preserve old builds. Owner merges50 then51 (or retarget51 once50merged); no releasepublication.

## Local execution evidence — 2026-11-15

- Full locked restore/Release build/test/publish: 325 Core tests and 119 App tests passed, zero warnings; the native encoder test is deliberately runner-only locally.
- Packaging safeguards: installer24, capture-prerequisites12 and release26 checks passed without running an installer or capture runtime.
- Fresh review found a durable source-credit gap. Added a failing saved-PNG metadata assertion, then retained source URL and publisher credit in the selected PNG Description. All18 focused portrait App tests passed after the fix.
- Final full rerun, exact-head GitHub verification and clean/local-seeded handoff remain delivery gates. Owner UI/native/real-game checks remain pending.