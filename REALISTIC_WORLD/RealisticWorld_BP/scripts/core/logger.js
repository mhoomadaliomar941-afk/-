// Readable logging with an in-memory ring buffer. Errors are kept separately so
// the control center can show them, and can be persisted by the runtime.

/** @typedef {"debug" | "info" | "warn" | "error"} LevelName */

/** @type {Readonly<Record<LevelName, number>>} */
export const LEVELS = Object.freeze({ debug: 0, info: 1, warn: 2, error: 3 });

/**
 * @typedef {{ tick: number, level: LevelName, source: string, message: string }} LogEntry
 */

export class Logger {
  /**
   * @param {{ maxEntries?: number, maxErrors?: number, sink?: { info: Function, warn: Function, error: Function }, tickSource?: () => number }} [opts]
   */
  constructor(opts = {}) {
    this.maxEntries = opts.maxEntries ?? 60;
    this.maxErrors = opts.maxErrors ?? 20;
    this.sink = opts.sink ?? console;
    this.tickSource = opts.tickSource ?? (() => 0);
    /** @type {LevelName} */
    this.level = "info";
    /** @type {LogEntry[]} */
    this.entries = [];
    /** @type {LogEntry[]} */
    this.errors = [];
    /** @type {((e: LogEntry) => void)[]} */
    this.errorListeners = [];
  }

  /** @param {LevelName} level */
  setLevel(level) {
    if (level in LEVELS) this.level = level;
  }

  debug(source, message) { this._log("debug", source, message); }
  info(source, message) { this._log("info", source, message); }
  warn(source, message) { this._log("warn", source, message); }

  /** @param {string} source @param {string} message @param {unknown} [err] */
  error(source, message, err) {
    let text = message;
    if (err !== undefined) {
      const e = /** @type {any} */ (err);
      text += `: ${e?.message ?? String(err)}`;
      if (e?.stack) text += `\n${String(e.stack).split("\n").slice(0, 4).join("\n")}`;
    }
    this._log("error", source, text);
  }

  /** Restore previously persisted errors (oldest first). @param {LogEntry[]} list */
  restoreErrors(list) {
    if (!Array.isArray(list)) return;
    const valid = list.filter(
      (e) => e && typeof e.message === "string" && typeof e.source === "string" && typeof e.tick === "number",
    );
    this.errors = valid.slice(-this.maxErrors).map((e) => ({ ...e, level: "error" }));
  }

  clearErrors() {
    this.errors = [];
  }

  /** @param {(e: LogEntry) => void} fn */
  onError(fn) {
    this.errorListeners.push(fn);
  }

  /** @param {LogEntry} e */
  static format(e) {
    return `[t${e.tick}] ${e.level.toUpperCase()} ${e.source}: ${e.message}`;
  }

  /** @param {LevelName} level @param {string} source @param {string} message */
  _log(level, source, message) {
    if (LEVELS[level] < LEVELS[this.level]) return;
    let tick = 0;
    try {
      tick = this.tickSource();
    } catch {
      // tick source can be unavailable during early startup
    }
    /** @type {LogEntry} */
    const entry = { tick, level, source, message };
    this.entries.push(entry);
    if (this.entries.length > this.maxEntries) this.entries.shift();

    const line = `[RW] ${Logger.format(entry)}`;
    if (level === "error") {
      this.errors.push(entry);
      if (this.errors.length > this.maxErrors) this.errors.shift();
      this.sink.error(line);
      for (const fn of this.errorListeners) {
        try {
          fn(entry);
        } catch {
          // a failing listener must never break logging
        }
      }
    } else if (level === "warn") {
      this.sink.warn(line);
    } else {
      this.sink.info(line);
    }
  }
}
