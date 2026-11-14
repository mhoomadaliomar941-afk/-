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
