# Where game information comes from

GameVault fills a game's information (description, release date, developers, publishers, genres, install size) from public sources that need **no account and no key**. Nothing is sent except a title or an id, and nothing you typed or chose is ever replaced.

## Sources (checked 2026-11-21 / 22)

| Source | Used for | How a game is found | Notes |
|---|---|---|---|
| Steam store (`appdetails`, `storesearch`) | description, date, developers, publishers, genres, cover | Steam install: by app id. Other games: exact title in the store search | A title with a sibling that only adds a year ("Dead Space" and "Dead Space (2008)") is never guessed |
| Epic Games Store page data (`store-content`) | description, developer, publisher, date | Epic installs only: the page name is guessed from the title and accepted only if the page namespace equals the namespace in the local Epic manifest | Epic has no usable public search; the login and GraphQL APIs are not used |
| GOG catalog and product API | description, date, developers, publishers, genres | GOG Galaxy install (`goggame-<id>.info`): by id. Other games: exact title, one match only | |
| Wikidata (normal API, CC0) | one-line description, date with its precision, developers, publishers, genres, and the ids of the same game elsewhere (Steam, GOG, HowLongToBeat) | by Steam or GOG id, or an exact title that is a video game and unique | The query service (SPARQL) is not used because it is heavily rate limited |
| The game file | developer from the exe's version resource | local only | Engine companies (Epic Games, Unity) are ignored |
| Your PC | install size | local only | Steam games use the size in their own manifest |

Not used: IGDB, RAWG and SteamGridDB (need a key or account), PCGamingWiki (CC BY-NC-SA, see `save-location-database.md`), Metacritic and OpenCritic.

## Identity and "Not the right game?"

Each game remembers which game it is in each source (`metadata.json`, `identity`). Local launcher files and exact matches fill it in automatically. If a title is not unique, nothing is guessed. On the game page choose **Not the right game?**: GameVault lists the matching games from Steam, GOG and Wikidata, you pick one (or say none fits), and that choice is saved as "picked". Automatic runs never change a picked match, and facts that came from the previous (wrong) match are dropped while your own description stays.

## Behaviour

- Order: the game's own store first, then Wikidata, then the game file. The per-field order is editable in Settings → Game information.
- Every provider has a timeout, a size limit, a minimum gap between requests, no cookies and no redirects. A failed or rate-limited lookup is reported quietly and retried on a later start; it never blocks anything.
- Wikidata asks clients to be polite: the gap is one second and a "slow down" answer simply ends that lookup.

## Rich data for store games (phase 2)

Besides the basics, a game from a store gets everything the store publishes without an account. What each source supplies:

| Fact | Steam | Epic | GOG |
|---|---|---|---|
| Long description | `about_the_game` | page description | product description |
| Tags | user tags from the store hover card | – | catalog tags |
| Features | categories (single-player, co-op, cloud saves, controller...) | – | features |
| Platforms, languages | yes | platforms | yes |
| Age rating | PEGI / ESRB / USK / CERO | ESRB | PEGI |
| Metacritic score and page | yes | – | – |
| Player reviews summary | hover card ("Mixed, 27,925 reviews") | – | – |
| System requirements | minimum and recommended | Windows table | – |
| Links | website, support, store page | store page | store page, support, forum |
| Achievements and DLC counts | yes | – | – |
| Screenshots | up to 10 thumbnails | – | up to 10 |
| Background image | library banner | hero | Galaxy background |
| Installed (local, no network) | build, last update, language, beta branch | installed version | – |

Screenshots are only loaded from the store when you press **Show screenshots**. Links open in your browser only when you click them, and only HTTPS addresses are kept. The hover card is read as HTML and may change; if it fails nothing else is lost. Games that were looked up before these facts existed get the missing ones filled in once at startup (`LookupVersion`); nothing already stored or typed is changed.
