// Central engine loop. Each game tick it runs the systems that are due, oldest
// first, until the profile's time budget is spent; the rest wait for the next
// tick. A bounded queue holds deferred work. A system that keeps throwing is
// switched off ("faulted") instead of breaking the others.

import { ema, round } from "../utils/math.js";

/**
 * @typedef {{
 *   tick: number,          // current engine tick
 *   deltaTicks: number,    // ticks since this system last ran
 *   timeLeftMs: () => number, // budget remaining in this tick
 * }} UpdateContext
 *
 * @typedef {{
 *   id: string,
 *   name: string,
 *   intervalTicks: number,
 *   scalable?: boolean,   // interval stretched by profile and throttling
 *   start?: () => void,
 *   stop?: () => void,
 *   update: (ctx: UpdateContext) => number | void,  // returns operations done
 *   describe?: () => Record<string, string | number | boolean>,
 * }} SimSystem
 */

export const MAX_CONSECUTIVE_ERRORS = 5;

export class Scheduler {
  /**
   * @param {{
   *   perf: import("./performance.js").PerformanceManager,
   *   logger: import("./logger.js").Logger,
   *   clock?: () => number,
   *   startTick?: number,  // persisted simulation time, so it keeps counting across sessions
   * }} deps
   */
  constructor({ perf, logger, clock, startTick }) {
    this.perf = perf;
    this.logger = logger;
    this.clock = clock ?? (() => Date.now());
    /** @type {Map<string, ReturnType<Scheduler["_entry"]>>} */
    this.systems = new Map();
    /** @type {{ key: string, owner: string, fn: (timeLeftMs: () => number) => boolean }[]} */
    this.queue = [];
    this.queueKeys = new Set();
    this.runCounter = 0;
    this.paused = false;
    /** Simulation tick. Advances even while paused so stored timestamps stay monotonic. */
    this.ticks = Number.isInteger(startTick) && /** @type {number} */ (startTick) >= 0 ? /** @type {number} */ (startTick) : 0;
    this.stats = { deferredRuns: 0, queueRejected: 0, queueDone: 0, lastTickMs: 0 };
  }

  /** @param {SimSystem} system */
  _entry(system) {
    return {
      system,
      enabled: false,
      faulted: false,
      nextDue: 0,
      lastRun: -1,
      runSeq: 0, // global run order, used to break ties fairly
      runs: 0,
      errors: 0,
      consecutiveErrors: 0,
      lastOps: 0,
      totalOps: 0,
      lastMs: 0,
      avgMs: /** @type {number | undefined} */ (undefined),
      lastError: "",
    };
  }

  /** @param {SimSystem} system @param {boolean} enabled */
  register(system, enabled) {
    if (this.systems.has(system.id)) throw new Error(`system "${system.id}" registered twice`);
    if (!(system.intervalTicks >= 1)) throw new Error(`system "${system.id}" needs intervalTicks >= 1`);
    this.systems.set(system.id, this._entry(system));
    if (enabled) this.setEnabled(system.id, true);
  }

  /** Turn a system on/off. Turning on also clears a fault. */
  setEnabled(id, enabled) {
    const e = this.systems.get(id);
    if (!e) return false;
    if (enabled && (!e.enabled || e.faulted)) {
      e.faulted = false;
      e.consecutiveErrors = 0;
      try {
        e.system.start?.();
        e.enabled = true;
        e.nextDue = this.ticks;
      } catch (err) {
        e.enabled = false;
        e.faulted = true;
        e.lastError = String(/** @type {any} */ (err)?.message ?? err);
        this.logger.error(`system.${id}`, "failed to start", err);
        return false;
      }
    } else if (!enabled && e.enabled) {
      e.enabled = false;
      try {
        e.system.stop?.();
      } catch (err) {
        this.logger.error(`system.${id}`, "failed to stop cleanly", err);
      }
      this._dropQueued(id);
    }
    return true;
  }

  pause() {
    this.paused = true;
  }

  resume() {
    this.paused = false;
  }

  /**
   * Queue deferred work. fn gets the time left in this tick and returns true
   * when finished, false to be called again later (lets long jobs be split
   * across ticks). Duplicate keys are ignored.
   * @returns {boolean} false if rejected (queue full or duplicate)
   */
  enqueue(key, owner, fn) {
    if (this.queueKeys.has(key)) return false;
    if (this.queue.length >= this.perf.profile.maxQueuedTasks) {
      this.stats.queueRejected++;
      return false;
    }
    this.queue.push({ key, owner, fn });
    this.queueKeys.add(key);
    return true;
  }

  _dropQueued(owner) {
    this.queue = this.queue.filter((t) => {
      if (t.owner !== owner) return true;
      this.queueKeys.delete(t.key);
      return false;
    });
  }

  /** Run one engine tick. Call once per game tick. */
  tick() {
    const tick = this.ticks++;
    if (this.paused) return;
    const start = this.clock();
    const budget = this.perf.profile.tickBudgetMs;
    const timeLeftMs = () => budget - (this.clock() - start);

    const due = [];
    for (const e of this.systems.values()) {
      if (e.enabled && !e.faulted && tick >= e.nextDue) due.push(e);
    }
    // Most overdue first; on a tie, the one that ran least recently.
    due.sort((a, b) => a.nextDue - b.nextDue || a.runSeq - b.runSeq);

    let ran = 0;
    for (const e of due) {
      // Always run at least one due system per tick so nothing starves forever.
      if (ran > 0 && timeLeftMs() <= 0) {
        this.stats.deferredRuns += due.length - ran;
        break;
      }
      ran++;
      this._runSystem(e, tick, timeLeftMs);
    }

    while (this.queue.length > 0 && timeLeftMs() > 0) {
      const task = /** @type {(typeof this.queue)[number]} */ (this.queue.shift());
      let done = true;
      try {
        done = task.fn(timeLeftMs) !== false;
      } catch (err) {
        this.logger.error(`queue.${task.owner}`, `task ${task.key} failed`, err);
      }
      if (done) {
        this.queueKeys.delete(task.key);
        this.stats.queueDone++;
      } else {
        this.queue.push(task);
      }
    }

    const spent = this.clock() - start;
    this.stats.lastTickMs = spent;
    this.perf.recordTick(spent);
  }

  /** @param {ReturnType<Scheduler["_entry"]>} e */
  _runSystem(e, tick, timeLeftMs) {
    const s = e.system;
    const t0 = this.clock();
    try {
      const ops = s.update({ tick, deltaTicks: e.lastRun < 0 ? 0 : tick - e.lastRun, timeLeftMs });
      e.lastOps = typeof ops === "number" ? ops : 0;
      e.totalOps += e.lastOps;
      e.consecutiveErrors = 0;
    } catch (err) {
      e.errors++;
      e.consecutiveErrors++;
      e.lastError = String(/** @type {any} */ (err)?.message ?? err);
      this.logger.error(`system.${s.id}`, "update failed", err);
      if (e.consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) {
        e.faulted = true;
        this.logger.error(`system.${s.id}`, `disabled after ${MAX_CONSECUTIVE_ERRORS} errors in a row; re-enable it from the control center`);
      }
    }
    const dt = this.clock() - t0;
    e.lastMs = dt;
    e.avgMs = ema(e.avgMs, dt, 0.1);
    e.runs++;
    e.runSeq = ++this.runCounter;
    e.lastRun = tick;
    const interval = s.scalable ? this.perf.scaledInterval(s.intervalTicks) : s.intervalTicks;
    e.nextDue = tick + interval;
  }

  /** Read-only view for UI and tests. */
  list() {
    return [...this.systems.values()].map((e) => ({
      id: e.system.id,
      name: e.system.name,
      enabled: e.enabled,
      faulted: e.faulted,
      state: e.faulted ? "FAULTED" : e.enabled ? "RUNNING" : "OFF",
      intervalTicks: e.system.scalable ? this.perf.scaledInterval(e.system.intervalTicks) : e.system.intervalTicks,
      runs: e.runs,
      errors: e.errors,
      lastError: e.lastError,
      lastOps: e.lastOps,
      totalOps: e.totalOps,
      lastMs: round(e.lastMs, 3),
      avgMs: round(e.avgMs ?? 0, 3),
      details: e.system.describe?.() ?? {},
    }));
  }

  queueSize() {
    return this.queue.length;
  }
}
