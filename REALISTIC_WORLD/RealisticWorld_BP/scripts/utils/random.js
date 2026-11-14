// Seeded random numbers with a state that can be saved in the world, so the
// weather continues the same way after a reload and forecasts can replay the
// future from a copy of the state.

/** mulberry32 step. @param {{ s: number }} st @returns {number} [0, 1) */
export function nextFloat(st) {
  st.s = (st.s + 0x6d2b79f5) >>> 0;
  let t = st.s;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

/** Standard normal sample (Box-Muller). @param {{ s: number }} st */
export function nextGaussian(st) {
  let u = 0;
  while (u === 0) u = nextFloat(st);
  const v = nextFloat(st);
  return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
}

/** Seed from a world seed string or number. @returns {{ s: number }} */
export function seedFrom(value) {
  const str = String(value);
  let h = 2166136261;
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return { s: h >>> 0 };
}
