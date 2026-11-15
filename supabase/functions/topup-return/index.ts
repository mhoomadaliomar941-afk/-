// HyperPay sends the shopper here after paying (?id=<checkoutId>&resourcePath=...).
// We ask HyperPay for the result (never trust the redirect itself), settle, and hand back to the app.
import { admin, env, hyperpay, log } from '../_shared/env.ts';
import { getCheckoutStatus } from '../_shared/hyperpay.ts';
import { methodFromGateway } from '../_shared/validate.ts';
import { settle } from '../_shared/settle.ts';

Deno.serve(async (req) => {
  const id = new URL(req.url).searchParams.get('id') ?? '';
  const app = env('APP_RETURN_URL'); // e.g. nazih://topup
  const db = admin();
  const { data: pay } = await db.from('payments').select('id, gateway').eq('external_ref', id).maybeSingle();
  if (!pay) return Response.redirect(`${app}?status=unknown`, 302);
  try {
    const { outcome } = await getCheckoutStatus(hyperpay(methodFromGateway(pay.gateway)), id);
    await settle(db, pay.id, outcome, 'return');
    return Response.redirect(`${app}?status=${outcome}`, 302);
  } catch (e) {
    log('topup_return_error', { paymentId: pay.id, reason: (e as Error).message });
    return Response.redirect(`${app}?status=pending`, 302); // the webhook or reconcile job will settle it
  }
});
