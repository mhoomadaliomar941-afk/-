// Where poured water ends up on the terrain ("fill and spill").
//
// Water poured at a point runs downhill to the local low point, fills that
// depression with a flat surface until it reaches the lowest rim (the spill
// point), and any extra runs on from the spill point into the next depression.
// Each column is described by `base`: the y of the first open block above the
// ground (water stands from `base` upwards). Pure functions; the terrain is read
// through heightAt(x, z), which the game adapter backs with a cached heightmap.

/**
 * @typedef {{ base: number, kind: "ground" | "water", ground: string }} Column
 *   kind "water": the column already holds natural water (lake, river, sea):
 *   poured water joins it and leaves the simulation.
 * @typedef {(x: number, z: number) => Column | null} HeightFn   null = unknown/unloaded: treated as a wall
 * @typedef {{ x: number, z: number, base: number, ground: string }} PoolColumn
 */

const N4 = [[1, 0], [-1, 0], [0, 1], [0, -1]];
/**
 * Thinnest sheet (m) water spreads into. On flat ground water would otherwise
 * spread without end; it stops spreading once a new column would get less.
 */
export const MIN_FILM = 0.1;
const key = (x, z) => `${x},${z}`;

/** Minimal binary heap keyed by base height, ties broken by insertion order. */
class MinHeap {
  constructor() {
    this.a = [];
    this.n = 0;
  }
  get size() {
    return this.a.length;
  }
  push(item, k) {
    const a = this.a;
    a.push({ item, k, o: this.n++ });
    let i = a.length - 1;
    while (i > 0) {
      const p = (i - 1) >> 1;
      if (a[p].k < a[i].k || (a[p].k === a[i].k && a[p].o < a[i].o)) break;
      [a[p], a[i]] = [a[i], a[p]];
      i = p;
    }
  }
  pop() {
    const a = this.a;
    const top = a[0];
    const last = a.pop();
    if (a.length > 0 && last) {
      a[0] = last;
      let i = 0;
      for (;;) {
        const l = 2 * i + 1;
        const r = l + 1;
        let m = i;
        const less = (x, y) => a[x].k < a[y].k || (a[x].k === a[y].k && a[x].o < a[y].o);
        if (l < a.length && less(l, m)) m = l;
        if (r < a.length && less(r, m)) m = r;
        if (m === i) break;
        [a[m], a[i]] = [a[i], a[m]];
        i = m;
      }
    }
    return top.item;
  }
}

/**
 * Follow the way down from (x, z) to a low point. Minecraft terrain has flat
 * areas, so when no neighbour is lower the flat area is searched (up to
 * `maxFlat` columns) for an edge that leads lower.
 * @param {HeightFn} heightAt
 * @returns {{ x: number, z: number, col: Column } | null}
 */
export function descend(x, z, heightAt, maxSteps = 64, maxFlat = 48) {
  let col = heightAt(x, z);
  if (!col) return null;
  for (let i = 0; i < maxSteps && col.kind !== "water"; i++) {
    let best = null;
    for (const [dx, dz] of N4) {
      const c = heightAt(x + dx, z + dz);
      if (c && (c.base < col.base || (c.kind === "water" && c.base <= col.base)) && (!best || c.base < best.c.base)) {
        best = { x: x + dx, z: z + dz, c };
      }
    }
    if (!best) best = flatExit(x, z, col.base, heightAt, maxFlat);
    if (!best) break;
    x = best.x;
    z = best.z;
    col = best.c;
  }
  return { x, z, col };
}

/** Breadth-first search over a flat area for a neighbour that is lower. */
function flatExit(x0, z0, base, heightAt, maxFlat) {
  const seen = new Set([key(x0, z0)]);
  const queue = [[x0, z0]];
  for (let qi = 0; qi < queue.length && seen.size <= maxFlat; qi++) {
    const [x, z] = queue[qi];
    for (const [dx, dz] of N4) {
      const nx = x + dx;
      const nz = z + dz;
      const k = key(nx, nz);
      if (seen.has(k)) continue;
      seen.add(k);
      const c = heightAt(nx, nz);
      if (!c) continue;
      if (c.base < base || c.kind === "water") return { x: nx, z: nz, c };
      if (c.base === base) queue.push([nx, nz]);
    }
  }
  return null;
}

/**
 * Fill the depression around a low point with `volume` m³.
 * @param {{ x: number, z: number }} start a low point (from descend)
 * @param {number} volume
 * @param {HeightFn} heightAt
 * @param {{ maxCols?: number, maxRadius?: number }} [opts]
 * @returns {{
 *   cols: PoolColumn[], level: number, used: number,
 *   spill: { x: number, z: number } | null, leftover: number,
 *   joinedWater: boolean, hitLimit: boolean,
 *   nextBase: number,   // level up to which these columns describe the pool (refill above it)
 * }}
 */
export function fillBasin(start, volume, heightAt, opts = {}) {
  const maxCols = opts.maxCols ?? 512;
  const maxRadius = opts.maxRadius ?? 32;
  const first = heightAt(start.x, start.z);
  /** @type {PoolColumn[]} */
  const cols = [];
  const empty = { cols, level: 0, used: 0, spill: null, leftover: volume, joinedWater: false, hitLimit: false, nextBase: Infinity };
  if (!first) return empty;
  if (first.kind === "water") return { ...empty, leftover: 0, joinedWater: true };

  const seen = new Set([key(start.x, start.z)]);
  const heap = new MinHeap();
  heap.push({ x: start.x, z: start.z, c: first }, first.base);
  let level = first.base;
  let v = Math.max(0, volume);

  while (heap.size > 0) {
    const { x, z, c } = heap.pop();
    if (c.kind === "water" || c.base < level - 1e-9) {
      // Rim reached: the water runs over into natural water or downhill.
      if (c.kind === "water") return { cols, level, used: volume - v, spill: null, leftover: 0, joinedWater: true, hitLimit: false, nextBase: level };
      return { cols, level, used: volume - v, spill: { x, z }, leftover: v, joinedWater: false, hitLimit: false, nextBase: level };
    }
    if (cols.length > 0) {
      const need = (c.base - level) * cols.length;
      // Stop spreading if the next column would not get at least a thin film.
      if (v < need + MIN_FILM * (cols.length + 1)) {
        level += v / cols.length;
        return { cols, level, used: volume, spill: null, leftover: 0, joinedWater: false, hitLimit: false, nextBase: c.base + MIN_FILM };
      }
      v -= need;
    }
    level = c.base;
    cols.push({ x, z, base: c.base, ground: c.ground });
    if (cols.length >= maxCols) {
      return { cols, level, used: volume - v, spill: null, leftover: v, joinedWater: false, hitLimit: true, nextBase: level };
    }
    for (const [dx, dz] of N4) {
      const nx = x + dx;
      const nz = z + dz;
      const k = key(nx, nz);
      if (seen.has(k)) continue;
      seen.add(k);
      if (Math.abs(nx - start.x) > maxRadius || Math.abs(nz - start.z) > maxRadius) continue;
      const nc = heightAt(nx, nz);
      if (nc) heap.push({ x: nx, z: nz, c: nc }, nc.base);
    }
  }
  if (v > 0 && cols.length > 0) level += v / cols.length; // closed basin (walled in)
  return { cols, level, used: volume, spill: null, leftover: 0, joinedWater: false, hitLimit: false, nextBase: Infinity };
}

/**
 * Surface height of `volume` spread over fixed columns (water fills the
 * lowest columns first, flat surface).
 * @param {number[]} bases
 */
export function levelOverColumns(bases, volume) {
  return levelOverSorted([...bases].sort((a, b) => a - b), volume);
}

/** Same as levelOverColumns for bases already sorted ascending (no copying). */
export function levelOverSorted(sorted, volume) {
  if (sorted.length === 0) return -Infinity;
  let v = Math.max(0, volume);
  let level = sorted[0];
  for (let i = 0; i < sorted.length; i++) {
    const next = i + 1 < sorted.length ? sorted[i + 1] : Infinity;
    const need = (next - level) * (i + 1);
    if (v <= need) return level + v / (i + 1);
    v -= need;
    level = next;
  }
  return level;
}

/** Depth of water in a column at a surface level. */
export function depthAt(base, level) {
  return Math.max(0, level - base);
}
