# Architecture

```
OrganizationEmpire.sln
├─ src/OrganizationEmpire.Core      net48 + net8.0, no game references
│  ├─ Json/          dependency-free JSON parser/writer + reflection mapper
│  ├─ Config/        INI file + ModConfig (sections, comments, sanitizing)
│  ├─ Util/          logging with secret redaction, RNG, Vec3/Rgb, names
│  ├─ Model/         FactionState, Member, Rank, Squad, Territory, RivalFaction, Government...
│  ├─ Data/          world factory (territories on GTA zone codes, rivals), catalogs (ranks, uniforms, loadouts...)
│  ├─ Systems/       Factory, Rank, Squad, Member, Recruitment, Economy/Progression, Territory, War,
│  │                 Suspicion, Events, Missions (catalog/board/rewards), Takeover, Government, LivingWorld, Simulation
│  ├─ Persistence/   SaveManager (atomic writes, .bak recovery, migration hook)
│  ├─ Localization/  Loc + Translator (English-keyed, templates), ArabicText (shaping + bidi),
│  │                 GlyphFont (glyph metrics/layout), ar.txt (embedded Arabic table)
│  ├─ Integration/   ModCatalog (researched mods), ModScanner + CompatibilityAnalyzer (read-only detection,
│  │                 conflicts), IntegrationHost (isolated modules, circuit breaker), MapLocations
│  ├─ Api/           EmpireApi (public API for other scripts) + ApiHub (events, queued commands)
│  └─ AI/            providers (OpenAI-compatible, Anthropic, Gemini, Offline), prompting, parsing,
│                    intent detection, memory store, DialogueService (async + main-thread pump)
├─ src/OrganizationEmpire           net48 ScriptHookVDotNet v3 script (Core sources compiled in -> one DLL)
│  ├─ EmpireScript.cs   entry point: init, compatibility checks, guarded tick, autosave, shutdown
│  ├─ ModContext.cs     service hub, load/save/reset
│  ├─ Native/           native wrappers, keyboard text (ToUnicodeEx), DPAPI key protector
│  ├─ World/            EntityTracker (caps), RelationshipManager, MemberManager, CommandSystem,
│  │                    InteractionController, Headquarters, Vehicles, Territory, Rivals,
│  │                    LivingWorldController (street battles, crews, leaders, recruitment scenes, map markers)
│  ├─ Integration/      RuntimeModEnvironment (game folder/process view), IntegrationManager + modules
│  ├─ Missions/         MissionController, MissionRuntime base, templates
│  └─ UI/               Renderer (translation at draw time, RTL mirroring), SpriteText (Arabic glyph textures), UiInput, items/pages, dialogs, notifier, flag renderer, chat window,
│                       UiManager, Pages/*
├─ tests/OrganizationEmpire.Core.Tests   xUnit on net8.0
├─ tools/OrganizationEmpire.PackTool     generates default ini + sample flag PNGs, copies Arabic assets
├─ tools/glyphgen                        renders the Arabic/Latin glyph textures (Node + Playwright, Cairo font)
├─ tools/loc                             extracts display strings for translation
└─ assets/text                           generated glyph textures + metrics shipped as scripts/OrganizationEmpire/text
```

## Key design decisions

- **One DLL, zero dependencies.** The core is compiled into the script assembly and uses its own JSON/INI code, so the
  mod can't clash with other mods' versions of Newtonsoft.Json or similar in the shared `scripts` folder.
- **Core vs runtime split.** All rules live in the core and are unit-tested; the runtime translates them into peds,
  tasks, blips and UI. The runtime reports events back (kills, presence, deaths, wanted level).
- **Strategic tick.** Every *StrategicTickSeconds* real seconds the simulation advances one faction hour: economy,
  members, territory, wars, suspicion, takeover, government, events, vehicles, mission board.
- **Threading.** Everything touching game state runs on the script thread. AI requests run on the thread pool and their
  results are queued; `DialogueService.Pump` applies them during the tick. Natives are never called off-thread.
- **Fault isolation.** Each subsystem tick runs in its own guard; repeated failures pause only that subsystem.
  Simulation systems are guarded individually as well.
- **Entity budget.** All spawns go through `EntityTracker` with per-category and global caps; everything is cleaned up
  when scripts unload or a save is loaded.
- **Validated commands.** `CommandSystem` is the single entry point for orders (menu, hotkeys, popups, confirmed chat
  suggestions). The AI produces text only.
- **Immediate-mode UI.** Pages declare their rows every frame (`ItemList`), so they always show live data. The renderer
  uses a 1080-unit-tall virtual canvas and caches text metrics.

## Extending

### Add a mission

1. Add a `MissionDefinition` to `MissionCatalog.All` (types, template, requirements, rewards, target rule).
2. If an existing template fits (Assault, Defend, Delivery, Collect, Escort, Recruit, Recon, Meeting, Assassination,
   FinalCampaign), you're done - the board will offer it when eligible.
3. For a new template: add a `MissionTemplate` value, implement a `MissionRuntime` subclass (`Start`, `Tick`,
   `Cleanup`) and map it in `MissionController.Create`.

### Add an AI provider

Subclass `HttpChatProvider` (build request, parse response, optional adaptive retry flags), add an
`AiProviderKind` value and a case in `ProviderFactory`. Add tests with `FakeHandler` (see `AiTests.cs`).

### Add a page

Subclass `Page`, implement `Build(ItemList)` (and optionally `DrawDetail`), and register a tab in
`UiManager.BuildTabs` or link to it from another page.

### Add a rival or territory

Edit `Data/WorldFactory.cs`. Territories reference GTA zone codes (`GET_NAME_OF_ZONE`); centers are approximate and
self-correct when visited. New worlds pick up additions; existing saves keep their stored world.

## Testing

```bash
dotnet test tests/OrganizationEmpire.Core.Tests
```

Covers JSON/INI round trips, save recovery, rank/squad editing rules, recruitment odds, economy, territory influence,
wars/diplomacy, suspicion raids, events, the takeover campaign and government for all three types, mission rewards,
a 30-day simulation soak per faction type, prompts/voices, response parsing, intent detection, memory limits, the
three HTTP providers against a fake handler (request shapes, adaptive retries, refusals, errors, timeouts, key
redaction) and the asynchronous dialogue pipeline with offline fallback.
