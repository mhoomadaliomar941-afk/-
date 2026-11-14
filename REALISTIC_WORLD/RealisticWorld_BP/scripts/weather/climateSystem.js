// Regional climate system: for every active overworld region, keeps soil
// moisture current and knows its biome and surface height, so temperature,
// precipitation type and fog can be read anywhere near a player.
//
// Work is spread out:
//  - each update walks the active regions from a cursor until the tick budget
//    is used, and continues there next time
//  - NEAR regions are updated every 100 ticks, MEDIUM every 200, FAR every 400
//  - the first visit of a region looks up biome and surface height (two game
//    calls); at most a few per update
//  - a region that comes back after being away is caught up in one step
// Stored per region (record.data.c): [soil moisture in 1/1000, biome id, surface y].

import { biomeInfo, catchUpSoil, initialSoil, localFog, precipitationType, soilRates, stepSoil, temperatureAt } from "./climate.js";
import { LOD } from "../simulation/regions.js";
import { TICKS_PER_HOUR } from "./model.js";
import { round } from "../utils/math.js";

const OVERWORLD = "minecraft:overworld";
/** Ticks between updates of one region, by detail tier. */
export const TIER_INTERVAL = Object.freeze({ [LOD.NEAR]: 100, [LOD.MEDIUM]: 200, [LOD.FAR]: 400 });
/** Biome lookups per update (each is two game calls). */
const MAX_INITS_PER_UPDATE = 6;

/** Fixed climates of the other dimensions. */
const OTHER_DIMENSIONS = Object.freeze({
  "minecraft:nether": { tempC: 45, humidity: 5 },
  "minecraft:the_end": { tempC: -5, humidity: 10 },
});

export class ClimateSystem {
  /**
   * @param {{
   *   tracker: import("../simulation/regions.js").RegionTracker,
   *   store: import("../simulation/regions.js").RegionStore,
   *   weather: import("./weatherSystem.js").WeatherSystem,
   *   getBiomeId: (dim: string, x: number, y: number, z: number) => string,
   *   getSurfaceY: (dim: string, x: number, z: number) => number | undefined,
   *   getTimeOfDay: () => number,
   *   logger: import("../core/logger.js").Logger,
   * }} deps
   */
  constructor(deps) {
    this.id = "climate";
    this.name = "Regional climate";
    this.intervalTicks = 20;
    this.scalable = true;
    this.deps = deps;
    /** @type {import("../simulation/regions.js").ActiveRegion[]} */
    this.queue = [];
    this.cursor = 0;
    this.stats = { updated: 0, initialized: 0, caughtUp: 0, initFailures: 0, passes: 0 };
    deps.tracker.onResume((r, away) => this._catchUp(r, away));
  }

  /** @param {import("../simulation/regions.js").ActiveRegion} r */
  _data(r) {
    const c = r.record?.data?.c;
    return Array.isArray(c) && c.length === 3 ? /** @type {[number, string, number]} */ (c) : null;
  }

  /** Look up biome and height the first time a region is seen. @returns {boolean} */
  _init(r) {
    const x = r.cx * 16 + 8;
    const z = r.cz * 16 + 8;
    let y;
    let biome;
    try {
      y = this.deps.getSurfaceY(r.dim, x, z);
      if (y === undefined) return false;
      biome = this.deps.getBiomeId(r.dim, x, y, z);
    } catch {
      this.stats.initFailures++;
      return false; // chunk not ready; try again on a later pass
    }
    const short = biome.startsWith("minecraft:") ? biome.slice(10) : biome;
    const sm = initialSoil(biomeInfo(biome));
    r.record.data = { ...(r.record.data ?? {}), c: [Math.round(sm * 1000), short, Math.round(y)] };
    this.deps.store.markDirty(r.dim, r.cx, r.cz);
    r.lastSim = this.deps.weather.state.tick;
    this.stats.initialized++;
    return true;
  }

  /** Region came back after `away` ticks: apply the averaged change at once. */
  _catchUp(r, away) {
    if (r.dim !== OVERWORLD) return;
    const c = this._data(r);
    if (!c) return;
    const sm = catchUpSoil(c[0] / 1000, biomeInfo(c[1]), c[2], this.deps.weather.state, away / TICKS_PER_HOUR);
    this._setSoil(r, c, sm);
    r.lastSim = this.deps.weather.state.tick;
    this.stats.caughtUp++;
  }

  _setSoil(r, c, sm) {
    const q = Math.round(sm * 1000);
    if (q !== c[0]) {
      c[0] = q;
      this.deps.store.markDirty(r.dim, r.cx, r.cz);
    }
  }

  /** @param {import("../core/scheduler.js").UpdateContext} ctx */
  update(ctx) {
    const w = this.deps.weather.state;
    const now = w.tick;
    if (this.cursor >= this.queue.length) {
      this.queue = [...this.deps.tracker.active.values()].filter((r) => r.dim === OVERWORLD);
      this.cursor = 0;
      this.stats.passes++;
    }
    const time = this.deps.getTimeOfDay();
    let inits = 0;
    let ops = 0;
    while (this.cursor < this.queue.length) {
      if (ops > 0 && ctx.timeLeftMs() <= 0) break;
      const r = this.queue[this.cursor++];
      if (!r.record) continue;
      let c = this._data(r);
      if (!c) {
        if (inits >= MAX_INITS_PER_UPDATE) continue;
        inits++;
        ops++;
        if (!this._init(r)) continue;
        c = /** @type {[number, string, number]} */ (this._data(r));
      }
      if (r.lastSim === undefined) {
        r.lastSim = now; // known region without a gap: start timing from now
        continue;
      }
      const last = r.lastSim;
      if (now - last < TIER_INTERVAL[r.tier]) continue;
      ops++;
      const info = biomeInfo(c[1]);
      const temp = temperatureAt(info, c[2], time, w);
      const sm = stepSoil(c[0] / 1000, soilRates(info, temp, w), (now - last) / TICKS_PER_HOUR);
      this._setSoil(r, c, sm);
      r.lastSim = now;
      this.stats.updated++;
    }
    return ops;
  }

  /**
   * Local conditions at a position, for the UI, HUD and fog.
   * Uses the region's stored data when available, otherwise asks the game.
   * @returns {null | { biome: string, tempC: number, soil: number | null, humidity: number, precip: string, fog: string | null, fogScore: number, wind: number, frost: boolean, drought: boolean, tier: string | null }}
   */
  sample(dim, x, y, z) {
    const w = this.deps.weather.state;
    const other = OTHER_DIMENSIONS[/** @type {keyof typeof OTHER_DIMENSIONS} */ (dim)];
    if (other) {
      return { biome: dim.slice(10), tempC: other.tempC, soil: null, humidity: other.humidity, precip: "none", fog: null, fogScore: 0, wind: 0, frost: other.tempC <= 0, drought: false, tier: null };
    }
    if (dim !== OVERWORLD) return null;
    const r = this.deps.tracker.regionAt(dim, x, z);
    const c = r ? this._data(r) : null;
    let biomeId = c?.[1];
    if (!biomeId) {
      try {
        biomeId = this.deps.getBiomeId(dim, x, y, z);
      } catch {
        return null;
      }
    }
    const info = biomeInfo(biomeId);
    const tempC = temperatureAt(info, y, this.deps.getTimeOfDay(), w);
    const fog = localFog(info, this.deps.getTimeOfDay(), tempC, w);
    const soil = c ? c[0] / 1000 : null;
    return {
      biome: info.id.slice(10),
      tempC: round(tempC, 1),
      soil,
      humidity: Math.round(w.H * 100),
      precip: precipitationType(tempC, w),
      fog: fog.fog,
      fogScore: round(fog.score, 2),
      wind: round(w.wind, 1),
      frost: tempC <= 0,
      drought: soil !== null && soil < 0.15 && w.dryDays >= 4,
      tier: r?.tier ?? null,
    };
  }

  describe() {
    let withData = 0;
    let soilSum = 0;
    for (const r of this.deps.tracker.active.values()) {
      const c = this._data(r);
      if (c) {
        withData++;
        soilSum += c[0];
      }
    }
    return {
      regionsWithClimate: withData,
      avgSoilPct: withData ? Math.round(soilSum / withData / 10) : 0,
      updated: this.stats.updated,
      initialized: this.stats.initialized,
      caughtUp: this.stats.caughtUp,
    };
  }
}
