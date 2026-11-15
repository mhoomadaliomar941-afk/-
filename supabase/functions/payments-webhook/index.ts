// HyperPay webhook: encrypted body; decrypting with our key is the authenticity check.
// Retries are safe: settle() is idempotent. Every event id is logged, nothing personal.
import { admin, env, log } from '../_shared/env.ts';
import { decryptWebhook, parseWebhook } from '../_shared/hyperpay.ts';
import { settle } from '../_shared/settle.ts';
import { isUuid } from '../_shared/validate.ts';

Deno.serve(async (req) => {
  if (req.method !== 'POST') return new Response('method', { status: 405 });
  let evt: unknown;
  try {
    evt = await decryptWebhook(env('HYPERPAY_WEBHOOK_KEY'), req.headers.get('X-Initialization-Vector') ?? '',
      req.headers.get('X-Authentication-Tag') ?? '', (await req.text()).trim());
  } catch {
    log('webhook_rejected');
    return new Response('unauthorized', { status: 401 });
  }
  const p = parseWebhook(evt);
  if (!p) return new Response('ignored', { status: 200 });
  if (!isUuid(p.paymentId)) return new Response('ignored', { status: 200 });
  try {
    await settle(admin(), p.paymentId, p.outcome, 'webhook');
    return new Response('ok', { status: 200 });
  } catch (e) {
    log('webhook_error', { paymentId: p.paymentId, reason: (e as Error).message });
    return new Response('retry', { status: 500 }); // HyperPay retries
  }
});
