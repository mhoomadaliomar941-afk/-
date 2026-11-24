# Known Limitations

This page lists, honestly, where GTA V / ScriptHookV / ScriptHookVDotNet constraints meant implementing the closest
practical alternative, and what has not been verified.

## Verification status

- The engine-independent core (simulation, economy, ranks, squads, recruitment math, territory, wars, suspicion,
  events, missions catalog and rewards, takeover, government, persistence, JSON/INI, AI providers, prompts, memory,
  async dialogue pipeline) is covered by **148 automated tests** run on .NET 8.
- The in-game layer (spawning, tasks, UI drawing, missions) compiles with zero warnings against the real
  ScriptHookVDotNet **3.6.0** API, with every native name checked against that release. It was **not** run inside
  GTA V during development (no game available in the build environment). Behavior that depends on the game's AI and
  rendering - task details, text metrics, spawn spots, mission pacing - may need tuning after real play testing.

## Arabic interface

- GTA V's fonts contain no Arabic letters and its text engine cannot join letters or lay text out right-to-left.
  The mod therefore draws Arabic from its own glyph textures (`scripts\OrganizationEmpire\text`). Text that the
  game draws itself - map blip names, the pause menu, GTA's native notifications and the on-screen keyboard - stays
  English/Latin.
- Arabic diacritics (harakat) are dropped when drawing; the text remains readable.
- Arabic glyph rendering was not verified inside a running game (no GTA V in the build environment). Shaping, bidi
  ordering and translation are unit-tested; size can be tuned with *Arabic text size* if it looks off.
- Glyph textures always draw above GTA's own UI; the mod hides glyphs under its own opaque panels, but a GTA overlay
  (e.g. the phone) can appear under Arabic menu text if both are open at once.

## Flags and visuals

- Custom image flags are displayed in the interface only. A custom image can't be applied to a 3D prop at runtime;
  the HQ flag pole uses one of the game's flag models. An optional OpenIV texture-replacement procedure is documented
  in [FLAG_ASSET_PIPELINE.md](FLAG_ASSET_PIPELINE.md).
- Uniforms are model sets. Random NPC clothing can't be recolored or given faction patches.
- Some emblem icons use game textures that may not exist in every build; the mod falls back to a monogram.
- GTA's fonts don't contain every Unicode glyph; non-Latin text (names, AI replies) may not render.

## Members and AI tasks

- GTA's native player group holds 7 followers. Additional followers use formation-follow tasks; they board your
  vehicle only if seats are free and otherwise catch up (or are repositioned off-screen when far behind).
- Members are abstract when not deployed (no physical simulation off-map). This keeps entity counts safe.
- Pathfinding is the game's; NPCs can still occasionally run into traffic or get stuck. The mod re-paths and
  repositions stuck members when off-screen.

## World and missions

- Territories are built on GTA's map zones; the overlay draws circles that approximate zone shapes. Ownership
  checks use the real zone codes.
- Missions in progress are not saved; reloading or a script reload aborts the active mission.
- Mod missions are blocked while a story mission is active, and an active mod mission fails if a story mission starts.
- Removing a rival faction removes it from the mod's simulation. The optional model suppression stops GTA from
  spawning that gang's dedicated ambient models during the session; it doesn't delete anything from the game and
  doesn't affect story missions' own NPCs.
- Vanilla police keep working normally, including after the takeover; the "state" you overthrow is the mod's
  fictional government entity.

## AI dialogue

- Quality, speed and cost depend on the provider and model. Network latency means replies take a moment (the game
  never waits for them).
- Locally stored API keys can't be guaranteed secure; see [AI_SETUP.md](AI_SETUP.md) for safer options.

## Input

- The menu supports keyboard and mouse. Gamepad navigation of the mod menu is not implemented; with the
  *NativeKeyboard* text mode, typing works with GTA's on-screen keyboard.

## Platform

- Story Mode on PC only. GTA Online is not supported (ScriptHookV disables itself online).
- Requires a ScriptHookV build that supports your game version.
