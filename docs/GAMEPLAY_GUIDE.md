# Gameplay Guide

## Founding a faction

Press **F7** with no faction loaded to open the creation wizard. You choose:

- **Type** (fixed for the faction's lifetime): Street Gang, Mafia, Organization/Militia.
- **Name, tag, description, motto** - the description and motto are also used as lore for AI dialogue.
- **Primary/secondary colors, flag pattern, emblem** (refine later in the Flag Designer).
- **Uniform** - the ped model set for hired members and reinforcements; optionally swap recruits into it.
- **Faction personality** (Ruthless, Honorable, Pragmatic, Flamboyant, Secretive, Zealous) and **behavior**
  (Balanced, Aggressive, Defensive, Expansionist, Covert).
- **Street gang side activities**: none, *Product*, *Arms trade*, or both (abstract resource systems).
- **Difficulty**, **starting progression** (Humble / Established / Powerful), **initial members** (0-12).
- **Headquarters** at your current position (or set it later).

The faction is saved immediately to a slot named after it.

## How the three types differ

| | Street Gang | Mafia | Organization / Militia |
|---|---|---|---|
| Ranks | Leader > Deputy > Boss > Senior Member > Member > Recruit | Don > Underboss > Consigliere > Capo > Soldier > Associate | Commander > Deputy Commander > Unit Commander > Officer > Member > Recruit |
| Tiers | Street Crew > Set > Gang > Syndicate > Street Empire | Crew > Family > Borgata > Syndicate > The Commission | Cell > Movement > Front > Liberation Army > National Force |
| Income | Residential turf, corners, side activities | Businesses (+25%), commercial/industrial protection, Influence | Strategic/rural sites, supplies, public support |
| Pressure | **Heat** (raids at very high heat) | **Heat**; betrayal and omertà events | **Suspicion** is central: low profile unlocks opportunities, high suspicion brings investigations and raids |
| Recruitment | Easiest; aggressive types fit | Hardest; intelligent/ambitious fit; "an offer they can't refuse" | Disciplined types fit; *appeal to the cause* uses public support |
| Events | Block parties, diss tracks, young hopefuls, drive-bys | Weddings, funerals, made men, betrayal rumors, favors, envoys | Training exercises, defectors, propaganda, supply drops, ideological splits, surveillance |
| Missions | Corner takeover, hold the block, stash run, arms pickup, supply run, payback, protection rounds, give back to the block | Protection racket, sit-down, make an example, escort the shipment, hostile acquisition, the councilman's favor | Recon patrol, supply cache, strike on outpost, hold the line, convoy escort, hearts and minds, seize the relay |
| Path to power | 5 stages, slowest (x0.7) | 4 stages through influence and the other families (x1.0) | 4 stages, fastest and most direct (x1.3) |
| Dialogue voice | Street slang, the block, respect | Measured, euphemistic, honor and family | Concise, ranks, situation reports, the cause |

## Members

- **Recruit from the world:** look at a pedestrian within a few meters (or aim at them) and press **E**. The popup shows
  their profile and your odds per approach with the full factor breakdown (reputation, level, willingness, personality,
  difficulty, suspicion, affiliation with a rival crew, cash offers...). Refusals put that person on a cooldown.
  Mission-critical, scripted, persistent and other mods' NPCs are never recruitable, and recruitment pauses during
  story missions.
- **Hire through your network** on the Recruitment page: a uniformed recruit arrives at your position.
- **Roster capacity** grows with level and the HQ Barracks (capped by *MaxRosterSize*).
- **Deployment:** only members with an active field order exist physically (up to *MaxDeployedMembers*); everyone
  else is simulated off-map. Followers stream in near you; members holding a position appear when you're within
  ~150 m and vanish beyond ~260 m when out of sight.
- **Loyalty and morale** drift every faction hour with pay, rank, personality, wins and losses, deputies, wars and
  events. Unpaid members lose both; very disloyal members can desert (and talk).
- **Death:** with *PermanentDeath* on, a fallen member usually dies (Infirmary upgrades improve survival); otherwise
  they are wounded and recover.

### Orders

Members follow orders from the Members page, the member's interaction popup (**E**), quick orders (**F8**) or a
confirmed conversation suggestion:

Follow - Defend me - Stay - Guard - Defend location - Move to waypoint - Get in / out of the vehicle -
Attack my target (aim at someone first) - Return to HQ - Stand down.

The first 7 followers join GTA's native player group (they follow, fight and ride with you); additional followers
use formation following and board your vehicle if there's room. Formations: Default, Loose, Circle, Line abreast,
Single file, Surround. Stuck members re-path, then reposition when off-screen.

## Ranks and squads

Ranks are fully editable: create, rename, delete (members move down), reorder, salary, and permissions (Recruit,
Command squads, Lead squad, Promote, Negotiate, Manage resources, Armory, Leadership). Your top rank can only be
renamed. Squads (units for the militia) have a leader (needs *Lead squad*), role, formation, assigned vehicle,
a garrison territory, an HQ-guard flag and squad-wide orders.

## Territory

San Andreas is divided into 37 territories built on GTA's own map zones (e.g. `DAVIS`, `RANCHO`, `PBOX`). Each has
influence per faction; the faction above 50% with the largest share owns it and collects its income. Gain influence by:
completing missions there, killing rival members inside it, being present with members, garrisoning a squad,
your HQ, and fortifications. Rivals maintain their home turf, expand, and attack yours - an attack shows up as an
**urgent mission**; ignore it and your garrison fights it out abstractly. Strategic sites (port, airport, Pillbox Hill,
Mission Row, Rockford Hills, power station, Fort Zancudo, Bolingbroke, N.O.O.S.E.) start under state control.

## Rival factions and wars

Ballas, The Families, Los Santos Vagos, Marabunta Grande, Varrios Los Aztecas, The Lost MC, Kkangpae, Wei Cheng Triads,
Armenian Mob, Madrazo Cartel, O'Neil Brothers and Merryweather Security. Relations: Neutral, Rivalry, War, Ceasefire,
Peace, Alliance. Hostility grows when you take their turf or kill their people; rivals escalate on their own. At war,
GTA's ambient gang NPCs of that faction (where the game has a matching group) turn hostile, and patrols spawn when you
enter their territory. Diplomacy: declare rivalry/war, propose ceasefire/peace/alliance (shown with odds), pay
tribute, covert operations (Intel). A rival with no strength and no territory is **eliminated**.

Settings > World & Factions lets you remove any rival (or all of them) from the simulation and restore the defaults.
Removing a faction affects this mod's world. Optionally the mod also *suppresses GTA's ambient spawns* of that gang's
dedicated models for the session - a runtime, fully reversible switch; no game files are touched.

## Missions

The board offers up to 6 operations, refreshed every few faction hours, plus urgent defense missions. Each mission has a
location, live objectives (top-center HUD), success and failure conditions and rewards (cash, XP, reputation,
resources, influence, territory control, war score, state stability). Failing costs reputation and morale. Missions are
blocked during story missions and abort if one starts.

## Headquarters

Set or move the HQ to your exact position. It provides a map blip, a ground marker and floating banner, a physical
flag pole (an in-game flag model you choose), guard posts (static, patrol or alert), a territory foothold, and six
upgrades: Armory, Barracks, Garage, Comms, Fortifications, Infirmary.

## Vehicles

Register the vehicle you're in to the faction garage (capacity grows with level and Garage upgrades). Spawn it on the
nearest road, have a driver bring it to you, paint it in faction colors with a faction plate, assign squads and
drivers, return it to the garage. Destroyed vehicles are repaired over time.

## Resources

Cash, Supplies, Arms, Influence, Intel (and Product for gangs). The Resources page shows a live hourly ledger
(income by source, salaries, vehicles, HQ upkeep) and lets you trade and buy/upgrade front businesses.

## The state campaign and government

The **Government** tab shows your campaign: each stage lists requirements with live progress. State stability falls
as you hold strategic sites, win campaign operations, grow public support and (mafia) accumulate influence; it slowly
recovers otherwise. When the last stage is complete, the **final campaign mission** unlocks (City Hall for the
militia, Pillbox Hill for the mafia, Legion Square for the gang): survive waves of state forces and hold the area.
A backup save (`backup_pre_takeover`) is made right before it starts.

After victory you **form your government**: state name, government name, your title, form of government, and whether
to keep your faction flag. Your members become state forces with government ranks (the old ranks are archived) and you
can then appoint ministers (Defense, Interior, Treasury, Intelligence, Justice, Information, Public Works, Foreign
Relations), set taxes and up to four policies, and manage stability, legitimacy, support, readiness and insurgency.
Remaining factions keep resisting. "Restore pre-takeover backup" brings back the faction era.

## Saving

Save slots live in `scripts/OrganizationEmpire/saves`. Autosave runs every 10 minutes by default (outside missions)
and when scripts unload. Each save keeps a `.bak` of the previous version, which is used automatically if a file is
ever damaged. Missions in progress are not saved.
