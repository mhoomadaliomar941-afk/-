// drand public randomness beacon (League of Entropy), default chain.
// Must match drand_round_after() in the database migration. [verify at https://api.drand.sh/info]
export const DRAND = { baseUrl: 'https://api.drand.sh', genesis: 1595431050, period: 30 };

/** The first round produced after `at` (same formula as SQL drand_round_after). */
export function roundAfter(at: Date): number {
  return Math.floor((at.getTime() / 1000 - DRAND.genesis) / DRAND.period) + 2;
}
export function roundTime(round: number): Date {
  return new Date((DRAND.genesis + (round - 1) * DRAND.period) * 1000);
}

export async function fetchRound(round: number, f: typeof fetch = fetch): Promise<{ round: number; randomness: string } | null> {
  if (roundTime(round).getTime() > Date.now()) return null; // not produced yet
  const res = await f(`${DRAND.baseUrl}/public/${round}`);
  if (!res.ok) throw new Error(`drand ${res.status}`);
  const j = await res.json() as { round: number; randomness: string };
  if (j.round !== round || !/^[0-9a-f]{64}$/.test(j.randomness)) throw new Error('unexpected drand response');
  // The signature is not verified here; anyone can re-check the value against drand.
  // TODO before launch: verify with the drand-client library.
  return j;
}
