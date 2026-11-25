# GTA V: Organization Empire

A single-player **faction, crime family, militia and nation management mod** for Grand Theft Auto V (PC), built on
ScriptHookV + ScriptHookVDotNet v3. Found a **Street Gang**, a **Mafia family** or an **Organization / Militia**, recruit
NPCs off the street, command them in the field, fight rival factions for territory, talk to your people through
AI-generated dialogue - and eventually take over the (fictional) state of San Andreas and run it as its ruler.

> Everything here is fictional GTA gameplay. Crime, conflict and the takeover campaign are abstract game systems
> (resources, missions, progression), not real-world guidance.

## Highlights

| Area | What you get |
|---|---|
| **Three faction types** | Different ranks, tiers, income models, missions, recruitment modifiers, events, dialogue voice and takeover paths. The militia has the strongest path to power, the mafia rises through influence, the gang path is slow and hard. |
| **Command interface** (F7) | 19-tab command console: Dashboard, Faction, Members, Recruitment, Ranks, Squads/Units, AI Dialogue, Missions, Territory, Vehicles, Headquarters, Relations, World, Resources, Identity, Flag Designer, Government, Settings, Save/Load. Keyboard **and** mouse, confirmations, toasts, optional pause/slow-motion. |
| **Recruit real NPCs** | Look at a pedestrian, press **E**: view their generated profile, see your odds with a factor breakdown, and persuade / pay / intimidate / appeal to the cause. Mission-critical and protected NPCs are never eligible. |
| **Controllable members** | Follow (native group AI), stay, guard, defend you, defend a location, move to waypoint, return to HQ, enter/exit vehicles, attack your target, formations, loadouts, stuck recovery, deployment caps. Quick field orders on **F8**. |
| **Ranks & squads** | Fully customizable ranks (create/rename/delete/reorder/permissions/salary) with per-type templates; squads with leaders, roles, formations, vehicles, garrisons and orders. |
| **AI conversations** (T) | Type to any member (face to face or by radio) or to civilians. Works with OpenAI-compatible APIs (OpenAI, OpenRouter, Groq, Ollama, LM Studio...), Anthropic and Gemini, or a built-in **offline** engine. Async (never freezes the game), memory with limits, faction-specific voices. Orders you type are detected and only run after **you confirm** them. |
| **Territory & wars** | 37 territories mapped onto GTA's own map zones, influence-based control, map overlay, 12 rival factions with diplomacy (rivalry, war, ceasefire, peace, alliance, tribute, covert ops), attacks on your turf, patrols, elimination. |
| **Missions** | 28 missions across 10 objective types (assault, defend, delivery, collection, escort, recruitment, recon, sit-down meetings with choices, assassination, final campaign) with real objectives, failure conditions and rewards. |
| **Identity & flags** | Colors, tag, motto, uniforms, identity presets, a flag designer with 12 patterns and emblems, custom PNG flags (shown in menus), and a physical flag pole at your HQ. |
| **Government** | After the takeover: name the state and government, choose your title and flag, appoint ministers from your members, set taxes and policies, manage stability, legitimacy, insurgency and forces. A pre-takeover backup lets you revert. |
| **Living world** | Rival factions have named leaders and lieutenants, money, members, arms and morale, and act on their own: recruit, arm, expand, ally, make peace, retaliate. Their wars are **fought for real** in the street when you're near (you can take a side), battles show on the map, crews recruit passers-by in front of you, and leaders can be found with bodyguards. See [docs/LIVING_WORLD.md](docs/LIVING_WORLD.md). |
| **Mod integration** | Detects other installed mods (read-only), warns about hotkey clashes and overlapping gang mods with one-click fixes, runs optional isolated integrations (public API for other scripts, map-mod HQ sites, police-mod compatibility, phone contacts via iFruitAddon2) and documents every one in an integration registry. See [docs/INTEGRATIONS.md](docs/INTEGRATIONS.md). |
| **English / العربية** | Full Arabic interface (2,000+ translated strings) with right-to-left layout, Arabic AI replies and Arabic chat commands. GTA's fonts have no Arabic, so the mod draws Arabic itself from bundled glyph textures. Switch any time in Settings. See [docs/ARABIC.md](docs/ARABIC.md). |
| **Robust** | Zero third-party dependencies (one DLL), atomic saves with backup recovery, every subsystem error-isolated, entity caps, API keys redacted from logs (optional DPAPI encryption). |

## Quick start

1. Install **ScriptHookV** and **ScriptHookVDotNet v3.6.0+** (see [docs/INSTALL.md](docs/INSTALL.md)).
2. Copy the package's `scripts` folder into your GTA V folder.
3. Start Story Mode and press **F7** to found your faction.

| Key | Action |
|---|---|
| **F7** | Open / close the command menu |
| **F8** | Quick field orders (everyone near you, squads, formations, vehicles) |
| **E** | Interact with the NPC you're looking at (recruit, profile, orders) |
| **T** | Talk (AI dialogue) to the NPC you're looking at / nearest member |
| Menu | Arrows, Enter, Backspace (back), Tab / PgUp / PgDn (tabs), Esc; mouse click, wheel, right-click |

All keys are configurable (Settings > Hotkeys or `scripts/OrganizationEmpire/OrganizationEmpire.ini`).

## Documentation

- [Installation](docs/INSTALL.md)
- [الواجهة العربية / Arabic interface](docs/ARABIC.md)
- [Living world](docs/LIVING_WORLD.md)
- [Mod integration & compatibility](docs/INTEGRATIONS.md) · [Mod research notes](docs/MOD_RESEARCH.md)
- [Gameplay guide](docs/GAMEPLAY_GUIDE.md)
- [AI dialogue setup & privacy](docs/AI_SETUP.md)
- [Flags & asset pipeline](docs/FLAG_ASSET_PIPELINE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Known limitations](docs/LIMITATIONS.md)
- [Architecture & extending the mod](docs/ARCHITECTURE.md)

## Building from source

Requires the .NET 8 SDK (works on Windows and Linux; the mod itself targets .NET Framework 4.8 via reference assemblies).

```bash
./packaging/build-release.sh          # Linux / macOS / Git Bash
# or
powershell -ExecutionPolicy Bypass -File packaging/build-release.ps1
```

This runs the unit tests, builds `OrganizationEmpire.dll`, generates the default `.ini` and sample flags, and writes
`dist/OrganizationEmpire-v<version>.zip`.

```
src/OrganizationEmpire.Core   engine-independent simulation, persistence, AI layer (net48 + net8.0)
src/OrganizationEmpire        ScriptHookVDotNet v3 script: world, missions, UI (net48, single DLL)
tests/                        xUnit tests for the core (net8.0)
tools/                        packaging helper
docs/                         user and developer documentation
```

## Status

The core simulation, persistence and AI layers are covered by 186 automated tests. The in-game layer compiles cleanly
against the ScriptHookVDotNet 3.6.0 API, but it could not be exercised inside a running GTA V session during
development, so treat this release as a first version: please report anything that misbehaves (attach
`scripts/OrganizationEmpire/logs/OrganizationEmpire.log`). See [docs/LIMITATIONS.md](docs/LIMITATIONS.md).
