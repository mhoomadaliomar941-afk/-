// Water system through the real runtime with a small voxel world (no mocks of
// @minecraft/server needed: the runtime only talks to the host adapter).
import test from "node:test";
import assert from "node:assert/strict";
import { createRuntime } from "../RealisticWorld_BP/scripts/core/runtime.js";
import { MemoryBackend } from "../RealisticWorld_BP/scripts/core/storage.js";

const quiet = { info() {}, warn() {}, error() {} };
const OW = "minecraft:overworld";

function makeWorld(surface = () => 64) {
  const cells = new Map();
  const k = (x, y, z) => `${x},${y},${z}`;
  const typeAt = (x, y, z) => cells.get(k(x, y, z)) ?? (y <= surface(x, z) ? "minecraft:grass_block" : "minecraft:air");
  const set = (x, y, z, t) => cells.set(k(x, y, z), t);
  const host = {
    backend: new MemoryBackend(),
    memoryTier: 2,
    getPlayerPositions: () => [{ dim: OW, x: 8, z: 8 }],
    isChunkLoaded: () => true,
    getHudPlayers: () => [],
    worldSeed: "w",
    setVanillaWeather: () => {},
    getBiomeId: () => "minecraft:plains",
    getSurfaceY: (d, x, z) => surface(x, z),
    getTimeOfDay: () => 6000,
    getFogPlayers: () => [],
    water: {
      cellAt: (d, x, y, z) => {
        const t = typeAt(x, y, z);
        return t === "minecraft:water" ? "source" : t === "minecraft:air" ? "air" : t === "rw:pipe" ? "pipe" : "solid";
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
    clock: () => 0,
    log: quiet,
  };
  return { host, set, typeAt };
}

/** Walled 3x3 tank, interior x0..x0+2, z 0..2, floor ground y=64, walls y 65..70. */
function tank(w, x0, waterTop) {
  for (let y = 65; y <= 70; y++) {
    for (let x = x0 - 1; x <= x0 + 3; x++) for (let z = -1; z <= 3; z++) {
      const wall = x === x0 - 1 || x === x0 + 3 || z === -1 || z === 3;
      if (wall) w.set(x, y, z, "minecraft:stone");
      else if (y <= waterTop) w.set(x, y, z, "minecraft:water");
    }
  }
}

async function run(rt, ticks) {
  for (let i = 0; i < ticks; i++) rt.tick();
}

test("two tanks joined by a pipe level out, and the blocks follow", async () => {
  const w = makeWorld();
  tank(w, 0, 68); // A: 4 layers of water
  tank(w, 6, 65); // B: 1 layer
  for (const x of [3, 4, 5]) w.set(x, 65, 1, "rw:pipe"); // through A's east wall and B's west wall
  const rt = createRuntime(w.host);
  rt.weather.force("CLEAR", 1e6, 0);
  rt.water.requestScan(OW, 1, 66, 1, "gauge");
  await run(rt, 40);
  assert.equal(rt.water.describe().tanks, 2, "the pipe led to the second tank");
  const [a, b] = [rt.water.tankAt(OW, 1, 65, 1), rt.water.tankAt(OW, 7, 65, 1)];
  assert.ok(Math.abs(a.volume + b.volume + a.stats.evaporated + b.stats.evaporated - 45) < 1e-6);
  await run(rt, 20 * 60);
  const da = rt.water.describeBody(a);
  const db = rt.water.describeBody(b);
  assert.ok(Math.abs(da.level - db.level) < 0.05, `levels ${da.level} / ${db.level}`);
  const evaporated = a.stats.evaporated + b.stats.evaporated;
  assert.ok(Math.abs(a.volume + b.volume + evaporated - 45) < 1e-6, "exact mass balance: tanks + evaporated = 45 m³");
  let inB = 0;
  for (let y = 65; y <= 70; y++) for (let x = 6; x <= 8; x++) for (let z = 0; z <= 2; z++) if (w.typeAt(x, y, z) === "minecraft:water") inB++;
  assert.ok(inB >= 18, `B now shows ${inB} water blocks (was 9)`);
});

test("rain on soaked ground forms puddles in low places; off in settings means none", async () => {
  // Gentle dip around (8, 8) so runoff has somewhere to collect.
  const surface = (x, z) => (Math.abs(x - 8) <= 2 && Math.abs(z - 8) <= 2 ? 62 : 64);
  const w = makeWorld(surface);
  const rt = createRuntime(w.host);
  await run(rt, 200); // regions and climate initialise
  for (const r of rt.tracker.active.values()) if (r.record?.data?.c) r.record.data.c[0] = 950; // soaked
  rt.weather.force("HEAVY_RAIN", 1e6, rt.scheduler.ticks);
  await run(rt, 20 * 60);
  const d = rt.water.describe();
  assert.ok(rt.water.stats.runoff > 0, "runoff happened");
  assert.ok(d.pools > 0, `pools formed: ${d.pools}`);

  const w2 = makeWorld(surface);
  const rt2 = createRuntime(w2.host);
  rt2.config.set("water.rainPuddles", false);
  await run(rt2, 200);
  for (const r of rt2.tracker.active.values()) if (r.record?.data?.c) r.record.data.c[0] = 950;
  rt2.weather.force("HEAVY_RAIN", 1e6, rt2.scheduler.ticks);
  await run(rt2, 20 * 60);
  assert.equal(rt2.water.describe().pools, 0);
});

test("dry ground soaks up rain instead (no runoff below saturation)", async () => {
  const w = makeWorld();
  const rt = createRuntime(w.host);
  await run(rt, 200);
  for (const r of rt.tracker.active.values()) if (r.record?.data?.c) r.record.data.c[0] = 300;
  rt.weather.force("HEAVY_RAIN", 1e6, rt.scheduler.ticks);
  await run(rt, 20 * 30);
  assert.equal(rt.water.stats.runoff, 0);
});

// Found on a real Forge server with the Java port (same logic as here): water
// is drawn in whole layers, and a rescan counted a half-empty top layer as
// full, read blocks while a layer was being removed, forgot a hole above the
// water, and reset the simulation clock. Rescans must not change anything.
test("rescans of a leaking tank never create water or change the outcome", async () => {
  const worlds = [makeWorld(), makeWorld()];
  const rts = worlds.map((w) => {
    tank(w, 0, 69); // 3x3, water y 65..69 (45 m³), walls to y 70
    w.set(-1, 67, 1, "minecraft:air"); // hole in the west wall, 3rd layer
    const rt = createRuntime(w.host);
    rt.weather.force("CLEAR", 1e6, 0);
    rt.water.requestScan(OW, 1, 66, 1, "gauge");
    return rt;
  });
  for (const rt of rts) await run(rt, 20);
  const [a, b] = rts;
  const id = b.water.tankAt(OW, 1, 65, 1).id;
  let last = b.water.bodies.get(id).volume;
  for (let i = 0; i < 60; i++) {
    b.water._rescan(b.water.bodies.get(id), "change");
    await run(a, 20);
    await run(b, 20);
    const v = b.water.bodies.get(id).volume;
    assert.ok(v <= last + 1e-6, `rescan ${i} raised the volume from ${last} to ${v}`);
    last = v;
  }
  const [ta, tb] = [a.water.bodies.get(id), b.water.bodies.get(id)];
  assert.ok(Math.abs(ta.volume - tb.volume) < 1e-3, `with rescans ${tb.volume}, without ${ta.volume}`);
  assert.ok(tb.volume > 17.5 && tb.volume < 22, `drains to the hole (18 m³ below it, minus evaporation): ${tb.volume}`);
  assert.equal(tb.openings.length, 1, "the hole above the surface is still known");
  assert.deepEqual([tb.openings[0].x, tb.openings[0].y], [-1, 67]);
});

// Found on the Forge server: TNT removed several wall blocks at once, each
// started a scan of the same water, and three tanks were made for one body.
test("several blocks broken at once (explosion) make one tank", async () => {
  const w = makeWorld();
  tank(w, 0, 68);
  const rt = createRuntime(w.host);
  rt.weather.force("CLEAR", 1e6, 0);
  await run(rt, 20);
  const blast = [[-1, 66, 0], [-1, 66, 1], [-1, 67, 1], [1, 67, -1], [3, 66, 2]];
  for (const [x, y, z] of blast) w.set(x, y, z, "minecraft:air");
  for (const [x, y, z] of blast) rt.water.onBlockRemoved(OW, x, y, z);
  await run(rt, 20);
  const tanks = [...rt.water.bodies.values()].filter((b) => b.type === "tank");
  assert.equal(tanks.length, 1, "one tank for one body of water");
  assert.ok(Math.abs(tanks[0].volume - 36) < 0.5, `volume ${tanks[0].volume}`);
  assert.equal(tanks[0].openings.length, 5);
});
