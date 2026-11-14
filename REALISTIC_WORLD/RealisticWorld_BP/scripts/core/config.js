// World settings: typed schema, validation of every stored and user-entered
// value, per-system on/off switches, change listeners and dirty-flag saving.

import { DATA_SCHEMA_VERSION, KEYS } from "./constants.js";
import { clamp } from "../utils/math.js";

/**
 * @typedef {{ type: "bool", default: boolean, label: string }
 *   | { type: "enum", values: string[], default: string, label: string }
 *   | { type: "int", min: number, max: number, default: number, label: string }} SettingDef
 */

/**
 * Global settings. Per-system switches are added with defineSystem().
 * @type {Readonly<Record<string, SettingDef>>}
 */
export const BASE_SCHEMA = Object.freeze({
  enabled: { type: "bool", default: true, label: "Simulation enabled" },
  profile: { type: "enum", values: ["LOW", "MEDIUM", "ULTRA"], default: "MEDIUM", label: "Performance profile" },
  adaptive: { type: "bool", default: true, label: "Adaptive throttling" },
  logLevel: { type: "enum", values: ["debug", "info", "warn", "error"], default: "info", label: "Log level" },
});

/**
 * Validate and coerce one value.
 * @param {SettingDef} def
 * @param {unknown} value
 * @returns {{ ok: boolean, value: any, error?: string }}
 */
export function validateValue(def, value) {
  switch (def.type) {
    case "bool":
      if (typeof value === "boolean") return { ok: true, value };
      return { ok: false, value: def.default, error: `expected true/false, got ${JSON.stringify(value)}` };
    case "enum":
      if (typeof value === "string" && def.values.includes(value)) return { ok: true, value };
      return { ok: false, value: def.default, error: `expected one of ${def.values.join("/")}, got ${JSON.stringify(value)}` };
    case "int": {
      if (typeof value !== "number" || !Number.isFinite(value)) {
        return { ok: false, value: def.default, error: `expected a number, got ${JSON.stringify(value)}` };
      }
      const v = clamp(Math.round(value), def.min, def.max);
      return v === value ? { ok: true, value: v } : { ok: false, value: v, error: `${value} adjusted to ${v}` };
    }
    default:
      return { ok: false, value: undefined, error: "unknown setting type" };
  }
}

export class Config {
  /**
   * @param {import("./storage.js").Storage} storage
   * @param {import("./logger.js").Logger} logger
   */
  constructor(storage, logger) {
    this.storage = storage;
    this.logger = logger;
    /** @type {Record<string, SettingDef>} */
    this.schema = { ...BASE_SCHEMA };
    /** @type {Record<string, any>} */
    this.values = {};
    for (const [k, def] of Object.entries(this.schema)) this.values[k] = def.default;
    this.dirty = false;
    /** @type {((key: string, value: any) => void)[]} */
    this.listeners = [];
  }

  static systemKey(id) {
    return `system.${id}`;
  }

  /** Declare a system switch. Must be called before load(). */
  defineSystem(id, label, defaultEnabled = true) {
    const key = Config.systemKey(id);
    this.schema[key] = { type: "bool", default: defaultEnabled, label };
    if (!(key in this.values)) this.values[key] = defaultEnabled;
  }

  /** Override the default of a setting (e.g. profile picked from device memory). */
  setDefault(key, value) {
    const def = this.schema[key];
    if (!def) return;
    const res = validateValue(def, value);
    if (res.ok) {
      /** @type {any} */ (def).default = res.value;
      this.values[key] = res.value;
    }
  }

  /**
   * Load saved settings. Invalid or unknown entries are dropped with a warning;
   * missing entries keep their defaults.
   * @returns {{ source: "saved" | "recovered" | "defaults", warnings: string[] }}
   */
  load() {
    const warnings = [];
    const res = this.storage.load(KEYS.settings);
    if (!res.ok) {
      if (!res.missing) {
        warnings.push(`saved settings unreadable (${res.error}); using defaults`);
        this.logger.warn("config", warnings[warnings.length - 1]);
        this.dirty = true;
      }
      return { source: "defaults", warnings };
    }
    const data = res.value;
    if (!data || typeof data !== "object" || typeof data.values !== "object" || data.values === null) {
      warnings.push("saved settings have an unexpected shape; using defaults");
      this.dirty = true;
      return { source: "defaults", warnings };
    }
    if (typeof data.schema === "number" && data.schema > DATA_SCHEMA_VERSION) {
      warnings.push(`settings were saved by a newer version (schema ${data.schema}); unknown entries ignored`);
    }
    for (const [key, raw] of Object.entries(data.values)) {
      const def = this.schema[key];
      if (!def) {
        warnings.push(`unknown setting "${key}" ignored`);
        continue;
      }
      const v = validateValue(def, raw);
      if (!v.ok) {
        warnings.push(`setting "${key}": ${v.error}`);
        this.dirty = true;
      }
      this.values[key] = v.value;
    }
    for (const w of warnings) this.logger.warn("config", w);
    return { source: res.recovered ? "recovered" : "saved", warnings };
  }

  get(key) {
    return this.values[key];
  }

  /** @returns {{ ok: boolean, value?: any, error?: string }} */
  set(key, value) {
    const def = this.schema[key];
    if (!def) return { ok: false, error: `unknown setting "${key}"` };
    const v = validateValue(def, value);
    if (!v.ok && def.type !== "int") return { ok: false, error: v.error };
    if (this.values[key] === v.value) return { ok: true, value: v.value };
    this.values[key] = v.value;
    this.dirty = true;
    for (const fn of this.listeners) {
      try {
        fn(key, v.value);
      } catch (e) {
        this.logger.error("config", `listener failed for "${key}"`, e);
      }
    }
    return { ok: true, value: v.value };
  }

  isSystemEnabled(id) {
    return this.values[Config.systemKey(id)] === true;
  }

  setSystemEnabled(id, enabled) {
    return this.set(Config.systemKey(id), enabled);
  }

  /** @param {(key: string, value: any) => void} fn */
  onChange(fn) {
    this.listeners.push(fn);
  }

  /** Restore every setting to its default (listeners are notified). */
  reset() {
    for (const [key, def] of Object.entries(this.schema)) this.set(key, def.default);
    this.dirty = true;
  }

  /** Write to storage if anything changed. @returns {boolean} true if saved */
  flush() {
    if (!this.dirty) return false;
    const res = this.storage.save(KEYS.settings, { schema: DATA_SCHEMA_VERSION, values: this.values });
    if (!res.ok) {
      this.logger.error("config", `could not save settings: ${res.error}`);
      return false;
    }
    this.dirty = false;
    return true;
  }
}
