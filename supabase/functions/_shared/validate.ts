export const MIN_TOPUP_FILS = 1_000;   // 1 JD
export const MAX_TOPUP_FILS = 500_000; // 500 JD per top-up

export function parseTopup(body: unknown): { amountFils: number; method: 'card' | 'apple_pay' } {
  const b = body as { amountFils?: unknown; method?: unknown };
  const amountFils = b?.amountFils;
  if (typeof amountFils !== 'number' || !Number.isInteger(amountFils) || amountFils < MIN_TOPUP_FILS || amountFils > MAX_TOPUP_FILS || amountFils % 1000 !== 0) {
    throw new Error('amountFils must be whole dinars between 1 and 500');
  }
  if (b.method !== 'card' && b.method !== 'apple_pay') throw new Error('method must be card or apple_pay');
  return { amountFils, method: b.method };
}

export const isUuid = (s: unknown): s is string => typeof s === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(s);
