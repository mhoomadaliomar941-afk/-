// Small pure helpers shared by every system. No Minecraft imports here, so the
// whole file is testable in Node.

/** @param {number} v @param {number} min @param {number} max */
export function clamp(v, min, max) {
  return v < min ? min : v > max ? max : v;
}

/** Round to a fixed number of decimals (for display and stable storage). */
export function round(v, decimals = 2) {
  const f = 10 ** decimals;
  return Math.round(v * f) / f;
}

/** Block coordinate -> chunk coordinate (16x16 columns). */
export function toChunk(blockCoord) {
  return Math.floor(blockCoord / 16);
}

/**
 * FNV-1a 32-bit hash of a string, as 8 hex chars. Used as a checksum to detect
 * corrupted or truncated saved data. Not cryptographic.
 * @param {string} str
 */
export function fnv1a(str) {
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return (h >>> 0).toString(16).padStart(8, "0");
}

/** Exponential moving average step. */
export function ema(prev, sample, alpha) {
  return prev === undefined ? sample : prev + alpha * (sample - prev);
}
