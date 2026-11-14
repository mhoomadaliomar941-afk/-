// Weather engine system: advances the global model, keeps the vanilla weather
// (rain / thunder visuals and mechanics) in step with it, respects /weather
// commands typed by players, and saves its state with the world.
//
// Bedrock has one weather per dimension (no local rain), so the vanilla
// weather shows the global state; regional differences (snow vs rain, fog,
// soil moisture) come from the climate system.

import { classify, compass, createState, forceWeather, forecast, releaseForce, sanitizeState, step, vanillaWeather, FORCEABLE } from "./model.js";
import { seedFrom } from "../utils/random.js";

/** How long the vanilla weather is set for; re-sent well before it runs out. */
export const VANILLA_DURATION = 6000;
export const VANILLA_REFRESH = 1200;
/** A /weather command typed by a player holds for this long (5 min). */
export const EXTERNAL_FORCE_TICKS = 6000;
const STORAGE_KEY = "weather";

const FROM_VANILLA = Object.freeze({ Clear: "CLEAR", Rain: "MODERATE_RAIN", Thunder: "THUNDERSTORM" });

export class WeatherSystem {
  /**
   * @param {{
   *   storage: import("../core/storage.js").Storage,
   *   config: import("../core/config.js").Config,
   *   logger: import("../core/logger.js").Logger,
   *   setVanillaWeather: (type: "Clear" | "Rain" | "Thunder", durationTicks: number) => void,
   *   worldSeed: string | number,
   * }} deps
   */
  constructor({ storage, config, logger, setVanillaWeather, worldSeed }) {
    this.id = "weather";
    this.name = "Weather engine";
    this.intervalTicks = 100;
    this.scalable = true; // the model integrates any time step exactly enough
    this.storage = storage;
    this.config = config;
    this.logger = logger;
    this.setVanillaWeather = setVanillaWeather;
    this.dirty = false;
    /** @type {{ type: string, tick: number } | null} */
    this.expect = null;
    this.lastSent = "";
    this.lastSentTick = -Infinity;
    this.externalChanges = 0;
    /** @type {{ tick: number, data: ReturnType<typeof forecast> } | null} */
    this.forecastCache = null;

    const saved = storage.load(STORAGE_KEY);
    const state = saved.ok ? sanitizeState(saved.value) : null;
    if (saved.ok && !state) logger.warn("weather", "saved weather state invalid; starting fresh");
    this.state = state ?? createState(seedFrom(worldSeed));
    this.restored = !!state;
  }

  get rainFrequency() {
    return this.config.get("weather.rainFrequency");
  }

  /** @param {import("../core/scheduler.js").UpdateContext} ctx */
  update(ctx) {
    const dt = ctx.deltaTicks > 0 ? ctx.deltaTicks : this.intervalTicks;
    step(this.state, dt, { rainFrequency: this.rainFrequency });
    this.dirty = true;
    this.syncVanilla(ctx.tick);
    return Math.ceil(dt / 250);
  }

  /** Send the vanilla weather if it changed or is about to run out. */
  syncVanilla(tick, force = false) {
    if (!this.config.get("weather.syncVanilla")) return;
    const want = vanillaWeather(this.state);
    if (!force && want === this.lastSent && tick - this.lastSentTick < VANILLA_REFRESH) return;
    try {
      this.expect = { type: want, tick };
      this.setVanillaWeather(want, VANILLA_DURATION);
      this.lastSent = want;
      this.lastSentTick = tick;
    } catch (e) {
      this.logger.error("weather", "could not set the vanilla weather", e);
    }
  }

  /**
   * world.afterEvents.weatherChange. Our own changes are recognised and ignored;
   * anything else (a player typing /weather) becomes a forced condition.
   * @param {string} newWeather "Clear" | "Rain" | "Thunder"
   * @param {number} tick
   */
  onVanillaChange(newWeather, tick) {
    if (this.expect && this.expect.type === newWeather && tick - this.expect.tick <= 40) return;
    if (!this.config.get("weather.syncVanilla")) return;
    const type = FROM_VANILLA[/** @type {keyof typeof FROM_VANILLA} */ (newWeather)];
    if (!type) return;
    forceWeather(this.state, type, EXTERNAL_FORCE_TICKS);
    this.externalChanges++;
    this.lastSent = newWeather;
    this.lastSentTick = tick;
    this.dirty = true;
    this.forecastCache = null;
    this.logger.info("weather", `vanilla weather changed to ${newWeather} (e.g. /weather); following it for 5 minutes`);
  }

  /** Operator: hold a condition for a number of ticks. */
  force(type, durationTicks, tick) {
    if (!(type in FORCEABLE)) return false;
    forceWeather(this.state, type, durationTicks);
    this.dirty = true;
    this.forecastCache = null;
    this.syncVanilla(tick, true);
    return true;
  }

  release(tick) {
    releaseForce(this.state);
    this.dirty = true;
    this.forecastCache = null;
    this.syncVanilla(tick, true);
  }

  /** Forecast for the next 3 in-game days, recomputed at most every 500 ticks. */
  getForecast() {
    if (!this.forecastCache || Math.abs(this.state.tick - this.forecastCache.tick) >= 500) {
      this.forecastCache = { tick: this.state.tick, data: forecast(this.state, { rainFrequency: this.rainFrequency }) };
    }
    return this.forecastCache.data;
  }

  save() {
    if (!this.dirty) return false;
    const res = this.storage.save(STORAGE_KEY, this.state);
    if (!res.ok) {
      this.logger.error("weather", `could not save weather: ${res.error}`);
      return false;
    }
    this.dirty = false;
    return true;
  }

  summary() {
    const s = this.state;
    return {
      condition: classify(s),
      raining: s.raining,
      storm: s.storm,
      intensity: Math.round(s.I * 100) / 100,
      humidity: Math.round(s.H * 100),
      pressure: Math.round(s.P * 100) / 100,
      anomaly: Math.round(s.A * 10) / 10,
      wind: Math.round(s.wind * 10) / 10,
      windFrom: compass(s.windDir),
      fog: Math.round(s.fogG * 100) / 100,
      dryDays: Math.round(s.dryDays * 10) / 10,
      forced: s.force ? { type: s.force.type, ticksLeft: Math.max(0, s.force.until - s.tick) } : null,
    };
  }

  describe() {
    const w = this.summary();
    return { condition: w.condition, forced: w.forced ? w.forced.type : "no", vanilla: this.lastSent || "-", external: this.externalChanges };
  }
}
