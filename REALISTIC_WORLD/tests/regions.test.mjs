import test from "node:test";
import assert from "node:assert/strict";
import { MemoryBackend, Storage } from "../RealisticWorld_BP/scripts/core/storage.js";
import { Logger } from "../RealisticWorld_BP/scripts/core/logger.js";
import { PerformanceManager } from "../RealisticWorld_BP/scripts/core/performance.js";
import { RegionStore, RegionTracker, LOD } from "../RealisticWorld_BP/scripts/simulation/regions.js";

const quiet = { info() {}, warn() {}, error() {} };
const OW = "minecraft:overworld";

function setup({ backend = new MemoryBackend(), profile = "MEDIUM", players = [{ dim: OW, x: 8, z: 8 }], loaded = () => true } = {}) {
  const logger = new Logger({ sink: quiet });
  const storage = new Storage(backend, "rw:");
  const perf = new PerformanceManager(profile);
  const store = new RegionStore(storage, logger, perf.profile.maxCachedRegions);
  const state = { players };
  const tracker = new RegionTracker({ store, perf, getPlayers: () => state.players, isLoaded: (d, x, z) => loaded(d, x, z) });
  return { backend, store, tracker, state, perf };
}

test("LOD rings around one player (MEDIUM: 2/4/6 chunks)", () => {
  const { tracker } = setup();
  tracker.update({ tick: 100, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.equal(tracker.counts.NEAR, 5 * 5);
  assert.equal(tracker.counts.MEDIUM, 9 * 9 - 5 * 5);
  assert.equal(tracker.counts.FAR, 13 * 13 - 9 * 9);
  assert.equal(tracker.tierAt(OW, 8, 8), LOD.NEAR);
  assert.equal(tracker.tierAt(OW, 16 * 3, 0), LOD.MEDIUM);
  assert.equal(tracker.tierAt(OW, 16 * 6, 0), LOD.FAR);
  assert.equal(tracker.tierAt(OW, 16 * 7, 0), undefined);
});

test("LOW profile simulates far fewer regions", () => {
  const { tracker } = setup({ profile: "LOW" });
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  const total = tracker.counts.NEAR + tracker.counts.MEDIUM + tracker.counts.FAR;
  assert.equal(total, 9 * 9);
});

test("unloaded chunks are never simulated", () => {
  const { tracker } = setup({ loaded: (d, x) => x < 0 });
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  for (const r of tracker.active.values()) assert.ok(r.cx < 0);
});

test("two players: overlapping regions take the most detailed tier", () => {
  const { tracker } = setup({ players: [{ dim: OW, x: 0, z: 0 }, { dim: OW, x: 16 * 5, z: 0 }] });
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.equal(tracker.tierAt(OW, 16 * 5, 0), LOD.NEAR);
  assert.equal(tracker.tierAt(OW, 0, 0), LOD.NEAR);
});

test("dimensions are kept apart", () => {
  const { tracker } = setup({ players: [{ dim: OW, x: 0, z: 0 }, { dim: "minecraft:nether", x: 0, z: 0 }] });
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.equal(tracker.tierAt("minecraft:nether", 0, 0), LOD.NEAR);
  assert.equal(tracker.counts.NEAR, 50);
});

test("returning to a region reports how long it was away", () => {
  const { tracker, state } = setup();
  const resumed = [];
  tracker.onResume((r, away) => resumed.push([r.cx, r.cz, away]));
  tracker.update({ tick: 100, deltaTicks: 0, timeLeftMs: () => 1 });
  state.players = [{ dim: OW, x: 16 * 100, z: 0 }]; // walk far away
  tracker.update({ tick: 200, deltaTicks: 100, timeLeftMs: () => 1 });
  state.players = [{ dim: OW, x: 8, z: 8 }]; // come back
  tracker.update({ tick: 1300, deltaTicks: 1100, timeLeftMs: () => 1 });
  const home = resumed.find(([cx, cz]) => cx === 0 && cz === 0);
  assert.ok(home, "home region resumed");
  assert.equal(home[2], 1100, "simulated until the player left at tick 200, back at 1300");
});

test("region records survive save and a fresh load (world reload)", () => {
  const first = setup();
  first.tracker.update({ tick: 500, deltaTicks: 0, timeLeftMs: () => 1 });
  first.tracker.stop();
  const saved = first.store.flush();
  assert.ok(saved > 0);
  assert.equal(first.store.dirtyCount(), 0);

  const second = setup({ backend: first.backend });
  const resumed = [];
  second.tracker.onResume((r, away) => resumed.push(away));
  second.tracker.update({ tick: 900, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.equal(resumed.length, 13 * 13, "every known region resumed");
  assert.ok(resumed.every((a) => a === 400), "away time = 900 - 500");
  const rec = second.store.get(OW, 0, 0);
  assert.equal(rec.visits, 2);
  assert.equal(rec.created, 500);
});

test("flush is capped per call", () => {
  const { tracker, store } = setup();
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  const dirty = store.dirtyCount();
  assert.ok(dirty > 2);
  assert.equal(store.flush(2), 2);
  assert.equal(store.dirtyCount(), dirty - 2);
});

test("memory stays bounded while travelling, without any manual flush", () => {
  const { tracker, store, state, backend } = setup({ profile: "LOW" });
  const visited = [];
  for (let step = 0; step < 60; step++) {
    state.players = [{ dim: OW, x: step * 16 * 10, z: 0 }];
    visited.push(step * 10);
    tracker.update({ tick: step * 20, deltaTicks: 20, timeLeftMs: () => 1 });
    assert.ok(store.sectors.size <= store.maxSectors + 2, `step ${step}: ${store.sectors.size} sectors in memory`);
  }
  assert.ok(store.stats.evictions > 0);
  assert.ok(store.stats.forcedSaves > 0, "evicted unsaved sectors were saved first");
  // Every region visited early on is still on disk after eviction.
  const fresh = setup({ backend, profile: "LOW" });
  for (const cx of visited.slice(0, 20)) assert.ok(fresh.store.get(OW, cx, 0), `region ${cx},0 lost`);
});

test("idle updates skip the recompute; moving inside a chunk does not trigger it", () => {
  const { tracker, state } = setup();
  tracker.update({ tick: 0, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.equal(tracker.recomputes, 1);
  state.players = [{ dim: OW, x: 15, z: 3 }]; // same chunk 0,0
  for (let i = 1; i < 5; i++) assert.equal(tracker.update({ tick: i * 20, deltaTicks: 20, timeLeftMs: () => 1 }), 0);
  assert.equal(tracker.recomputes, 1);
  state.players = [{ dim: OW, x: 17, z: 3 }]; // chunk 1,0
  tracker.update({ tick: 200, deltaTicks: 20, timeLeftMs: () => 1 });
  assert.equal(tracker.recomputes, 2);
});

test("periodic full refresh picks up chunks that finished loading", () => {
  let loadedNow = false;
  const { tracker } = setup({ loaded: (d, x) => x < 64 || loadedNow });
  tracker.update({ tick: 0, deltaTicks: 0, timeLeftMs: () => 1 });
  const before = tracker.active.size;
  loadedNow = true;
  for (let i = 1; i <= 10; i++) tracker.update({ tick: i * 20, deltaTicks: 20, timeLeftMs: () => 1 });
  assert.ok(tracker.active.size > before, "late-loaded chunks joined the active set");
  assert.equal(tracker.active.size, 13 * 13);
});

test("checkpoint records active time so a crash does not inflate away time", () => {
  const first = setup();
  first.tracker.update({ tick: 100, deltaTicks: 0, timeLeftMs: () => 1 });
  first.tracker.checkpoint(5000); // periodic checkpoint while the player stays
  first.store.flush();
  // world closes without stop() (crash); reload at 5100
  const second = setup({ backend: first.backend });
  const away = [];
  second.tracker.onResume((r, a) => away.push(a));
  second.tracker.update({ tick: 5100, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.ok(away.every((a) => a === 100), "away measured from the checkpoint, not from tick 100");
});

test("corrupt sector is started fresh with a warning, not a crash", () => {
  const { backend, tracker, store } = setup();
  tracker.update({ tick: 1, deltaTicks: 0, timeLeftMs: () => 1 });
  store.flush();
  for (const id of backend.ids()) if (id.includes("#")) backend.set(id, "broken");
  const again = setup({ backend });
  again.tracker.update({ tick: 2, deltaTicks: 0, timeLeftMs: () => 1 });
  assert.ok(again.store.stats.corruptSectors > 0);
  assert.equal(again.tracker.counts.NEAR, 25);
});
