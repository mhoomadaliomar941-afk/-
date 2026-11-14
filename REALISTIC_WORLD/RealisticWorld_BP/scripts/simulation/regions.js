// Simulation regions and level of detail (LOD).
//
// The world is split into regions of one chunk (16x16 block columns). Around
// each player, regions get a detail tier by distance in chunks:
//   NEAR   full detail, MEDIUM reduced detail, FAR cheap stored-state updates.
// Regions outside every player's FAR radius, or in unloaded chunks, are not
// simulated at all.
//
// Every region the engine has seen gets a small persistent record. Records are
// grouped into 8x8-region sectors, one storage key per sector, loaded only when
// needed and evicted from memory (least recently used) above the profile limit.
// When a region becomes active again, listeners get the number of simulation
// ticks it was away, so later systems can apply a cheap catch-up instead of
// simulating every missed tick.
//
// Cost control (measured with scripts/benchmark.mjs):
//  - the active set is recomputed only when a player changes chunk, the profile
//    changes, or every FULL_REFRESH_UPDATES updates (to pick up chunks that
//    finished loading); otherwise an update costs almost nothing
//  - chunk-loaded checks run only for regions entering the set
//  - records are written when a region enters or leaves the set and at
//    checkpoints, not on every update

import { KEYS } from "../core/constants.js";
import { toChunk } from "../utils/math.js";

export const LOD = Object.freeze({ NEAR: "NEAR", MEDIUM: "MEDIUM", FAR: "FAR" });
const TIER_RANK = { NEAR: 0, MEDIUM: 1, FAR: 2 };
export const SECTOR_SIZE = 8;
/** v1 rows: [cx, cz, created, lastActive, visits]; v2 adds a 6th entry: system data or 0. */
const RECORD_VERSION = 2;
/** Full recompute at least this often (in tracker updates, 1 update = 1 s). */
export const FULL_REFRESH_UPDATES = 10;
/** Dirty sectors force-saved per evict() call when memory is over the limit. */
const MAX_FORCED_SAVES = 8;

/**
 * Per-region data owned by other systems lives in `data` under a short key
 * (e.g. data.c = climate). Systems call RegionStore.markDirty after changing it.
 * @typedef {{ dim: string, cx: number, cz: number, created: number, lastActive: number, visits: number, data?: Record<string, any> }} RegionRecord
 * @typedef {{ key: string, dim: string, sx: number, sz: number, regions: Map<string, RegionRecord>, dirty: boolean, lastUsed: number }} Sector
 * @typedef {{ dim: string, cx: number, cz: number, tier: string, record: RegionRecord, lastSim?: number }} ActiveRegion
 */

export function regionKey(dim, cx, cz) {
  return `${dim}|${cx}|${cz}`;
}

/** Short dimension id for storage keys ("minecraft:overworld" -> "overworld"). */
function dimShort(dim) {
  const i = dim.indexOf(":");
  return i >= 0 ? dim.slice(i + 1) : dim;
}

function sectorKeyOf(dim, cx, cz) {
  return KEYS.regionSector(dimShort(dim), Math.floor(cx / SECTOR_SIZE), Math.floor(cz / SECTOR_SIZE));
}

export class RegionStore {
  /**
   * @param {import("../core/storage.js").Storage} storage
   * @param {import("../core/logger.js").Logger} logger
   * @param {number} maxCachedRegions
   */
  constructor(storage, logger, maxCachedRegions) {
    this.storage = storage;
    this.logger = logger;
    this.setCapacity(maxCachedRegions);
    /** @type {Map<string, Sector>} */
    this.sectors = new Map();
    this.useCounter = 0;
    this.stats = { sectorLoads: 0, sectorSaves: 0, evictions: 0, forcedSaves: 0, corruptSectors: 0 };
  }

  setCapacity(maxCachedRegions) {
    this.maxSectors = Math.max(4, Math.ceil(maxCachedRegions / (SECTOR_SIZE * SECTOR_SIZE)));
  }

  _sector(dim, cx, cz) {
    const key = sectorKeyOf(dim, cx, cz);
    let sector = this.sectors.get(key);
    if (!sector) {
      sector = {
        key,
        dim,
        sx: Math.floor(cx / SECTOR_SIZE),
        sz: Math.floor(cz / SECTOR_SIZE),
        regions: new Map(),
        dirty: false,
        lastUsed: 0,
      };
      const res = this.storage.load(key);
      this.stats.sectorLoads++;
      if (res.ok && res.value && (res.value.v === 1 || res.value.v === RECORD_VERSION) && Array.isArray(res.value.r)) {
        for (const row of res.value.r) {
          if (!Array.isArray(row) || row.length < 5 || !row.slice(0, 5).every((n) => Number.isInteger(n))) continue;
          const [rcx, rcz, created, lastActive, visits] = row;
          /** @type {RegionRecord} */
          const rec = { dim, cx: rcx, cz: rcz, created, lastActive, visits };
          if (row[5] && typeof row[5] === "object" && !Array.isArray(row[5])) rec.data = row[5];
          sector.regions.set(regionKey(dim, rcx, rcz), rec);
        }
      } else if (res.ok) {
        this.stats.corruptSectors++;
        this.logger.warn("regions", `sector ${key} has an unknown format; starting it fresh`);
      } else if (!res.missing) {
        this.stats.corruptSectors++;
        this.logger.warn("regions", `sector ${key} unreadable (${res.error}); starting it fresh`);
      }
      this.sectors.set(key, sector);
    }
    sector.lastUsed = ++this.useCounter;
    return sector;
  }

  /** @returns {RegionRecord | undefined} */
  get(dim, cx, cz) {
    return this._sector(dim, cx, cz).regions.get(regionKey(dim, cx, cz));
  }

  /**
   * Mark a region as active now.
   * @returns {{ record: RegionRecord, awayTicks: number }} awayTicks = -1 for a region never seen before
   */
  activate(dim, cx, cz, tick) {
    const sector = this._sector(dim, cx, cz);
    const key = regionKey(dim, cx, cz);
    let record = sector.regions.get(key);
    let awayTicks = -1;
    if (record) {
      awayTicks = Math.max(0, tick - record.lastActive);
      record.visits++;
      record.lastActive = tick;
    } else {
      record = { dim, cx, cz, created: tick, lastActive: tick, visits: 1 };
      sector.regions.set(key, record);
    }
    sector.dirty = true;
    return { record, awayTicks };
  }

  /** Record that a region was simulated up to `tick` (on leave or checkpoint). */
  touch(dim, cx, cz, tick) {
    const sector = this._sector(dim, cx, cz);
    const record = sector.regions.get(regionKey(dim, cx, cz));
    if (record && record.lastActive !== tick) {
      record.lastActive = tick;
      sector.dirty = true;
    }
  }

  /** Mark a region's sector as changed after editing record.data. */
  markDirty(dim, cx, cz) {
    const sector = this.sectors.get(sectorKeyOf(dim, cx, cz));
    if (sector) sector.dirty = true;
  }

  /** @param {Sector} sector */
  _save(sector) {
    const rows = [...sector.regions.values()].map((r) => [r.cx, r.cz, r.created, r.lastActive, r.visits, r.data ?? 0]);
    const res = this.storage.save(sector.key, { v: RECORD_VERSION, r: rows });
    if (!res.ok) {
      this.logger.error("regions", `could not save sector ${sector.key}: ${res.error}`);
      return false;
    }
    sector.dirty = false;
    this.stats.sectorSaves++;
    return true;
  }

  /**
   * Save up to maxSectors dirty sectors, least recently used first.
   * @param {number} [maxSectors]
   * @returns {number} sectors saved
   */
  flush(maxSectors = Infinity) {
    let saved = 0;
    const dirty = [...this.sectors.values()].filter((s) => s.dirty).sort((a, b) => a.lastUsed - b.lastUsed);
    for (const sector of dirty) {
      if (saved >= maxSectors) break;
      if (!this._save(sector)) break;
      saved++;
    }
    return saved;
  }

  /**
   * Keep memory under the limit: drop least-recently-used sectors that are not
   * in use. Unsaved ones are saved first (at most MAX_FORCED_SAVES per call), so
   * nothing is lost and memory stays bounded even while travelling fast.
   * @param {Set<string>} [inUse] sector keys that must stay in memory
   */
  evict(inUse = new Set()) {
    if (this.sectors.size <= this.maxSectors) return 0;
    const candidates = [...this.sectors.values()]
      .filter((s) => !inUse.has(s.key))
      .sort((a, b) => a.lastUsed - b.lastUsed);
    let removed = 0;
    let forced = 0;
    for (const s of candidates) {
      if (this.sectors.size <= this.maxSectors) break;
      if (s.dirty) {
        if (forced >= MAX_FORCED_SAVES || !this._save(s)) continue;
        forced++;
      }
      this.sectors.delete(s.key);
      removed++;
    }
    this.stats.evictions += removed;
    this.stats.forcedSaves += forced;
    return removed;
  }

  dirtyCount() {
    let n = 0;
    for (const s of this.sectors.values()) if (s.dirty) n++;
    return n;
  }
}

/**
 * Engine system that keeps the active region set and its LOD tiers current.
 */
export class RegionTracker {
  /**
   * @param {{
   *   store: RegionStore,
   *   perf: import("../core/performance.js").PerformanceManager,
   *   getPlayers: () => { dim: string, x: number, z: number }[],
   *   isLoaded: (dim: string, blockX: number, blockZ: number) => boolean,
   * }} deps
   */
  constructor({ store, perf, getPlayers, isLoaded }) {
    this.id = "regions";
    this.name = "Region tracker (LOD)";
    this.intervalTicks = 20;
    this.scalable = false; // LOD must stay current; idle updates are nearly free
    this.store = store;
    this.perf = perf;
    this.getPlayers = getPlayers;
    this.isLoaded = isLoaded;
    /** @type {Map<string, ActiveRegion>} */
    this.active = new Map();
    /** @type {((r: ActiveRegion, awayTicks: number) => void)[]} */
    this.resumeListeners = [];
    /** @type {((r: ActiveRegion) => void)[]} */
    this.enterListeners = [];
    this.counts = { NEAR: 0, MEDIUM: 0, FAR: 0 };
    this.resumedTotal = 0;
    this.lastResumeAway = 0;
    this.recomputes = 0;
    this.skipped = 0;
    this._signature = "";
    this._sinceFull = 0;
    this._lastTick = 0;
  }

  /** Called when a known region becomes active again, with the ticks it was away. */
  onResume(fn) {
    this.resumeListeners.push(fn);
  }

  /** Called for every region entering the active set (new or known). */
  onEnter(fn) {
    this.enterListeners.push(fn);
  }

  /** @returns {ActiveRegion | undefined} */
  regionAt(dim, blockX, blockZ) {
    return this.active.get(regionKey(dim, toChunk(blockX), toChunk(blockZ)));
  }

  /** Tier of a region right now, or undefined if it is not simulated. */
  tierAt(dim, blockX, blockZ) {
    return this.active.get(regionKey(dim, toChunk(blockX), toChunk(blockZ)))?.tier;
  }

  /** Write the current time into every active region's record (before saving). */
  checkpoint(tick = this._lastTick) {
    for (const r of this.active.values()) this.store.touch(r.dim, r.cx, r.cz, tick);
  }

  stop() {
    this.checkpoint();
    this.active.clear();
    this.counts = { NEAR: 0, MEDIUM: 0, FAR: 0 };
    this._signature = "";
  }

  /** @param {import("../core/scheduler.js").UpdateContext} ctx */
  update(ctx) {
    this._lastTick = ctx.tick;
    const { nearRadius, mediumRadius, farRadius } = this.perf.profile;
    const players = this.getPlayers();
    let signature = `${nearRadius}/${mediumRadius}/${farRadius}`;
    for (const p of players) signature += `;${p.dim}|${toChunk(p.x)}|${toChunk(p.z)}`;

    this._sinceFull++;
    if (signature === this._signature && this._sinceFull < FULL_REFRESH_UPDATES) {
      this.skipped++;
      return 0;
    }
    this._signature = signature;
    this._sinceFull = 0;
    this.recomputes++;

    /** @type {Map<string, ActiveRegion>} */
    const next = new Map();
    /** regions found unloaded in this pass, so a second player does not re-check them */
    const unloaded = new Set();
    let ops = 0;

    for (const p of players) {
      const pcx = toChunk(p.x);
      const pcz = toChunk(p.z);
      for (let dx = -farRadius; dx <= farRadius; dx++) {
        const adx = dx < 0 ? -dx : dx;
        for (let dz = -farRadius; dz <= farRadius; dz++) {
          const adz = dz < 0 ? -dz : dz;
          const d = adx > adz ? adx : adz;
          const tier = d <= nearRadius ? LOD.NEAR : d <= mediumRadius ? LOD.MEDIUM : LOD.FAR;
          const cx = pcx + dx;
          const cz = pcz + dz;
          const key = regionKey(p.dim, cx, cz);
          const existing = next.get(key);
          if (existing) {
            if (TIER_RANK[tier] < TIER_RANK[existing.tier]) existing.tier = tier;
            continue;
          }
          if (unloaded.has(key)) continue;
          // Regions already active were loaded a moment ago; only new ones are checked.
          if (!this.active.has(key)) {
            ops++;
            if (!this.isLoaded(p.dim, cx * 16 + 8, cz * 16 + 8)) {
              unloaded.add(key);
              continue;
            }
          }
          // Keep the same object for regions that stay active: other systems hold
          // references to it (e.g. their last simulation tick).
          const prev = this.active.get(key);
          if (prev) {
            prev.tier = tier;
            next.set(key, prev);
          } else {
            next.set(key, { dim: p.dim, cx, cz, tier, record: /** @type {any} */ (null) });
          }
        }
      }
    }

    for (const [key, r] of next) {
      if (this.active.has(key)) continue;
      ops++;
      const { record, awayTicks } = this.store.activate(r.dim, r.cx, r.cz, ctx.tick);
      r.record = record;
      for (const fn of this.enterListeners) fn(r);
      if (awayTicks > 0) {
        this.resumedTotal++;
        this.lastResumeAway = awayTicks;
        for (const fn of this.resumeListeners) fn(r, awayTicks);
      }
    }
    for (const [key, r] of this.active) {
      if (next.has(key)) continue;
      ops++;
      this.store.touch(r.dim, r.cx, r.cz, ctx.tick); // left the simulated area now
    }
    this.active = next;

    this.counts = { NEAR: 0, MEDIUM: 0, FAR: 0 };
    const inUse = new Set();
    for (const r of next.values()) {
      this.counts[/** @type {"NEAR"|"MEDIUM"|"FAR"} */ (r.tier)]++;
      inUse.add(sectorKeyOf(r.dim, r.cx, r.cz));
    }
    this.store.evict(inUse);
    return ops;
  }

  describe() {
    return {
      near: this.counts.NEAR,
      medium: this.counts.MEDIUM,
      far: this.counts.FAR,
      cachedSectors: this.store.sectors.size,
      dirtySectors: this.store.dirtyCount(),
      resumed: this.resumedTotal,
      lastResumeAwayTicks: this.lastResumeAway,
      recomputes: this.recomputes,
      skipped: this.skipped,
    };
  }
}
