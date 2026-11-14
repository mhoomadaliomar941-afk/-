// Corruption-resistant JSON storage on top of dynamic properties.
//
// Layout for a key K (all ids get the add-on prefix):
//   K@a, K@b            headers  "RW1|<seq>|<checksum>|<chunkCount>|<length>"
//   K@a#0..n, K@b#0..n  string chunks of the JSON text
//
// Every save goes to the slot that does NOT hold the newest valid copy, and the
// header is written last. If the game closes in the middle of a save, the other
// slot still holds the previous complete copy, and load() falls back to it.
// A checksum detects truncated or edited data.

import { fnv1a } from "../utils/math.js";

/**
 * Minimal key/value backend. In game this wraps world dynamic properties; in
 * tests it is a Map.
 * @typedef {{
 *   get(id: string): unknown,
 *   set(id: string, value: string | number | boolean | undefined): void,
 *   ids?(): string[],
 *   totalBytes?(): number,
 * }} StorageBackend
 */

/** Characters per chunk. The game limits one string property to 32767 bytes;
 * 10000 chars stays under that even if every char needs 3 bytes in UTF-8. */
export const CHUNK_CHARS = 10000;
const HEADER_TAG = "RW1";
const SLOTS = /** @type {const} */ (["a", "b"]);

/**
 * ok=true: value, slot, seq and recovered are set.
 * ok=false: missing (nothing was ever saved) and error are set.
 * @typedef {{ ok: boolean, value?: any, slot?: string, seq?: number, recovered?: boolean, missing?: boolean, error?: string }} LoadResult
 */

export class Storage {
  /** @param {StorageBackend} backend @param {string} prefix */
  constructor(backend, prefix) {
    this.backend = backend;
    this.prefix = prefix;
    this.stats = { saves: 0, loads: 0, recoveries: 0, failures: 0, lastSaveChars: 0 };
  }

  /** @param {string} key @param {string} slot */
  _headerId(key, slot) {
    return `${this.prefix}${key}@${slot}`;
  }

  /** @param {string} key @param {string} slot @param {number} i */
  _chunkId(key, slot, i) {
    return `${this.prefix}${key}@${slot}#${i}`;
  }

  /** @returns {{ seq: number, crc: string, chunks: number, length: number } | null} */
  _readHeader(key, slot) {
    const raw = this.backend.get(this._headerId(key, slot));
    if (typeof raw !== "string") return null;
    const parts = raw.split("|");
    if (parts.length !== 5 || parts[0] !== HEADER_TAG) return null;
    const seq = Number(parts[1]);
    const chunks = Number(parts[3]);
    const length = Number(parts[4]);
    if (!Number.isInteger(seq) || !Number.isInteger(chunks) || !Number.isInteger(length)) return null;
    if (chunks < 0 || chunks > 4096 || length < 0) return null;
    return { seq, crc: parts[2], chunks, length };
  }

  /**
   * Save any JSON-serialisable value.
   * @param {string} key
   * @param {unknown} value
   * @returns {{ ok: boolean, chars: number, chunks: number, error?: string }}
   */
  save(key, value) {
    let json;
    try {
      json = JSON.stringify(value);
    } catch (e) {
      this.stats.failures++;
      return { ok: false, chars: 0, chunks: 0, error: `not serialisable: ${/** @type {any} */ (e)?.message}` };
    }
    if (json === undefined) json = "null";

    const headers = SLOTS.map((s) => this._readHeader(key, s));
    // Newest valid-looking slot stays untouched; write into the other one.
    const newestIdx = headers[0] && headers[1] ? (headers[0].seq >= headers[1].seq ? 0 : 1) : headers[0] ? 0 : headers[1] ? 1 : -1;
    const targetIdx = newestIdx === 0 ? 1 : 0;
    const target = SLOTS[targetIdx];
    const nextSeq = Math.max(headers[0]?.seq ?? 0, headers[1]?.seq ?? 0) + 1;

    const chunkCount = Math.max(1, Math.ceil(json.length / CHUNK_CHARS));
    try {
      // Invalidate the target header first so a half-written slot is never
      // mistaken for a valid one.
      this.backend.set(this._headerId(key, target), undefined);
      for (let i = 0; i < chunkCount; i++) {
        this.backend.set(this._chunkId(key, target, i), json.slice(i * CHUNK_CHARS, (i + 1) * CHUNK_CHARS));
      }
      const oldCount = headers[targetIdx]?.chunks ?? 0;
      for (let i = chunkCount; i < oldCount; i++) this.backend.set(this._chunkId(key, target, i), undefined);
      this.backend.set(
        this._headerId(key, target),
        [HEADER_TAG, nextSeq, fnv1a(json), chunkCount, json.length].join("|"),
      );
    } catch (e) {
      this.stats.failures++;
      return { ok: false, chars: json.length, chunks: chunkCount, error: String(/** @type {any} */ (e)?.message ?? e) };
    }
    this.stats.saves++;
    this.stats.lastSaveChars = json.length;
    return { ok: true, chars: json.length, chunks: chunkCount };
  }

  /**
   * Load the newest valid copy of a key.
   * @param {string} key
   * @returns {LoadResult}
   */
  load(key) {
    this.stats.loads++;
    const candidates = SLOTS.map((slot) => ({ slot, header: this._readHeader(key, slot) }))
      .filter((c) => c.header !== null)
      .sort((x, y) => /** @type {any} */ (y.header).seq - /** @type {any} */ (x.header).seq);

    if (candidates.length === 0) return { ok: false, missing: true, error: "no saved data" };

    let firstError = "";
    for (let idx = 0; idx < candidates.length; idx++) {
      const { slot } = candidates[idx];
      const header = /** @type {NonNullable<ReturnType<Storage["_readHeader"]>>} */ (candidates[idx].header);
      let json = "";
      let broken = false;
      for (let i = 0; i < header.chunks; i++) {
        const part = this.backend.get(this._chunkId(key, slot, i));
        if (typeof part !== "string") {
          broken = true;
          break;
        }
        json += part;
      }
      let error = "";
      if (broken) error = `slot ${slot}: missing chunk`;
      else if (json.length !== header.length) error = `slot ${slot}: length ${json.length} != ${header.length}`;
      else if (fnv1a(json) !== header.crc) error = `slot ${slot}: checksum mismatch`;
      if (!error) {
        try {
          const value = JSON.parse(json);
          const recovered = idx > 0;
          if (recovered) this.stats.recoveries++;
          return { ok: true, value, slot, seq: header.seq, recovered };
        } catch {
          error = `slot ${slot}: invalid JSON`;
        }
      }
      if (!firstError) firstError = error;
    }
    this.stats.failures++;
    return { ok: false, missing: false, error: `all copies corrupt (${firstError})` };
  }

  /** Remove every property of a key. @param {string} key */
  delete(key) {
    for (const slot of SLOTS) {
      const header = this._readHeader(key, slot);
      const count = Math.max(header?.chunks ?? 0, 1);
      for (let i = 0; i < count; i++) this.backend.set(this._chunkId(key, slot, i), undefined);
      this.backend.set(this._headerId(key, slot), undefined);
    }
  }

  /** Number of dynamic properties owned by this add-on (debug/dashboard). */
  countOwnProperties() {
    if (!this.backend.ids) return -1;
    return this.backend.ids().filter((id) => id.startsWith(this.prefix)).length;
  }

  /** Total bytes used by all world dynamic properties, if the backend knows. */
  totalBytes() {
    return this.backend.totalBytes ? this.backend.totalBytes() : -1;
  }
}

/** In-memory backend used by tests and by the in-game self-test sandbox. */
export class MemoryBackend {
  constructor() {
    /** @type {Map<string, string | number | boolean>} */
    this.map = new Map();
  }
  get(id) {
    return this.map.get(id);
  }
  set(id, value) {
    if (value === undefined) this.map.delete(id);
    else this.map.set(id, value);
  }
  ids() {
    return [...this.map.keys()];
  }
  totalBytes() {
    let n = 0;
    for (const [k, v] of this.map) n += k.length + String(v).length;
    return n;
  }
}
