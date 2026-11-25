# GTA V mod research notes

What was looked at while building the integration layer, what each mod does, whether it can be connected to, and what
Organization Empire did about it. **No code or assets were copied**; where a mechanic was useful it was reimplemented
independently. Licenses marked "not verified" could not be confirmed from public pages during research; those mods
are therefore only detected, never bundled or copied.

## Method

1. Search for single-player mods in the categories of the brief (gangs/factions, NPC AI, territory and gang wars,
   police, military/combat, vehicles, interiors/HQ, missions/robbery, animation/interaction, UI/maps).
2. For each candidate: runtime (ScriptHookV `.asi`, ScriptHookVDotNet script, RAGE Plugin Hook plugin), public API,
   license, default keys and files.
3. Decide: integrate directly / connect through an API / optional dependency / detect and adapt / reimplement.

## Findings by category

### Gangs, factions and territory

| Mod | Runtime | License | Decision |
|---|---|---|---|
| **Gang and Turf Mod** (lucasvinbr) — AI gangs fight for zones; the player's gang expands; open source on GitHub; depends on ScriptHookVDotNet (nightly) and LemonUI (older versions NativeUI). | SHVDN | Open source, license not verified | **Detect + coexistence.** No public API to call. Its core idea — AI gangs that expand and fight each other without the player — was reimplemented independently as the Living World (utility-AI decisions, multi-hour battles, leaders), not from its code. |
| **Los Santos RED** (thatoneguy650) — large RPH mod with gangs, police behavior, crime reporting, property; needs RAGE Plugin Hook 1.100+, Packfile Limit Adjuster and Heap Adjuster. | RPH | Open source on GitHub, license not verified | **Unavailable** for direct linking (different hook). Detected → coexistence + police compatibility. |
| GangWarfare, The Gang, Liberty Underworld (.Net) | SHVDN | See their pages | Not added to the catalog yet (no stable detection signature found). They still appear as "unknown scripts" by assembly name. |

### NPC behavior and bodyguards

| Mod | Decision |
|---|---|
| **Bodyguard Squads** (Eddlm; up to seven independent squads, NativeUI) and **Squads Manager** (an "enhanced" rewrite) | Detect only. Their bodyguards are script-owned mission entities; Organization Empire's recruitment and cleanup already skip those (entity ownership guard). |
| **Police Bodyguard** (menu on F6) | Hotkey check. |
| NativeTrainer Bodyguards Extension | Part of a trainer; covered by trainer detection. |

### Police and law enforcement

| Mod | Decision |
|---|---|
| **LSPD First Response** — RAGE Plugin Hook plugin in `Plugins/LSPDFR`; RPH and ScriptHookV are normally installed together. | No API reachable from ScriptHookVDotNet. Detected → law enforcement is never recruitable while it is installed, so its officers are not pulled into factions. |

### Trainers, menus and UI

| Mod | Decision |
|---|---|
| **Menyoo** — opens with **F8** (the same default as Organization Empire's Quick Orders). Deprecated original, maintained as Menyoo 2.0. | Hotkey conflict + one-click rebind. Spooner XML maps used as HQ locations. |
| **Simple Trainer** — keys in `trainerv.ini` `[KeyBindings]` (`MenuKey=114` = F3, `HideMenuKey=115` = F4 by default). | Reads the actual keys from its config for the conflict check. |
| Native Trainer / Enhanced Native Trainer — F4 commonly reported. | Hotkey check marked "commonly reported". |
| **LemonUI** (MIT; replacement for NativeUI) / **NativeUI** (deprecated, archived) | Not used — Organization Empire has its own UI with Arabic/RTL support. Detected for other mods' missing-dependency checks. |
| **iFruitAddon2** (Bob74; update of CamxxCore's iFruitAddon; NuGet package, SHVDN v3) — adds phone contacts. | **Optional dependency** through its public API by reflection: phone contacts for the menu and for calling the crew. Experimental until verified in game. |

### Interiors, maps and headquarters

| Mod | Decision |
|---|---|
| **Map Editor** (Guadmaz) — XML maps. **MapLoaderAuto** — loads Map Editor XML from `scripts\MapLoaderAuto\`, zones set in its INI. | Read map XML (Map Editor `Position` and Menyoo `PositionRotation`/`ReferenceCoords`), compute a robust center, offer it as an HQ site. |

### Vehicles, missions/robbery, animation

No single-player mod in these categories exposed an API a ScriptHookVDotNet script can safely call. Organization
Empire keeps its own implementations (faction garage, 28 missions, scenario-based idle animations). Add-on vehicle and
ped models work wherever this mod accepts model names (uniform presets, rival models via the API).

### Frameworks and performance

ScriptHookV (Alexander Blade), ScriptHookVDotNet (zlib), ASI loader, RAGE Plugin Hook (separate hook), OpenIV ASI,
Heap Limit Adjuster and Packfile Limit Adjuster are detected for information and version checks.

## Mechanics adopted (reimplemented independently)

| Mechanic seen in the ecosystem | Organization Empire implementation |
|---|---|
| AI gangs that expand and fight each other on their own | Living World: per-faction utility AI (recruit, arm, expand, retaliate, seek allies, make peace, lay low), multi-hour battles, coexistence when another gang mod does the same |
| Visible gang wars in the street | Battles near the player are fought by spawned fighters; deaths decide the outcome; taking a side changes relations |
| Gang leaders / hierarchy | Named leaders and lieutenants with traits, successions, coups, killable in the world |
| Squads/bodyguards | Already present (members, squads, formations); other mods' bodyguards are left alone |
| Phone integration | Optional iFruitAddon2 contacts |
| Map-editor interiors as bases | Map XML → HQ locations |

## Sources

- [Gang and Turf Mod (gta5-mods.com)](https://www.gta5-mods.com/de/scripts/gang-and-turf-mod)
- [PC Gamer: Inside the GTA 5 mod that brings San Andreas gang warfare to Los Santos](https://pcgamer.com/inside-the-gta-5-mod-that-brings-san-andreas-gang-warfare-to-modern-day-los-santos)
- [Los Santos RED (GitHub)](https://github.com/thatoneguy650/Los-Santos-RED) · [DeepWiki overview](https://deepwiki.com/thatoneguy650/Los-Santos-RED) · [LCPDFR page](https://www.lcpdfr.com/downloads/gta5mods/scripts/36665-los-santos-red/)
- [iFruitAddon2 (GitHub)](https://github.com/bob74/ifruitaddon2) · [NuGet](https://www.nuget.org/packages/ifruitaddon2)
- [Menyoo PC (gta5-mods.com)](https://www.gta5-mods.com/scripts/menyoo-pc-sp)
- [Simple Trainer key bindings (TrainerV KeyBinding Fix)](https://www.gta5-mods.com/de/misc/native-trainer-v-keybinding-fix)
- [LemonUI (GitHub)](https://github.com/justalemon/LemonUI) · [NativeUI (GitHub, archived)](https://github.com/Guad/NativeUI)
- [MapLoaderAuto (gta5-mods.com)](https://www.gta5-mods.com/scripts/maploader-pro)
- [Bodyguard Squads](https://www.gta5-mods.com/fi/scripts/bodyguard-squads-1-2-2022-update) · [Squads Manager](https://www.gta5-mods.com/pl/scripts/squads-manager-enhanced-bodyguard-squads) · [Police Bodyguard](https://www.gta5-mods.com/nl/scripts/police-bodyguard-for-gta-v)
- [ScriptHookVDotNet project (zlib)](https://awesome.ecosyste.ms/projects/github.com%2Fscripthookvdotnet%2Fscripthookvdotnet)
- [RAGE Plugin Hook install guide](https://dy96wf624pa9e.cloudfront.net/gta-5/tutorials/9525-how-to-install-rage-plugin-hook-for-gta-5-complete-guide-2026)
