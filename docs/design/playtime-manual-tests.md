# Issue #7 — owner playtime checks

Use the **GameVault-issue-7-playtime** development package. Close older GameVault windows before copying the old build's entire Data and Profiles folders into this new folder if you want to retain your library. The distributed package intentionally contains no user data.

## What changed

Confirmed game starts now record live session time and total playtime. Checkpoints are saved every 10 awake seconds, plus exit and normal GameVault shutdown. Menus, pauses, minimized play, and time away count; computer sleep/hibernate do not. Detailed history, charts, and manual corrections remain #8. Automatic save discovery and dated backups remain #9/#10.

## Checks

1. **Normal session:** Select RE2, click Play, and approve Windows if requested. The session and total should advance about once per second. Spend roughly 30–60 seconds in menus or gameplay, then exit the game normally. The timer should stop, show the last session, and retain the new total.
2. **Persisted total:** Close/reopen GameVault with the game stopped. The total should remain. Launch a second short session, exit, and confirm it adds once to the previous total. Changing filters, themes, and navigation must not restart or duplicate a session.
3. **Sleep exclusion:** While a game runs, note its session time, put Windows to sleep for roughly a minute, then wake it. The sleeping minute should not appear in playtime. A few awake seconds around sleeping/waking still count. Pause/menu/minimized/AFK time while Windows is awake should count.
4. **Close while game stays open:** Note the time, close GameVault, and leave RE2 open for 30 seconds. Reopen GameVault. It should recover the verified running session, block a duplicate Play, and continue from the saved duration. The 30 seconds while GameVault was absent must not be added. Exit RE2 and confirm the combined observed time is preserved as one session.
5. **Game exits while launcher is closed:** Close GameVault while the game runs, then exit the game. Reopen GameVault later. It should retain the last observed duration and show an interrupted-session notice rather than invent time up to restart. Closing/reopening again must not add that session twice.
6. **Optional crash check:** With a disposable test session, end only the main GameVault task in Task Manager while the game stays open, then reopen it. Recovery should preserve the last checkpoint and exclude the unknown gap. Up to about 10 awake seconds since the last successful checkpoint may be unavailable. The game must never be killed by recovery.
7. **Launch failures and cancelled approval:** A failed start or declined Windows prompt must not create a timed session or change Not started to Playing. Ordinary duplicate prevention and manual Completed/On hold status behavior should remain as in issue #6.
8. **Appearance/responsiveness:** Check both themes, resizing/scaling, keyboard navigation, and Library/Settings switching during a session. Large time values should remain readable; idle libraries should not show a running timer.

## Recovery limits and data

Session journals are under `Profiles/<game-id>/Sessions/<session-id>.json`. Each retains state (Active/Completed/Interrupted), trusted elapsed ticks, timestamps, process identity, and recovery count. `statistics.json` uses the same session ID for checkpoints, so retries and recovery replace rather than duplicate a record.

Windows may remove a process group's discoverable name when its last observation handle closes. Recovery first tries the saved group, then verifies the exact executable path, process ID, and creation time. It never attaches solely by name or PID. If a fallback process ends and another same-path process cannot be attributed, tracking stops with an explicit interruption warning rather than claiming a confirmed complete session. Unknown time stays excluded.

Storage errors display a warning and preserve prior committed files. Corrupt, recovered, or future-version journals/statistics are not silently overwritten. If saving fails, free disk space/fix access and retry; abrupt loss before a successful checkpoint cannot be reconstructed. Copying only a subset of a profile's journal/statistics files between builds is unsupported; copy the full Profiles folder while apps are closed.

Automated validation uses fake clocks, disposable storage, and windowless process/helper fixtures. The assistant has not opened UI, run RE2, triggered UAC, changed Windows time, or put the device to sleep. Owner approval remains pending.
