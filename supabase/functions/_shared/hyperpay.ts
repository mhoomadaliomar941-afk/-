// HyperPay (OPPWA platform) — hosted checkout ("COPYandPAY") + encrypted webhooks.
// Written from HyperPay's public integration docs. The docs site was not reachable
// from the build environment: check every marked line against the sandbox before
// going live (see replica/backend.md).

export interface HyperPayConfig {
  baseUrl: string;      // sandbox: https://eu-test.oppwa.com, live: https://eu-prod.oppwa.com  [verify]
  accessToken: string;  // Bearer token from the HyperPay dashboard
  entityId: string;     // one entity id per payment brand group (cards / Apple Pay)        [verify]
}

export type PaymentOutcome = 'paid' | 'pending' | 'failed';

// Result code groups from the OPPWA docs: success, success-needing-review, pending.  [verify]
const SUCCESS = /^(000\.000\.|000\.100\.1|000\.[36])/;
const REVIEW = /^(000\.400\.0[^3]|000\.400\.100)/;
const PENDING = /^(000\.200)|^(800\.400\.5|100\.400\.500)/;

export function classifyResult(code: string): PaymentOutcome {
  if (SUCCESS.test(code) || REVIEW.test(code)) return 'paid';
  if (PENDING.test(code)) return 'pending';
  return 'failed';
}

/** JOD has 3 decimals. [verify: whether the entity accepts 3 decimals or wants 2] */
export function formatAmount(fils: number): string {
  if (!Number.isInteger(fils) || fils <= 0) throw new Error('amount must be a positive integer of fils');
  return `${Math.floor(fils / 1000)}.${String(fils % 1000).padStart(3, '0')}`;
}

export async function createCheckout(cfg: HyperPayConfig, p: { amountFils: number; paymentId: string }, f: typeof fetch = fetch) {
  const body = new URLSearchParams({
    entityId: cfg.entityId,
    amount: formatAmount(p.amountFils),
    currency: 'JOD',
    paymentType: 'DB',
    merchantTransactionId: p.paymentId, // our payments.id, echoed back in the webhook
  });
  const res = await f(`${cfg.baseUrl}/v1/checkouts`, {
    method: 'POST', body,
    headers: { Authorization: `Bearer ${cfg.accessToken}`, 'Content-Type': 'application/x-www-form-urlencoded' },
  });
  const json = await res.json() as { id?: string; result?: { code: string; description: string } };
  if (!res.ok || !json.id) throw new Error(`hyperpay checkout failed: ${json.result?.code ?? res.status}`);
  return { checkoutId: json.id };
}

export async function getCheckoutStatus(cfg: HyperPayConfig, checkoutId: string, f: typeof fetch = fetch) {
  if (!/^[A-Za-z0-9.\-]{8,64}$/.test(checkoutId)) throw new Error('bad checkout id');
  const res = await f(`${cfg.baseUrl}/v1/checkouts/${checkoutId}/payment?entityId=${encodeURIComponent(cfg.entityId)}`, {
    headers: { Authorization: `Bearer ${cfg.accessToken}` },
  });
  const json = await res.json() as { result?: { code: string }; merchantTransactionId?: string };
  const code = json.result?.code ?? '';
  return { outcome: classifyResult(code), code, paymentId: json.merchantTransactionId ?? null };
}

const hex = (s: string) => {
  if (!/^([0-9a-fA-F]{2})*$/.test(s)) throw new Error('not hex');
  return Uint8Array.from(s.match(/../g) ?? [], (b) => parseInt(b, 16));
};

/** Webhook body is hex(AES-256-GCM ciphertext); IV and auth tag arrive in headers.
 *  A body that does not decrypt with our key is rejected: this is the signature check. */
export async function decryptWebhook(keyHex: string, ivHex: string, tagHex: string, bodyHex: string): Promise<unknown> {
  const key = await crypto.subtle.importKey('raw', hex(keyHex), 'AES-GCM', false, ['decrypt']);
  const data = new Uint8Array([...hex(bodyHex), ...hex(tagHex)]); // WebCrypto wants ciphertext||tag
  const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: hex(ivHex), tagLength: 128 }, key, data);
  return JSON.parse(new TextDecoder().decode(plain));
}

/** Pulls what we need out of a decrypted webhook; null for events we ignore. */
export function parseWebhook(evt: unknown): { paymentId: string; checkoutId: string | null; outcome: PaymentOutcome } | null {
  const e = evt as { type?: string; payload?: { merchantTransactionId?: string; ndc?: string; result?: { code?: string } } };
  if (e?.type !== 'PAYMENT' || !e.payload?.merchantTransactionId || !e.payload.result?.code) return null;
  return { paymentId: e.payload.merchantTransactionId, checkoutId: e.payload.ndc ?? null, outcome: classifyResult(e.payload.result.code) };
}
