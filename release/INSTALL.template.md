# Installing GameVault @@VERSION@@ (development preview)

1. Download `GameVault-Setup-@@VERSION@@-win-x64.exe` and its `.sha256` file from the same release.
2. In PowerShell, run `Get-FileHash .\GameVault-Setup-@@VERSION@@-win-x64.exe -Algorithm SHA256` and compare the hash with the checksum file. A matching checksum checks integrity; it is not a publisher signature.
3. The installer is currently unsigned, so Windows may warn about an unknown publisher. Do not disable Windows security controls; only continue if the checksum matches.
4. Quit GameVault before updating. Run setup and choose a writable local folder. The default is `%LOCALAPPDATA%\Programs\GameVault`. Do not choose the source-code or game installation folder.
5. Choose shortcuts and optionally launch the app. The .NET/Windows Desktop runtimes and their notices are bundled; no separate runtime installer is needed.

Data and Profiles live beside GameVault.exe. Upgrades and uninstall preserve them; reinstall into the same folder to reuse them. Keep a separate copy before moving an installation. Setup does not import an existing portable library into a different folder.

This Windows x64 preview needs Windows build 19041 or later. The final supported Windows matrix, clean-machine checks and performance acceptance are still pending, so treat this as a preview.

GameVault is source-available and free for personal, non-commercial use; all rights are reserved (see `LICENSE` in the repository). Bundled component notices are supplied separately and in the installed licenses directory.
