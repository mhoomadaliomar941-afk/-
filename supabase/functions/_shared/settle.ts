import type { SupabaseClient } from 'npm:@supabase/supabase-js@2.118.0';
import type { PaymentOutcome } from './hyperpay.ts';
import { log } from './env.ts';

/** One place that turns a gateway outcome into money. Idempotent: confirm_topup
 *  credits once per payment, failed only moves pending -> failed. */
export async function settle(db: SupabaseClient, paymentId: string, outcome: PaymentOutcome, source: string) {
  if (outcome === 'paid') {
    const { error } = await db.rpc('confirm_topup', { p_payment: paymentId });
    if (error) throw new Error(`confirm_topup: ${error.message}`);
  } else if (outcome === 'failed') {
    const { error } = await db.from('payments').update({ status: 'failed' }).eq('id', paymentId).eq('status', 'pending');
    if (error) throw new Error(`mark failed: ${error.message}`);
  }
  log('payment_settled', { paymentId, outcome, source });
}
