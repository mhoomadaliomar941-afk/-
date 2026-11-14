// Game events for the water system: breaking, exploding and placing blocks,
// opening/closing valves, and reading the water gauge.

import { world } from "@minecraft/server";
import { bodyLines } from "../ui/waterText.js";

export const GAUGE_ITEM_ID = "rw:water_gauge";
export const VALVE_ID = "rw:valve";
const SIX = [[0, 0, 0], [1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1], [0, 1, 0], [0, -1, 0]];

/** @param {ReturnType<typeof import("../core/runtime.js").createRuntime>} rt */
export function subscribeWaterEvents(rt) {
  const water = rt.water;
  /** @type {Map<string, import("@minecraft/server").Player>} */
  const waiting = new Map();

  const safe = (label, fn) => (ev) => {
    try {
      fn(ev);
    } catch (e) {
      rt.logger.error("water", `${label} handling failed`, e);
    }
  };

  water.scanListeners.push((m) => {
    const key = `${m.dim}|${m.x},${m.y},${m.z}`;
    const player = waiting.get(key);
    if (!player) return;
    waiting.delete(key);
    if (!player.isValid) return;
    const tank = m.tankId ? water.bodies.get(m.tankId) : undefined;
    if (tank) player.sendMessage(bodyLines(water.describeBody(tank)).join("\n"));
    else if (m.status === "natural") player.sendMessage("§7Natural water (lake, river or sea): not simulated, never drained.§r");
    else player.sendMessage(`§7Could not measure this water (${m.status}).§r`);
  });

  world.afterEvents.playerBreakBlock.subscribe(
    safe("block break", (ev) => water.onBlockRemoved(ev.dimension.id, ev.block.x, ev.block.y, ev.block.z)),
  );
  world.afterEvents.blockExplode.subscribe(
    safe("explosion", (ev) => water.onBlockRemoved(ev.dimension.id, ev.block.x, ev.block.y, ev.block.z)),
  );
  world.afterEvents.playerPlaceBlock.subscribe(
    safe("block place", (ev) => water.onBlockPlaced(ev.dimension.id, ev.block.x, ev.block.y, ev.block.z)),
  );

  world.afterEvents.playerInteractWithBlock.subscribe(
    safe("interaction", (ev) => {
      if (!ev.isFirstEvent) return;
      const b = ev.block;
      const dim = b.dimension.id;
      const holdingGauge = ev.itemStack?.typeId === GAUGE_ITEM_ID;
      if (b.typeId === VALVE_ID && !holdingGauge) {
        const open = b.permutation.getAllStates()["rw:open"] === true;
        b.setPermutation(b.permutation.withState(/** @type {any} */ ("rw:open"), /** @type {any} */ (!open)));
        ev.player.onScreenDisplay.setActionBar(open ? "Valve closed" : "Valve opened");
        water.onValve(dim, b.x, b.y, b.z);
        return;
      }
      if (!holdingGauge) return;
      // Measure: the clicked block or water right next to it.
      for (const [dx, dy, dz] of SIX) {
        const x = b.x + dx;
        const y = b.y + dy;
        const z = b.z + dz;
        const tank = water.tankAt(dim, x, y, z);
        if (tank) {
          ev.player.sendMessage(bodyLines(water.describeBody(tank)).join("\n"));
          return;
        }
      }
      for (const [dx, , dz] of SIX) {
        const pool = water.poolAt(dim, b.x + dx, b.z + dz);
        if (pool) {
          ev.player.sendMessage(bodyLines(water.describeBody(pool)).join("\n"));
          return;
        }
      }
      for (const [dx, dy, dz] of SIX) {
        const x = b.x + dx;
        const y = b.y + dy;
        const z = b.z + dz;
        if (water.d.host.cellAt(dim, x, y, z) !== "source") continue;
        if (water.requestScan(dim, x, y, z, "gauge")) {
          waiting.set(`${dim}|${x},${y},${z}`, ev.player);
          ev.player.sendMessage("§7Measuring the water...§r");
        } else {
          ev.player.sendMessage("§7This water is natural (lake, river or sea) or already being measured.§r");
        }
        return;
      }
      ev.player.sendMessage("§7No simulated water here. Use the gauge on a tank wall or next to water.§r");
    }),
  );
}
