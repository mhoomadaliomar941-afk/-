// Global weather model for the overworld (pure functions, no game imports).
//
// State variables, all evolving with time steps of at most 250 ticks:
//   P  air pressure, 0..1 (0.5 normal). Mean-reverting random walk; low
//      pressure brings rain, fast changes bring wind.
//   A  temperature anomaly in °C added to every region (heat waves, cold snaps).
//   H  air humidity, 0..1. Rises by evaporation (faster when warm), falls when
//      it rains.
//   I  precipitation intensity, 0..1. Follows a target computed from H and P.
//      Rain starts above RAIN_ON and stops below RAIN_OFF (hysteresis).
//   storm, wind (m/s), windDir (degrees, direction the wind blows FROM),
//   fogG (fog tendency 0..1), dryDays (days without rain), rainEma (fraction
//   of time raining over ~3 days; used for cheap catch-up of absent regions).
// Rates are per in-game hour (1000 ticks). One in-game day is 24000 ticks.

import { clamp } from "../utils/math.js";
import { nextGaussian } from "../utils/random.js";

export const TICKS_PER_HOUR = 1000;
export const TICKS_PER_DAY = 24000;
export const MAX_STEP_TICKS = 250;
export const RAIN_ON = 0.2;
export const RAIN_OFF = 0.05;
/**
 * Intensity class limits, set from the measured distribution while raining
 * (scripts: tests/weatherModel.test.mjs checks the resulting shares):
 * about half light, a third moderate, a fifth heavy.
 */
export const MODERATE_INTENSITY = 0.22;
export const HEAVY_INTENSITY = 0.34;
/** Thunderstorms need heavy-ish rain plus low pressure (~10% of rain time). */
export const STORM_INTENSITY = 0.28;
/** Days without rain after which the weather counts as drought. */
export const DROUGHT_DAYS = 4;
/** Humidity at which rain becomes likely, per rain-frequency setting. */
export const RAIN_FREQUENCY = Object.freeze({ LOW: 0.72, NORMAL: 0.66, HIGH: 0.6 });

/**
 * Conditions an operator can force, with what they hold.
 * @type {Readonly<Record<string, { I: number, storm?: boolean, fog?: boolean, anomaly?: number, drought?: boolean }>>}
 */
export const FORCEABLE = Object.freeze({
  CLEAR: { I: 0 },
  LIGHT_RAIN: { I: 0.2 },
  MODERATE_RAIN: { I: 0.28 },
  HEAVY_RAIN: { I: 0.45 },
  THUNDERSTORM: { I: 0.5, storm: true },
  FOG: { I: 0, fog: true },
  HEATWAVE: { I: 0, anomaly: 8 },
  DROUGHT: { I: 0, anomaly: 3, drought: true },
});

/**
 * @typedef {{
 *   v: number, rng: { s: number }, tick: number,
 *   P: number, A: number, H: number, I: number,
 *   raining: boolean, storm: boolean,
 *   wind: number, windDir: number, fogG: number,
 *   dryDays: number, rainEma: number,
 *   force: { type: keyof typeof FORCEABLE, until: number } | null,
 * }} WeatherState
 */

/** @param {{ s: number }} rng @returns {WeatherState} */
export function createState(rng) {
  return {
    v: 1, rng: { s: rng.s >>> 0 }, tick: 0,
    P: 0.5, A: 0, H: 0.55, I: 0,
    raining: false, storm: false,
    wind: 4, windDir: 270, fogG: 0,
    dryDays: 0, rainEma: 0.25,
    force: null,
  };
}

/** Validate a loaded state; returns null if it cannot be used. @returns {WeatherState | null} */
export function sanitizeState(raw) {
  if (!raw || typeof raw !== "object" || raw.v !== 1) return null;
  const nums = ["tick", "P", "A", "H", "I", "wind", "windDir", "fogG", "dryDays", "rainEma"];
  for (const k of nums) if (typeof raw[k] !== "number" || !Number.isFinite(raw[k])) return null;
  if (!raw.rng || !Number.isInteger(raw.rng.s)) return null;
  const s = /** @type {WeatherState} */ ({ ...raw, rng: { s: raw.rng.s >>> 0 } });
  s.P = clamp(s.P, 0, 1);
  s.H = clamp(s.H, 0.05, 1);
  s.I = clamp(s.I, 0, 1);
  s.A = clamp(s.A, -15, 15);
  s.raining = !!s.raining;
  s.storm = !!s.storm;
  if (s.force && !(s.force.type in FORCEABLE && Number.isFinite(s.force.until))) s.force = null;
  return s;
}

const relax = (cur, target, h, tau) => cur + (target - cur) * (1 - Math.exp(-h / tau));

/**
 * Advance the model.
 * @param {WeatherState} st mutated in place
 * @param {number} dtTicks
 * @param {{ rainFrequency?: keyof typeof RAIN_FREQUENCY }} [opts]
 */
export function step(st, dtTicks, opts = {}) {
  const h0 = RAIN_FREQUENCY[opts.rainFrequency ?? "NORMAL"] ?? RAIN_FREQUENCY.NORMAL;
  let remaining = Math.max(0, dtTicks);
  while (remaining > 0) {
    const dt = Math.min(MAX_STEP_TICKS, remaining);
    remaining -= dt;
    st.tick += dt;
    const h = dt / TICKS_PER_HOUR;
    const sq = Math.sqrt(h);
    const rng = st.rng;

    if (st.force && st.tick >= st.force.until) st.force = null;
    const force = st.force ? FORCEABLE[st.force.type] : null;

    st.P = clamp(st.P + 0.03 * (0.5 - st.P) * h + 0.05 * sq * nextGaussian(rng), 0, 1);
    const aTarget = force?.anomaly ?? 0;
    st.A = clamp(st.A + (force?.anomaly !== undefined ? 0.5 : 0.02) * (aTarget - st.A) * h + 0.55 * sq * nextGaussian(rng), -15, 15);

    const evap = 0.012 * (1 + Math.max(0, st.A) / 12);
    st.H = clamp(st.H + (evap * (1 - st.H) - 0.09 * st.I * st.H) * h + 0.015 * sq * nextGaussian(rng), 0.05, 1);

    let iTarget = clamp((st.H - h0) / 0.1 + (0.42 - st.P) * 3, 0, 1);
    if (!st.raining && iTarget < RAIN_ON) iTarget = 0;
    if (force) iTarget = force.I;
    st.I = clamp(relax(st.I, iTarget, h, 1), 0, 1);
    if (force && force.I > 0 && st.I < RAIN_ON) st.I = RAIN_ON; // forced rain starts at once

    if (!st.raining && st.I >= RAIN_ON) st.raining = true;
    else if (st.raining && st.I < RAIN_OFF) st.raining = false;
    st.storm = force ? !!force.storm : st.raining && st.I > STORM_INTENSITY && st.P < 0.32 && st.A > -2;

    const windTarget = 2.5 + 22 * Math.abs(st.P - 0.5) + 7 * st.I + (st.storm ? 7 : 0) - (force?.fog ? 3 : 0);
    st.wind = clamp(relax(st.wind, windTarget, h, 0.5) + 0.6 * sq * nextGaussian(rng), 0, 30);
    st.windDir = (((st.windDir + 25 * sq * nextGaussian(rng)) % 360) + 360) % 360;

    const fogTarget = force?.fog
      ? 1
      : st.raining
        ? 0
        : clamp((st.H - (h0 - 0.04)) / 0.06, 0, 1) * clamp((5 - st.wind) / 3, 0, 1);
    st.fogG = clamp(relax(st.fogG, fogTarget, h, force?.fog ? 0.2 : 1), 0, 1);

    st.dryDays = st.raining ? Math.max(0, st.dryDays - st.I * h * 2) : st.dryDays + h / 24;
    if (force?.drought) st.dryDays = Math.max(st.dryDays, DROUGHT_DAYS);
    st.rainEma = relax(st.rainEma, st.raining ? 1 : 0, h, 72);
  }
  return st;
}

/** @param {number} i precipitation intensity */
export function rainClass(i) {
  return i < MODERATE_INTENSITY ? "LIGHT_RAIN" : i < HEAVY_INTENSITY ? "MODERATE_RAIN" : "HEAVY_RAIN";
}

/** Overall condition for display. Snow/blizzard is decided per region by temperature. */
export function classify(st) {
  if (st.storm) return "THUNDERSTORM";
  if (st.raining) return rainClass(st.I);
  if (st.fogG > 0.6) return "FOG";
  if (st.A >= 4.5) return "HEATWAVE";
  if (st.dryDays >= DROUGHT_DAYS) return "DROUGHT";
  if (st.wind >= 12) return "WINDY";
  if (st.H >= 0.75) return "CLOUDY";
  return "CLEAR";
}

/** What the vanilla game should show. @returns {"Clear" | "Rain" | "Thunder"} */
export function vanillaWeather(st) {
  return st.storm ? "Thunder" : st.raining ? "Rain" : "Clear";
}

/** Force a condition for a number of ticks (operator command or /weather). */
export function forceWeather(st, type, durationTicks) {
  if (!(type in FORCEABLE)) throw new Error(`unknown weather type ${type}`);
  st.force = { type, until: st.tick + Math.max(1, Math.round(durationTicks)) };
  const f = FORCEABLE[type];
  // Apply at once, so the vanilla weather can be updated in the same tick.
  if (f.I === 0) {
    st.I = 0;
    st.raining = false;
    st.storm = false;
  } else {
    st.I = Math.max(st.I, RAIN_ON);
    st.raining = true;
    st.storm = !!f.storm;
  }
  if (f.fog) st.fogG = Math.max(st.fogG, 0.9);
}

export function releaseForce(st) {
  st.force = null;
}

const DIRS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
/** Compass name of the direction the wind comes from. */
export function compass(deg) {
  return DIRS[Math.round((((deg % 360) + 360) % 360) / 45) % 8];
}

/**
 * Ensemble forecast: replay the model forward from copies of the current state.
 * Member 0 keeps the real random sequence (the future as it will happen if
 * nothing intervenes); the others use perturbed sequences to show uncertainty.
 * @param {WeatherState} st
 * @param {{ hours?: number, periodHours?: number, members?: number, rainFrequency?: keyof typeof RAIN_FREQUENCY }} [opts]
 */
export function forecast(st, opts = {}) {
  const hours = opts.hours ?? 72;
  const periodHours = opts.periodHours ?? 6;
  const members = opts.members ?? 6;
  const periods = Math.ceil(hours / periodHours);
  const out = Array.from({ length: periods }, (_, i) => ({
    fromHour: i * periodHours,
    rain: 0,
    storm: 0,
    maxI: 0,
    minA: Infinity,
    maxA: -Infinity,
    maxWind: 0,
  }));
  for (let m = 0; m < members; m++) {
    const s = /** @type {WeatherState} */ (JSON.parse(JSON.stringify(st)));
    if (m > 0) s.rng.s = (s.rng.s ^ Math.imul(m, 0x9e3779b1)) >>> 0;
    for (let p = 0; p < periods; p++) {
      let rained = false;
      let stormed = false;
      for (let k = 0; k < (periodHours * TICKS_PER_HOUR) / MAX_STEP_TICKS; k++) {
        step(s, MAX_STEP_TICKS, opts);
        rained ||= s.raining;
        stormed ||= s.storm;
        const o = out[p];
        o.maxI = Math.max(o.maxI, s.I);
        o.minA = Math.min(o.minA, s.A);
        o.maxA = Math.max(o.maxA, s.A);
        o.maxWind = Math.max(o.maxWind, s.wind);
      }
      if (rained) out[p].rain++;
      if (stormed) out[p].storm++;
    }
  }
  return out.map((o) => ({
    fromHour: o.fromHour,
    toHour: o.fromHour + periodHours,
    rainChance: Math.round((o.rain / members) * 100),
    stormChance: Math.round((o.storm / members) * 100),
    maxIntensity: Math.round(o.maxI * 100) / 100,
    anomalyMin: Math.round(o.minA * 10) / 10,
    anomalyMax: Math.round(o.maxA * 10) / 10,
    maxWind: Math.round(o.maxWind * 10) / 10,
  }));
}
