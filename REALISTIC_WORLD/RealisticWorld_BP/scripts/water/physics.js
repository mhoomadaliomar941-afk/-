// Water physics formulas (pure, SI units: metres, seconds, m³; 1 block = 1 m³).
//
//  - Hydrostatic pressure      P = ρ g h
//  - Leak through an opening   Q = Cd · A · sqrt(2 g h)          (Torricelli)
//    h is measured from the water surface to the middle of the wetted part
//    of the opening; a partly covered opening only uses its wetted area.
//  - Infiltration into the ground and evaporation are rates in metres of
//    water per second, scaled for gameplay (real soils are far slower).

import { clamp } from "../utils/math.js";

export const G = 9.81;
export const RHO = 1000;
/** Discharge coefficient of a sharp-edged opening. */
export const CD = 0.6;
/** Area in m² of each kind of opening. */
export const OPENING_AREA = Object.freeze({ hole: 1, floor: 1, valve: 0.25, pipe: 0.25, drain: 0.25 });

/** Hydrostatic pressure in kPa at depth h metres. */
export function pressureKPa(h) {
  return (RHO * G * Math.max(0, h)) / 1000;
}

/**
 * Flow out of a side opening whose bottom edge is at height `bottom` (block y),
 * one block tall, with the water surface at `level`.
 * @returns {number} m³/s
 */
export function sideFlow(level, bottom, area = 1) {
  const wetted = clamp(level - bottom, 0, 1);
  if (wetted <= 0) return 0;
  const head = level - (bottom + wetted / 2);
  return CD * area * wetted * Math.sqrt(2 * G * Math.max(0, head));
}

/** Flow out of an opening in the floor: the whole water column above presses. */
export function floorFlow(level, floorTop, area = 1) {
  const head = level - floorTop;
  return head > 0 ? CD * area * Math.sqrt(2 * G * head) : 0;
}

/**
 * Flow through a pipe between two water surfaces (or a surface and an open end).
 * Positive: from side A to side B. `endA`/`endB` are the pipe end heights; a
 * side whose water is below its pipe end cannot push water into the pipe.
 */
export function pipeFlow(levelA, endA, levelB, endB, area = OPENING_AREA.pipe) {
  const pushA = Math.max(levelA, endA + 0.5);
  const pushB = Math.max(levelB, endB + 0.5);
  const dh = pushA - pushB;
  if (dh > 0 && levelA <= endA) return 0;
  if (dh < 0 && levelB <= endB) return 0;
  return Math.sign(dh) * CD * area * Math.sqrt(2 * G * Math.abs(dh));
}

/**
 * Level of water of volume V in a container described by the area of each
 * layer: layers[i] = number of 1x1 cells at block y = baseY + i.
 * Water fills from the bottom; returns the surface height (block units).
 */
export function levelFromVolume(layers, baseY, volume) {
  let v = Math.max(0, volume);
  for (let i = 0; i < layers.length; i++) {
    const a = layers[i];
    if (a <= 0) continue;
    if (v <= a) return baseY + i + v / a;
    v -= a;
  }
  return baseY + layers.length; // full (extra volume is handled by the caller)
}

/** Volume held below a surface height. */
export function volumeBelow(layers, baseY, level) {
  let v = 0;
  for (let i = 0; i < layers.length; i++) {
    const y = baseY + i;
    if (level <= y) break;
    v += layers[i] * Math.min(1, level - y);
  }
  return v;
}

export function capacity(layers) {
  return layers.reduce((a, b) => a + b, 0);
}

/**
 * How fast water soaks into the ground, m/s, by the block under the water.
 * Gameplay-scaled: sand drains a 1-block puddle in ~2 min, dirt in ~8 min,
 * clay and stone barely at all.
 */
const infiltrationCache = new Map();
export function infiltrationRate(groundBlock) {
  let r = infiltrationCache.get(groundBlock);
  if (r === undefined) {
    r = infiltrationOf(groundBlock);
    infiltrationCache.set(groundBlock, r);
  }
  return r;
}

function infiltrationOf(groundBlock) {
  const id = groundBlock.startsWith("minecraft:") ? groundBlock.slice(10) : groundBlock;
  if (/sand|gravel/.test(id)) return 0.008;
  if (/grass|dirt|podzol|mycelium|farmland|moss|mud_brick/.test(id)) return 0.002;
  if (id === "mud" || /clay|terracotta/.test(id)) return 0.0003;
  if (/leaves|snow/.test(id)) return 0.004;
  return 0; // stone, planks, concrete, glass, metal...
}

/**
 * Evaporation from an open water surface, m/s. Warm, dry, windy -> faster.
 * Gameplay-scaled: a 10 cm puddle at 25°C in dry air lasts ~15 minutes.
 */
export function evaporationRate(tempC, humidity, wind) {
  if (tempC <= 0) return 0;
  return 1e-4 * (tempC / 20) * clamp(1.1 - humidity, 0.05, 1.1) * (1 + wind / 10);
}

/** Rain falling into an open surface, m/s, from intensity 0..1. */
export function rainRate(intensity) {
  return 4e-4 * intensity;
}

/** Freezing state with hysteresis: freezes below -1°C, thaws above +1°C. */
export function nextFrozen(frozen, tempC) {
  if (tempC <= -1) return true;
  if (tempC >= 1) return false;
  return frozen;
}
