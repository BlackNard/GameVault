# NVIDIA App capture

GameVault's own recorder (Windows Graphics Capture + a software/hardware H.264 encoder inside the launcher) competes with the game for the GPU. On PCs with NVIDIA App, a game can record with NVIDIA instead, and GameVault copies the results into the game's profile.

## What GameVault does and does not do

- **Detects** NVIDIA App (an `NVIDIA App` or `NVIDIA GeForce Experience` folder under `Program Files\NVIDIA Corporation`, plus whether the `NVIDIA Overlay` process is running) and reads the capture folder NVIDIA has configured (`HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS`, value `DefaultPathW`; default `Videos\NVIDIA`). All of this is read-only.
- **Does not** start or stop NVIDIA recordings, send it keystrokes, or change any NVIDIA setting. NVIDIA publishes no API for that, and its capture folder is a single global setting, so it cannot be pointed at one game's profile.
- **Copies** clips (`.mp4`) and screenshots (`.png`) that NVIDIA saved during one of the game's recorded play sessions (file time from one minute before the session start to ten minutes after its end) into `Profiles/<game>/Media/<session>/`. Other formats inside a session window (`.jpg`, `.jxr`, ...) are counted as unsupported and left alone.
- Originals are only read: never moved, renamed, changed or deleted.
- Each original is copied once. A fingerprint of its path, size and write time is stored with the copy (`sourceFingerprint` in the media sidecar), so repeating the import does nothing new.
- A file written in the last few seconds, or still open in another program, is reported as "still being written" and picked up by a later import. A corrupt or incomplete copy is removed; it never appears as a capture.
- Imported clips show as `from NVIDIA App` in the game's Media list.

## Choosing the recorder

Per game, on its **Media** tab: Automatic (NVIDIA App when installed, otherwise GameVault), GameVault recorder, or NVIDIA App. The choice is stored in `Profiles/<game>/capture-source.json` (schema 1; missing means Automatic).

When a game uses NVIDIA App, GameVault's own recorder buttons and global shortcuts stay off for it (so they never fight NVIDIA's keys). NVIDIA's defaults are Alt+F9 (record), Alt+F1 (screenshot) and Alt+Z (overlay); they are customizable in NVIDIA App, so GameVault only shows them as a hint.

GameVault copies automatically about 8 seconds and again about 90 seconds after the game session ends, and **Copy NVIDIA captures now** does it on demand for all of the game's recorded sessions.

## GameVault's own recorder: frozen-picture detection

An owner TEKKEN 8 recording showed video frames stopping for 141 s while audio continued, then sporadic gaps and repeated frames (effective 35 fps against 60 requested). While recording, GameVault now counts the frames the recorder produces and, if none arrive for about 4 seconds, says so immediately ("video stalled N s (audio continues)") and records `warning` in the clip's metadata; the Media list marks such a clip `video froze`. A recorder that cannot count frames, or has not produced its first frame, never raises an alarm.

This detects the freeze; it does not remove its cause. Windows Graphics Capture goes through the desktop compositor, which can cost a fullscreen game frame pacing, and 60 fps costs about twice the GPU time of 30 fps. For NVIDIA GPUs the intended fix is recording with NVIDIA App (above); otherwise prefer 30 fps and a borderless/windowed game.

## Manual checks (owner)

1. With NVIDIA App installed, open a game's Media tab: it should say NVIDIA App is detected and show its capture folder.
2. Play, record with NVIDIA (Alt+F9) and take a screenshot (Alt+F1), quit the game. Within about two minutes the clips appear under the game's Media list marked `from NVIDIA App`, and the originals are still in the NVIDIA folder.
3. Press **Copy NVIDIA captures now** again: nothing is duplicated.
4. Switch the game to GameVault recorder: the buttons and shortcuts come back for that game.
