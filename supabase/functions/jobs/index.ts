// Scheduled work, called by pg_cron (see supabase/cron.sql) with ?task=<name>.
// Protected by CRON_SECRET. Each task is safe to run twice.
import { admin, env, hyperpay, json, log } from '../_shared/env.ts';
import { fetchRound, roundAfter } from '../_shared/drand.ts';
import { getCheckoutStatus } from '../_shared/hyperpay.ts';
import { settle } from '../_shared/settle.ts';
import { pushText, sendPush } from '../_shared/push.ts';
import { methodFromGateway } from '../_shared/validate.ts';

type Task = 'assign-owners' | 'cancel-expired' | 'expire-redemptions' | 'reconcile-payments' | 'push';

Deno.serve(async (req) => {
  if (req.headers.get('x-cron-secret') !== env('CRON_SECRET')) return json({ error: 'unauthorized' }, 401);
  const task = new URL(req.url).searchParams.get('task') as Task;
  const db = admin();
  try {
    const result = await ({
      // full products -> wait for the first drand round after they filled -> assign
      'assign-owners': async () => {
        const { data } = await db.from('products').select('id, filled_at').eq('status', 'full').not('filled_at', 'is', null).limit(20);
        let done = 0;
        for (const p of data ?? []) {
          const beacon = await fetchRound(roundAfter(new Date(p.filled_at)));
          if (!beacon) continue; // round not produced yet; next minute
          const { error } = await db.rpc('assign_owner', { p_product: p.id, p_round: beacon.round, p_beacon: beacon.randomness });
          if (error) log('assign_error', { productId: p.id, reason: error.message }); else done++;
        }
        return { assigned: done };
      },
      'cancel-expired': async () => {
        const { data } = await db.from('products').select('id').eq('status', 'open').lt('closes_at', new Date().toISOString());
        for (const p of data ?? []) await db.rpc('cancel_product', { p_product: p.id });
        return { cancelled: data?.length ?? 0 };
      },
      'expire-redemptions': async () => {
        const { data, error } = await db.rpc('expire_redemptions');
        if (error) throw error;
        return { expired: data };
      },
      // missed webhooks: ask the gateway about card/wallet top-ups pending for more than 30 minutes
      'reconcile-payments': async () => {
        const cutoff = new Date(Date.now() - 30 * 60_000).toISOString();
        const { data } = await db.from('payments').select('id, external_ref, gateway, created_at').eq('status', 'pending').like('gateway', 'hyperpay%').lt('created_at', cutoff).limit(50);
        for (const p of data ?? []) {
          if (!p.external_ref) { await settle(db, p.id, 'failed', 'reconcile'); continue; }
          const { outcome } = await getCheckoutStatus(hyperpay(methodFromGateway(p.gateway)), p.external_ref);
          const stale = Date.now() - new Date(p.created_at).getTime() > 24 * 3600_000;
          await settle(db, p.id, outcome === 'pending' && stale ? 'failed' : outcome, 'reconcile'); // abandoned after a day
        }
        // CliQ is confirmed by the owner, never by the gateway: only drop requests nobody marked as sent
        const { data: expired } = await db.rpc('expire_cliq_requests');
        return { checked: data?.length ?? 0, cliqExpired: Number(expired ?? 0) };
      },
      push: async () => {
        const { data: notes } = await db.from('notifications').select('id, user_id, type, payload').is('pushed_at', null).order('created_at').limit(500);
        if (!notes?.length) return { sent: 0 };
        const { data: tokens } = await db.from('push_tokens').select('token, user_id').in('user_id', [...new Set(notes.map((n) => n.user_id))]);
        const msgs = notes.flatMap((n) => (tokens ?? []).filter((t) => t.user_id === n.user_id).map((t) => ({
          to: t.token, ...(pushText[n.type]?.(n.payload) ?? { title: 'نزيه', body: '' }), data: { type: n.type, ...n.payload },
        })));
        if (msgs.length) await sendPush(msgs);
        await db.from('notifications').update({ pushed_at: new Date().toISOString() }).in('id', notes.map((n) => n.id));
        return { sent: msgs.length };
      },
    } as Record<Task, () => Promise<unknown>>)[task]?.();
    if (result === undefined) return json({ error: 'unknown task' }, 400);
    log('job_done', { task, ...(result as Record<string, number>) });
    return json(result);
  } catch (e) {
    log('job_failed', { task, reason: (e as Error).message }); // dead-letter: pg_cron keeps the response, logs keep this line
    return json({ error: 'failed' }, 500);
  }
});
