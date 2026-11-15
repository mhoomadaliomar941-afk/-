// POST { amountFils, method } with the member's JWT -> { paymentId, payUrl }
import { admin, asUser, env, hyperpay, json, log } from '../_shared/env.ts';
import { createCheckout } from '../_shared/hyperpay.ts';
import { parseTopup } from '../_shared/validate.ts';

Deno.serve(async (req) => {
  if (req.method !== 'POST') return json({ error: 'method' }, 405);
  const { data: { user } } = await asUser(req).auth.getUser();
  if (!user) return json({ error: 'not_signed_in' }, 401);

  let input;
  try { input = parseTopup(await req.json()); } catch (e) { return json({ error: 'invalid', message: (e as Error).message }, 400); }

  const db = admin();
  // max 5 pending top-ups per member: stops checkout spam
  const { count } = await db.from('payments').select('id', { count: 'exact', head: true }).eq('user_id', user.id).eq('status', 'pending');
  if ((count ?? 0) >= 5) return json({ error: 'too_many_pending' }, 429);

  const { data: pay, error } = await db.from('payments')
    .insert({ user_id: user.id, gateway: `hyperpay:${input.method}`, amount_fils: input.amountFils }).select('id').single();
  if (error || !pay) return json({ error: 'db' }, 500);

  try {
    const { checkoutId } = await createCheckout(hyperpay(input.method), { amountFils: input.amountFils, paymentId: pay.id });
    await db.from('payments').update({ external_ref: checkoutId }).eq('id', pay.id);
    log('topup_started', { paymentId: pay.id, method: input.method });
    const payUrl = `${env('SUPABASE_URL')}/functions/v1/pay-page?id=${encodeURIComponent(checkoutId)}&m=${input.method}`;
    return json({ paymentId: pay.id, payUrl });
  } catch (e) {
    await db.from('payments').update({ status: 'failed' }).eq('id', pay.id);
    log('topup_start_failed', { paymentId: pay.id, reason: (e as Error).message });
    return json({ error: 'gateway' }, 502);
  }
});
