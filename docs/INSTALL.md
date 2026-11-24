# Installation

## Requirements

| Component | Version | Notes |
|---|---|---|
| GTA V (PC) | A build supported by your ScriptHookV version | Story Mode only. ScriptHookV blocks GTA Online. |
| [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) | Latest | Must match your game build. After every GTA update, wait for a ScriptHookV update. |
| [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) | **v3.6.0 or newer** (nightly builds support the newest game versions) | The mod is a v3 script. |
| .NET Framework | 4.8 | Preinstalled on current Windows 10/11. |
| Visual C++ Redistributable | 2015-2022 x64 | Needed by ScriptHookV / SHVDN. |

Optional: an AI provider account or a local model server for AI dialogue (an offline dialogue engine is built in).

## Steps

1. **Install ScriptHookV.** From the ScriptHookV archive copy `ScriptHookV.dll` and `dinput8.dll` into the GTA V
   folder (the one containing `GTA5.exe`).
2. **Install ScriptHookVDotNet.** Copy `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll`
   (and their `.xml` files) into the same folder.
3. **Create a `scripts` folder** in the GTA V folder if it doesn't exist.
4. **Copy the mod.** From this package, copy the *contents* of `scripts` into your GTA V `scripts` folder so you end up with:

```
Grand Theft Auto V/
├─ GTA5.exe
├─ ScriptHookV.dll, dinput8.dll
├─ ScriptHookVDotNet.asi, ScriptHookVDotNet2.dll, ScriptHookVDotNet3.dll
└─ scripts/
   ├─ OrganizationEmpire.dll
   └─ OrganizationEmpire/
      ├─ OrganizationEmpire.ini      settings (also editable in-game)
      ├─ flags/                      custom flag images (PNG/JPG/DDS/BMP)
      ├─ saves/                      save slots (created as you play)
      └─ logs/                       OrganizationEmpire.log
```

5. **Play.** Start Story Mode. After loading you'll see *"Organization Empire ready. Press F7..."*. Press **F7** and
   found your faction in the creation wizard.

## Optional: AI dialogue

Works out of the box with the offline engine. To use a real model, open **F7 > Settings > AI dialogue**, apply a
provider preset, set the model and API key, and press **Test connection**. Details: [AI_SETUP.md](AI_SETUP.md).

## Updating

Replace `scripts/OrganizationEmpire.dll`. Keep the `scripts/OrganizationEmpire` folder - it contains your settings and
saves. New settings are added to the `.ini` automatically with default values.

## Uninstalling

Delete `scripts/OrganizationEmpire.dll` and the `scripts/OrganizationEmpire` folder (back up `saves/` first if you
want to keep your factions). The mod never modifies game files or your GTA story saves, and every in-world change it
makes (spawned NPCs, blips, relationship and model-suppression tweaks) only lasts for the current session.

## Building it yourself

See the README: `packaging/build-release.sh` or `packaging/build-release.ps1` with the .NET 8 SDK.
To deploy directly while developing, copy `src/OrganizationEmpire/bin/Release/net48/OrganizationEmpire.dll`
into your `scripts` folder; ScriptHookVDotNet reloads scripts with the **Insert** key.
