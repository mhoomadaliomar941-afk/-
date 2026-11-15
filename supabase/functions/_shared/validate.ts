export const MIN_TOPUP_FILS = 1_000;   // 1 JD
export const MAX_TOPUP_FILS = 500_000; // 500 JD per top-up

export const METHODS = ['card', 'apple_pay', 'google_pay'] as const;
export type Method = (typeof METHODS)[number];
const isMethod = (m: unknown): m is Method => typeof m === 'string' && (METHODS as readonly string[]).includes(m);

/** payments.gateway is stored as "hyperpay:<method>". */
export function methodFromGateway(gateway: string | null | undefined): Method {
  const m = (gateway ?? '').split(':')[1];
  return isMethod(m) ? m : 'card';
}

export function parseTopup(body: unknown): { amountFils: number; method: Method } {
  const b = body as { amountFils?: unknown; method?: unknown };
  const amountFils = b?.amountFils;
  if (typeof amountFils !== 'number' || !Number.isInteger(amountFils) || amountFils < MIN_TOPUP_FILS || amountFils > MAX_TOPUP_FILS || amountFils % 1000 !== 0) {
    throw new Error('amountFils must be whole dinars between 1 and 500');
  }
  if (!isMethod(b.method)) throw new Error(`method must be one of ${METHODS.join(', ')}`);
  return { amountFils, method: b.method };
}

export const isUuid = (s: unknown): s is string => typeof s === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(s);
