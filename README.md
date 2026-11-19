# Hallow Setup

One window to install, update, start and uninstall the Hallow family - [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote), [Hallow Clips](https://github.com/Dom428/hallow-clips), [Hallow Keys](https://github.com/Dom428/hallow-keys) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- A checklist of every Hallow app: what it does, the version on your PC and the newest one on GitHub
- **Install / Update** per app: nothing is downloaded until you click. The release zip is checked against the SHA-256 checksum GitHub publishes for it (no checksum or a mismatch = not installed), unpacked to a temp folder, and the app's own `Install.ps1` runs as you - never as admin
- **Hallow Toolkit** installs as admin, so Setup only starts its own installer and Windows asks you (UAC)
- **Install ticked**: several apps in one go, one after another
- **Updates itself** from its own GitHub releases (checksum checked, reopens by itself)
- **Uninstall** per app (the bin button): says what goes and closes the app first. Your settings stay unless you tick **Also remove my settings and data**, and everything goes to the Recycle Bin. Hallow Vault's backups, Hallow Clips' videos and Hallow Keys' password vault are never touched. Hallow Toolkit: Setup starts its own uninstaller (it needs admin) or tells you how to remove it
- **Start with Windows** per app: the app's own `Hallow<App>` value in HKCU\...\Run (`"<exe>" --tray`), the same one its Settings switch writes

Nothing to install for Setup itself: download **Hallow Setup.zip** from the [latest release](../../releases/latest), unblock and extract it, and run `HallowSetup.exe`.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## What's new

- **1.3** - Uninstall any Hallow app from Setup: the app closes first, settings stay unless you tick them, and everything goes to the Recycle Bin. Backups, clips and your password vault are never touched.
- **1.2** - Hallow Keys is in the list.
- **1.1** - Tick apps and click **Install ticked** to install or update them one after another (updates are ticked for you, Toolkit goes last). Hallow Setup updates itself too - same checksum check, then it reopens.
- **1.0** - First version: install, update and start-with-Windows for all seven Hallow apps, with checksum-verified downloads.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).