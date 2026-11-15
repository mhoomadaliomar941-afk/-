// POST with the member's JWT. Scrubs personal data in the database, then soft-deletes the login.
import { admin, asUser, json, log } from '../_shared/env.ts';

Deno.serve(async (req) => {
  if (req.method !== 'POST') return json({ error: 'method' }, 405);
  const userDb = asUser(req);
  const { data: { user } } = await userDb.auth.getUser();
  if (!user) return json({ error: 'not_signed_in' }, 401);
  const { error } = await userDb.rpc('request_account_deletion');
  if (error) {
    const code = ['balance_not_zero', 'active_participation', 'open_order'].find((c) => error.message.includes(c)) ?? 'failed';
    return json({ error: code }, 409);
  }
  const { error: authError } = await admin().auth.admin.deleteUser(user.id, true); // soft delete: login gone, ledger keeps its id
  if (authError) return json({ error: 'auth' }, 500);
  log('account_deleted', { userId: user.id });
  return json({ ok: true });
});
