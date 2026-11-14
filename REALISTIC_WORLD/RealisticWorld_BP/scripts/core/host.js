// Bedrock adapter: the only core file that touches the game. It turns world,
// players and dimensions into the plain functions the runtime expects.

import { PlayerPermissionLevel, system, world } from "@minecraft/server";

const HUD_PROPERTY = "rw:hud";

/** @type {Map<string, import("@minecraft/server").Dimension>} */
const dimensionCache = new Map();

/** @param {string} id */
function dimension(id) {
  let d = dimensionCache.get(id);
  if (!d) {
    d = world.getDimension(id);
    dimensionCache.set(id, d);
  }
  return d;
}

function validPlayers() {
  return world.getAllPlayers().filter((p) => p.isValid);
}

/** @returns {import("./runtime.js").Host} */
export function createBedrockHost() {
  let memoryTier;
  try {
    memoryTier = system.serverSystemInfo.memoryTier;
  } catch {
    memoryTier = undefined;
  }
  return {
    backend: {
      get: (id) => world.getDynamicProperty(id),
      set: (id, value) => world.setDynamicProperty(id, value),
      ids: () => world.getDynamicPropertyIds(),
      totalBytes: () => world.getDynamicPropertyTotalByteCount(),
    },
    memoryTier,
    getPlayerPositions: () =>
      validPlayers().map((p) => ({ dim: p.dimension.id, x: p.location.x, z: p.location.z })),
    isChunkLoaded: (dim, x, z) => {
      try {
        return dimension(dim).isChunkLoaded({ x, y: 0, z });
      } catch {
        return false; // unknown dimension or location outside the world
      }
    },
    getHudPlayers: () =>
      validPlayers().map((p) => ({
        name: p.name,
        hudEnabled: () => p.getDynamicProperty(HUD_PROPERTY) === true,
        show: (text) => p.onScreenDisplay.setActionBar(text),
      })),
    clock: () => Date.now(),
    log: console,
  };
}

/** Per-player preferences and permission checks for the control center. */
export const playerPrefs = {
  /** @param {import("@minecraft/server").Player} p */
  isOperator: (p) => p.playerPermissionLevel === PlayerPermissionLevel.Operator,
  /** @param {import("@minecraft/server").Player} p */
  getHud: (p) => p.getDynamicProperty(HUD_PROPERTY) === true,
  /** @param {import("@minecraft/server").Player} p @param {boolean} on */
  setHud: (p, on) => p.setDynamicProperty(HUD_PROPERTY, on ? true : undefined),
};
