HALLOW SETUP - install, update, start and uninstall the Hallow apps

HOW TO USE
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click HallowSetup.exe (no admin needed, nothing to install)

WHAT IT DOES
- Shows every Hallow app (Mixer, Hub, Vault, Overlay, Remote, Clips, Keys, Assist, Guard, Toolkit) and the Hallow
  Shield browser extension, with the version on your PC and the newest one.
- Install / Update downloads that app's release from GitHub only when you click, checks it against
  GitHub's SHA-256 checksum, and runs the app's own installer as you (never as admin).
- Hallow Toolkit installs as admin: Setup starts its installer and Windows asks you first.
- Start with Windows: the same switch as in each app's own Settings.
- Uninstall (bin button): closes the app, removes it, and keeps your settings unless you tick them.
  Everything goes to the Recycle Bin. Backups, clips and your password vault are never touched.

HALLOW SHIELD (BROWSER EXTENSION)
- Install / Update puts Shield's newest release (checked against GitHub's SHA-256) into
  %LOCALAPPDATA%\Hallow Shield\chrome, replacing the old files. Your Shield settings live in the browser, not there.
- Then: Open Chrome / Edge / Brave extensions (the address is also copied, in case the browser doesn't open it),
  Developer mode on -> Load unpacked -> pick that folder. After an update, click the reload arrow on Shield's card.
- Uninstall moves the folder to the Recycle Bin; remove Shield's card in the browser yourself.

WHAT'S NEW IN 1.7
- New app icon (a box with a download arrow), and every app in the list shows its own icon.

WHAT'S NEW IN 1.6
- Hallow Shield, the browser extension, is in the list.

WHAT'S NEW IN 1.5
- Hallow Guard is in the list.

WHAT'S NEW IN 1.4
- Hallow Assist is in the list.

WHAT'S NEW IN 1.3
- Uninstall per app (to the Recycle Bin; settings only when you tick them).

WHAT'S NEW IN 1.2
- Hallow Keys is in the list.

WHAT'S NEW IN 1.1
- Install ticked: tick apps and install or update them one after another.
- Hallow Setup updates itself (checksum checked, reopens by itself).

WHAT'S NEW IN 1.0
- First version.

CREDITS
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.