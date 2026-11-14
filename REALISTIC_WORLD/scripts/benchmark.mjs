#!/usr/bin/env node
// Performance benchmark of the engine core, run in Node with an in-memory
// world. It measures script time per tick for each profile and player count,
// region counts, save time and stored data size, walking players across the
// map so new regions keep loading.
//
// Numbers are from this computer, not a phone. Use them to compare profiles
// and catch regressions; on Android check the in-game HUD / Dashboard instead.

import { createRuntime } from "../RealisticWorld_BP/scripts/core/runtime.js";
import { MemoryBackend } from "../RealisticWorld_BP/scripts/core/storage.js";

const TICKS = 2400; // 2 minutes of game time
const quiet = { info() {}, warn() {}, error() {} };

function run(profile, playerCount) {
  const backend = new MemoryBackend();
  const players = Array.from({ length: playerCount }, (_, i) => ({ dim: "minecraft:overworld", x: i * 500, z: 0 }));
  let time = 0;
  const biomes = ["minecraft:plains", "minecraft:forest", "minecraft:desert", "minecraft:river", "minecraft:taiga"];
  const host = {
    backend,
    memoryTier: 2,
    getPlayerPositions: () => players,
    isChunkLoaded: () => true,
    getHudPlayers: () =>
      players.map((p) => ({
        name: "p", hudEnabled: () => true, weatherHudEnabled: () => true,
        position: () => ({ ...p, y: 70 }), show: () => {},
      })),
    worldSeed: "bench",
    setVanillaWeather: () => {},
    getBiomeId: (d, x, y, z) => biomes[Math.abs(Math.floor(x / 160) + Math.floor(z / 160)) % biomes.length],
    getSurfaceY: () => 70,
    getTimeOfDay: () => time,
    getFogPlayers: () => players.map((p, i) => ({ id: String(i), ...p, y: 70, outdoors: () => true, setFog: () => {} })),
    clock: () => performance.now(),
    log: quiet,
  };
  const rt = createRuntime(host);
  rt.config.set("profile", profile);
  rt.config.set("adaptive", false); // measure raw cost

  const times = [];
  let maxActive = 0;
  for (let t = 0; t < TICKS; t++) {
    for (const p of players) p.x += 0.25; // walking: ~5 blocks/s
    time = (time + 1) % 24000;
    const t0 = performance.now();
    rt.tick();
    times.push(performance.now() - t0);
    const r = rt.tracker.counts;
    maxActive = Math.max(maxActive, r.NEAR + r.MEDIUM + r.FAR);
  }
  const s0 = performance.now();
  rt.saveNow(Infinity);
  const saveMs = performance.now() - s0;
  times.sort((a, b) => a - b);
  const avg = times.reduce((a, b) => a + b, 0) / times.length;
  const sys = (id) => rt.scheduler.list().find((s) => s.id === id);
  const trackerRuns = sys("regions");
  return {
    profile,
    players: playerCount,
    avgTickMs: avg.toFixed(4),
    p99TickMs: times[Math.floor(times.length * 0.99)].toFixed(4),
    maxTickMs: times[times.length - 1].toFixed(4),
    trackerAvgMs: trackerRuns.avgMs,
    weatherAvgMs: sys("weather").avgMs,
    climateAvgMs: sys("climate").avgMs,
    fogHudAvgMs: Math.round((sys("fog").avgMs + sys("hud").avgMs) * 1000) / 1000,
    activeRegions: maxActive,
    sectorsInMemory: rt.regionStore.sectors.size,
    fullSaveMs: saveMs.toFixed(3),
    storedKB: (backend.totalBytes() / 1024).toFixed(1),
    errors: rt.logger.errors.length,
  };
}

const rows = [];
for (const profile of ["LOW", "MEDIUM", "ULTRA"]) {
  for (const n of [1, 4, 10]) rows.push(run(profile, n));
}
console.table(rows);

// ---- water under load: leaking tanks + heavy rain on soaked ground ----
function runWater(profile, tankCount) {
  const cells = new Map();
  const k = (x, y, z) => `${x},${y},${z}`;
  const surface = (x, z) => 64 + Math.round(2 * Math.sin(x / 7) + 2 * Math.cos(z / 9)); // gentle hills
  const typeAt = (x, y, z) => cells.get(k(x, y, z)) ?? (y <= surface(x, z) ? "minecraft:grass_block" : "minecraft:air");
  const set = (x, y, z, t) => cells.set(k(x, y, z), t);
  const tanks = [];
  for (let i = 0; i < tankCount; i++) {
    const x0 = (i % 2) * 20 - 10;
    const z0 = Math.floor(i / 2) * 20 - 10;
    const floor = Math.max(...[0, 1, 2, 3, 4].flatMap((dx) => [0, 1, 2, 3, 4].map((dz) => surface(x0 + dx, z0 + dz))));
    for (let y = floor + 1; y <= floor + 5; y++) {
      for (let x = x0 - 1; x <= x0 + 5; x++) for (let z = z0 - 1; z <= z0 + 5; z++) {
        const wall = x === x0 - 1 || x === x0 + 5 || z === z0 - 1 || z === z0 + 5;
        if (wall) set(x, y, z, "minecraft:stone");
        else if (y <= floor + 4) set(x, y, z, "minecraft:water");
      }
    }
    for (let x = x0; x <= x0 + 4; x++) for (let z = z0; z <= z0 + 4; z++) for (let y = surface(x, z) + 1; y <= floor; y++) set(x, y, z, "minecraft:stone");
    tanks.push({ hole: [x0 - 1, floor + 2, z0 + 2], seed: [x0 + 2, floor + 2, z0 + 2] });
  }
  const host = {
    backend: new MemoryBackend(), memoryTier: 2,
    getPlayerPositions: () => [{ dim: "minecraft:overworld", x: 0, z: 0 }],
    isChunkLoaded: () => true, getHudPlayers: () => [], worldSeed: "bench", setVanillaWeather: () => {},
    getBiomeId: () => "minecraft:plains", getSurfaceY: (d, x, z) => surface(x, z), getTimeOfDay: () => 6000, getFogPlayers: () => [],
    water: {
      cellAt: (d, x, y, z) => {
        const t = typeAt(x, y, z);
        return t === "minecraft:water" ? "source" : t === "minecraft:air" ? "air" : "solid";
      },
      columnAt: (d, x, z) => {
        for (let y = 120; y >= 0; y--) {
          const t = typeAt(x, y, z);
          if (t !== "minecraft:air") return { base: y + 1, kind: t === "minecraft:water" ? "water" : "ground", ground: t };
        }
        return null;
      },
      fill: (d, list, type, from) => {
        for (const c of list) if (from.includes(typeAt(c.x, c.y, c.z))) set(c.x, c.y, c.z, type);
      },
      isLoaded: () => true,
    },
    clock: () => performance.now(), log: quiet,
  };
  const rt = createRuntime(host);
  rt.config.set("profile", profile);
  rt.config.set("adaptive", false);
  for (let i = 0; i < 200; i++) rt.tick();
  for (const r of rt.tracker.active.values()) if (r.record?.data?.c) r.record.data.c[0] = 950;
  rt.weather.force("HEAVY_RAIN", 1e6, rt.scheduler.ticks);
  for (const t of tanks) {
    set(...t.hole, "minecraft:air");
    rt.water.onBlockRemoved("minecraft:overworld", ...t.hole);
  }
  const times = [];
  for (let t = 0; t < TICKS; t++) {
    const t0 = performance.now();
    rt.tick();
    times.push(performance.now() - t0);
  }
  times.sort((a, b) => a - b);
  const d = rt.water.describe();
  return {
    profile, tanks: tankCount,
    avgTickMs: (times.reduce((a, b) => a + b, 0) / times.length).toFixed(4),
    p99TickMs: times[Math.floor(times.length * 0.99)].toFixed(4),
    maxTickMs: times[times.length - 1].toFixed(4),
    waterAvgMs: rt.scheduler.list().find((s) => s.id === "water").avgMs,
    pools: d.pools, liters: d.liters, blockOps: d.blockOps, layerOps: rt.water.stats.layerOps,
    runoffM3: Math.round(rt.water.stats.runoff), errors: rt.logger.errors.length,
  };
}

const waterRows = [];
for (const profile of ["LOW", "MEDIUM", "ULTRA"]) for (const n of [1, 4]) waterRows.push(runWater(profile, n));
console.log("\nWater under load (leaking tanks + heavy rain on soaked ground):");
console.table(waterRows);
