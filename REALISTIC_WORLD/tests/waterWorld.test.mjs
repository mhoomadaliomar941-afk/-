// End-to-end water test: the real main.js in a voxel world (tests/mocks).
// Mandatory scenario: build a tank, fill it, break a wall block, measure the
// leak, watch the level fall and stop at the hole, water collect in a pit,
// save/reload, and check that no player block was ever replaced.
// The mock does not simulate vanilla water flow; it checks what the add-on
// does to the world (fillBlocks calls with type filters).
import test from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { __mc, world } from "@minecraft/server";
import { __ui, pick, close } from "@minecraft/server-ui";

const FOG_DIR = new URL("../RealisticWorld_RP/fogs/", import.meta.url);
for (const f of readdirSync(FOG_DIR)) {
  __mc.knownFogs.add(JSON.parse(readFileSync(new URL(f, FOG_DIR), "utf8"))["minecraft:fog_settings"].description.identifier);
}
const MAIN = new URL("../RealisticWorld_BP/scripts/main.js", import.meta.url).href;
let loads = 0;
async function enterWorld() {
  await import(`${MAIN}?load=${++loads}`);
  __mc.startup();
  world.afterEvents.worldLoad.fire({});
}

const W = "minecraft:water";
const STONE = "minecraft:stone";
/** Liters in a gauge message line like "Tank t1: 25,000 L of 100,000 L". */
const liters = (text, re) => Number((re.exec(text)?.[1] ?? "NaN").replace(/,/g, ""));
const countType = (x0, x1, y0, y1, z0, z1, type) => {
  let n = 0;
  for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) for (let z = z0; z <= z1; z++) if (__mc.typeAt(x, y, z) === type) n++;
  return n;
};

test("water physics in a real world", async (t) => {
  const quiet = { info: console.info, warn: console.warn };
  console.info = () => {};
  console.warn = () => {};
  t.after(() => Object.assign(console, quiet));

  __mc.surfaceFn = () => 70; // flat grass at y=70 (water stands from y=71)
  const steve = __mc.addPlayer("Steve", { x: 2, y: 71, z: 12, operator: true });

  // Tank: inner 5x5 (x,z 0..4), floor = ground, stone walls y=71..75, water y=71..74 (100 m³).
  for (let y = 71; y <= 75; y++) {
    for (let x = -1; x <= 5; x++) for (let z = -1; z <= 5; z++) {
      const wall = x === -1 || x === 5 || z === -1 || z === 5;
      if (wall) __mc.setBlock(x, y, z, STONE);
      else if (y <= 74) __mc.setBlock(x, y, z, W);
    }
  }
  // A pit next to it (x -9..-5, z 0..4), 2 blocks deep, with a chest a player left in it.
  for (let x = -9; x <= -5; x++) for (let z = 0; z <= 4; z++) for (const y of [69, 70]) __mc.setBlock(x, y, z, "minecraft:air");
  __mc.setBlock(-7, 69, 2, "minecraft:chest");
  // A big natural lake far away.
  for (let x = 100; x < 170; x++) for (let z = 100; z < 170; z++) __mc.setBlock(x, 70, z, W);
  // A second, sealed tank with a closed valve at its bottom (x,z 10..12, water y=71..73).
  for (let y = 71; y <= 74; y++) {
    for (let x = 9; x <= 13; x++) for (let z = -1; z <= 3; z++) {
      const wall = x === 9 || x === 13 || z === -1 || z === 3;
      if (wall) __mc.setBlock(x, y, z, STONE);
      else if (y <= 73) __mc.setBlock(x, y, z, W);
    }
  }
  __mc.setBlock(13, 71, 1, "rw:valve");
  const setupChanges = __mc.blockChanges.length;

  await enterWorld();
  __mc.typeWeatherCommand("Clear"); // keep rain out of the measurements
  await __mc.tick(60);

  await t.test("breaking a wall block next to the water finds the tank", async () => {
    __mc.breakBlock(steve, -1, 72, 2);
    await __mc.tick(10);
    __mc.interact(steve, -1, 73, 2, "rw:water_gauge");
    const msg = steve.messages.at(-1);
    assert.match(msg, /Tank t\d+/);
    assert.match(msg, /of 125,000 L/, "capacity: 100 m³ of water + 25 m³ of space inside the walls");
    assert.match(msg, /Leaking [\d.]+ L\/s/);
  });

  await t.test("the level falls layer by layer and stops at the hole", async () => {
    await __mc.tick(20 * 40); // 40 s
    const mid = countType(0, 4, 71, 74, 0, 4, W);
    assert.ok(mid < 100 && mid >= 25, `water blocks after 40 s: ${mid}`);
    await __mc.tick(20 * 120); // well past the analytical 109 s
    assert.equal(countType(0, 4, 72, 74, 0, 4, W), 0, "no water above the hole");
    assert.equal(countType(0, 4, 71, 71, 0, 4, W), 25, "the layer below the hole stays");
    __mc.interact(steve, -1, 73, 2, "rw:water_gauge");
    const msg = steve.messages.at(-1);
    const left = liters(msg, /: ([\d,]+) L of/);
    assert.ok(left > 24000 && left <= 25000, `left in the tank: ${left} L (25,000 minus a little evaporation)`);
    assert.match(msg, /Openings: 1 \(water is at or below them\)/);
  });

  await t.test("the escaped water collects in the pit; the player's chest is untouched", async () => {
    const pitWater = countType(-9, -5, 69, 70, 0, 4, W);
    assert.ok(pitWater >= 40, `water blocks in the pit: ${pitWater}`);
    assert.equal(__mc.typeAt(-7, 69, 2), "minecraft:chest");
    __mc.interact(steve, -7, 68, 1, "rw:water_gauge");
    const msg = steve.messages.at(-1);
    assert.match(msg, /(Pool|Flood) p\d+§r: [\d,]+ L over \d+ m²/);
    const pool = liters(msg, /: ([\d,]+) L over/);
    const soaked = liters(msg, /ground: ([\d,]+) L/);
    const evaporated = liters(msg, /evaporated: ([\d,]+) L/);
    // Mass balance: what left the tank is in the pool, soaked in, or evaporated.
    const total = pool + soaked + evaporated;
    assert.ok(total > 74000 && total < 76000, `pool ${pool} + soaked ${soaked} + evaporated ${evaporated} = ${total} L of ~75,000 L`);
  });

  await t.test("the Water Physics page lists what is near", async () => {
    __ui.answer(pick("Water Physics"));
    __ui.answer(close());
    __mc.runCommand("rw:menu", steve);
    await __mc.tick(5);
    const page = __ui.last();
    assert.equal(page.title, "Water Physics");
    assert.match(page.body, /1 tank\(s\), 1 pool\(s\)/);
    assert.match(page.body, /pressure at the bottom [\d.]+ kPa/);
  });

  await t.test("a closed valve keeps water in; opening it drains the tank", async () => {
    __mc.interact(steve, 13, 72, 1, "rw:water_gauge");
    await __mc.tick(10);
    let msg = steve.messages.at(-1);
    assert.match(msg, /: 27,000 L of 36,000 L/);
    assert.match(msg, /Sealed: no openings/);
    assert.match(msg, /Closed valves: 1/);
    __mc.interact(steve, 13, 71, 1); // open the valve by hand
    assert.equal(__mc.overworld()._get(13, 71, 1).states["rw:open"], true);
    await __mc.tick(20 * 120);
    __mc.interact(steve, 13, 72, 1, "rw:water_gauge");
    msg = steve.messages.at(-1);
    const left = liters(msg, /: ([\d,]+) L of/);
    assert.ok(left < 2000, `left behind an open bottom valve: ${left} L`);
    assert.equal(countType(10, 12, 71, 73, 0, 2, W), 0, "tank blocks emptied");
  });

  await t.test("natural water is never drained", async () => {
    const lakeBefore = countType(100, 169, 70, 70, 100, 169, W);
    __mc.breakBlock(steve, 99, 70, 120);
    await __mc.tick(20 * 30);
    assert.equal(countType(100, 169, 70, 70, 100, 169, W), lakeBefore);
    __mc.interact(steve, 99, 70, 120, "rw:water_gauge");
    await __mc.tick(40);
    assert.match(steve.messages.at(-1), /natural|already being measured/);
  });

  await t.test("the add-on never replaced a block other than air, water, ice or puddles", () => {
    const allowedFrom = new Set(["minecraft:air", W, "minecraft:flowing_water", "minecraft:ice", "rw:puddle",
      "minecraft:short_grass", "minecraft:tall_grass", "minecraft:fern", "minecraft:deadbush"]);
    const byAddon = __mc.blockChanges.slice(setupChanges).filter((c) => !(c.x === -1 && c.y === 72 && c.z === 2) && !(c.x === 99 && c.y === 70) && !(c.x === 13 && c.y === 71));
    assert.ok(byAddon.length > 50, `the add-on changed ${byAddon.length} blocks`);
    for (const c of byAddon) assert.ok(allowedFrom.has(c.from), `replaced ${c.from} at ${c.x},${c.y},${c.z}`);
    assert.equal(countType(-1, 5, 71, 75, -1, 5, STONE), 7 * 7 * 5 - 25 * 5 - 1, "every tank wall still stands (minus the broken one)");
  });

  await t.test("layers are changed whole: no water layer is split across ticks", () => {
    // Every removal of water inside the first tank took a whole 25-block layer at once.
    const inTank = (c) => c.x >= 0 && c.x <= 4 && c.z >= 0 && c.z <= 4;
    const tankRemovals = __mc.fillCalls.filter((f) => f.type === "minecraft:air" && f.from?.includes(W) && f.cells.some(inTank));
    assert.equal(tankRemovals.length, 3, "three layers (y 74, 73, 72) removed");
    for (const f of tankRemovals) {
      assert.equal(f.count, 25);
      assert.equal(new Set(f.cells.map((c) => c.y)).size, 1, "one height per call");
    }
    assert.deepEqual(tankRemovals.map((f) => f.cells[0].y), [74, 73, 72], "from the top down");
    // Puddles (thin custom blocks, no vanilla flow) may change one by one.
    const puddleOps = __mc.fillCalls.filter((f) => f.type === "rw:puddle" || (f.type === "minecraft:air" && f.from?.includes("rw:puddle")));
    assert.ok(puddleOps.length > 0, "the thin sheet beyond the pit shows as puddles");
  });

  await t.test("close and re-open the world: tanks and pools come back, nothing is doubled", async () => {
    const blocksBefore = countType(-12, 5, 66, 75, -3, 8, W);
    __mc.closeWorld();
    await enterWorld();
    await __mc.tick(60);
    __mc.interact(steve, -1, 73, 2, "rw:water_gauge");
    const msg = steve.messages.at(-1);
    assert.match(msg, /Tank t\d+/);
    const left = liters(msg, /: ([\d,]+) L of/);
    assert.ok(left > 24000 && left <= 25000, `after reload: ${left} L`);
    const blocksAfter = countType(-12, 5, 66, 75, -3, 8, W);
    assert.ok(Math.abs(blocksAfter - blocksBefore) <= 2, `water blocks before ${blocksBefore}, after ${blocksAfter}`);
    __mc.interact(steve, -7, 68, 1, "rw:water_gauge");
    assert.match(steve.messages.at(-1), /Pool p\d+/);
  });

  await t.test("patching the hole stops tracking the leak (rescan finds no opening)", async () => {
    __mc.placeBlock(steve, -1, 72, 2, STONE);
    await __mc.tick(20);
    __mc.interact(steve, -1, 73, 2, "rw:water_gauge");
    const msg = steve.messages.at(-1);
    assert.match(msg, /Tank t\d+/);
    assert.match(msg, /Sealed: no openings/, "the rescan after the patch found no hole");
  });
});
