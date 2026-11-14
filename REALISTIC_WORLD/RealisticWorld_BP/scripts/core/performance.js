// Mobile performance manager: profiles, measured script time per tick, and
// adaptive throttling that stretches update intervals when the add-on spends
// too long per tick. It measures script time only; Script API has no reliable
// FPS reading, so no FPS number is ever shown.

import { ema, round } from "../utils/math.js";

/**
 * Every field here is read by the engine or the region tracker. New fields are
 * added only together with the system that uses them.
 * @typedef {{
 *   tickBudgetMs: number,     // script time allowed per game tick
 *   intervalScale: number,    // multiplier for every scalable system interval
 *   maxQueuedTasks: number,   // deferred work queue cap (prevents task pile-up)
 *   nearRadius: number,       // chunks around a player simulated in full detail
 *   mediumRadius: number,     // chunks simulated at reduced detail
 *   farRadius: number,        // chunks kept as stored state with cheap updates
 *   maxCachedRegions: number, // region records kept in memory
 *   sectorWritesPerSave: number, // region sectors written per 10 s save cycle
 *   waterBlocksPerUpdate: number, // water/ice/puddle blocks changed per water update (0.5 s)
 * }} Profile
 */

/** @type {Readonly<Record<"LOW" | "MEDIUM" | "ULTRA", Readonly<Profile>>>} */
export const PROFILES = Object.freeze({
  LOW: Object.freeze({
    tickBudgetMs: 2, intervalScale: 2, maxQueuedTasks: 64,
    nearRadius: 1, mediumRadius: 2, farRadius: 4, maxCachedRegions: 256, sectorWritesPerSave: 6,
    waterBlocksPerUpdate: 64,
  }),
  MEDIUM: Object.freeze({
    tickBudgetMs: 4, intervalScale: 1, maxQueuedTasks: 128,
    nearRadius: 2, mediumRadius: 4, farRadius: 6, maxCachedRegions: 512, sectorWritesPerSave: 12,
    waterBlocksPerUpdate: 160,
  }),
  ULTRA: Object.freeze({
    tickBudgetMs: 6, intervalScale: 1, maxQueuedTasks: 256,
    nearRadius: 3, mediumRadius: 5, farRadius: 8, maxCachedRegions: 1024, sectorWritesPerSave: 20,
    waterBlocksPerUpdate: 320,
  }),
});

/** Pick a starting profile from the device memory tier (MemoryTier enum 0..4). */
export function suggestProfile(memoryTier) {
  if (typeof memoryTier !== "number") return "MEDIUM";
  if (memoryTier <= 1) return "LOW"; // SuperLow, Low
  return "MEDIUM"; // ULTRA is opt-in only, never automatic on phones
}

export const MAX_THROTTLE = 4;

export class PerformanceManager {
  constructor(profileName = "MEDIUM") {
    this.setProfile(profileName);
    this.adaptive = true;
    /** current interval multiplier from adaptive throttling (1 = none) */
    this.throttle = 1;
    this.avgTickMs = 0;
    this.peakTickMs = 0;
    this.overBudgetTicks = 0;
    this.ticksMeasured = 0;
    this._windowTicks = 0;
    this._windowOver = 0;
    this._windowSumMs = 0;
  }

  /** @param {string} name */
  setProfile(name) {
    const key = /** @type {keyof typeof PROFILES} */ (name in PROFILES ? name : "MEDIUM");
    this.profileName = key;
    this.profile = PROFILES[key];
  }

  setAdaptive(on) {
    this.adaptive = !!on;
    if (!this.adaptive) this.throttle = 1;
  }

  /** Effective interval for a system that allows scaling. */
  scaledInterval(baseTicks) {
    return Math.max(1, Math.round(baseTicks * this.profile.intervalScale * this.throttle));
  }

  /**
   * Record the script time spent in one game tick.
   * Every 40 ticks the throttle is re-evaluated:
   *  - more than 25% of ticks over budget -> throttle x2 (up to MAX_THROTTLE)
   *  - average under 40% of budget and no overruns -> throttle /2
   */
  recordTick(ms) {
    this.ticksMeasured++;
    this.avgTickMs = ema(this.ticksMeasured === 1 ? undefined : this.avgTickMs, ms, 0.05);
    if (ms > this.peakTickMs) this.peakTickMs = ms;
    const over = ms > this.profile.tickBudgetMs;
    if (over) this.overBudgetTicks++;

    this._windowTicks++;
    this._windowSumMs += ms;
    if (over) this._windowOver++;
    if (this._windowTicks >= 40) {
      if (this.adaptive) {
        const overRatio = this._windowOver / this._windowTicks;
        const avg = this._windowSumMs / this._windowTicks;
        if (overRatio > 0.25 && this.throttle < MAX_THROTTLE) this.throttle *= 2;
        else if (this._windowOver === 0 && avg < this.profile.tickBudgetMs * 0.4 && this.throttle > 1) this.throttle /= 2;
      }
      this._windowTicks = 0;
      this._windowOver = 0;
      this._windowSumMs = 0;
    }
  }

  resetPeak() {
    this.peakTickMs = 0;
  }

  snapshot() {
    return {
      profile: this.profileName,
      budgetMs: this.profile.tickBudgetMs,
      adaptive: this.adaptive,
      throttle: this.throttle,
      avgTickMs: round(this.avgTickMs, 3),
      peakTickMs: round(this.peakTickMs, 3),
      overBudgetTicks: this.overBudgetTicks,
      ticksMeasured: this.ticksMeasured,
    };
  }
}
