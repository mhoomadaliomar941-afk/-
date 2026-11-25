# Mod integration & compatibility

> **بالعربية باختصار:** المود يكتشف المودات المثبّتة الأخرى (قراءة فقط، لا يعدّل ملفاتها أبدًا)، ويعرض
> التعارضات مثل تشابه المفاتيح (Menyoo يستخدم F8 مثل الأوامر السريعة) مع زر إصلاح يغيّر إعدادات هذا المود فقط،
> ويشغّل تكاملات اختيارية معزولة: إذا فشل أحدها يتوقف وحده ولا يتأثر باقي المود. الصفحة في اللعبة:
> **F7 ← الإعدادات ← المودات والتكامل**.

Organization Empire is meant to live alongside other single-player mods, not replace them. This page is the
integration registry: what is supported, what is experimental, what is only detected, and what is not possible.
The research behind it is in [MOD_RESEARCH.md](MOD_RESEARCH.md).

## How detection works (read-only)

At startup (and on *Rescan*) a background thread looks at three things, all read-only:

1. **Files and folders** in the GTA V folder (e.g. `Menyoo.asi`, `scripts/MapEditor.dll`, `Plugins/LSPDFR/`).
2. **Native modules loaded in the game process** (ScriptHookV, `.asi` plugins).
3. **.NET assemblies loaded by ScriptHookVDotNet**, with their version, their references and how many `Script`
   classes they contain. Every script assembly is listed, even ones the catalog doesn't know.

Paths are shown relative to the GTA V folder, so nothing about your drive or user name reaches the log or the menu.
Other mods' files are never written, copied, moved or deleted, and nothing is downloaded.

## Compatibility checks

| Check | What you get |
|---|---|
| Hotkeys | Our keys are compared with detected mods' defaults, or the keys read from their config (Simple Trainer's `trainerv.ini`). A conflict offers **"Move X to Y"**, which only changes Organization Empire's own key. |
| Overlapping systems | Gang/war mods (Gang and Turf, Los Santos RED) → coexistence mode. Bodyguard/squad mods → their NPCs are never touched. Police mods → law-enforcement recruitment stays off. |
| Missing dependencies | Catalog dependencies (e.g. Gang and Turf needs LemonUI) and, for any script, references to LemonUI / NativeUI / iFruitAddon2 that are not installed. |
| Duplicates | More than one `OrganizationEmpire*.dll` in `scripts`. |
| Versions | ScriptHookVDotNet older than 3.6.0. |
| Hooks | RAGE Plugin Hook present: explained that its plugins can't be called from here. |

## Integration registry

Status meanings: **Supported** = implemented and covered by automated tests; **Experimental** = implemented against
the other mod's documented API but not verified in a running game; **Detected only** = recognized, and this mod
adapts its own behavior, but there is no API to connect to; **Unavailable** = researched, no integration possible from
a ScriptHookVDotNet script; **Incompatible** = known to clash (none listed so far).

### Integrations (things this mod actively does)

| Integration | Status | Needs | What it does |
|---|---|---|---|
| Public API for other scripts | Supported | — | Other SHVDN scripts read faction/territory/battle/news data, subscribe to events, and (only if *Allow external commands* is on) add factions, show notifications and report kills. |
| Coexistence with gang/war mods | Supported | Gang and Turf or Los Santos RED (Auto), or forced On | Half the street-battle fighters, no ambient crews or recruitment scenes, no suppression of vanilla gang models (the other mod may rely on them). |
| Police mod compatibility | Supported | LSPDFR or Los Santos RED | Law enforcement can't be recruited while it is installed (runtime only, not saved to the ini). |
| Map mods as HQ sites | Supported | Map Editor / Menyoo / MapLoaderAuto XML files | Lists maps on the Headquarters page with their median center; pick one to set a waypoint. Files are parsed with DTDs disabled and a size limit. |
| Hotkey conflict check | Supported | — | See above. |
| Entity ownership guard | Supported | — | NPCs and vehicles owned by other scripts (mission entities) are never recruited, ordered or deleted; cleanup only touches entities this mod spawned. |
| Phone contacts | **Experimental** | iFruitAddon2 | Adds "Organization Empire" (opens the menu) and "Call the crew" (summons members) to the phone through iFruitAddon2's API via reflection. Not bundled, not referenced at compile time. |

### Catalog of recognized mods

| Mod | Category | Status | Notes |
|---|---|---|---|
| Script Hook V | Framework | Supported (required) | |
| ScriptHookVDotNet | Framework | Supported (required) | 3.6.0+ recommended |
| ASI Loader (dinput8.dll) | Framework | Detected only | |
| RAGE Plugin Hook | Framework | Unavailable | Separate hook; can run side by side |
| LemonUI / NativeUI | Library | Detected only | Not needed by this mod; used for dependency checks |
| iFruitAddon2 | Library (phone) | Experimental | Phone contacts integration |
| Gang and Turf Mod | Gangs & territory | Detected only | Coexistence mode |
| Los Santos RED | Gangs/police (RPH) | Unavailable | Coexistence + police compatibility |
| Bodyguard Squads, Squads Manager, Police Bodyguard | NPC behavior | Detected only | Entity guard; F6 hotkey check for Police Bodyguard |
| LSPD First Response | Police (RPH) | Detected only | Police compatibility |
| Menyoo PC | Trainer / maps | Supported | F8 conflict check; Spooner maps as HQ sites |
| Simple Trainer | Trainer | Supported | Keys read from `trainerv.ini` |
| Native Trainer, Enhanced Native Trainer | Trainer | Detected only | F4 (commonly reported) |
| Map Editor, MapLoaderAuto | Maps | Supported | Maps as HQ sites |
| OpenIV ASI, Heap/Packfile limit adjusters | Tools/performance | Detected only | Informational |

Detection signatures marked as file-name guesses (Gang and Turf, Bodyguard Squads, Squads Manager, Police Bodyguard,
Los Santos RED) may miss renamed installs; unknown scripts still appear in the list by assembly name.

## Failure isolation

Each integration runs inside the integration host: start, tick and stop are wrapped individually. An error at start,
or three errors while running, switches **only that integration** off with the reason shown in the menu and the
log (`[Integration:<id>] ...`, secrets redacted). *Enable* in the menu resets it. A failing scan leaves the mod running
with no integrations.

## Settings (`[Integrations]` in OrganizationEmpire.ini)

| Key | Default | |
|---|---|---|
| `Enabled` | true | Master switch |
| `ScanOnStartup` | true | Scan when the game starts |
| `CheckHotkeyConflicts` | true | |
| `Coexistence` | Auto | Auto / On / Off |
| `PublicApi` | true | Expose `EmpireApi` |
| `AllowExternalCommands` | **false** | Let other scripts change game state |
| `PhoneContact` | true | iFruitAddon2 contacts when installed |
| `MapLocations` | true | |
| `ExtraMapFolders` | "" | e.g. `mymaps;scripts/MyMapMod` |
| `DisabledIntegrations` | "" | Managed by the menu |

## Public API (for mod authors)

`OrganizationEmpire.dll` exposes `OrganizationEmpire.Api.EmpireApi` (static). Reference the DLL, or use reflection
so your mod still runs without it.

```csharp
using OrganizationEmpire.Api;

public class MyScript : GTA.Script
{
    public MyScript()
    {
        Tick += (s, e) =>
        {
            if (!EmpireApi.IsReady) return;              // mod not loaded yet, or API switched off
            foreach (var rival in EmpireApi.GetRivals()) { /* rival.Name, rival.LeaderName, rival.Manpower, rival.Territories */ }
        };
        EmpireApi.BattleStarted += b => GTA.UI.Notification.Show("Battle in " + b.TerritoryId);
        EmpireApi.NewsPosted += n => { /* n.Text, n.Kind, n.FactionIds */ };
    }
}
```

Queries: `ApiVersion`, `IsReady`, `HasFaction`, `FactionName`, `FactionType`, `MemberCount`, `IsFactionMember(pedHandle)`,
`GetTerritoryByZone(zone)`, `GetTerritories()`, `GetRivals()`, `GetActiveBattles()`, `GetRecentNews(max)`.

Events: `NewsPosted`, `BattleStarted`, `BattleEnded`, `TerritoryChanged`, `MemberRecruited`, `MemberLost`,
`MissionFinished`. Handlers run on the script thread; keep them fast (over 50 ms is logged). A handler that throws
three times is unsubscribed.

Commands (need `AllowExternalCommands = true`; queued and run on Organization Empire's next tick):

```csharp
EmpireApi.RegisterFaction(new ExternalFactionSpec
{
    Id = "nightowls", Name = "Night Owls", Color = "#7A3DB8",
    PedModels = new[] { "g_m_y_ballaeast_01" }, Weapon = "WEAPON_MICROSMG",
    HomeTerritories = new[] { "lamesa" }, Strength = 45,
}, out string error);                              // becomes "ext_nightowls": recruits, fights, gets a leader
EmpireApi.ReportKill(victimPedHandle, killerPedHandle);   // counts like kills the mod sees itself
EmpireApi.ShowNotification("Hello from my mod");
```

Reflection (no compile-time reference):

```csharp
var api = Type.GetType("OrganizationEmpire.Api.EmpireApi, OrganizationEmpire");
bool ready = api != null && (bool)api.GetProperty("IsReady").GetValue(null);
```

## Credits and licensing

No code or assets from other mods are included in Organization Empire. Integrations use public APIs at runtime or
read files the player installed. Names belong to their authors: Script Hook V (Alexander Blade), ScriptHookVDotNet
(crosire and contributors, zlib), LemonUI (Lemon, MIT), NativeUI (Guad), iFruitAddon2 (Bob74, based on iFruitAddon by
CamxxCore), Gang and Turf Mod (lucasvinbr), Los Santos RED (thatoneguy650), Bodyguard Squads (Eddlm), LSPD First Response
(G17 Media), Menyoo (MAFINS; Menyoo 2.0 by ItsJustCurtis), Simple Trainer (sjaak327), Map Editor (Guadmaz), OpenIV
(OpenIV team), RAGE Plugin Hook (its team).
