// In-game self-test. Run it from the control center or with /rw:selftest to
// check, on the actual device, that saving, corruption recovery, settings
// validation and the engine loop work. The same function runs in the Node tests.

import { KEYS } from "./constants.js";
import { MemoryBackend, Storage } from "./storage.js";
import { validateValue, BASE_SCHEMA } from "./config.js";
import { classify, createState, forceWeather, step, vanillaWeather } from "../weather/model.js";
import { biomeInfo, temperatureAt } from "../weather/climate.js";
import { TankScan } from "../water/tankScan.js";
import { stepTank, tankLevel } from "../water/bodies.js";

/**
 * @typedef {{ name: string, ok: boolean, detail: string }} TestResult
 */

/**
 * @param {{
 *   storage: import("./storage.js").Storage,
 *   scheduler: import("./scheduler.js").Scheduler,
 *   tickAtLastCheck?: number,
 * }} rt
 * @returns {TestResult[]}
 */
export function runSelfTest(rt) {
  /** @type {TestResult[]} */
  const results = [];
  const check = (name, fn) => {
    try {
      const detail = fn();
      results.push({ name, ok: true, detail: detail ?? "" });
    } catch (e) {
      results.push({ name, ok: false, detail: String(/** @type {any} */ (e)?.message ?? e) });
    }
  };
  const assert = (cond, msg) => {
    if (!cond) throw new Error(msg);
  };

  check("World storage round trip (multi-chunk)", () => {
    const payload = { text: "x".repeat(25000), n: 42, list: [1, 2, 3] };
    const saved = rt.storage.save(KEYS.selfTest, payload);
    assert(saved.ok, `save failed: ${saved.error}`);
    const loaded = rt.storage.load(KEYS.selfTest);
    assert(loaded.ok, `load failed: ${loaded.ok ? "" : loaded.error}`);
    const v = loaded.ok ? loaded.value : null;
    assert(v.text.length === 25000 && v.n === 42 && v.list.length === 3, "data changed after loading");
    rt.storage.delete(KEYS.selfTest);
    assert(!rt.storage.load(KEYS.selfTest).ok, "delete left data behind");
    return `${saved.chars} chars in ${saved.chunks} chunks`;
  });

  check("Corruption recovery (backup copy)", () => {
    const s = new Storage(new MemoryBackend(), "t:");
    s.save("k", { version: 1 }); // goes to slot a
    s.save("k", { version: 2 }); // goes to slot b (newest)
    // Damage the newest copy the way a crash during saving could.
    const backend = /** @type {MemoryBackend} */ (s.backend);
    assert(backend.get("t:k@b#0") === '{"version":2}', "test setup failed");
    backend.set("t:k@b#0", '{"version":9');
    const res = s.load("k");
    assert(res.ok && res.value.version === 1 && res.recovered, "did not fall back to the previous copy");
    return "damaged copy detected, previous copy restored";
  });

  check("Settings validation", () => {
    assert(!validateValue(BASE_SCHEMA.profile, "TURBO").ok, "accepted an invalid profile");
    assert(validateValue(BASE_SCHEMA.profile, "LOW").ok, "rejected a valid profile");
    assert(!validateValue(BASE_SCHEMA.enabled, "yes").ok, "accepted a non-boolean");
    return "invalid values rejected, valid values kept";
  });

  check("Weather model and climate math", () => {
    const st = createState({ s: 12345 });
    step(st, 24000);
    assert([st.P, st.H, st.I, st.A, st.wind].every(Number.isFinite), "model produced an invalid number");
    forceWeather(st, "THUNDERSTORM", 2000);
    step(st, 500);
    assert(classify(st) === "THUNDERSTORM" && vanillaWeather(st) === "Thunder", "forced storm not applied");
    const calm = { A: 0, I: 0, H: 0.5, raining: false };
    const desert = temperatureAt(biomeInfo("minecraft:desert"), 64, 8000, calm);
    const ice = temperatureAt(biomeInfo("minecraft:ice_plains"), 64, 8000, calm);
    assert(desert > 30 && ice < 0, `unexpected temperatures: desert ${desert}, ice plains ${ice}`);
    return `desert ${desert.toFixed(1)}°C, ice plains ${ice.toFixed(1)}°C at mid-afternoon`;
  });

  check("Water physics (virtual 3x3 tank with a hole)", () => {
    // In memory only: a 3x3 tank, water y 1..3, stone walls to y 4, hole at y 2.
    const cellAt = (x, y, z) => {
      if (y <= 0) return "solid";
      const inside = x >= 0 && x <= 2 && z >= 0 && z <= 2;
      if (inside) return y <= 3 ? "source" : "air";
      if (x === -1 && y === 2 && z === 1) return "air"; // the hole
      return y <= 4 && x >= -1 && x <= 3 && z >= -1 && z <= 3 ? "solid" : "air";
    };
    const scan = new TankScan(cellAt, { x: 1, y: 2, z: 1 });
    while (!scan.step(8));
    const s = scan.result();
    assert(scan.status === "done" && s.cells === 27 && s.openings.length === 1, `scan found ${s.cells} cells, ${s.openings.length} openings`);
    /** @type {import("../water/bodies.js").Tank} */
    const t = { id: "test", type: "tank", dim: "", ...s, openings: s.openings.map((o) => ({ ...o, open: true })), pipes: [], volume: 27, frozen: false, lastSim: 0, stats: { out: 0, rained: 0, evaporated: 0, outflow: 0 } };
    const env = { tempC: 0, humidity: 1, wind: 0, raining: false, intensity: 0, soil: 1 };
    let out = 0;
    for (let i = 0; i < 120; i++) for (const v of stepTank(t, 1, env).pours.values()) out += v;
    assert(Math.abs(tankLevel(t) - 2) < 1e-6, `level ${tankLevel(t)}, expected 2 (the hole)`);
    assert(Math.abs(out - 18) < 1e-6 && Math.abs(t.volume - 9) < 1e-6, `leaked ${out} m³, kept ${t.volume} m³`);
    return "18 m³ leaked, level stopped at the hole, 9 m³ kept";
  });

  check("Engine loop is ticking", () => {
    const now = rt.scheduler.ticks;
    if (rt.tickAtLastCheck !== undefined) {
      assert(now > rt.tickAtLastCheck, `engine tick did not advance (${now})`);
    }
    const faulted = rt.scheduler.list().filter((s) => s.faulted).map((s) => s.id);
    assert(faulted.length === 0, `faulted systems: ${faulted.join(", ")}`);
    return `tick ${now}, ${rt.scheduler.list().filter((s) => s.enabled).length} systems running`;
  });

  return results;
}
