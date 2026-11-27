# Backend

Supabase (Postgres + RLS + Auth + Edge Functions + pg_cron), HyperPay for
top-ups, drand for public randomness, Expo for push. Everything is in
`supabase/`; the app switches from fake data to this backend when
`app/.env.local` has the two public Supabase values.

**Status: written and tested locally, not yet run against a live Supabase
project or the HyperPay sandbox.** Neither service was reachable from the
build environment, and the accounts are yours to create. What was tested is
listed under "Tests"; what still needs a live check is under "Before go-live".

## What you set up (Claude never creates accounts or handles keys)

1. **Supabase project** (Frankfurt region). Then, from the repo root:
   `supabase link --project-ref <ref>` → `supabase db push` (runs
   `supabase/migrations/`) → optionally `supabase db reset --linked` on a dev
   project to load `supabase/seed.sql`.
2. **Phone sign-in:** Dashboard → Authentication → Providers → Phone →
   Twilio Verify (your Twilio account). Rate limits are in `supabase/config.toml`.
3. **HyperPay merchant account** (through your Jordanian bank). Sandbox first:
   access token, one entity id for cards and one for Apple Pay, and a webhook
   pointing at `https://<ref>.supabase.co/functions/v1/payments-webhook`
   (copy its decryption key).
4. **Secrets:** copy `supabase/.env.example` → `supabase/.env`, fill it,
   `supabase secrets set --env-file supabase/.env`, then
   `supabase functions deploy`.
5. **Schedules:** put `project_url` and `cron_secret` in Vault, then run
   `supabase/cron.sql` once in the SQL editor.
6. **App:** copy `app/.env.example` → `app/.env.local` with the project URL and
   anon key. Push needs an EAS project id in `app.json` (`extra.eas.projectId`).
7. **Backups:** Dashboard → Database → Backups. Daily backups are included on
   paid plans; turn on point-in-time recovery before taking real money.

## Map

| piece | where | notes |
| --- | --- | --- |
| core schema + money functions | `supabase/migrations/…01_init.sql` | same SQL as `replica/schema.sql`, tested in the architect step |
| backend additions | `…02_backend.sql` | sign-up trigger, `filled_at`, fixed drand round, `open_product`, views, deletion, client write grants revoked |
| live updates | `…03_realtime.sql` | wallets, notifications, products, orders (RLS applies) |
| top-up start | `functions/topup-start` | member JWT; whole dinars 1–500; max 5 pending per member |
| card page | `functions/pay-page` | HyperPay's own widget: card numbers never touch our app or servers |
| return from payment | `functions/topup-return` | asks HyperPay for the result; never trusts the redirect |
| webhook | `functions/payments-webhook` | AES-GCM decrypt = authenticity; idempotent settle |
| jobs | `functions/jobs` + `cron.sql` | assign-owners, push (1 min), expire-redemptions (1 min), cancel-expired (5 min), reconcile-payments (hourly) |
| account deletion | `functions/delete-account` + `request_account_deletion()` | scrubs personal data, soft-deletes the login, keeps money records |
| app data layer | `app/src/data/supabase.ts` | same `Api` as the fake; errors mapped in `supabase-map.ts` |

## Fairness (how the owner is picked, and why nobody can steer it)

1. `open_product` creates a random secret and publishes only its SHA-256 before anyone can buy.
2. The last purchase stamps `filled_at`.
3. `assign_owner` only accepts the **first drand round after `filled_at`**
   (`drand_round_after()`); any other round is refused. So the operator can't
   pick a round they like.
4. Winner = `sha256(secret ‖ drand value)` → first 60 bits → mod spots → +1. The app
   shows the secret, the round and the value; anyone can recompute it. The app's
   formula and the database's agree (tested on 25 random cases).
5. **Before launch:** verify the drand signature in the job (drand-client), and
   confirm genesis/period at `https://api.drand.sh/info`.

## Tests (all passing)

- `supabase/tests/run.sh`: migrations on a throwaway Postgres, 36 checks (22 must-pass, 14 must-be-refused):
  sign-up trigger, idempotent webhook credit, `open_product`, **second user sees
  none of the first user's wallet, ledger, payments, participations, spots or
  profile**, writes to money tables refused, seeds unreadable, wrong/late drand
  round refused, one notification per member, credit-back, winner id hidden
  from other members, anon can't read results, deletion rules, ledger = balances,
  `service_role` can run jobs and members can't.
- `node --test supabase/functions/_shared/shared.test.ts`: HyperPay result codes,
  3-decimal JOD amounts, webhook decrypt + tamper/wrong-key rejection, checkout
  request shape (token only in header), drand round = SQL, app winner = SQL winner,
  top-up validation, push batching.
- `node --test app/src/data/supabase-map.test.ts`: every SQL exception → app error code, row mapping.
- `deno check` on all six functions; local runs: pay-page rejects injection (400),
  webhook rejects a body not encrypted with our key (401), jobs refuse calls without the secret (401).
- App: `tsc`, `expo lint` clean; full end-to-end flow on the fake layer still passes.

## Before go-live (needs the live services)

- [ ] Run the app against the dev project: sign in by SMS, top up in the HyperPay sandbox, buy, fill, assign.
- [ ] HyperPay: confirm the lines marked `[verify]` in `functions/_shared/hyperpay.ts`
      (base URL, result-code groups, whether JOD takes 3 decimals, entity ids per brand).
- [ ] drand signature verification in `jobs` (TODO in `_shared/drand.ts`).
- [ ] Apple Pay merchant id + domain verification with HyperPay (if offering Apple Pay).
- [ ] Legal opinion on the prize-draw question (see `recon.md`). Launch blocker.

## Security checklist

- [x] secrets only in env vars; `.env*` ignored (examples allowed); only the public anon key is in the app
- [x] input validated on the server: SQL functions check every argument; `topup-start` validates amount and method
- [x] authorisation on every read and write: RLS on all tables, client write grants revoked on money tables, **tested with a second user**
- [x] rate limits: SMS sends and verifications (`config.toml`), max 5 pending top-ups per member
- [x] webhooks verified (AES-GCM with our key; tampered or foreign bodies rejected, tested)
- [ ] uploads: none yet (owner photos come later: size/type limits + a separate bucket then)
- [x] no user data in URLs or logs: logs carry ids and codes only; payment URLs carry the gateway's checkout id only
- [x] dependencies audited: `npm audit --omit=dev` → 3 moderate in Expo's build tooling (`minimatch` via `@expo/fingerprint`); the offered fix downgrades to SDK 52, so wait for an Expo patch
- [ ] privacy policy lists every processor: Supabase, Twilio, HyperPay, Expo (push), drand (no personal data). Written in `/replica-launch`

## Differences from the skill's defaults (and why)

- **No Stripe:** it does not onboard businesses in Jordan. HyperPay hosted checkout plays the same role (no card forms in the app).
- **No email:** members sign in by phone; there is nothing to email in the core loop.
- **Subscriptions:** none. The product sells spots, not plans.

## The owner's store and the admin panel (added 2026-11-15)

Migration `…05_store_admin.sql`: `store_products`, `store_orders`,
`store_order_items`, `checkout_store()` (shopping balance first, then cash;
stock locked; idempotent), and admin functions that refuse anyone who isn't
an admin (`admin_save_product`, `admin_open_product`, `admin_cancel_product`,
`admin_save_store_product`, `admin_set_order_status`,
`admin_set_store_order_status`). Product photos go to the public
`product-images` bucket; only admins can upload.

**Make yourself the owner/admin (once, after you sign up in the app):**
```sql
update profiles set is_admin = true where phone = '+9627XXXXXXXX';
```
For the owner's number (0786055772), ready to paste:
```sql
update profiles set is_admin = true where phone = '+962786055772';
select phone, is_admin from profiles where is_admin = true;   -- check
```
The "لوحة الإدارة" (admin panel) entry then appears under حسابي (Profile).
In the demo build, log in with **79 000 0000** to see it.

Tested in `supabase/tests/run.sh`: a non-admin is refused every admin
action; checkout splits shopping/cash correctly, never oversells, and a retry
doesn't charge twice; other members can't see your orders; the admin sees all;
cancelling returns the money to the same balances and the stock.

## CliQ top-ups, confirmed by the owner (added 2026-11-27)

No gateway needed. Migration `…06_cliq.sql`:
- `app_settings` (admin-only edits via `admin_save_settings`): `cliq_alias`, `cliq_holder`, `cliq_hours`, `card_enabled`. The app shows CliQ only when an alias is set, and card / Google Pay / Apple Pay only when `card_enabled` is true (turn it on after a gateway is connected).
- `cliq_start_topup(amount)` makes a request with a unique reference (`NZ-123456`), max 3 open per member; `cliq_mark_sent()` is the member's "I've transferred"; `admin_review_cliq(id, approve, reason)` credits through `confirm_topup()` (once, even on a double tap) or rejects with a reason. Both notify the member.
- `expire_cliq_requests()` (run by the hourly job) drops requests nobody marked as sent within 24 h. The gateway reconcile job now only looks at `hyperpay:*` payments, never CliQ.

**Owner's routine:** Admin panel → طلبات شحن CliQ → for each request, open your bank app, find a transfer with the **same amount and reference**, then تم الاستلام (or رفض with a reason). Never approve from a screenshot.
