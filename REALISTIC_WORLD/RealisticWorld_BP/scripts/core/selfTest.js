// In-game self-test. Run it from the control center or with /rw:selftest to
// check, on the actual device, that saving, corruption recovery, settings
// validation and the engine loop work. The same function runs in the Node tests.

import { KEYS } from "./constants.js";
import { MemoryBackend, Storage } from "./storage.js";
import { validateValue, BASE_SCHEMA } from "./config.js";

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
