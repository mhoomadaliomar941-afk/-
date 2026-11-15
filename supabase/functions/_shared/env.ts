import { createClient, SupabaseClient } from 'npm:@supabase/supabase-js@2.118.0';
import type { HyperPayConfig } from './hyperpay.ts';
import type { Method } from './validate.ts';

export function env(name: string): string {
  const v = Deno.env.get(name);
  if (!v) throw new Error(`missing env ${name}`);
  return v;
}

/** Service-role client: bypasses RLS. Server only, never sent to the app. */
export function admin(): SupabaseClient {
  return createClient(env('SUPABASE_URL'), env('SUPABASE_SERVICE_ROLE_KEY'), { auth: { persistSession: false } });
}

/** A client acting as the caller (their JWT), so RLS and auth.uid() apply. */
export function asUser(req: Request): SupabaseClient {
  return createClient(env('SUPABASE_URL'), env('SUPABASE_ANON_KEY'), {
    auth: { persistSession: false },
    global: { headers: { Authorization: req.headers.get('Authorization') ?? '' } },
  });
}

/** One HyperPay entity per method. Google Pay usually rides on the card entity, so it
 *  falls back to it when HYPERPAY_ENTITY_ID_GOOGLEPAY isn't set. [verify with HyperPay] */
export function hyperpay(method: Method): HyperPayConfig {
  const entityId = method === 'apple_pay' ? env('HYPERPAY_ENTITY_ID_APPLEPAY')
    : method === 'google_pay' ? (Deno.env.get('HYPERPAY_ENTITY_ID_GOOGLEPAY') || env('HYPERPAY_ENTITY_ID_CARD'))
    : env('HYPERPAY_ENTITY_ID_CARD');
  return { baseUrl: env('HYPERPAY_BASE_URL'), accessToken: env('HYPERPAY_ACCESS_TOKEN'), entityId };
}

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/** Logs without user data: ids and codes only. */
export const log = (event: string, fields: Record<string, string | number | boolean | null> = {}) =>
  console.log(JSON.stringify({ event, ...fields }));
