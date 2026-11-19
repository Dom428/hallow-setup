# Hallow Setup

One window to install, update and start the Hallow family - [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote), [Hallow Clips](https://github.com/Dom428/hallow-clips) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- A checklist of every Hallow app: what it does, the version on your PC and the newest one on GitHub
- **Install / Update** per app: nothing is downloaded until you click. The release zip is checked against the SHA-256 checksum GitHub publishes for it (no checksum or a mismatch = not installed), unpacked to a temp folder, and the app's own `Install.ps1` runs as you - never as admin
- **Hallow Toolkit** installs as admin, so Setup only starts its own installer and Windows asks you (UAC)
- **Start with Windows** per app: the app's own `Hallow<App>` value in HKCU\...\Run (`"<exe>" --tray`), the same one its Settings switch writes

Nothing to install for Setup itself: download **Hallow Setup.zip** from the [latest release](../../releases/latest), unblock and extract it, and run `HallowSetup.exe`.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## What's new

- **1.0** - First version: install, update and start-with-Windows for all seven Hallow apps, with checksum-verified downloads.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).