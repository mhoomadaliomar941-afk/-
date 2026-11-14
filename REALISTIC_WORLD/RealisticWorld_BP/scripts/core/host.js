// Bedrock adapter: the only core file that touches the game. It turns world,
// players and dimensions into the plain functions the runtime expects.

import { PlayerPermissionLevel, WeatherType, system, world } from "@minecraft/server";

const HUD_PROPERTY = "rw:hud";
const WEATHER_HUD_PROPERTY = "rw:hudw";
const FOG_TAG = "rw_weather";
/** Sky light at or above this counts as outdoors (for fog). */
const OUTDOOR_SKY_LIGHT = 8;

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
        weatherHudEnabled: () => p.getDynamicProperty(WEATHER_HUD_PROPERTY) === true,
        position: () => ({ dim: p.dimension.id, x: p.location.x, y: p.location.y, z: p.location.z }),
        show: (text) => p.onScreenDisplay.setActionBar(text),
      })),
    worldSeed: world.seed,
    setVanillaWeather: (type, duration) =>
      dimension("minecraft:overworld").setWeather(WeatherType[type], duration),
    getBiomeId: (dim, x, y, z) => dimension(dim).getBiome({ x, y, z }).id,
    getSurfaceY: (dim, x, z) => dimension(dim).getTopmostBlock({ x, z })?.location.y,
    getTimeOfDay: () => world.getTimeOfDay(),
    getFogPlayers: () =>
      validPlayers().map((p) => ({
        id: p.id,
        dim: p.dimension.id,
        x: p.location.x,
        y: p.location.y,
        z: p.location.z,
        outdoors: () => {
          try {
            return p.dimension.getSkyLightLevel(p.getHeadLocation()) >= OUTDOOR_SKY_LIGHT;
          } catch {
            return false;
          }
        },
        setFog: (fogId) => {
          p.fogSettings.remove(FOG_TAG);
          if (fogId) p.fogSettings.push(fogId, FOG_TAG);
        },
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
  /** @param {import("@minecraft/server").Player} p */
  getWeatherHud: (p) => p.getDynamicProperty(WEATHER_HUD_PROPERTY) === true,
  /** @param {import("@minecraft/server").Player} p @param {boolean} on */
  setWeatherHud: (p, on) => p.setDynamicProperty(WEATHER_HUD_PROPERTY, on ? true : undefined),
  /** @param {import("@minecraft/server").Player} p */
  position: (p) => ({ dim: p.dimension.id, x: p.location.x, y: p.location.y, z: p.location.z }),
};
