// Realistic World - entry point.
//
// Startup order (Script API 2.x):
//  1. system.beforeEvents.startup: register custom commands (world not usable yet)
//  2. world.afterEvents.worldLoad: build the runtime and start the engine loop
//
// Ways to open the control center:
//  - use the "Realistic World Control Center" item (crafted, or from creative)
//  - chat command /rw:menu        (no cheats needed)
//  - /scriptevent rw:menu         (fallback, needs cheats)

import {
  CommandPermissionLevel,
  CustomCommandStatus,
  system,
  world,
} from "@minecraft/server";
import { CONTROL_ITEM_ID } from "./core/constants.js";
import { createBedrockHost, playerPrefs } from "./core/host.js";
import { createRuntime } from "./core/runtime.js";
import { ControlCenter } from "./ui/controlCenter.js";

/** @type {ReturnType<typeof createRuntime> | undefined} */
let runtime;
/** @type {ControlCenter | undefined} */
let controlCenter;

/** @param {import("@minecraft/server").Entity | undefined} entity */
function asPlayer(entity) {
  return entity && entity.typeId === "minecraft:player"
    ? /** @type {import("@minecraft/server").Player} */ (entity)
    : undefined;
}

function notReady() {
  return { status: CustomCommandStatus.Failure, message: "Realistic World is still starting, try again in a moment." };
}

system.beforeEvents.startup.subscribe((event) => {
  const registry = event.customCommandRegistry;

  registry.registerCommand(
    {
      name: "rw:menu",
      description: "Open the Realistic World control center",
      permissionLevel: CommandPermissionLevel.Any,
      cheatsRequired: false,
    },
    (origin) => {
      const player = asPlayer(origin.sourceEntity);
      if (!player) return { status: CustomCommandStatus.Failure, message: "Only a player can open the control center." };
      if (!controlCenter) return notReady();
      const cc = controlCenter;
      // Command callbacks run in restricted mode; open the form on the next tick.
      system.run(() => cc.open(player));
      return { status: CustomCommandStatus.Success };
    },
  );

  registry.registerCommand(
    {
      name: "rw:status",
      description: "Show Realistic World engine status",
      permissionLevel: CommandPermissionLevel.Any,
      cheatsRequired: false,
    },
    () => {
      if (!runtime) return notReady();
      const d = runtime.dashboard();
      const p = d.perf;
      return {
        status: CustomCommandStatus.Success,
        message:
          `Realistic World ${runtime.version.addon} | ${d.paused ? "PAUSED" : "RUNNING"} | tick ${d.tick}\n` +
          `profile ${p.profile} | avg ${p.avgTickMs} ms / ${p.budgetMs} ms | throttle x${p.throttle}\n` +
          `regions NEAR ${d.regions.near} MEDIUM ${d.regions.medium} FAR ${d.regions.far} | errors ${d.errors}`,
      };
    },
  );

  registry.registerCommand(
    {
      name: "rw:selftest",
      description: "Run the Realistic World self-test",
      permissionLevel: CommandPermissionLevel.GameDirectors,
      cheatsRequired: false,
    },
    (origin) => {
      if (!runtime) return notReady();
      const rt = runtime;
      const player = asPlayer(origin.sourceEntity);
      // The test writes world data, which is not allowed inside a command callback.
      system.run(() => {
        const results = rt.selfTest();
        const passed = results.filter((r) => r.ok).length;
        const lines = [`§b[Realistic World]§r self-test: ${passed}/${results.length} passed`];
        for (const r of results) lines.push(`${r.ok ? "§aPASS" : "§cFAIL"}§r ${r.name} - ${r.detail}`);
        const text = lines.join("\n");
        if (player) player.sendMessage(text);
        else world.sendMessage(text);
      });
      return { status: CustomCommandStatus.Success, message: "Self-test started." };
    },
  );
});

world.afterEvents.worldLoad.subscribe(() => {
  try {
    runtime = createRuntime(createBedrockHost());
  } catch (e) {
    console.error(`[RW] Realistic World failed to start: ${/** @type {any} */ (e)?.message ?? e}`);
    return;
  }
  const rt = runtime;
  controlCenter = new ControlCenter(rt, playerPrefs);
  const cc = controlCenter;

  system.runInterval(() => {
    try {
      rt.tick();
    } catch (e) {
      // The scheduler isolates system errors; this only catches engine bugs.
      rt.logger.error("core", "engine tick failed", e);
    }
  }, 1);

  world.afterEvents.itemUse.subscribe((event) => {
    if (event.itemStack?.typeId === CONTROL_ITEM_ID) cc.open(event.source);
  });

  system.afterEvents.scriptEventReceive.subscribe((event) => {
    if (event.id !== "rw:menu") return;
    const player = asPlayer(event.sourceEntity);
    if (player) cc.open(player);
  });

  // Follow /weather commands and other weather changes not made by the engine.
  world.afterEvents.weatherChange.subscribe((event) => {
    if (!event.dimension.endsWith("overworld")) return;
    try {
      rt.weather.onVanillaChange(event.newWeather, rt.scheduler.ticks);
    } catch (e) {
      rt.logger.error("weather", "weather change handling failed", e);
    }
  });

  // Save when a player leaves (single player: before the world closes).
  world.afterEvents.playerLeave.subscribe(() => {
    try {
      rt.saveNow(Infinity);
    } catch (e) {
      rt.logger.error("core", "save on player leave failed", e);
    }
  });
});

// Final save before the world closes. Periodic saves every 10 s are the main
// mechanism; this only shortens the window of lost changes.
system.beforeEvents.shutdown.subscribe(() => {
  try {
    runtime?.saveNow(Infinity);
  } catch {
    // world may already be unavailable; periodic saves cover this case
  }
});
