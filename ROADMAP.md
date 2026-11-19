# Hallow Setup roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Checklist of the seven Hallow apps with installed and latest versions; Install / Update with SHA-256-verified release zips and each app's own installer run as you; Toolkit's installer started for UAC; Start with Windows per app |
| **1.1** | Install ticked (several apps in sequence, Toolkit last, updates pre-ticked); Setup updates itself (SHA-256 checked, replaces its exe after closing and reopens) |

## Next

### Still to check by hand
- A real install / update from GitHub (tested against a local stand-in for GitHub only)
- Hallow Toolkit's installer through UAC
- Self-update from the real GitHub release (tested against the stand-in, including a real exe swap in a temp folder)

### Later
- Uninstall per app (each app's own Uninstall.ps1)