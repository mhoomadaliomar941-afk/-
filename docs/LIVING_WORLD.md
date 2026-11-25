# The living world

> **بالعربية باختصار:** الفصائل المنافسة لم تعد مجرد نصوص. كل فصيل له زعيم ونواب بأسماء وصفات (طموح، حذر،
> وحشية، كاريزما)، وأموال وأعضاء وسلاح ومعنويات، ويقرر بنفسه كل بضع ساعات: يجنّد، يتسلّح، يتوسّع، ينتقم،
> يبحث عن حلفاء، يصنع السلام أو يختفي عن الأنظار. الحروب بين الفصائل تصبح معارك حقيقية في الشارع عندما تكون
> قريبًا: مقاتلون يظهرون ويتقاتلون، ومن يموت منهم يحسم المعركة، وإذا تدخلت فأنت تختار طرفًا. المعارك تظهر على
> الخريطة، وطواقم المنافسين تتسكع في مناطقها وتجنّد المارة أمامك، وقد تجد زعيم الفصيل مع حراسه في منطقته؛
> قتله يؤدي إلى خلافة حقيقية. تبويب **العالم** في القائمة يعرض كل ذلك وأخبار العالم.

## What every rival faction has

- A **leader** and two **lieutenants** with names, ages and traits: ambition, caution, brutality, charisma.
- **Members** (manpower), **wealth**, **arms**, **morale**, **influence** and a **temperament**
  (aggressive, expansionist, opportunist, defensive, mercantile, fanatic).
- **Relations with every other faction** (neutral, rivalry, war, ceasefire, peace, alliance) and with you.

## How it decides

Every 6–11 strategic hours (faster with higher *Rival activity*) each faction scores its options and picks the best,
with a little randomness:

| Goal | Chosen when | Effect |
|---|---|---|
| Recruit | Fewer members than its turf and wars need, money available, charismatic leader | +members, −money; a visible recruitment scene if you're there |
| Arm up | Low arms, especially at war | +arms, −money |
| Expand | A reachable territory held by a weaker or hated faction, or unclaimed ground; ambitious leader, good morale | Starts a battle (declares war if needed) |
| Retaliate | High hostility toward you | Raises hostility; at war it attacks one of your territories |
| Seek allies | At war, and another faction hates the same enemy | Alliance if the other leader accepts |
| Make peace | Losing or war-weary, cautious leader | Ceasefire if the enemy accepts; ceasefires become peace or collapse |
| Lay low | Morale or money very low | Recovers |
| Consolidate | Default | Strengthens its own turf |

Relations drift: neighbors and contested territory raise hostility; alliances and peace calm it.

## Battles

A battle for a territory lasts up to 4–8 hours. Each hour both sides lose fighters according to numbers, arms, morale
and fortification; a side reduced to a quarter of its starting force loses. A rout hands the territory over; a narrow
win only dents the defender's hold. Losers can lose a lieutenant or their leader.

**When you are near** (inside the territory or within ~400 m), the battle is fought by real spawned fighters of both
factions (models, weapons and colors of each faction). Every fighter who dies is subtracted from the battle; killing
fighters of one side counts as fighting for the other: the helped faction warms up to you, the other remembers.
Reinforcement waves arrive while a side still has strength. Every active battle is marked on the map (turf-war icon).

## Leaders in the world

At a faction's home turf its leader may appear with two bodyguards (at most once per faction every 10 minutes). The
leader has a crown blip. If the leader dies, the strongest lieutenant takes over (sometimes after a power struggle
that splits the faction), and if you did it, that faction's hostility jumps. Unhappy factions can also suffer a coup.

## People in the street

In rival turf you'll see their crew hanging out (smoking, drinking, leaning). When their faction recruits in that
territory, a member walks up to a passer-by, they talk, and the new recruit follows them away armed. Crews only attack
you if their faction is at war with you (then the war patrols take over).

## Performance and safety

- Caps: *Max street battle fighters* (default 10), *Max rival crew members* (4), plus the global entity cap.
- Nothing spawns during story missions or this mod's missions; everything is cleaned up when you leave.
- With another gang/war mod installed, coexistence mode halves battle fighters and disables crews and scenes.
- The simulation itself costs well under a millisecond per strategic hour (a simulated year runs in seconds in tests).

## Settings (Settings > World & factions)

`LivingWorld`, `LivingWorldNotifications` (None / Major / All), `PhysicalBattles`, `ShowWorldEventsOnMap`,
`AmbientFactionCrews`, `RivalLeadersInWorld`; caps under Spawning: `MaxBattlePeds`, `MaxAmbientCrewPeds`.
