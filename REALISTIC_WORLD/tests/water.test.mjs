import test from "node:test";
import assert from "node:assert/strict";
import { sideFlow, floorFlow, pipeFlow, pressureKPa, levelFromVolume, volumeBelow, capacity, infiltrationRate, evaporationRate, nextFrozen, CD, G } from "../RealisticWorld_BP/scripts/water/physics.js";
import { descend, fillBasin, levelOverColumns } from "../RealisticWorld_BP/scripts/water/basin.js";
import { TankScan, tracePipe } from "../RealisticWorld_BP/scripts/water/tankScan.js";
import { stepTank, stepPool, stepPipeLink, tankLevel, tankOutflow, poolLevel, catchUp, drainFloor } from "../RealisticWorld_BP/scripts/water/bodies.js";

const close = (a, b, eps, msg) => assert.ok(Math.abs(a - b) <= eps, `${msg ?? ""} ${a} vs ${b}`);
const calmEnv = { tempC: 15, humidity: 0.6, wind: 2, raining: false, intensity: 0, soil: 0.5 };
const noLoss = { tempC: 0, humidity: 1, wind: 0, raining: false, intensity: 0, soil: 1 }; // no evaporation, not frozen

/** Tiny voxel world for the scanner. */
function voxels() {
  const m = new Map();
  const k = (x, y, z) => `${x},${y},${z}`;
  return {
    set: (x, y, z, kind) => m.set(k(x, y, z), kind),
    get: (x, y, z) => m.get(k(x, y, z)) ?? (y < 64 ? "solid" : "air"),
  };
}

/** A walled 5x5 tank (interior x,z 0..4), floor y=64, water y=65..68, walls up to 69. */
function buildTank(w) {
  for (let x = -1; x <= 5; x++) for (let z = -1; z <= 5; z++) w.set(x, 64, z, "solid");
  for (let y = 65; y <= 69; y++) {
    for (let x = -1; x <= 5; x++) for (let z = -1; z <= 5; z++) {
      const wall = x === -1 || x === 5 || z === -1 || z === 5;
      if (wall) w.set(x, y, z, "solid");
      else if (y <= 68) w.set(x, y, z, "source");
    }
  }
}

function scanTank(w, seed = { x: 2, y: 66, z: 2 }) {
  const scan = new TankScan(w.get, seed);
  while (!scan.step(16));
  return scan;
}

function makeTank(shape) {
  return {
    id: "t1", type: "tank", dim: "o", ...shape,
    openings: shape.openings.map((o) => ({ ...o, open: true })),
    pipes: [], volume: shape.cells, frozen: false, lastSim: 0,
    stats: { out: 0, rained: 0, evaporated: 0, outflow: 0 },
  };
}

test("physics: pressure and Torricelli flow", () => {
  close(pressureKPa(10), 98.1, 1e-9, "10 m of water");
  close(sideFlow(69, 66), CD * Math.sqrt(2 * G * 2.5), 1e-12, "full hole, head 2.5 m to its middle");
  assert.equal(sideFlow(66, 66), 0, "no water at the hole");
  assert.ok(sideFlow(66.5, 66) < sideFlow(67, 66), "half-covered hole leaks less");
  close(floorFlow(68, 65), CD * Math.sqrt(2 * G * 3), 1e-12);
  assert.ok(pipeFlow(70, 65, 66, 65) > 0 && pipeFlow(66, 65, 70, 65) < 0, "pipe flows toward the lower surface");
  assert.equal(pipeFlow(64, 65, -Infinity, 60), 0, "water below the pipe inlet cannot enter it");
});

test("physics: level and volume of layered containers are inverses", () => {
  const layers = [4, 9, 9, 16];
  for (const v of [0, 2, 4, 7.5, 13, 22, 38]) close(volumeBelow(layers, 10, levelFromVolume(layers, 10, v)), v, 1e-9, `v=${v}`);
  assert.equal(capacity(layers), 38);
  assert.equal(levelFromVolume(layers, 10, 4), 11);
});

test("physics: ground absorbs by type, evaporation by weather, freezing has hysteresis", () => {
  assert.ok(infiltrationRate("minecraft:sand") > infiltrationRate("minecraft:grass_block"));
  assert.ok(infiltrationRate("minecraft:grass_block") > infiltrationRate("minecraft:clay"));
  assert.equal(infiltrationRate("minecraft:stone"), 0);
  assert.ok(evaporationRate(30, 0.2, 5) > evaporationRate(15, 0.8, 0));
  assert.equal(evaporationRate(-5, 0.2, 5), 0);
  assert.equal(nextFrozen(false, 0), false);
  assert.equal(nextFrozen(false, -2), true);
  assert.equal(nextFrozen(true, 0), true, "stays frozen until clearly above zero");
  assert.equal(nextFrozen(true, 2), false);
});

test("scan: finds the tank, its 100 m³ and no openings when sealed", () => {
  const w = voxels();
  buildTank(w);
  const scan = scanTank(w);
  assert.equal(scan.status, "done");
  const shape = scan.result();
  assert.equal(shape.cells, 100);
  assert.deepEqual(shape.layers, [25, 25, 25, 25, 25], "4 water layers + 1 empty layer inside the walls");
  assert.equal(shape.waterTop, 68);
  assert.equal(shape.baseY, 65);
  assert.equal(shape.openTop, 25);
  assert.equal(shape.openings.length, 0);
  assert.equal(shape.columns.length, 25);
});

test("scan: a hole in the wall, a floor hole, valves, pipes and drains are found", () => {
  const w = voxels();
  buildTank(w);
  w.set(-1, 66, 2, "air"); // hole in the west wall
  w.set(3, 64, 3, "air"); // hole in the floor
  w.set(5, 67, 1, "valve_closed");
  w.set(2, 65, 5, "pipe");
  w.set(1, 64, 1, "drain");
  const s = scanTank(w).result();
  const kinds = s.openings.map((o) => `${o.kind}@${o.x},${o.y},${o.z}/${o.bottom}`).sort();
  assert.deepEqual(kinds, ["drain@1,64,1/65", "floor@3,64,3/65", "hole@-1,66,2/66"]);
  const hole = s.openings.find((o) => o.kind === "hole");
  assert.deepEqual([hole.dx, hole.dz], [-1, 0], "outward direction of the hole");
  assert.deepEqual(s.valves, [{ x: 5, y: 67, z: 1 }]);
  assert.deepEqual(s.pipes, [{ x: 2, y: 65, z: 5, fromY: 65 }]);
});

test("scan: natural water larger than the cap is refused; unloaded chunks abort", () => {
  const w = voxels();
  for (let x = 0; x < 30; x++) for (let z = 0; z < 30; z++) w.set(x, 64, z, "source");
  const big = new TankScan(w.get, { x: 5, y: 64, z: 5 }, 500);
  while (!big.step(100));
  assert.equal(big.status, "too_big");
  const w2 = voxels();
  buildTank(w2);
  w2.set(5, 66, 2, "unloaded");
  assert.equal(scanTank(w2).status, "unloaded");
  assert.equal(new TankScan(w2.get, { x: 20, y: 70, z: 20 }).status, "not_water");
});

test("scan work is spread: each step handles a bounded number of cells", () => {
  const w = voxels();
  buildTank(w);
  const scan = new TankScan(w.get, { x: 2, y: 66, z: 2 });
  let steps = 0;
  while (!scan.step(10)) steps++;
  assert.ok(steps >= 9, `done in ${steps} steps of 10 cells`);
  assert.ok(scan.blockReads <= 100 * 6 + 10, `block reads ${scan.blockReads}`);
});

test("MANDATORY: tank with a hole leaks, level falls, flow stops at the hole, outflow is measured", () => {
  const w = voxels();
  buildTank(w);
  w.set(-1, 66, 2, "air"); // break one wall block at the 2nd water layer
  const t = makeTank(scanTank(w).result());
  assert.equal(t.volume, 100);
  close(tankLevel(t), 69, 1e-9, "full: surface at the top of layer 68");

  // First second: Torricelli with 2.5 m head over the hole's middle.
  const q0 = tankOutflow(t);
  close(q0, 0.6 * Math.sqrt(2 * 9.81 * 2.5), 1e-9, "initial outflow m³/s");

  const levels = [];
  let poured = 0;
  for (let s = 0; s < 120; s++) {
    const r = stepTank(t, 1, noLoss);
    for (const v of r.pours.values()) poured += v;
    assert.deepEqual([...r.pours.keys()].every((k) => k === "-2,2"), true, "water lands just outside the hole");
    levels.push(tankLevel(t));
  }
  for (let i = 1; i < levels.length; i++) assert.ok(levels[i] <= levels[i - 1] + 1e-12, "level never rises");
  close(tankLevel(t), 66, 1e-6, "stops exactly at the bottom of the hole");
  close(t.volume, 25, 1e-6, "one layer of 25 m³ stays below the hole");
  close(poured, 75, 1e-6, "75 m³ left through the hole (mass conserved)");
  close(t.stats.out, 75, 1e-6);
  // Time matches the analytical solution of A dL/dt = -Q(L):
  //  hole fully covered (L 69 -> 67): Q = Cd sqrt(2g (L-66.5))
  //    t1 = 2A/(Cd sqrt(2g)) (sqrt(2.5) - sqrt(0.5))
  //  hole partly covered (w = L-66, 1 -> RUNOFF_FILM): Q = Cd sqrt(g) w^1.5
  //    t2 = 2A/(Cd sqrt(g)) (1/sqrt(film) - 1)
  const A = 25;
  const t1 = ((2 * A) / (0.6 * Math.sqrt(2 * 9.81))) * (Math.sqrt(2.5) - Math.sqrt(0.5));
  const t2 = ((2 * A) / (0.6 * Math.sqrt(9.81))) * (1 / Math.sqrt(0.05) - 1);
  const tEmpty = levels.findIndex((l) => l < 66 + 1e-6) + 1;
  close(tEmpty, t1 + t2, 0.05 * (t1 + t2), `drained to the hole in ${tEmpty} s, analytical`);
  const t67 = levels.findIndex((l) => l <= 67) + 1;
  close(t67, t1, 1.5, `hole uncovered at ${t67} s, analytical`);
  assert.equal(stepTank(t, 10, noLoss).pours.size, 0, "no more flow once at the hole");
});

test("tank: valve, drain and floor hole behave as openings; closing stops the flow", () => {
  const w = voxels();
  buildTank(w);
  w.set(1, 64, 1, "drain");
  const t = makeTank(scanTank(w).result());
  const r = stepTank(t, 5, noLoss);
  assert.equal(r.pours.size, 0, "a drain removes water instead of spilling it");
  assert.ok(r.lost > 0);
  t.openings[0].open = false;
  const v = t.volume;
  stepTank(t, 5, noLoss);
  assert.equal(t.volume, v, "closed: no flow");
  t.openings[0].open = true;
  stepTank(t, 600, noLoss);
  assert.ok(t.volume < 1e-6, "a floor drain empties the tank completely");
  assert.equal(drainFloor(t), 65);
});

test("headroom: empty space inside walls counts; an open rim or a natural shore does not", () => {
  const w = voxels();
  buildTank(w); // walls to y=69, water to 68
  for (let x = -1; x <= 5; x++) for (let z = -1; z <= 5; z++) if (x === -1 || x === 5 || z === -1 || z === 5) w.set(x, 70, z, "solid");
  w.set(2, 70, -1, "air"); // a notch in the rim at y=70
  const s = scanTank(w).result();
  assert.equal(s.layers.length, 5, "y=69 is closed, y=70 has a notch: capacity stops at the notch");
  const pond = voxels();
  for (let x = 0; x < 4; x++) for (let z = 0; z < 4; z++) pond.set(x, 63, z, "source"); // pond in the ground, open shore above
  const p = new TankScan(pond.get, { x: 1, y: 63, z: 1 });
  while (!p.step(16));
  assert.equal(p.result().layers.length, 1, "no extra capacity above a pond");
});

test("tank: rain fills, evaporation lowers, freezing stops evaporation", () => {
  const w = voxels();
  buildTank(w);
  const t = makeTank(scanTank(w).result());
  t.volume = 50;
  stepTank(t, 100, { ...calmEnv, raining: true, intensity: 0.5 });
  assert.ok(t.volume > 50 && t.stats.rained > 0);
  const v = t.volume;
  stepTank(t, 1000, { tempC: 30, humidity: 0.2, wind: 5, raining: false, intensity: 0, soil: 0.5 });
  assert.ok(t.volume < v);
  t.volume = 50;
  stepTank(t, 1000, { tempC: -10, humidity: 0.2, wind: 5, raining: false, intensity: 0, soil: 0.5 });
  assert.equal(t.frozen, true);
  assert.equal(t.volume, 50);
  t.volume = 125;
  const r = stepTank(t, 10, { ...calmEnv, raining: true, intensity: 1 });
  assert.ok(r.overflow > 0 && t.volume === 125, "a full tank overflows instead of exceeding its capacity");
});

test("pipes: two tanks joined by a pipe level out (communicating vessels)", () => {
  const w = voxels();
  buildTank(w);
  const a = makeTank(scanTank(w).result());
  const b = makeTank(scanTank(w).result());
  b.id = "t2";
  a.volume = 100;
  b.volume = 20;
  const total = a.volume + b.volume;
  let moved = 0;
  for (let s = 0; s < 300; s++) moved += stepPipeLink(a, { x: 0, y: 65, z: 0, startY: 65, endY: 65, out: null }, b, 65, 1);
  close(a.volume + b.volume, total, 1e-9, "no water created or lost");
  close(tankLevel(a), tankLevel(b), 0.02, "same level at the end");
  close(moved, 40, 0.5);
});

test("pipe tracing follows the chain to its far end", () => {
  const w = voxels();
  for (let x = 0; x < 6; x++) w.set(x, 65, 0, "pipe");
  w.set(6, 65, 0, "drain");
  const own = () => false;
  const r = tracePipe(w.get, { x: 0, y: 65, z: 0 }, own);
  assert.deepEqual(r.end, { x: 5, y: 65, z: 0 });
  assert.equal(r.out.kind, "drain");
  assert.equal(r.length, 6);
});

// ---------- terrain ----------

/** Height map: bowl with its rim at 64 and bottom at 60, centred at 0,0, radius 4. */
function bowl(extra = {}) {
  return (x, z) => {
    const k = `${x},${z}`;
    if (k in extra) return extra[k];
    const d = Math.max(Math.abs(x), Math.abs(z));
    return { base: d <= 4 ? 60 + d : 65, kind: "ground", ground: "minecraft:grass_block" };
  };
}

test("basin: water runs downhill to the low point", () => {
  const m = descend(3, 2, bowl());
  assert.deepEqual([m.x, m.z], [0, 0]);
});

test("basin: fills the bowl with a flat surface; volume is conserved", () => {
  const r = fillBasin({ x: 0, z: 0 }, 20, bowl());
  const stored = r.cols.reduce((a, c) => a + Math.max(0, r.level - c.base), 0);
  close(stored, 20, 1e-9, "stored volume");
  assert.equal(r.spill, null);
  assert.ok(r.level > 61 && r.level < 63, `level ${r.level}`);
  assert.ok(r.nextBase > r.level, "the shape is valid until the next ring");
});

test("basin: overflow spills at the rim and continues downhill", () => {
  const h = bowl({ "5,0": { base: 62, kind: "ground", ground: "minecraft:stone" } }); // notch in the rim
  const r = fillBasin({ x: 0, z: 0 }, 500, h);
  assert.ok(r.spill, "spilled");
  assert.ok(r.leftover > 0);
  close(r.used + r.leftover, 500, 1e-9);
  close(r.level, 64, 1e-9, "fills to the inner ring (64), then runs over to the notch");
  assert.deepEqual(r.spill, { x: 5, z: 0 });
});

test("basin: water reaching natural water joins it", () => {
  const h = bowl({ "1,0": { base: 60, kind: "water", ground: "minecraft:water" } });
  const r = fillBasin({ x: 0, z: 0 }, 50, h);
  assert.equal(r.joinedWater, true);
});

test("basin: on flat ground water spreads into a sheet no thinner than the minimum film", () => {
  const flat = () => ({ base: 64, kind: "ground", ground: "minecraft:grass_block" });
  const r = fillBasin({ x: 0, z: 0 }, 10, flat);
  assert.equal(r.hitLimit, false);
  close(r.cols.reduce((a, c) => a + (r.level - c.base), 0), 10, 1e-9, "all water stored");
  assert.ok(r.level - 64 >= 0.1 - 1e-9 && r.cols.length <= 100, `${r.cols.length} columns at ${r.level - 64} m`);
});

test("basin: size limit stops unbounded floods", () => {
  const flat = () => ({ base: 64, kind: "ground", ground: "minecraft:grass_block" });
  const r = fillBasin({ x: 0, z: 0 }, 1e6, flat, { maxCols: 100 });
  assert.equal(r.hitLimit, true);
  assert.equal(r.cols.length, 100);
});

test("pool: soaks into the ground, evaporates, and stops soaking on wet or frozen ground", () => {
  const r = fillBasin({ x: 0, z: 0 }, 10, bowl());
  const mk = () => ({
    id: "p", type: "pool", dim: "o", cols: r.cols.map((c) => [c.x, c.z, c.base, c.ground]),
    min: [0, 0], volume: 10, limit: r.nextBase, spill: null, frozen: false, lastSim: 0,
    stats: { absorbed: 0, rained: 0, evaporated: 0, drained: 0 },
  });
  const dry = mk();
  const res = stepPool(dry, 60, { ...calmEnv, soil: 0.2 });
  assert.ok(dry.volume < 10 && res.absorbed > 0);
  const wet = mk();
  stepPool(wet, 60, { ...calmEnv, soil: 1 });
  assert.ok(wet.volume > dry.volume, "saturated ground absorbs nothing");
  const frozen = mk();
  stepPool(frozen, 60, { ...calmEnv, tempC: -10, soil: 0.2 });
  assert.equal(frozen.volume, 10);
  const grow = mk();
  const g = stepPool(grow, 600, { ...calmEnv, raining: true, intensity: 1, soil: 1 });
  assert.ok(g.regrow, "rain raised it past its shape: needs a re-pour");
  close(poolLevel(mk()), levelOverColumns(r.cols.map((c) => c.base), 10), 1e-12);
});

test("catch-up: an absent leaking tank settles at its hole in one step", () => {
  const w = voxels();
  buildTank(w);
  w.set(-1, 67, 2, "air");
  const t = makeTank(scanTank(w).result());
  const r = catchUp(t, 3600, noLoss);
  close(tankLevel(t), 67, 1e-6);
  const poured = [...r.pours.values()].reduce((a, b) => a + b, 0);
  close(poured + t.volume, 100, 1e-6, "mass conserved across the jump");
});
