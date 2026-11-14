// Regional climate (pure functions, no game imports).
//
// Temperature of a region =
//     biome base temperature (from the official vanilla climate value)
//   - elevation cooling above sea level
//   + day/night swing (bigger in dry biomes, smaller over water and under clouds)
//   + global anomaly from the weather model (heat waves, cold snaps)
//   - cooling while it rains
//
// Soil moisture (0..1) follows   dSM/dt = gain*(1-SM) - loss*SM + k*(SM0-SM)
//   gain: rain soaking in (none while frozen: it falls as snow)
//   loss: evaporation, faster when warm, dry air, and in sandy biomes
//   k*(SM0-SM): slow pull toward the biome's ground-water level
// With coefficients held constant over a step the equation has an exact
// solution, used both for normal steps and to catch up a region that was away
// (with averaged weather), so long gaps cost one calculation.

import { BIOME_CLIMATE } from "./biomeClimate.js";
import { HEAVY_INTENSITY } from "./model.js";
import { clamp } from "../utils/math.js";

export const SEA_LEVEL = 64;
/** °C lost per block above sea level (exaggerated vs. real 0.0065 so mountains matter). */
export const LAPSE_PER_BLOCK = 0.06;
/** Used for biomes not in the table (custom/added biomes): plains values. */
export const DEFAULT_CLIMATE = /** @type {const} */ ([0.8, 0.4, 0]);

// Vanilla climate temperature -> daily mean °C at sea level.
const TEMP_POINTS = [
  [-0.7, -20],
  [0, -8],
  [0.15, 0],
  [0.5, 9],
  [0.8, 15],
  [1.2, 24],
  [2.0, 32],
];

/** @param {number} t vanilla temperature */
export function vanillaToCelsius(t) {
  if (t <= TEMP_POINTS[0][0]) return TEMP_POINTS[0][1];
  for (let i = 1; i < TEMP_POINTS.length; i++) {
    const [t1, c1] = TEMP_POINTS[i];
    if (t <= t1) {
      const [t0, c0] = TEMP_POINTS[i - 1];
      return c0 + ((t - t0) / (t1 - t0)) * (c1 - c0);
    }
  }
  return TEMP_POINTS[TEMP_POINTS.length - 1][1];
}

/**
 * @typedef {{
 *   id: string, temp: number, downfall: number, water: boolean,
 *   baseC: number, amplitude: number, drain: number, groundWater: number, known: boolean,
 * }} BiomeInfo
 */

/** @type {Map<string, BiomeInfo>} */
const infoCache = new Map();

/** @param {string} biomeId e.g. "minecraft:plains" or "plains" @returns {BiomeInfo} */
export function biomeInfo(biomeId) {
  const id = biomeId.includes(":") ? biomeId : `minecraft:${biomeId}`;
  let info = infoCache.get(id);
  if (info) return info;
  const row = BIOME_CLIMATE[id];
  const [temp, downfall, water] = row ?? DEFAULT_CLIMATE;
  const sandy = /desert|mesa|badlands|beach/.test(id) || (downfall === 0 && temp >= 1.2);
  const swampy = /swamp|mangrove/.test(id);
  // Humid tropical biomes (jungles) are hotter than their vanilla value suggests.
  const tropical = temp >= 0.9 && downfall >= 0.8;
  info = {
    id,
    temp,
    downfall,
    water: water === 1,
    baseC: vanillaToCelsius(temp) + (tropical ? 7 : 0),
    amplitude: water === 1 ? 2 : 3 + 5 * (1 - downfall),
    drain: sandy ? 2.5 : swampy || water === 1 ? 0.5 : 1,
    groundWater: clamp(0.15 + 0.6 * downfall, 0, 1),
    known: !!row,
  };
  infoCache.set(id, info);
  return info;
}

/** Daily mean temperature without the day/night swing (for catch-up). */
export function meanTemperature(info, elevY, w) {
  return info.baseC - Math.max(0, elevY - SEA_LEVEL) * LAPSE_PER_BLOCK + w.A - (w.raining ? 4 * w.I : 0);
}

/**
 * Air temperature at a time of day (0..24000, 6000 = noon; warmest ~8000).
 * @param {BiomeInfo} info
 * @param {number} elevY
 * @param {number} timeOfDay
 * @param {{ A: number, I: number, H: number, raining: boolean }} w weather state
 */
export function temperatureAt(info, elevY, timeOfDay, w) {
  const clouds = 1 - 0.5 * clamp((w.H - 0.6) / 0.4, 0, 1);
  const amp = info.amplitude * (w.raining ? 1 - 0.6 * w.I : 1) * clouds;
  const diurnal = amp * Math.cos((2 * Math.PI * (timeOfDay - 8000)) / 24000);
  return meanTemperature(info, elevY, w) + diurnal;
}

/** @returns {"none" | "rain" | "sleet" | "snow" | "blizzard"} */
export function precipitationType(tempC, w) {
  if (!w.raining) return "none";
  if (tempC <= 0) return w.I >= HEAVY_INTENSITY && w.wind >= 10 ? "blizzard" : "snow";
  if (tempC < 2) return "sleet";
  return "rain";
}

/**
 * Soil coefficients per in-game hour.
 * @param {BiomeInfo} info @param {number} tempC
 * @param {{ raining: boolean, I: number, H: number }} w
 */
export function soilRates(info, tempC, w) {
  const frozen = tempC <= 0;
  return {
    gain: w.raining && !frozen ? 0.5 * w.I : 0,
    loss: frozen ? 0 : 0.02 * info.drain * (Math.max(0, tempC) / 20) * (1.1 - w.H),
    k: 0.004,
    sm0: info.groundWater,
  };
}

/** Exact solution of the soil equation over `hours` with constant rates. */
export function stepSoil(sm, rates, hours) {
  const a = rates.gain + rates.k * rates.sm0;
  const b = rates.gain + rates.loss + rates.k;
  if (b <= 0 || hours <= 0) return sm;
  const eq = a / b;
  return clamp(eq + (sm - eq) * Math.exp(-b * hours), 0, 1);
}

/**
 * Cheap catch-up for a region that was away: average weather over the gap
 * (rain for the fraction of time it rained recently, at a typical intensity).
 */
export function catchUpSoil(sm, info, elevY, w, hours) {
  const tMean = meanTemperature(info, elevY, { ...w, raining: false });
  // Over a long gap it rained `rainEma` of the time at a typical intensity of
  // 0.25, which averages to a steady intensity of 0.25 * rainEma.
  const rates = soilRates(info, tMean, { raining: true, I: 0.25 * w.rainEma, H: w.H });
  return stepSoil(sm, rates, hours);
}

/** Initial soil moisture for a region seen for the first time. */
export function initialSoil(info) {
  return info.groundWater;
}

/**
 * Local fog score 0..1 and the fog to show.
 * @param {BiomeInfo} info
 * @param {number} timeOfDay
 * @param {number} tempC
 * @param {{ fogG: number, H: number, wind: number, raining: boolean, I: number, A: number, dryDays: number }} w
 * @returns {{ score: number, fog: string | null }}
 */
export function localFog(info, timeOfDay, tempC, w) {
  const precip = precipitationType(tempC, /** @type {any} */ (w));
  if (precip === "blizzard") return { score: 1, fog: "rw:fog_blizzard" };
  if (w.raining && w.I >= HEAVY_INTENSITY) return { score: 0.8, fog: "rw:fog_rain" };
  const dawn = timeOfDay >= 22500 || timeOfDay <= 1500;
  const calm = w.wind < 4;
  let score = w.fogG + (info.water ? 0.25 : 0) + (dawn && calm ? 0.35 : 0) + (w.H > 0.75 ? 0.15 : 0) - Math.max(0, (w.wind - 6) / 10);
  if (w.raining) score -= 0.2;
  score = clamp(score, 0, 1);
  if (score >= 0.85) return { score, fog: "rw:fog_dense" };
  if (score >= 0.5) return { score, fog: "rw:fog_mist" };
  const day = timeOfDay >= 2000 && timeOfDay <= 11000;
  if (day && tempC >= 30 && !w.raining && (w.A >= 4.5 || w.dryDays >= 4)) return { score, fog: "rw:fog_haze" };
  return { score, fog: null };
}
