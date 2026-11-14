import test from "node:test";
import assert from "node:assert/strict";
import { MemoryBackend, Storage } from "../RealisticWorld_BP/scripts/core/storage.js";
import { Logger } from "../RealisticWorld_BP/scripts/core/logger.js";
import { Config } from "../RealisticWorld_BP/scripts/core/config.js";
import { PerformanceManager } from "../RealisticWorld_BP/scripts/core/performance.js";
import { RegionStore, RegionTracker } from "../RealisticWorld_BP/scripts/simulation/regions.js";
import { WeatherSystem, VANILLA_REFRESH, EXTERNAL_FORCE_TICKS } from "../RealisticWorld_BP/scripts/weather/weatherSystem.js";
import { ClimateSystem, TIER_INTERVAL } from "../RealisticWorld_BP/scripts/weather/climateSystem.js";
import { FogSystem, FOG_REFRESH_TICKS } from "../RealisticWorld_BP/scripts/weather/fogSystem.js";
import { RAIN_FREQUENCY } from "../RealisticWorld_BP/scripts/weather/model.js";

const quiet = { info() {}, warn() {}, error() {} };
const OW = "minecraft:overworld";
const ctx = (tick, deltaTicks = 100, timeLeftMs = () => 10) => ({ tick, deltaTicks, timeLeftMs });

function world({ backend = new MemoryBackend(), biome = () => "minecraft:plains", loaded = () => true, players = [{ dim: OW, x: 8, z: 8 }] } = {}) {
  const logger = new Logger({ sink: quiet });
  const storage = new Storage(backend, "rw:");
  const config = new Config(storage, logger);
  config.define("weather.rainFrequency", { type: "enum", values: Object.keys(RAIN_FREQUENCY), default: "NORMAL", label: "" });
  config.define("weather.syncVanilla", { type: "bool", default: true, label: "" });
  config.load();
  const perf = new PerformanceManager("MEDIUM");
  const store = new RegionStore(storage, logger, 512);
  const tracker = new RegionTracker({ store, perf, getPlayers: () => players, isLoaded: loaded });
  const vanilla = [];
  const weather = new WeatherSystem({ storage, config, logger, setVanillaWeather: (t, d) => vanilla.push({ t, d }), worldSeed: "seed" });
  const lookups = { biome: 0, surface: 0 };
  const time = { now: 6000 };
  const climate = new ClimateSystem({
    tracker, store, weather, logger,
    getBiomeId: (d, x, y, z) => {
      lookups.biome++;
      return biome(x, z);
    },
    getSurfaceY: (d, x, z) => {
      lookups.surface++;
      if (!loaded(d, x, z)) throw new Error("unloaded");
      return 70;
    },
    getTimeOfDay: () => time.now,
  });
  return { backend, storage, config, logger, store, tracker, weather, climate, vanilla, lookups, time, players };
}

test("weather: vanilla weather follows the engine and is refreshed before it runs out", () => {
  const w = world();
  w.weather.force("THUNDERSTORM", 5000, 0);
  assert.equal(w.vanilla.at(-1).t, "Thunder");
  assert.ok(w.vanilla.at(-1).d > VANILLA_REFRESH, "duration outlasts the refresh interval");
  const n = w.vanilla.length;
  w.weather.update(ctx(100));
  assert.equal(w.vanilla.length, n, "no resend when nothing changed");
  w.weather.update(ctx(100 + VANILLA_REFRESH));
  assert.equal(w.vanilla.length, n + 1, "resent before the vanilla duration ends");
});

test("weather: its own change events are ignored, /weather from a player is followed", () => {
  const w = world();
  w.weather.force("CLEAR", 100000, 0);
  w.weather.onVanillaChange("Clear", 5); // echo of our own call
  assert.equal(w.weather.externalChanges, 0);
  w.weather.onVanillaChange("Rain", 500); // a player typed /weather rain
  assert.equal(w.weather.externalChanges, 1);
  w.weather.update(ctx(600));
  assert.equal(w.weather.summary().raining, true);
  assert.equal(w.weather.summary().forced.type, "MODERATE_RAIN");
  assert.ok(w.weather.summary().forced.ticksLeft <= EXTERNAL_FORCE_TICKS);
  assert.equal(w.vanilla.at(-1).t, "Clear", "the engine did not fight the player's command");
});

test("weather: with sync off nothing is sent and /weather is left to the game", () => {
  const w = world();
  w.config.set("weather.syncVanilla", false);
  w.weather.update(ctx(0));
  w.weather.force("HEAVY_RAIN", 1000, 0);
  w.weather.onVanillaChange("Thunder", 50);
  assert.equal(w.vanilla.length, 0);
  assert.equal(w.weather.externalChanges, 0);
});

test("weather: state is saved and restored; a corrupt save starts fresh", () => {
  const a = world();
  for (let t = 0; t < 30; t++) a.weather.update(ctx(t * 100));
  a.weather.force("FOG", 9000, 3000);
  assert.equal(a.weather.save(), true);
  assert.equal(a.weather.save(), false, "nothing new to save");
  const b = world({ backend: a.backend });
  assert.equal(b.weather.restored, true);
  assert.deepEqual(b.weather.state, a.weather.state);
  a.weather.update(ctx(3100));
  b.weather.update(ctx(3100));
  assert.deepEqual(b.weather.state, a.weather.state, "continues identically");

  a.backend.set("rw:weather@b#0", "{broken");
  a.backend.set("rw:weather@a#0", "{broken");
  const c = world({ backend: a.backend });
  assert.equal(c.weather.restored, false);
});

test("climate: first visit looks up biome and height once, data is stored per region", () => {
  const w = world();
  w.tracker.update(ctx(0, 0));
  for (let i = 0; i < 60; i++) w.climate.update(ctx(i * 20, 20));
  const active = w.tracker.active.size;
  assert.equal(w.climate.describe().regionsWithClimate, active);
  assert.equal(w.lookups.biome, active, "one biome lookup per region, never repeated");
  const rec = w.store.get(OW, 0, 0);
  assert.deepEqual(rec.data.c.slice(1), ["plains", 70]);
});

test("climate: rain raises soil moisture; near regions update more often than far ones", () => {
  const w = world();
  w.tracker.update(ctx(0, 0));
  for (let i = 0; i < 40; i++) w.climate.update(ctx(i * 20, 20)); // initialise everything
  const near = w.tracker.regionAt(OW, 8, 8);
  const far = w.tracker.regionAt(OW, 8 + 16 * 6, 8);
  assert.equal(near.tier, "NEAR");
  assert.equal(far.tier, "FAR");
  const soil0 = near.record.data.c[0];
  w.weather.force("HEAVY_RAIN", 100000, 0);
  const updates = { near: 0, far: 0 };
  let lastNear = near.lastSim;
  let lastFar = far.lastSim;
  for (let t = 0; t < 4000; t += 20) {
    w.weather.update(ctx(t, 20));
    w.climate.update(ctx(t, 20));
    if (near.lastSim !== lastNear) { updates.near++; lastNear = near.lastSim; }
    if (far.lastSim !== lastFar) { updates.far++; lastFar = far.lastSim; }
  }
  assert.ok(near.record.data.c[0] > soil0 + 100, `soil ${soil0} -> ${near.record.data.c[0]}`);
  assert.ok(updates.near >= 3 * updates.far, `near ${updates.near} vs far ${updates.far}`);
  assert.ok(updates.far >= 4000 / TIER_INTERVAL.FAR - 2);
});

test("climate: respects the time budget and continues where it stopped", () => {
  const w = world();
  w.tracker.update(ctx(0, 0));
  w.climate.update(ctx(0, 20, () => 0)); // no time left: still one region of progress
  assert.equal(w.climate.describe().regionsWithClimate, 1);
  w.climate.update(ctx(20, 20, () => 0));
  assert.equal(w.climate.describe().regionsWithClimate, 2);
});

test("climate: unloaded chunks are retried later, not marked", () => {
  let ready = false;
  const w = world({ loaded: (d, x) => x < 40 || ready });
  w.tracker.update(ctx(0, 0)); // only x<40 regions are active
  ready = true;
  w.tracker.update(ctx(20 * 10, 20)); // full refresh: more regions join
  for (let i = 0; i < 80; i++) w.climate.update(ctx(200 + i * 20, 20));
  assert.equal(w.climate.describe().regionsWithClimate, w.tracker.active.size);
});

test("climate: a region that was away is caught up in one step", () => {
  const w = world();
  w.tracker.update(ctx(0, 0));
  for (let i = 0; i < 60; i++) w.climate.update(ctx(i * 20, 20));
  const rec = w.store.get(OW, 0, 0);
  rec.data.c[0] = 50; // very dry soil
  w.players.length = 0;
  w.players.push({ dim: OW, x: 16 * 200, z: 0 }); // walk away
  w.tracker.update(ctx(2000, 20));
  w.weather.state.rainEma = 0.6; // it has been a rainy period
  w.players.length = 0;
  w.players.push({ dim: OW, x: 8, z: 8 }); // come back 10 in-game days later
  w.tracker.update(ctx(2000 + 240000, 20));
  assert.ok(rec.data.c[0] > 300, `soil after a rainy absence: ${rec.data.c[0]}`);
  assert.ok(w.climate.stats.caughtUp > 0);
});

test("climate: sample() gives local conditions; other dimensions have fixed climates", () => {
  const w = world({ biome: (x) => (x < 64 ? "minecraft:desert" : "minecraft:ice_plains") });
  w.tracker.update(ctx(0, 0));
  for (let i = 0; i < 60; i++) w.climate.update(ctx(i * 20, 20));
  w.time.now = 8000;
  const hot = w.climate.sample(OW, 8, 70, 8);
  const cold = w.climate.sample(OW, 16 * 5, 70, 8);
  assert.equal(hot.biome, "desert");
  assert.ok(hot.tempC > 30 && cold.tempC < 0, `${hot.tempC} / ${cold.tempC}`);
  assert.equal(hot.tier, "NEAR");
  assert.equal(w.climate.sample("minecraft:nether", 0, 0, 0).tempC, 45);
  assert.equal(w.climate.sample("mymod:dim", 0, 0, 0), null);
});

test("fog: applied outdoors in the overworld only, refreshed, removed when stopped", () => {
  const w = world({ biome: () => "minecraft:river" });
  w.tracker.update(ctx(0, 0));
  for (let i = 0; i < 60; i++) w.climate.update(ctx(i * 20, 20));
  w.weather.force("FOG", 100000, 0);
  w.weather.update(ctx(0, 500));
  const applied = { a: [], b: [], c: [] };
  const players = [
    { id: "a", dim: OW, x: 8, y: 70, z: 8, outdoors: () => true, setFog: (f) => applied.a.push(f) },
    { id: "b", dim: OW, x: 8, y: 20, z: 8, outdoors: () => false, setFog: (f) => applied.b.push(f) },
    { id: "c", dim: "minecraft:nether", x: 0, y: 70, z: 0, outdoors: () => true, setFog: (f) => applied.c.push(f) },
  ];
  const fog = new FogSystem({ getPlayers: () => players, climate: w.climate, logger: w.logger });
  fog.update(ctx(0));
  assert.equal(applied.a.at(-1), "rw:fog_dense");
  assert.equal(applied.b.at(-1), null, "underground: no weather fog");
  assert.equal(applied.c.at(-1), null);
  fog.update(ctx(40));
  assert.equal(applied.a.length, 1, "unchanged fog is not re-sent every update");
  fog.update(ctx(FOG_REFRESH_TICKS + 1));
  assert.equal(applied.a.length, 2, "re-sent after the refresh time");
  fog.stop();
  assert.equal(applied.a.at(-1), null);
});

test("fog: a failing fog call is logged, not thrown", () => {
  const w = world();
  const fog = new FogSystem({
    getPlayers: () => [{ id: "a", dim: OW, x: 0, y: 70, z: 0, outdoors: () => true, setFog: () => { throw new Error("FogSettingsError"); } }],
    climate: w.climate,
    logger: w.logger,
  });
  for (let i = 0; i < 10; i++) fog.update(ctx(i * 1000));
  assert.equal(fog.failures, 10);
  assert.equal(w.logger.errors.length, 3, "only the first few failures are logged");
});
