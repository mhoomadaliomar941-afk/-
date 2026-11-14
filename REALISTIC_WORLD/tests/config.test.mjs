import test from "node:test";
import assert from "node:assert/strict";
import { MemoryBackend, Storage } from "../RealisticWorld_BP/scripts/core/storage.js";
import { Config, validateValue, BASE_SCHEMA } from "../RealisticWorld_BP/scripts/core/config.js";
import { Logger } from "../RealisticWorld_BP/scripts/core/logger.js";

const quiet = { info() {}, warn() {}, error() {} };
const make = (backend = new MemoryBackend()) => {
  const storage = new Storage(backend, "rw:");
  const c = new Config(storage, new Logger({ sink: quiet }));
  c.defineSystem("regions", "Regions", true);
  return { backend, storage, c };
};

test("defaults when nothing is saved", () => {
  const { c } = make();
  assert.equal(c.load().source, "defaults");
  assert.equal(c.get("profile"), "MEDIUM");
  assert.equal(c.get("enabled"), true);
  assert.equal(c.isSystemEnabled("regions"), true);
});

test("settings survive save and reload", () => {
  const { backend, c } = make();
  c.set("profile", "LOW");
  c.setSystemEnabled("regions", false);
  assert.equal(c.flush(), true);
  assert.equal(c.flush(), false, "second flush without changes writes nothing");
  const { c: c2 } = make(backend);
  assert.equal(c2.load().source, "saved");
  assert.equal(c2.get("profile"), "LOW");
  assert.equal(c2.isSystemEnabled("regions"), false);
});

test("invalid stored values are replaced by defaults with warnings", () => {
  const { storage, c } = make();
  storage.save("settings", { schema: 1, values: { profile: "TURBO", enabled: "yes", bogus: 1 } });
  const info = c.load();
  assert.equal(c.get("profile"), "MEDIUM");
  assert.equal(c.get("enabled"), true);
  assert.equal(info.warnings.length, 3);
  assert.equal(c.dirty, true, "repaired settings are re-saved");
});

test("settings from a newer version load with a warning", () => {
  const { storage, c } = make();
  storage.save("settings", { schema: 99, values: { profile: "ULTRA" } });
  const info = c.load();
  assert.equal(c.get("profile"), "ULTRA");
  assert.ok(info.warnings.some((w) => w.includes("newer")));
});

test("set() rejects invalid values and unknown keys", () => {
  const { c } = make();
  assert.equal(c.set("profile", "TURBO").ok, false);
  assert.equal(c.set("nope", 1).ok, false);
  assert.equal(c.get("profile"), "MEDIUM");
});

test("listeners fire on change only", () => {
  const { c } = make();
  const seen = [];
  c.onChange((k, v) => seen.push([k, v]));
  c.set("profile", "MEDIUM");
  c.set("profile", "ULTRA");
  assert.deepEqual(seen, [["profile", "ULTRA"]]);
});

test("reset restores every default", () => {
  const { c } = make();
  c.set("profile", "LOW");
  c.set("enabled", false);
  c.setSystemEnabled("regions", false);
  c.reset();
  assert.equal(c.get("profile"), "MEDIUM");
  assert.equal(c.get("enabled"), true);
  assert.equal(c.isSystemEnabled("regions"), true);
});

test("setDefault changes the default used by reset", () => {
  const { c } = make();
  c.setDefault("profile", "LOW");
  c.set("profile", "ULTRA");
  c.reset();
  assert.equal(c.get("profile"), "LOW");
});

test("validateValue", () => {
  assert.equal(validateValue(BASE_SCHEMA.adaptive, false).ok, true);
  assert.equal(validateValue(BASE_SCHEMA.adaptive, 0).ok, false);
  const intDef = { type: "int", min: 1, max: 10, default: 5, label: "" };
  assert.deepEqual(validateValue(intDef, 50).value, 10);
  assert.equal(validateValue(intDef, NaN).value, 5);
});
