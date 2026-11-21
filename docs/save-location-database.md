# Save-location database (ticket #84)

GameVault can use the community **Ludusavi manifest** as an extra source of candidate save folders.

## Licence finding

- The manifest repository (`mtkennerly/ludusavi-manifest`) is MIT licensed.
- Its README says the data is compiled from **PCGamingWiki** (plus the Steam API for install folder names).
- PCGamingWiki states that its content is available under **Creative Commons Attribution-NonCommercial-ShareAlike** (`PCGamingWiki:Copyrights`, checked 2026-11-21).

Because the data carries a NonCommercial / ShareAlike licence and GameVault has no selected licence, **the manifest is not bundled or committed**. This is a licence question for the owner, not something to assume. Instead:

- the owner presses *Download the save-location database* in Settings;
- GameVault downloads `manifest.yaml` from `raw.githubusercontent.com` into `Cache/` (git-ignored, not part of a GameVault data backup, not part of any installer);
- afterwards it works fully offline from that copy; there is no automatic update and no background request;
- nothing about the user or their games is sent (a plain GET, optionally with an ETag).

If the owner later decides to ship a snapshot, add the PCGamingWiki / Ludusavi attribution to `THIRD-PARTY-NOTICES.txt` first. The attribution text is already shown in Settings (`SaveLocationDatabase.Attribution`).

## How candidates are produced

1. `LudusaviManifest` reads the YAML with a small reader for the manifest's regular shape (game name, Steam id, install folder names, file entries with tags and OS/store conditions). Registry and cloud entries are ignored. Only Windows entries tagged `save` are kept (about 12,800 games; parses in roughly 0.1 s).
2. A game is matched by Steam app id (from its install folder) or by an **exact** normalised name or install-folder name. Near matches are never used.
3. Paths are expanded for Windows: `<home>`, `<winAppData>`, `<winLocalAppData>`, `<winLocalAppDataLow>`, `<winDocuments>`, `<winPublic>`, `<winProgramData>`, `<osUserName>`, `<storeGameId>`, `<storeUserId>` (one path per Steam account found in `userdata`), `<base>`, `<root>`, `<game>`. `*` and `?` match existing folders only. Unknown placeholders, relative paths and `..` produce nothing.
4. Only folders or files that exist right now are offered (at most 24).
5. They enter the normal discovery flow as **suggestions** that need the owner's confirmation. The existing save-source policy (no overlap with game or app folders, no links) still applies. Nothing is selected automatically.
