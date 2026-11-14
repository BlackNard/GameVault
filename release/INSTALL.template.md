# Installing GameVault @@VERSION@@ (private development preview)

This draft is unpublished. Repository access is required to download private release assets. Public distribution has not been authorized.

1. Download `GameVault-Setup-@@VERSION@@-win-x64.exe` and its `.sha256` file from this same draft.
2. In PowerShell, run `Get-FileHash .\GameVault-Setup-@@VERSION@@-win-x64.exe -Algorithm SHA256` and compare the hash with the checksum file. A matching checksum checks integrity; it is not a publisher signature.
3. The installer is currently unsigned. Do not disable Windows security controls. Public signing/trust policy remains an owner decision.
4. Quit GameVault before updating. Run setup and choose a writable local folder. The default is `%LOCALAPPDATA%\Programs\GameVault`. Do not choose the source-code or game installation folder.
5. Choose shortcuts and optionally launch the app. The .NET/Windows Desktop runtimes and their notices are bundled; no separate runtime installer is needed.

Data and Profiles live beside GameVault.exe. Upgrades and uninstall preserve them; reinstall into the same folder to reuse them. Keep a separate copy before moving an installation. Setup does not import an existing portable library into a different folder.

This Windows x64 preview enforces build 19041 or later. The final supported Windows client matrix, clean-machine/interactive installation checks, UI/game testing and performance acceptance remain pending. This is not a finished public release.

Project license selection, signing and the public download channel require owner approval. This draft grants no new project source/redistribution license. Bundled component notices are supplied separately and in the installed licenses directory.
