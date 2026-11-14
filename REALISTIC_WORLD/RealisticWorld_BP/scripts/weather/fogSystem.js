// Applies the local fog (mist, dense fog, heavy-rain fog, blizzard, heat haze)
// to each player who is outdoors in the overworld, using fog definitions from
// the resource pack (RealisticWorld_RP/fogs). Uses the stable
// player.fogSettings API with its own tag, so other fogs are left alone.

/**
 * @typedef {{ id: string, dim: string, x: number, y: number, z: number,
 *   outdoors: () => boolean, setFog: (fogId: string | null) => void }} FogPlayer
 */

/** Fog is re-applied at least this often, in case the game reset it (respawn, rejoin). */
export const FOG_REFRESH_TICKS = 600;

export class FogSystem {
  /**
   * @param {{
   *   getPlayers: () => FogPlayer[],
   *   climate: import("./climateSystem.js").ClimateSystem,
   *   logger: import("../core/logger.js").Logger,
   * }} deps
   */
  constructor({ getPlayers, climate, logger }) {
    this.id = "fog";
    this.name = "Weather fog";
    this.intervalTicks = 40;
    this.scalable = true;
    this.getPlayers = getPlayers;
    this.climate = climate;
    this.logger = logger;
    /** @type {Map<string, { fog: string | null, tick: number }>} */
    this.applied = new Map();
    this.failures = 0;
  }

  /** @param {import("../core/scheduler.js").UpdateContext} ctx */
  update(ctx) {
    let ops = 0;
    const seen = new Set();
    for (const p of this.getPlayers()) {
      seen.add(p.id);
      let want = null;
      if (p.dim === "minecraft:overworld" && p.outdoors()) want = this.climate.sample(p.dim, p.x, p.y, p.z)?.fog ?? null;
      const prev = this.applied.get(p.id);
      if (prev && prev.fog === want && ctx.tick - prev.tick < FOG_REFRESH_TICKS) continue;
      try {
        p.setFog(want);
        this.applied.set(p.id, { fog: want, tick: ctx.tick });
        ops++;
      } catch (e) {
        this.failures++;
        if (this.failures <= 3) this.logger.error("fog", `could not apply fog ${want}`, e);
      }
    }
    for (const id of this.applied.keys()) if (!seen.has(id)) this.applied.delete(id);
    return ops;
  }

  /** Remove our fog from everyone when the system is switched off. */
  stop() {
    for (const p of this.getPlayers()) {
      try {
        p.setFog(null);
      } catch {
        // player may be leaving
      }
    }
    this.applied.clear();
  }

  describe() {
    const counts = {};
    for (const a of this.applied.values()) counts[a.fog ?? "none"] = (counts[a.fog ?? "none"] ?? 0) + 1;
    return { players: this.applied.size, ...counts };
  }
}
