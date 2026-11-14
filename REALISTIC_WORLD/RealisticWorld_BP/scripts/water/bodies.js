// Water bodies and their time step (pure).
//
// Tank: still water enclosed by blocks, found by TankScan. Its volume (m³)
//   sets one flat level (hydrostatic: connected water has one surface). Water
//   leaves through openings (Torricelli), open pipe ends and drains, gains
//   rain on its open surface, loses evaporation, and never drops below the
//   lowest open opening by itself (the leak stops at the hole).
// Pool: water standing on the terrain, made by pouring (basin.js). Its volume
//   spread over its columns sets the level; it loses water into the ground
//   (feeding soil moisture) and to evaporation and drains, gains rain, and
//   asks to be re-poured when it grows past the shape it was computed for.

import {
  OPENING_AREA, capacity, evaporationRate, floorFlow, infiltrationRate, levelFromVolume, nextFrozen, pipeFlow, rainRate,
  sideFlow, volumeBelow,
} from "./physics.js";
import { depthAt, levelOverColumns, levelOverSorted } from "./basin.js";

/** Largest physics sub-step, seconds. Keeps the level change per step small. */
export const SUBSTEP = 0.25;
/** Catch-up runs real sub-steps up to this long, then jumps to the end state. */
export const MAX_DETAILED_CATCHUP = 120;
/** Volume below which a pool is gone. */
export const MIN_POOL_VOLUME = 0.02;
/**
 * Water film (m) over an opening's edge that runs off at once. Flow over a
 * barely covered edge goes like depth^1.5, so the last centimetres would
 * otherwise take forever; real water just runs off.
 */
export const RUNOFF_FILM = 0.05;

/**
 * @typedef {import("./tankScan.js").Opening & { open: boolean }} TankOpening
 * @typedef {{ x: number, y: number, z: number, endY: number, startY: number,
 *   out: { kind: "water" | "air" | "drain", x: number, y: number, z: number } | null, link?: string }} TankPipe
 * @typedef {{
 *   id: string, type: "tank", dim: string,
 *   baseY: number, layers: number[], columns: [number, number, number, number][], openTop: number,
 *   openings: TankOpening[], valves: { x: number, y: number, z: number }[], pipes: TankPipe[],
 *   volume: number, frozen: boolean, lastSim: number,
 *   stats: { out: number, rained: number, evaporated: number, outflow: number },
 * }} Tank
 * @typedef {{
 *   id: string, type: "pool", dim: string,
 *   cols: [number, number, number, string][],   // [x, z, base, ground block]
 *   min: [number, number], volume: number, limit: number, spill: [number, number] | null,
 *   frozen: boolean, lastSim: number,
 *   stats: { absorbed: number, rained: number, evaporated: number, drained: number },
 * }} Pool
 * @typedef {{ tempC: number, humidity: number, wind: number, raining: boolean, intensity: number, soil: number }} Env
 *   soil: soil moisture 0..1 of the region (wet ground absorbs less)
 * @typedef {{ pours: Map<string, number>, lost: number, absorbed: number, regrow: boolean, overflow: number }} StepResult
 *   pours: m³ to pour on the terrain, keyed "x,z"
 */

const isSide = (o) => o.y === o.bottom;

export function tankLevel(t) {
  return levelFromVolume(t.layers, t.baseY, t.volume);
}

/** Lowest height the tank drains to through its open openings (its own floor if none). */
export function drainFloor(t) {
  let floor = Infinity;
  for (const o of t.openings) if (o.open) floor = Math.min(floor, o.bottom);
  for (const p of t.pipes) if (p.out && p.out.kind !== "water") floor = Math.min(floor, p.startY);
  return floor;
}

/**
 * Current outflow of a tank in m³/s (for display and pressure readings).
 * @param {Tank} t
 */
export function tankOutflow(t) {
  const level = tankLevel(t);
  let q = 0;
  for (const o of t.openings) {
    if (!o.open) continue;
    const area = OPENING_AREA[o.kind] ?? 1;
    q += isSide(o) ? sideFlow(level, o.bottom, area) : floorFlow(level, o.bottom, area);
  }
  for (const p of t.pipes) {
    if (p.out && p.out.kind !== "water") q += Math.max(0, pipeFlow(level, p.startY, -Infinity, p.endY));
  }
  return q;
}

/** @returns {StepResult} */
function emptyResult() {
  return { pours: new Map(), lost: 0, absorbed: 0, regrow: false, overflow: 0 };
}

function addPour(res, x, z, v) {
  if (v <= 0) return;
  const k = `${x},${z}`;
  res.pours.set(k, (res.pours.get(k) ?? 0) + v);
}

/**
 * Advance a tank by `seconds`.
 * @param {Tank} t
 * @param {number} seconds
 * @param {Env} env
 * @returns {StepResult}
 */
export function stepTank(t, seconds, env) {
  const res = emptyResult();
  t.frozen = nextFrozen(t.frozen, env.tempC);
  const cap = capacity(t.layers);
  let remaining = Math.max(0, seconds);
  let flowSum = 0;
  while (remaining > 1e-9) {
    const h = Math.min(SUBSTEP, remaining);
    remaining -= h;
    const level = tankLevel(t);
    for (const o of t.openings) {
      if (!o.open) continue;
      const area = OPENING_AREA[o.kind] ?? 1;
      const q = isSide(o) ? sideFlow(level, o.bottom, area) : floorFlow(level, o.bottom, area);
      const avail = Math.max(0, t.volume - volumeBelow(t.layers, t.baseY, o.bottom));
      const dv = level - o.bottom < RUNOFF_FILM ? avail : Math.min(q * h, avail);
      if (dv <= 0) continue;
      t.volume -= dv;
      flowSum += dv;
      // Drains and floor holes take the water down into the ground; side
      // openings let it out just outside the wall.
      if (o.kind === "drain" || !isSide(o)) res.lost += dv;
      else addPour(res, o.x + (o.dx ?? 0), o.z + (o.dz ?? 0), dv);
    }
    for (const p of t.pipes) {
      if (!p.out || p.out.kind === "water") continue; // links between tanks are handled in pairs
      const q = Math.max(0, pipeFlow(tankLevel(t), p.startY, -Infinity, p.endY));
      const avail = Math.max(0, t.volume - volumeBelow(t.layers, t.baseY, p.startY));
      const dv = Math.min(q * h, avail);
      if (dv <= 0) continue;
      t.volume -= dv;
      flowSum += dv;
      if (p.out.kind === "drain") res.lost += dv;
      else addPour(res, p.out.x, p.out.z, dv);
    }
    if (t.openTop > 0) {
      if (env.raining) {
        const add = rainRate(env.intensity) * t.openTop * h;
        t.volume += add;
        t.stats.rained += add;
      }
      if (!t.frozen) {
        const ev = Math.min(t.volume, evaporationRate(env.tempC, env.humidity, env.wind) * t.openTop * h);
        t.volume -= ev;
        t.stats.evaporated += ev;
      }
    }
    if (t.volume > cap) {
      res.overflow += t.volume - cap;
      t.volume = cap;
    }
  }
  t.stats.out += flowSum;
  t.stats.outflow = seconds > 0 ? flowSum / seconds : 0;
  return res;
}

/**
 * Exchange water between two tanks joined by a pipe for `seconds`.
 * @param {Tank} a @param {TankPipe} pa pipe as seen from a
 * @param {Tank} b @param {number} endBY height of the pipe end at b
 * @returns {number} m³ moved from a to b (negative: b to a)
 */
export function stepPipeLink(a, pa, b, endBY, seconds) {
  let moved = 0;
  let remaining = Math.max(0, seconds);
  while (remaining > 1e-9) {
    const h = Math.min(SUBSTEP, remaining);
    remaining -= h;
    const la = tankLevel(a);
    const lb = tankLevel(b);
    const q = pipeFlow(la, pa.startY, lb, endBY);
    let dv = q * h;
    if (dv > 0) {
      dv = Math.min(dv, Math.max(0, a.volume - volumeBelow(a.layers, a.baseY, pa.startY)), capacity(b.layers) - b.volume);
      // never overshoot equal levels
      dv = Math.min(dv, Math.max(0, (la - lb) / (1 / topArea(a) + 1 / topArea(b))));
    } else if (dv < 0) {
      const up = Math.min(-dv, Math.max(0, b.volume - volumeBelow(b.layers, b.baseY, endBY)), capacity(a.layers) - a.volume);
      dv = -Math.min(up, Math.max(0, (lb - la) / (1 / topArea(a) + 1 / topArea(b))));
    }
    if (dv === 0) continue;
    a.volume -= dv;
    b.volume += dv;
    moved += dv;
  }
  return moved;
}

function topArea(t) {
  const level = tankLevel(t);
  const i = Math.min(t.layers.length - 1, Math.max(0, Math.floor(level - t.baseY)));
  return Math.max(1, t.layers[i] || 1);
}

export function poolLevel(p) {
  return levelOverColumns(p.cols.map((c) => c[2]), p.volume);
}

/**
 * Advance a pool by `seconds`.
 * @param {Pool} p
 * @param {number} seconds
 * @param {Env} env
 * @returns {StepResult}
 */
export function stepPool(p, seconds, env) {
  const res = emptyResult();
  p.frozen = nextFrozen(p.frozen, env.tempC);
  const sorted = p.cols.map((c) => c[2]).sort((a, b) => a - b);
  let remaining = Math.max(0, seconds);
  const soak = Math.max(0, 1 - env.soil);
  while (remaining > 1e-9 && p.volume > 0) {
    const h = Math.min(SUBSTEP * 8, remaining); // pools change slowly: 2 s steps
    remaining -= h;
    const level = levelOverSorted(sorted, p.volume);
    let wet = 0;
    let absorb = 0;
    let drain = 0;
    for (const c of p.cols) {
      const d = depthAt(c[2], level);
      if (d <= 0) continue;
      wet++;
      if (c[3] === "rw:drain") drain += floorFlow(level, c[2], OPENING_AREA.drain) * h;
      else if (!p.frozen) absorb += Math.min(d, infiltrationRate(c[3]) * soak * h);
    }
    const evap = p.frozen ? 0 : evaporationRate(env.tempC, env.humidity, env.wind) * wet * h;
    const rain = env.raining ? rainRate(env.intensity) * p.cols.length * h : 0;
    const out = Math.min(p.volume, absorb + evap + drain);
    const scale = out > 0 ? Math.min(1, p.volume / (absorb + evap + drain)) : 0;
    p.volume = p.volume - out + rain;
    p.stats.absorbed += absorb * scale;
    p.stats.evaporated += evap * scale;
    p.stats.drained += drain * scale;
    p.stats.rained += rain;
    res.absorbed += absorb * scale;
    res.lost += drain * scale;
  }
  if (p.volume < MIN_POOL_VOLUME) {
    res.absorbed += p.volume;
    p.stats.absorbed += p.volume;
    p.volume = 0;
  }
  const level = levelOverSorted(sorted, p.volume);
  if (level > p.limit + 1e-6) res.regrow = true;
  return res;
}

/**
 * Bring a body up to date after `seconds` away from the player, cheaply:
 * detailed steps for the first MAX_DETAILED_CATCHUP seconds, then one jump to
 * the state it settles into (a leaking tank at its lowest opening; a pool
 * reduced by its average losses).
 * @param {Tank | Pool} body
 * @returns {StepResult}
 */
export function catchUp(body, seconds, env) {
  const detailed = Math.min(seconds, MAX_DETAILED_CATCHUP);
  const before = body.type === "pool" ? body.stats.absorbed + body.stats.evaporated + body.stats.drained : 0;
  const res = body.type === "tank" ? stepTank(body, detailed, env) : stepPool(body, detailed, env);
  const rest = seconds - detailed;
  if (rest <= 0) return res;
  if (body.type === "tank") {
    const floor = drainFloor(body);
    if (Number.isFinite(floor)) {
      const settled = Math.min(body.volume, volumeBelow(body.layers, body.baseY, floor));
      const out = body.volume - settled;
      if (out > 0) {
        const o = body.openings.find((x) => x.open && x.bottom === floor);
        if (o && o.kind !== "drain" && isSide(o)) addPour(res, o.x + (o.dx ?? 0), o.z + (o.dz ?? 0), out);
        else res.lost += out;
        body.volume = settled;
        body.stats.out += out;
      }
    }
  } else if (body.volume > 0) {
    // Average loss rate from the detailed part, applied over the rest.
    const lossRate = (body.stats.absorbed + body.stats.evaporated + body.stats.drained - before) / Math.max(1, detailed);
    const loss = Math.min(body.volume, lossRate * rest);
    body.volume -= loss;
    res.absorbed += loss;
    if (body.volume < MIN_POOL_VOLUME) body.volume = 0;
  }
  return res;
}
