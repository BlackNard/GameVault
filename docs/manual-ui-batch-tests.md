# Manual UI fixes #44–#49: owner retest

Automated checks cover template geometry/layout, image aspect ratio and caption input policy without opening a window. Actual native interactions remain owner checks.

- #44: expand/collapse Filter games, Screenshots and recordings, Game file locations and Local profile storage. Indicators should be clean arrows in both states/themes. Check shared menu ticks/arrows too.
- #45: library and dedicated game-page covers show the complete image proportionally. Check a portrait cover plus a square/landscape example; no cropping or distortion.
- #46: titlebar icons are clear and centered. Minimize, maximize/restore and close respond once per click. Check hover/press, drag-out cancellation, double-click titlebar, resize borders/corners, close-to-tray, Windows Snap and multiple display scales/monitors. No focus changes or unintended game interruption.
- #47: Add game stays a compact40px normal-height action, centered beside the Library heading, rather than stretching.
- #48: Sessions & notes heading/status/export/sort/navigation have consistent proportions. Statistics stay grouped. Longest session has no permanent box but remains keyboard/click navigable. Verify status updates, export, session selection, notes/review, activity and time corrections still work.
- #49: Advanced privacy and its legacy artwork-key button are absent. Local profile storage and key-free cover import remain available.

Use the newly delivered local test build with the latest imported library. Earlier builds and private snapshots remain untouched. Report additional failures as new tickets; do not upload personal notes/saves/captures.
