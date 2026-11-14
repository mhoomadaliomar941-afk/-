// Water physics system: finds tanks, simulates every body, pours escaping
// water onto the terrain, makes puddles and floods from rain, and shows the
// result with real blocks.
//
// Safety rules for the world:
//  - Natural water (anything bigger than MAX_TANK_CELLS) is never touched.
//  - Blocks are only ever changed through type filters: water is added only
//    into air, flowing water or grass; water/ice/puddles are removed only where
//    this system placed them or inside a detected tank. A player's block is
//    never replaced.
//  - A whole water layer is added or removed in one tick (one fillBlocks
//    call), so Minecraft's infinite-source rule cannot recreate water that the
//    simulation removed.

import { TankScan, tracePipe } from "./tankScan.js";
import { descend, fillBasin } from "./basin.js";
import { catchUp, poolLevel, stepPipeLink, stepPool, stepTank, tankLevel, tankOutflow, MIN_POOL_VOLUME } from "./bodies.js";
import { pressureKPa, rainRate, capacity } from "./physics.js";
import { LOD } from "../simulation/regions.js";
import { toChunk, round } from "../utils/math.js";

export const WATER = "minecraft:water";
export const FLOWING = "minecraft:flowing_water";
export const AIR = "minecraft:air";
export const ICE = "minecraft:ice";
export const PUDDLE = "rw:puddle";
/** Blocks that water may be placed into. */
export const FILLABLE = Object.freeze([AIR, FLOWING, "minecraft:short_grass", "minecraft:tall_grass", "minecraft:fern", "minecraft:deadbush"]);

/** Ticks between physics steps of a body, by detail tier. */
export const BODY_INTERVAL = Object.freeze({ [LOD.NEAR]: 10, [LOD.MEDIUM]: 20, [LOD.FAR]: 60 });
/** Longer gaps than this (seconds) use the cheap catch-up. */
const CATCHUP_AFTER = 30;
const MAX_TANKS = 64;
const MAX_POOLS = 128;
const MAX_POURS_PER_UPDATE = 4;
const HEIGHT_CACHE_MAX = 8192;
const NATURAL_IGNORE_TICKS = 6000;
/** Runoff collected in a region before it is poured (m³): a 3 m² puddle, 10 cm deep. */
const RUNOFF_POUR = 0.3;
/** Region soil holds 256 m² x 0.5 m = 128 m³ of water at saturation. */
const REGION_SOIL_M3 = 128;
const STORE_INDEX = "water/index";
const storeKey = (id) => `water/b/${id}`;

const k3 = (x, y, z) => `${x},${y},${z}`;
const k2 = (x, z) => `${x},${z}`;
/** Numeric column key (exact for |x|, |z| < 2^25, beyond the 30M world border). */
const ck = (x, z) => (x + 33554432) * 67108864 + (z + 33554432);
/** Map per dimension, created on first use. */
const dimMap = (outer, dim) => {
  let m = outer.get(dim);
  if (!m) {
    m = new Map();
    outer.set(dim, m);
  }
  return m;
};

/**
 * Game access needed by the water system.
 * @typedef {{
 *   cellAt: (dim: string, x: number, y: number, z: number) => import("./tankScan.js").CellKind,
 *   columnAt: (dim: string, x: number, z: number) => import("./basin.js").Column | null,
 *   fill: (dim: string, cells: { x: number, y: number, z: number }[], type: string, from: readonly string[]) => void,
 *   isLoaded: (dim: string, x: number, z: number) => boolean,
 * }} WaterHost
 * @typedef {import("./bodies.js").Tank & { r: { top: number, iced: boolean } }} RTank
 * @typedef {import("./bodies.js").Pool & { r: { w: string[], p: string[], iced: string[] } }} RPool
 */

export class WaterSystem {
  /**
   * @param {{
   *   storage: import("../core/storage.js").Storage,
   *   config: import("../core/config.js").Config,
   *   logger: import("../core/logger.js").Logger,
   *   tracker: import("../simulation/regions.js").RegionTracker,
   *   store: import("../simulation/regions.js").RegionStore,
   *   climate: import("../weather/climateSystem.js").ClimateSystem,
   *   weather: import("../weather/weatherSystem.js").WeatherSystem,
   *   perf: import("../core/performance.js").PerformanceManager,
   *   enqueue: (key: string, owner: string, fn: (timeLeftMs: () => number) => boolean) => boolean,
   *   host: WaterHost,
   *   now: () => number,
   * }} deps
   */
  constructor(deps) {
    this.id = "water";
    this.name = "Water physics";
    this.intervalTicks = 10;
    // Throttling stretches the interval on weak devices; physics stays correct
    // because every step is split into small sub-steps.
    this.scalable = true;
    this.d = deps;
    /** @type {Map<string, RTank | RPool>} */
    this.bodies = new Map();
    this.nextId = 1;
    /** @type {Map<string, { dim: string, x: number, y: number, z: number, reason: string, tankId?: string }>} */
    this.scans = new Map();
    /** @type {Map<string, number>} chunk key -> tick until which natural water there is ignored */
    this.ignored = new Map();
    /** @type {Map<string, Map<number, import("./basin.js").Column>>} dim -> column key -> terrain column */
    this.heights = new Map();
    this.heightCount = 0;
    /** @type {Map<string, { x: number, z: number }>} region -> where its runoff collects */
    this.runoffTarget = new Map();
    /** @type {Map<string, number>} "dim|x|z" -> m³ waiting to be poured */
    this.pending = new Map();
    /** @type {Set<string>} */
    this.dirty = new Set();
    /** @type {Map<string, Map<number, string>>} dim -> column key -> body id, for every tank and pool column */
    this.colIndex = new Map();
    this.indexDirty = false;
    /** @type {Map<string, number>} region key -> accumulated runoff m³ */
    this.runoff = new Map();
    this.runoffCursor = 0;
    /** @type {((msg: { dim: string, x: number, y: number, z: number, tankId: string | null, status: string }) => void)[]} */
    this.scanListeners = [];
    this.stats = { scans: 0, tanksFound: 0, natural: 0, pours: 0, lost: 0, absorbed: 0, runoff: 0, blockOps: 0, layerOps: 0 };
    this._load();
    for (const b of this.bodies.values()) this._index(b);
  }

  _cols(b) {
    return b.type === "tank" ? b.columns : b.cols;
  }

  _index(b) {
    const m = dimMap(this.colIndex, b.dim);
    for (const c of this._cols(b)) m.set(ck(c[0], c[1]), b.id);
  }

  _unindex(b) {
    const m = dimMap(this.colIndex, b.dim);
    for (const c of this._cols(b)) {
      const key = ck(c[0], c[1]);
      if (m.get(key) === b.id) m.delete(key);
    }
  }

  _bodyAt(dim, x, z) {
    const id = this.colIndex.get(dim)?.get(ck(x, z));
    return id === undefined ? undefined : this.bodies.get(id);
  }

  // ---------------------------------------------------------------- storage

  _load() {
    const idx = this.d.storage.load(STORE_INDEX);
    if (!idx.ok) return;
    const { next, ids } = idx.value ?? {};
    if (Number.isInteger(next)) this.nextId = next;
    for (const id of Array.isArray(ids) ? ids : []) {
      const res = this.d.storage.load(storeKey(id));
      const b = res.ok ? res.value : null;
      if (b && (b.type === "tank" || b.type === "pool") && typeof b.volume === "number" && b.r) {
        this.bodies.set(id, b);
      } else {
        this.d.logger.warn("water", `water body ${id} could not be loaded; dropped`);
        this.indexDirty = true;
      }
    }
  }

  /** Save the index and up to `max` changed bodies. @returns {number} writes */
  save(max = 8) {
    let writes = 0;
    for (const id of [...this.dirty]) {
      if (writes >= max) break;
      const b = this.bodies.get(id);
      if (b) {
        if (!this.d.storage.save(storeKey(id), b).ok) continue;
      } else {
        this.d.storage.delete(storeKey(id));
      }
      this.dirty.delete(id);
      writes++;
    }
    if (this.indexDirty) {
      if (this.d.storage.save(STORE_INDEX, { next: this.nextId, ids: [...this.bodies.keys()] }).ok) {
        this.indexDirty = false;
        writes++;
      }
    }
    return writes;
  }

  _add(body) {
    const old = this.bodies.get(body.id);
    if (old) this._unindex(old);
    this.bodies.set(body.id, body);
    this._index(body);
    this.dirty.add(body.id);
    this.indexDirty = true;
  }

  _remove(id) {
    const b = this.bodies.get(id);
    if (b) this._unindex(b);
    this.bodies.delete(id);
    this.dirty.add(id); // deletes its stored copy
    this.indexDirty = true;
  }

  // ---------------------------------------------------------------- lookups

  /** @returns {RTank | undefined} */
  tankAt(dim, x, y, z) {
    const b = this._bodyAt(dim, x, z);
    if (!b || b.type !== "tank") return undefined;
    for (const [cx, cz, minY, maxY] of b.columns) if (cx === x && cz === z) return y >= minY && y <= maxY ? b : undefined;
    return undefined;
  }

  /** @returns {RPool | undefined} */
  poolAt(dim, x, z) {
    const b = this._bodyAt(dim, x, z);
    return b && b.type === "pool" ? b : undefined;
  }

  /** Terrain column, cached; our own pool water reads as the ground below it. */
  heightAt(dim, x, z) {
    const m = dimMap(this.heights, dim);
    const key = ck(x, z);
    const cached = m.get(key);
    if (cached) return cached;
    let col = this.d.host.columnAt(dim, x, z);
    if (col && col.kind === "water") {
      const pool = this.poolAt(dim, x, z);
      const pc = pool?.cols.find((c) => c[0] === x && c[1] === z);
      if (pc) col = { base: pc[2], kind: "ground", ground: pc[3] };
    }
    if (col === null) return null; // unloaded: not cached
    if (this.heightCount >= HEIGHT_CACHE_MAX) {
      this.heights.clear(); // simple and bounded; terrain is re-read on demand
      this.heightCount = 0;
    }
    dimMap(this.heights, dim).set(key, col);
    this.heightCount++;
    return col;
  }

  invalidateColumn(dim, x, z) {
    if (this.heights.get(dim)?.delete(ck(x, z))) this.heightCount--;
    for (const [k, t] of this.runoffTarget) if (t.x === x && t.z === z && k.startsWith(dim)) this.runoffTarget.delete(k);
  }

  // ---------------------------------------------------------------- detection

  /** Start (or repeat) a scan of the water body containing a source block. */
  requestScan(dim, x, y, z, reason) {
    const chunk = `${dim}|${toChunk(x)}|${toChunk(z)}`;
    if ((this.ignored.get(chunk) ?? -1) > this.d.now()) return false;
    const existing = this.tankAt(dim, x, y, z);
    const key = existing ? `tank:${existing.id}` : `seed:${dim}|${k3(x, y, z)}`;
    if (this.scans.has(key)) return false;
    const scan = new TankScan((sx, sy, sz) => this.d.host.cellAt(dim, sx, sy, sz), { x, y, z });
    if (scan.status === "not_water") return false;
    this.scans.set(key, { dim, x, y, z, reason, tankId: existing?.id });
    this.stats.scans++;
    const ok = this.d.enqueue(`water-scan:${key}`, this.id, (timeLeftMs) => {
      do {
        if (scan.step(32)) {
          this.scans.delete(key);
          this._scanDone(scan, dim, { x, y, z }, reason, existing?.id);
          return true;
        }
      } while (timeLeftMs() > 0);
      return false;
    });
    if (!ok) this.scans.delete(key);
    return ok;
  }

  _scanDone(scan, dim, seed, reason, tankId) {
    const notify = (status, id = null) => {
      for (const fn of this.scanListeners) fn({ dim, ...seed, tankId: id, status });
    };
    if (scan.status === "too_big") {
      this.stats.natural++;
      for (const c of scan.cells.slice(0, 64)) this.ignored.set(`${dim}|${toChunk(c.x)}|${toChunk(c.z)}`, this.d.now() + NATURAL_IGNORE_TICKS);
      notify("natural");
      return;
    }
    if (scan.status !== "done") {
      notify(scan.status);
      return;
    }
    if (!tankId) {
      // Several broken blocks (an explosion) start several scans of the same
      // water; the first to finish made the tank, the others update it.
      tankId = this.tankAt(dim, seed.x, seed.y, seed.z)?.id;
    }
    const shape = scan.result();
    const old = tankId ? /** @type {RTank | undefined} */ (this.bodies.get(tankId)) : undefined;
    const interesting = shape.openings.length > 0 || shape.pipes.length > 0 || shape.valves.length > 0 || reason === "gauge" || old;
    if (!interesting) {
      notify("sealed");
      return;
    }
    if (!old && [...this.bodies.values()].filter((b) => b.type === "tank").length >= MAX_TANKS) {
      this.d.logger.warn("water", `tank limit (${MAX_TANKS}) reached; new tank ignored`);
      notify("limit");
      return;
    }
    const isOwn = (x, y, z) => scan.seen.has(k3(x, y, z));
    const pipes = shape.pipes.map((p) => {
      const tr = tracePipe((x, y, z) => this.d.host.cellAt(dim, x, y, z), p, isOwn);
      return { x: p.x, y: p.y, z: p.z, startY: p.fromY, endY: tr?.end.y ?? p.y, out: tr?.out ?? null };
    });
    const id = old?.id ?? `t${this.nextId++}`;
    // Keep capacity a previous scan saw above the current water (it drained).
    let layers = shape.layers;
    let baseY = shape.baseY;
    if (old && old.baseY === baseY && old.layers.length > layers.length) layers = [...layers, ...old.layers.slice(layers.length)];
    let volume = shape.cells;
    if (old && shape.waterTop === (old.r.iced ? old.r.top - 1 : old.r.top)) {
      // The surface is where we last drew it, so nobody added or took water.
      // Water is drawn in whole layers: counting blocks would round the real
      // volume (a half-empty top layer counts as full) and every rescan would
      // create water. The simulated volume stays the truth.
      volume = Math.min(old.volume, capacity(layers));
    }
    const openings = shape.openings.map((o) => ({ ...o, open: true }));
    const valves = [...shape.valves];
    if (old) this._keepOpeningsAbove(old, dim, shape.waterTop, openings, valves);
    /** @type {RTank} */
    const tank = {
      id, type: "tank", dim,
      baseY, layers, columns: mergeColumns(old?.columns, shape.columns), openTop: shape.openTop,
      openings, valves, pipes,
      volume, frozen: old?.frozen ?? false, lastSim: old?.lastSim ?? this.d.now(), // time not yet simulated is not lost
      stats: old?.stats ?? { out: 0, rained: 0, evaporated: 0, outflow: 0 },
      r: { top: shape.waterTop, iced: false },
    };
    this._add(tank);
    if (!old) this.stats.tanksFound++;
    this.d.logger.info("water", `${old ? "rescanned" : "found"} tank ${id}: ${shape.cells} m³, ${shape.openings.length} openings, ${pipes.length} pipes`);
    for (const p of pipes) {
      if (p.out?.kind === "water" && !this.tankAt(dim, p.out.x, p.out.y, p.out.z)) this.requestScan(dim, p.out.x, p.out.y, p.out.z, "pipe");
    }
    notify("tank", id);
  }

  /**
   * The scan only sees openings touching the water. A known hole or valve above
   * the current surface still matters (rain or a refill reaches it), so it is
   * kept while the block there is still open.
   */
  _keepOpeningsAbove(old, dim, waterTop, openings, valves) {
    const seen = new Set([...openings, ...valves].map((o) => k3(o.x, o.y, o.z)));
    for (const o of old.openings) {
      if (o.y <= waterTop || seen.has(k3(o.x, o.y, o.z))) continue;
      seen.add(k3(o.x, o.y, o.z));
      const c = this.d.host.cellAt(dim, o.x, o.y, o.z);
      if (c === "air" || c === "flow" || c === "drain" || c === "valve_open") openings.push({ ...o });
    }
    for (const v of old.valves) {
      if (v.y <= waterTop || seen.has(k3(v.x, v.y, v.z))) continue;
      seen.add(k3(v.x, v.y, v.z));
      const c = this.d.host.cellAt(dim, v.x, v.y, v.z);
      if (c === "valve_open" || c === "valve_closed") valves.push(v);
    }
  }

  // ---------------------------------------------------------------- events

  /** A block at (x,y,z) was broken or blown up. */
  onBlockRemoved(dim, x, y, z) {
    this.invalidateColumn(dim, x, z);
    if (!this.d.config.get("water.autoDetect")) return;
    for (const [dx, dy, dz] of [[1, 0, 0], [-1, 0, 0], [0, 0, 1], [0, 0, -1], [0, 1, 0], [0, -1, 0]]) {
      if (this.d.host.cellAt(dim, x + dx, y + dy, z + dz) === "source") {
        this.requestScan(dim, x + dx, y + dy, z + dz, "break");
        return;
      }
    }
  }

  /** A water cell of a tank to start a rescan from (its current top layer). */
  _seed(t) {
    for (const [x, z, minY, maxY] of t.columns) {
      if (t.r.top >= minY) return { x, y: Math.min(maxY, t.r.top), z };
    }
    return null;
  }

  _rescan(t, reason) {
    const s = this._seed(t);
    if (s) this.requestScan(t.dim, s.x, s.y, s.z, reason);
  }

  /** A block was placed: it may close a hole or change a pool's ground. */
  onBlockPlaced(dim, x, y, z) {
    this.invalidateColumn(dim, x, z);
    for (const b of this.bodies.values()) {
      if (b.dim !== dim || b.type !== "tank") continue;
      if (b.openings.some((o) => o.x === x && o.y === y && o.z === z) || b.pipes.some((p) => p.x === x && p.y === y && p.z === z)) {
        this._rescan(b, "change");
      }
    }
  }

  /** A valve was opened or closed. */
  onValve(dim, x, y, z) {
    for (const b of this.bodies.values()) {
      if (b.type === "tank" && b.dim === dim && b.valves.some((v) => v.x === x && v.y === y && v.z === z)) {
        this._rescan(b, "valve");
      }
    }
  }

  // ---------------------------------------------------------------- simulation

  _env(dim, x, y, z) {
    const w = this.d.weather.state;
    const c = this.d.climate.sample(dim, x, y, z);
    return {
      tempC: c?.tempC ?? 15,
      humidity: w.H,
      wind: w.wind,
      raining: w.raining && (c?.precip ?? "rain") !== "none",
      intensity: w.I,
      soil: c?.soil ?? 0.5,
    };
  }

  _ref(b) {
    return b.type === "tank" ? { x: b.columns[0][0], y: Math.floor(tankLevel(b)), z: b.columns[0][1] } : { x: b.min[0], y: b.cols[0][2], z: b.min[1] };
  }

  /** @param {import("../core/scheduler.js").UpdateContext} ctx */
  update(ctx) {
    const now = ctx.tick;
    let ops = 0;
    /** @type {RTank[]} */
    const steppedTanks = [];

    for (const b of [...this.bodies.values()]) {
      const ref = this._ref(b);
      const region = this.d.tracker.regionAt(b.dim, ref.x, ref.z);
      if (!region) continue; // not simulated now; caught up when it comes back
      if (now - b.lastSim < BODY_INTERVAL[region.tier]) continue;
      const seconds = (now - b.lastSim) / 20;
      const env = this._env(b.dim, ref.x, ref.y, ref.z);
      const res = seconds > CATCHUP_AFTER
        ? catchUp(b, seconds, env)
        : b.type === "tank" ? stepTank(b, seconds, env) : stepPool(b, seconds, env);
      b.lastSim = now;
      ops++;
      for (const [k, v] of res.pours) this._queuePour(b.dim, k, v);
      this.stats.lost += res.lost;
      if (res.absorbed > 0) this._wetSoil(region, res.absorbed);
      if (res.overflow > 0 && b.type === "tank") this.stats.lost += res.overflow;
      if (b.type === "tank") steppedTanks.push(b);
      if (b.type === "pool" && res.regrow) this._refill(b);
      this.dirty.add(b.id);
    }

    // Tanks joined by pipes exchange water (each pair once).
    for (const a of steppedTanks) {
      for (const p of a.pipes) {
        if (p.out?.kind !== "water") continue;
        const b = this.tankAt(a.dim, p.out.x, p.out.y, p.out.z);
        if (!b || b.id <= a.id) continue;
        stepPipeLink(a, p, b, p.out.y, BODY_INTERVAL[LOD.NEAR] / 20);
        this.dirty.add(b.id);
      }
    }

    ops += this._pour();
    ops += this._rainRunoff(ctx);
    ops += this._render(ctx);
    this._cleanup();
    return ops;
  }

  _queuePour(dim, xz, v) {
    const key = `${dim}|${xz}`;
    this.pending.set(key, (this.pending.get(key) ?? 0) + v);
  }

  /** Add absorbed water to the region's soil moisture (climate data). */
  _wetSoil(region, m3) {
    this.stats.absorbed += m3;
    const c = region.record?.data?.c;
    if (!Array.isArray(c)) return;
    c[0] = Math.min(1000, c[0] + Math.round((m3 / REGION_SOIL_M3) * 1000));
    this.d.store.markDirty(region.dim, region.cx, region.cz);
  }

  _pour() {
    let n = 0;
    for (const [key, v] of [...this.pending]) {
      if (n >= MAX_POURS_PER_UPDATE) break;
      const [dim, xz] = key.split("|");
      const [x, z] = xz.split(",").map(Number);
      if (!this.d.host.isLoaded(dim, x, z)) continue;
      this.pending.delete(key);
      n++;
      this.pourAt(dim, x, z, v);
    }
    return n;
  }

  /** Pour `volume` m³ onto the terrain at (x, z). */
  pourAt(dim, x, z, volume, depth = 0) {
    if (volume <= 0 || depth > 6) {
      this.stats.lost += Math.max(0, volume);
      return;
    }
    this.stats.pours++;
    // Straight into a pool already there: no terrain search needed.
    const here = this.poolAt(dim, x, z);
    if (here) {
      here.volume += volume;
      if (poolLevel(here) > here.limit + 1e-6) this._refill(here, depth);
      else this.dirty.add(here.id);
      return;
    }
    const h = (px, pz) => this.heightAt(dim, px, pz);
    const low = descend(x, z, h);
    if (!low) {
      this._queuePour(dim, k2(x, z), volume); // not loaded yet
      return;
    }
    if (low.col.kind === "water") {
      const tank = this.tankAt(dim, low.x, low.col.base - 1, low.z);
      if (tank) {
        tank.volume = Math.min(capacity(tank.layers), tank.volume + volume); // falls into a detected tank
        this.dirty.add(tank.id);
      } else {
        this.stats.lost += volume; // joins a lake, river or the sea
      }
      return;
    }
    const existing = this.poolAt(dim, low.x, low.z);
    if (existing) {
      existing.volume += volume;
      // Recompute the shape only when the water rises past what it describes.
      if (poolLevel(existing) > existing.limit + 1e-6) this._refill(existing, depth);
      else this.dirty.add(existing.id);
      return;
    }
    const pools = [...this.bodies.values()].filter((b) => b.type === "pool").length;
    if (pools >= MAX_POOLS) {
      this.stats.lost += volume;
      return;
    }
    const r = fillBasin({ x: low.x, z: low.z }, volume, h, { maxCols: 512, maxRadius: 32 });
    if (r.cols.length === 0) {
      this.stats.lost += volume;
      return;
    }
    // Merge into a pool the new basin reaches.
    for (const c of r.cols) {
      const other = this.poolAt(dim, c.x, c.z);
      if (other) {
        other.volume += volume;
        if (poolLevel(other) > other.limit + 1e-6) this._refill(other, depth);
        else this.dirty.add(other.id);
        return;
      }
    }
    /** @type {RPool} */
    const pool = {
      id: `p${this.nextId++}`, type: "pool", dim,
      cols: r.cols.map((c) => [c.x, c.z, c.base, c.ground]),
      min: [low.x, low.z], volume: r.used, limit: r.nextBase, spill: r.spill ? [r.spill.x, r.spill.z] : null,
      frozen: false, lastSim: this.d.now(),
      stats: { absorbed: 0, rained: 0, evaporated: 0, drained: 0 },
      r: { w: [], p: [], iced: [] },
    };
    this._add(pool);
    if (r.spill && r.leftover > 0) this.pourAt(dim, r.spill.x, r.spill.z, r.leftover, depth + 1);
    else if (r.leftover > 0) this.stats.lost += r.leftover;
  }

  /** Recompute a pool's shape for its current volume (it grew). */
  _refill(p, depth = 0) {
    const h = (px, pz) => this.heightAt(p.dim, px, pz);
    const r = fillBasin({ x: p.min[0], z: p.min[1] }, p.volume, h, { maxCols: 512, maxRadius: 32 });
    if (r.cols.length === 0) return;
    this._unindex(p);
    p.cols = r.cols.map((c) => [c.x, c.z, c.base, c.ground]);
    this._index(p);
    p.volume = r.used;
    p.limit = r.nextBase;
    p.spill = r.spill ? [r.spill.x, r.spill.z] : null;
    this.dirty.add(p.id);
    if (r.spill && r.leftover > 0) this.pourAt(p.dim, r.spill.x, r.spill.z, r.leftover, depth + 1);
    else if (r.leftover > 0) this.stats.lost += r.leftover;
  }

  /**
   * Rain on saturated ground runs off into low spots (puddles, ponds, floods).
   * A couple of NEAR regions per update; each keeps its own runoff total.
   */
  _rainRunoff(ctx) {
    const w = this.d.weather.state;
    if (!w.raining || !this.d.config.get("water.rainPuddles")) return 0;
    const near = [...this.d.tracker.active.values()].filter((r) => r.tier === LOD.NEAR && r.dim === "minecraft:overworld");
    if (near.length === 0) return 0;
    let ops = 0;
    for (let i = 0; i < 2; i++) {
      const r = near[this.runoffCursor++ % near.length];
      const c = r.record?.data?.c;
      if (!Array.isArray(c) || c[0] < 850) continue; // ground still soaks it up
      const cell = this.d.climate.sample(r.dim, r.cx * 16 + 8, c[2], r.cz * 16 + 8);
      if (!cell || cell.precip !== "rain") continue;
      // runoff of the whole region since its last turn (~ every near.length/2 updates)
      const seconds = (Math.max(1, near.length / 2) * this.intervalTicks) / 20;
      const key = `${r.dim}|${r.cx}|${r.cz}`;
      const acc = (this.runoff.get(key) ?? 0) + rainRate(w.I) * 256 * 0.5 * seconds;
      if (acc < RUNOFF_POUR) {
        this.runoff.set(key, acc);
        continue;
      }
      this.runoff.delete(key);
      // Runoff collects at the region's low point, found once and remembered.
      let target = this.runoffTarget.get(key);
      if (!target) {
        const low = descend(r.cx * 16 + Math.floor(Math.random() * 16), r.cz * 16 + Math.floor(Math.random() * 16), (px, pz) => this.heightAt(r.dim, px, pz));
        if (!low) continue;
        target = { x: low.x, z: low.z };
        this.runoffTarget.set(key, target);
      }
      this.stats.runoff += acc;
      this.pourAt(r.dim, target.x, target.z, acc);
      ops++;
    }
    return ops;
  }

  // ---------------------------------------------------------------- rendering

  /** Show bodies near players with real blocks, within the block budget. */
  _render(ctx) {
    let budget = this.d.perf.profile.waterBlocksPerUpdate;
    let ops = 0;
    for (const b of this.bodies.values()) {
      if (budget <= 0) break;
      const ref = this._ref(b);
      const region = this.d.tracker.regionAt(b.dim, ref.x, ref.z);
      if (!region || region.tier === LOD.FAR) continue;
      if (!this.d.host.isLoaded(b.dim, ref.x, ref.z)) continue;
      if (b.type === "tank" && this.scans.has(`tank:${b.id}`)) continue; // a scan is reading these blocks: keep them still
      const used = b.type === "tank" ? this._renderTank(b, budget) : this._renderPool(b, budget);
      budget -= used;
      if (used) ops++;
    }
    return ops;
  }

  /** Cells of a tank at one height. */
  _tankLayer(t, y) {
    const cells = [];
    for (const [x, z, minY, maxY] of t.columns) if (y >= minY && y <= maxY) cells.push({ x, y, z });
    return cells;
  }

  _fill(dim, cells, type, from) {
    if (cells.length === 0) return 0;
    this.d.host.fill(dim, cells, type, from);
    this.stats.layerOps++;
    this.stats.blockOps += cells.length;
    return cells.length;
  }

  /** @param {RTank} t */
  _renderTank(t, budget) {
    const want = Math.min(t.baseY + t.layers.length, Math.floor(tankLevel(t) + 0.5)) - 1; // top full layer to show
    let used = 0;
    const r = t.r;
    // Freeze or thaw the top layer.
    if (t.frozen !== r.iced && r.top >= t.baseY) {
      used += t.frozen
        ? this._fill(t.dim, this._tankLayer(t, r.top), ICE, [WATER])
        : this._fill(t.dim, this._tankLayer(t, r.top), WATER, [ICE]);
      r.iced = t.frozen;
      this.dirty.add(t.id);
    }
    if (r.iced) return used;
    // Whole layers at a time (never split across ticks).
    while (r.top > want && (used === 0 || used < budget)) {
      used += this._fill(t.dim, this._tankLayer(t, r.top), AIR, [WATER, FLOWING]);
      r.top--;
      this.dirty.add(t.id);
    }
    while (r.top < want && (used === 0 || used < budget)) {
      r.top++;
      used += this._fill(t.dim, this._tankLayer(t, r.top), WATER, FILLABLE);
      this.dirty.add(t.id);
    }
    return used;
  }

  /** @param {RPool} p */
  _renderPool(p, budget) {
    const level = poolLevel(p);
    // Nothing visible changed since the last render: skip all the set work.
    const sig = `${p.cols.length}|${Math.round(level * 20)}|${p.frozen ? 1 : 0}|${p.volume > 0 ? 1 : 0}`;
    if (/** @type {any} */ (p)._sig === sig) return 0;
    /** @type {Map<number, { x: number, y: number, z: number }[]>} */
    const want = new Map();
    const puddles = new Set();
    if (p.volume > 0) {
      for (const [x, z, base] of p.cols) {
        const d = level - base;
        if (d >= 0.5) {
          const n = Math.round(d);
          for (let y = base; y < base + n; y++) {
            if (!want.has(y)) want.set(y, []);
            /** @type {any[]} */ (want.get(y)).push({ x, y, z });
          }
        } else if (d >= 0.05 && !p.frozen) {
          puddles.add(k3(x, base, z));
        }
      }
    }
    const shown = new Set(p.r.w);
    const wantSet = new Set();
    for (const cells of want.values()) for (const c of cells) wantSet.add(k3(c.x, c.y, c.z));
    let used = 0;
    const parse = (s) => {
      const [x, y, z] = s.split(",").map(Number);
      return { x, y, z };
    };

    // Thaw before any change; freeze only the top layer.
    if (p.r.iced.length && !p.frozen) {
      used += this._fill(p.dim, p.r.iced.map(parse), WATER, [ICE]);
      p.r.iced = [];
      this.dirty.add(p.id);
    }
    if (p.frozen) {
      if (!p.r.iced.length && shown.size) {
        const top = Math.max(...[...shown].map((s) => parse(s).y));
        const topCells = [...shown].filter((s) => parse(s).y === top);
        used += this._fill(p.dim, topCells.map(parse), ICE, [WATER]);
        p.r.iced = topCells;
        this.dirty.add(p.id);
      }
      return used;
    }

    // Remove extra water, highest layer first, one whole layer per step.
    const extra = [...shown].filter((s) => !wantSet.has(s)).map(parse);
    while (extra.length && (used === 0 || used < budget)) {
      const top = Math.max(...extra.map((c) => c.y));
      const layer = extra.filter((c) => c.y === top);
      used += this._fill(p.dim, layer, AIR, [WATER, FLOWING]);
      for (const c of layer) shown.delete(k3(c.x, c.y, c.z));
      for (let i = extra.length - 1; i >= 0; i--) if (extra[i].y === top) extra.splice(i, 1);
    }
    // Add missing water, lowest layer first.
    const ys = [...want.keys()].sort((a, b) => a - b);
    for (const y of ys) {
      if (used > 0 && used >= budget) break;
      const missing = /** @type {any[]} */ (want.get(y)).filter((c) => !shown.has(k3(c.x, c.y, c.z)));
      if (!missing.length) continue;
      used += this._fill(p.dim, missing, WATER, [...FILLABLE, PUDDLE]);
      for (const c of missing) shown.add(k3(c.x, c.y, c.z));
    }
    // Puddles (custom thin block): independent cells.
    const shownP = new Set(p.r.p);
    const goneP = [...shownP].filter((s) => !puddles.has(s) && !shown.has(s));
    const newP = [...puddles].filter((s) => !shownP.has(s) && !shown.has(s));
    if (goneP.length) used += this._fill(p.dim, goneP.map(parse), AIR, [PUDDLE]);
    if (newP.length && (used === 0 || used < budget)) used += this._fill(p.dim, newP.map(parse), PUDDLE, [AIR]);
    const nextP = new Set([...shownP].filter((s) => puddles.has(s)));
    if (newP.length) for (const s of newP) nextP.add(s);

    const nextW = [...shown];
    if (nextW.length !== p.r.w.length || nextP.size !== p.r.p.length || used) {
      p.r.w = nextW;
      p.r.p = [...nextP];
      this.dirty.add(p.id);
    }
    // Remember the signature only once the world fully matches it.
    const done = extra.length === 0 && ys.every((y) => /** @type {any[]} */ (want.get(y)).every((c) => shown.has(k3(c.x, c.y, c.z))));
    Object.defineProperty(p, "_sig", { value: done ? sig : null, writable: true, enumerable: false, configurable: true });
    return used;
  }

  /** Forget bodies that are empty and fully cleared from the world. */
  _cleanup() {
    for (const b of [...this.bodies.values()]) {
      if (b.type === "pool" && b.volume <= MIN_POOL_VOLUME && b.r.w.length === 0 && b.r.p.length === 0 && b.r.iced.length === 0) {
        this._remove(b.id);
      } else if (b.type === "tank" && b.volume < 0.01 && b.r.top < b.baseY) {
        this._remove(b.id);
      }
    }
  }

  // ---------------------------------------------------------------- info

  /** Readings for the gauge and the UI. */
  describeBody(b) {
    if (b.type === "tank") {
      const level = tankLevel(b);
      return {
        id: b.id, type: "tank",
        liters: Math.round(b.volume * 1000),
        capacityLiters: capacity(b.layers) * 1000,
        level: round(level, 2),
        depth: round(level - b.baseY, 2),
        bottomPressureKPa: round(pressureKPa(level - b.baseY), 1),
        openings: b.openings.filter((o) => o.open).length,
        closedValves: b.valves.length - b.openings.filter((o) => o.kind === "valve" && o.open).length,
        pipes: b.pipes.length,
        outflowLps: round(tankOutflow(b) * 1000, 1),
        leakedLiters: Math.round(b.stats.out * 1000),
        frozen: b.frozen,
      };
    }
    const level = poolLevel(b);
    const wet = b.cols.filter((c) => level - c[2] > 0).length;
    return {
      id: b.id, type: "pool",
      liters: Math.round(b.volume * 1000),
      areaM2: wet,
      maxDepth: round(Math.max(0, ...b.cols.map((c) => level - c[2])), 2),
      absorbedLiters: Math.round(b.stats.absorbed * 1000),
      evaporatedLiters: Math.round(b.stats.evaporated * 1000),
      frozen: b.frozen,
      flood: wet >= 64,
    };
  }

  /** Bodies within `radius` blocks, nearest first. */
  near(dim, x, z, radius = 48) {
    const out = [];
    for (const b of this.bodies.values()) {
      if (b.dim !== dim) continue;
      const ref = this._ref(b);
      const d = Math.hypot(ref.x - x, ref.z - z);
      if (d <= radius) out.push({ d, b });
    }
    return out.sort((a, b) => a.d - b.d).map((e) => e.b);
  }

  describe() {
    let tanks = 0;
    let pools = 0;
    let m3 = 0;
    for (const b of this.bodies.values()) {
      if (b.type === "tank") tanks++;
      else pools++;
      m3 += b.volume;
    }
    return { tanks, pools, liters: Math.round(m3 * 1000), scans: this.scans.size, pending: this.pending.size, blockOps: this.stats.blockOps };
  }
}

/** Union of tank columns from an older and a newer scan (keeps drained capacity). */
function mergeColumns(oldCols, newCols) {
  if (!oldCols) return newCols;
  const m = new Map(oldCols.map((c) => [k2(c[0], c[1]), [...c]]));
  for (const c of newCols) {
    const o = m.get(k2(c[0], c[1]));
    if (!o) m.set(k2(c[0], c[1]), [...c]);
    else {
      o[2] = Math.min(o[2], c[2]);
      o[3] = Math.max(o[3], c[3]);
    }
  }
  return /** @type {[number, number, number, number][]} */ ([...m.values()]);
}
