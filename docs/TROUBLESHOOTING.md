# Troubleshooting

First look at the logs:

- `scripts/OrganizationEmpire/logs/OrganizationEmpire.log` - this mod (API keys are redacted)
- `ScriptHookVDotNet.log` in the GTA V folder - script loading errors
- `ScriptHookV.log` in the GTA V folder - hook / game version problems

Turn on **Settings > Debug > Log level: Debug** and the **debug overlay** (entity counts, tick time) when reporting a problem.

## The mod doesn't load / F7 does nothing

| Check | Fix |
|---|---|
| No "Organization Empire ready" message after loading | Is `OrganizationEmpire.dll` directly in `scripts/` (not in a subfolder)? Does `ScriptHookVDotNet.log` mention it? |
| `ScriptHookVDotNet.log` says it can't load the file or a type | Install ScriptHookVDotNet **v3.6.0 or newer** (try the latest nightly for new game builds). Make sure `ScriptHookVDotNet3.dll` is in the GTA folder. |
| Game crashes / ScriptHookV error at start | GTA updated and ScriptHookV doesn't support the new build yet - wait for an update. |
| Windows blocked the DLL | Right-click the DLL > Properties > Unblock (zip files downloaded from the internet). |
| "failed to start" notification | Read the log; often the `scripts` folder isn't writable (e.g. protected Program Files folder). Run the game outside Program Files or give your user write access. |
| Key conflict | Another mod uses F7. Change `OpenMenu` in `OrganizationEmpire.ini` (any `System.Windows.Forms.Keys` name, e.g. `F9`, `Home`, `NumPad5`). |

## Menu and input

- **Typing doesn't produce the right characters**: switch *Settings > General > Text input* to *NativeKeyboard*.
- **Mouse doesn't work**: enable *Mouse support*; mouse input requires keyboard & mouse mode (not gamepad).
- **Esc opens the pause menu**: it shouldn't while the mod menu is open; close the mod menu with F7 if another mod
  re-enables the pause control.
- **Menu too big/small**: *Interface scale* (0.75 - 1.25).
- **Game keeps running while the menu is open**: by design. Choose *Slow motion* or *Pause* under *While the menu is open*.

## Members

| Symptom | Cause / fix |
|---|---|
| Members don't appear | Only members with field orders are spawned (Follow etc.), up to *Max deployed members*. Story mission active? They stand down until it ends. |
| Only some members follow | Deployment cap, or the global entity cap. Check Settings > Spawning. |
| A member is stuck | They re-path automatically and are repositioned when off-screen; or order *Follow* again. |
| Members fight each other or attack me | Shouldn't happen (shared relationship group). Report it with the log. |
| Can't recruit someone | The popup tells you why: mission/script NPC, already refused (cooldown), law enforcement (setting), roster full, story mission active, recruitment closed. |
| Members gone after loading | Field followers respawn next to you after loading; guards appear when you approach the HQ; others are off-map by design. |

## AI dialogue

See [AI_SETUP.md](AI_SETUP.md) for the error table. Quick checks: **Test connection**; the model name must match your
provider exactly; local servers must be running; firewall/antivirus may block the game process from the network.
While any problem persists, the offline engine answers so conversations still work.

## Territory, missions, world

- **Territory blip slightly off the area**: territory positions are approximate until you visit; the mod snaps them
  to the real zone the first time you enter. Ownership always uses GTA's real zone codes.
- **Mission won't start**: a story mission is active, another mod mission is running, or requirements aren't met
  (the menu shows the reason).
- **Flag pole missing at HQ**: the chosen flag model isn't in your game build (logged), the HQ flag is turned off, or
  you are more than ~300 m away.
- **Too many NPCs / low FPS**: lower *Max deployed members*, *Max mission hostiles*, *Max rival patrol NPCs* and the
  *Global entity cap*, or disable rival patrols.

## Saves

- Saves are in `scripts/OrganizationEmpire/saves`. If a slot is damaged the mod automatically loads the `.bak` copy.
- To start fresh: Faction tab > Disband (keeps files), or delete the save files.
- `backup_pre_takeover` is created right before the final campaign mission.

## Resetting settings

Delete `scripts/OrganizationEmpire/OrganizationEmpire.ini`; it's recreated with defaults on the next start.
Invalid values in the ini are reported in the log and replaced with defaults (and clamped to safe ranges).
