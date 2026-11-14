import test from "node:test";
import assert from "node:assert/strict";
import { Scheduler, MAX_CONSECUTIVE_ERRORS } from "../RealisticWorld_BP/scripts/core/scheduler.js";
import { PerformanceManager, PROFILES, suggestProfile, MAX_THROTTLE } from "../RealisticWorld_BP/scripts/core/performance.js";
import { Logger } from "../RealisticWorld_BP/scripts/core/logger.js";

const quiet = { info() {}, warn() {}, error() {} };

/** Fake clock: systems advance it to simulate work. */
function setup(profile = "MEDIUM") {
  const clock = { now: 0 };
  const perf = new PerformanceManager(profile);
  const logger = new Logger({ sink: quiet });
  const sched = new Scheduler({ perf, logger, clock: () => clock.now });
  return { clock, perf, logger, sched };
}

const sys = (id, intervalTicks, update, extra = {}) => ({ id, name: id, intervalTicks, update, ...extra });

test("systems run on their interval and get deltaTicks", () => {
  const { sched } = setup();
  const runs = [];
  sched.register(sys("a", 5, (ctx) => runs.push([ctx.tick, ctx.deltaTicks])), true);
  for (let i = 0; i < 16; i++) sched.tick();
  assert.deepEqual(runs, [[0, 0], [5, 5], [10, 5], [15, 5]]);
});

test("disabled systems do not run; enabling starts them", () => {
  const { sched } = setup();
  let n = 0;
  let started = 0;
  sched.register(sys("a", 1, () => n++, { start: () => started++ }), false);
  sched.tick();
  assert.equal(n, 0);
  sched.setEnabled("a", true);
  sched.tick();
  assert.equal(n, 1);
  assert.equal(started, 1);
});

test("budget: work beyond the budget is deferred, nothing starves", () => {
  const { clock, sched } = setup("LOW"); // 2 ms budget
  const counts = { a: 0, b: 0, c: 0 };
  for (const id of ["a", "b", "c"]) {
    sched.register(sys(id, 1, () => { counts[id]++; clock.now += 1.5; }), true);
  }
  for (let i = 0; i < 30; i++) sched.tick();
  // each tick fits 2 systems (1.5 + 1.5 > 2 stops after the second)
  assert.ok(sched.stats.deferredRuns > 0);
  for (const id of ["a", "b", "c"]) assert.ok(counts[id] >= 15, `${id} ran ${counts[id]} times`);
  assert.ok(Math.max(...Object.values(counts)) - Math.min(...Object.values(counts)) <= 1, "fair rotation");
});

test("a failing system is faulted after repeated errors; others keep running", () => {
  const { sched, logger } = setup();
  let good = 0;
  sched.register(sys("bad", 1, () => { throw new Error("boom"); }), true);
  sched.register(sys("good", 1, () => good++), true);
  for (let i = 0; i < 10; i++) sched.tick();
  const bad = sched.list().find((s) => s.id === "bad");
  assert.equal(bad.state, "FAULTED");
  assert.equal(bad.errors, MAX_CONSECUTIVE_ERRORS);
  assert.equal(good, 10);
  assert.ok(logger.errors.some((e) => e.message.includes("disabled after")));
  // re-enabling clears the fault
  sched.setEnabled("bad", true);
  assert.equal(sched.list().find((s) => s.id === "bad").state, "RUNNING");
});

test("a system whose start() throws is faulted, not crashing registration", () => {
  const { sched } = setup();
  sched.register(sys("x", 1, () => 0, { start: () => { throw new Error("no"); } }), true);
  assert.equal(sched.list()[0].state, "FAULTED");
});

test("queue: capped, de-duplicated, multi-step tasks continue later", () => {
  const { sched, perf } = setup("LOW");
  let steps = 0;
  assert.equal(sched.enqueue("job", "t", () => ++steps >= 3), true);
  assert.equal(sched.enqueue("job", "t", () => true), false, "duplicate key rejected");
  for (let i = 0; i < perf.profile.maxQueuedTasks + 5; i++) sched.enqueue(`k${i}`, "t", () => true);
  assert.equal(sched.queueSize(), perf.profile.maxQueuedTasks);
  assert.ok(sched.stats.queueRejected >= 6);
  for (let i = 0; i < 5; i++) sched.tick();
  assert.equal(steps, 3);
  assert.equal(sched.queueSize(), 0);
});

test("disabling a system drops its queued tasks", () => {
  const { sched } = setup();
  sched.register(sys("a", 100, () => 0), true);
  sched.enqueue("x", "a", () => true);
  sched.setEnabled("a", false);
  assert.equal(sched.queueSize(), 0);
});

test("paused engine runs nothing but the clock still advances", () => {
  const { sched } = setup();
  let n = 0;
  sched.register(sys("a", 1, () => n++), true);
  sched.pause();
  sched.tick();
  sched.tick();
  assert.equal(n, 0);
  assert.equal(sched.ticks, 2);
  sched.resume();
  sched.tick();
  assert.equal(n, 1);
});

test("startTick continues the saved simulation clock", () => {
  const perf = new PerformanceManager();
  const s = new Scheduler({ perf, logger: new Logger({ sink: quiet }), startTick: 5000 });
  s.tick();
  assert.equal(s.ticks, 5001);
});

test("scalable intervals follow profile and throttle", () => {
  const { sched, perf } = setup("LOW"); // intervalScale 2
  sched.register(sys("s", 10, () => 0, { scalable: true }), true);
  assert.equal(sched.list()[0].intervalTicks, 20);
  perf.throttle = 2;
  assert.equal(sched.list()[0].intervalTicks, 40);
});

test("adaptive throttle rises under load and falls when idle", () => {
  const perf = new PerformanceManager("MEDIUM"); // 4 ms
  for (let i = 0; i < 40; i++) perf.recordTick(8);
  assert.equal(perf.throttle, 2);
  for (let i = 0; i < 400; i++) perf.recordTick(8);
  assert.equal(perf.throttle, MAX_THROTTLE, "never above the cap");
  for (let i = 0; i < 40 * 3; i++) perf.recordTick(0.2);
  assert.equal(perf.throttle, 1);
  perf.setAdaptive(false);
  for (let i = 0; i < 80; i++) perf.recordTick(8);
  assert.equal(perf.throttle, 1, "no throttling when adaptive is off");
});

test("profiles and memory-tier suggestion", () => {
  for (const p of Object.values(PROFILES)) {
    assert.ok(p.nearRadius < p.mediumRadius && p.mediumRadius < p.farRadius);
  }
  assert.ok(PROFILES.LOW.tickBudgetMs < PROFILES.MEDIUM.tickBudgetMs && PROFILES.MEDIUM.tickBudgetMs < PROFILES.ULTRA.tickBudgetMs);
  assert.equal(suggestProfile(0), "LOW");
  assert.equal(suggestProfile(1), "LOW");
  assert.equal(suggestProfile(4), "MEDIUM", "ULTRA is never picked automatically");
  assert.equal(suggestProfile(undefined), "MEDIUM");
});
