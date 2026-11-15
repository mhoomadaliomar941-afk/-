# Architecture: working name "Spot" (a rebuild of FORTUNEiT's core features)

Built from `replica/recon.md` (a draft, inferred from public listings, with no
screenshots). The name, colours and copy come later, in `/replica-brand`.

## Stack

| layer | choice | why |
| --- | --- | --- |
| mobile app | Expo (React Native) + TypeScript | Android first, the same code gives iOS later; built-in Arabic RTL via `I18nManager` |
| admin + partner portal | Next.js (App Router) on Vercel | one small web app: admins add products and ship orders; store staff scan redemption codes |
| styling | mobile: StyleSheet + typed tokens generated from `tokens.json` (`app/src/theme/tokens.generated.ts`); web portal: Tailwind via `tailwind.tokens.js` | one set of tokens from `/replica-design`; NativeWind was dropped during the build to keep the Expo setup simple |
| database | Postgres on Supabase | money needs real transactions and row locks; RLS for access |
| auth | Supabase Auth, **phone + SMS OTP** (Twilio Verify) | Jordanian users expect a phone number, not email; works with Jordanian numbers |
| payments | **a gateway that serves Jordanian merchants**, e.g. HyperPay (cards, Apple Pay); CliQ later | Stripe does not onboard businesses in Jordan. Confirm the gateway with your bank; the code only assumes "hosted checkout + signed webhook" |
| server logic | Supabase Edge Functions | payment webhook, owner selection, push sending |
| jobs | `pg_cron` calling the Edge Functions | no extra service |
| fair randomness | drand public beacon (`api.drand.sh`) | a public random value nobody controls, including you; see "the parts that bite" |
| push | Expo Push (FCM underneath) | results matter most here; no email needed for the core loop |
| files | Supabase Storage | product photos, owner showcase photos |
| hosting | Supabase (Frankfurt region) + Vercel | closest managed region to Jordan; free tiers at zero users |

One database, no microservices.

## Schema

Tables: 17. Access rules: **Postgres RLS for every read; every write that moves
money goes through a `security definer` function.** Clients never update a
balance directly.

Full SQL: [`schema.sql`](schema.sql). It was run on Postgres 16 with a stand-in
for Supabase's `auth` schema and tested end to end (top-up, double webhook,
buy, retry, oversell, fill, owner pick, credit, redeem, ledger = balances).

| area | tables |
| --- | --- |
| users | profiles, push_tokens |
| catalogue | products, product_seeds (server only) |
| money | wallets, wallet_txns (ledger), payments |
| core loop | participations, spots, assignments |
| delivery | addresses, orders, owner_showcase |
| partners | partner_stores, partner_staff, redemptions |
| messages | notifications |

Key decisions:
- **Money in fils** (`bigint`; 1 JD = 1000 fils, because JOD has 3 decimals).
- **Two balances**: `cash` (topped up; buys spots) and `shopping` (credited when
  you are not picked; spends only at partner stores). Assumption from the
  recon, so flag it if the original lets shopping balance buy spots.
- **Ledger first**: every balance change is a `wallet_txns` row with a unique
  idempotency key, written in the same transaction as the balance. Check
  constraints stop a balance going below zero.
- **Numbered spots**: buying 3 spots creates spots #n..#n+2. The owner is the
  holder of the winning number, so more spots means proportionally better odds,
  and it is checkable.
- **Unfilled products**: optional `closes_at`; past it, `cancel_product` refunds
  everyone in cash.

Database functions (the write API):

| function | who | does |
| --- | --- | --- |
| `buy_spots(product, spots, key)` | member | locks the product, checks open/space/limit/balance, debits cash, numbers the spots, marks full |
| `confirm_topup(payment)` | webhook (service) | marks paid, credits cash, idempotent |
| `assign_owner(product, round, beacon)` | job (service) | verifies the seed hash, picks the spot, creates the order, credits everyone else, notifies |
| `cancel_product(product)` | admin / job | refunds all participations in cash |
| `create_redemption(store, amount)` | member | holds shopping balance, returns a 15-minute code |
| `confirm_redemption(code)` | store staff | marks used |
| `expire_redemptions()` | job | returns held balance of unscanned codes |
| `set_order_address(order, address)` | owner | sets delivery address |

## API

Mobile reads go straight to Supabase (RLS). Writes are the functions above
(`supabase.rpc`) or the Edge Functions below.

| method path | does | who | input | output | flow |
| --- | --- | --- | --- | --- | --- |
| POST auth/otp | send SMS code | anyone | phone | - | F01 |
| POST auth/verify | sign in | anyone | phone, code | session | F01 |
| GET products?status=open | feed | anyone | page | products | F01 |
| GET products/:id | detail + progress | anyone | id | product | F01 |
| RPC buy_spots | take part | member | product, spots, key | participation | F01 |
| POST fn/topup | start a top-up | member | amount, method | checkout URL | F05 |
| POST fn/payments/webhook | gateway confirms | gateway (signed) | gateway payload | 200 | F05 |
| GET participations | my spots | member | status tab | list | F01, F02 |
| GET assignments/:product | verify a result | anyone | product | seed, beacon, spot | F02 |
| GET wallets, wallet_txns | balance + history | member | page | rows | F04, F05 |
| CRUD addresses | delivery addresses | member | address | address | F03 |
| RPC set_order_address | ship here | owner | order, address | order | F03 |
| GET orders | delivery status | member | - | orders | F03 |
| GET owner_showcase | owners wall | anyone | page | rows | F03 |
| GET partner_stores | store list | anyone | governorate | stores | F04 |
| RPC create_redemption | spend at a store | member | store, amount | code | F04 |
| RPC confirm_redemption | scan the code | store staff | code | redemption | F04 |
| GET/PATCH notifications | inbox, mark read | member | - | rows | all |
| POST push_tokens | register device | member | token | - | all |
| admin: POST/PATCH products | create, open (generates seed + hash) | admin (service role, server side) | product | product | ops |
| admin: PATCH orders | ship / deliver | admin | status, tracking | order | F03 |
| admin: POST showcase | publish an owner (with consent) | admin | assignment, photo | row | F03 |
| admin: CRUD partner_stores, partner_staff | manage stores | admin | store | store | F04 |

Routes: 23.

Webhooks in: payment gateway (signature verified, deduplicated by
`external_ref`). Webhooks out: none.

Jobs:
| job | schedule | does |
| --- | --- | --- |
| assign-full-products | every minute | for each `full` product: wait for the **next** drand round after it filled, call `assign_owner`, send push |
| cancel-expired | every 5 min | `cancel_product` for open products past `closes_at` |
| expire-redemptions | every minute | `expire_redemptions()` |
| reconcile-payments | hourly | ask the gateway about `pending` payments older than 30 min (missed webhooks) |
| push-sender | on notification insert | send Expo push for new `notifications` rows |

Only official, public APIs with your own keys: Supabase, Twilio, your payment
gateway, drand, Expo. Nothing of FORTUNEiT's.

## The parts that bite

- **Legality (launch blocker):** "many pay, one gets the product" is very
  likely a prize draw/lottery under Jordanian law, even with credit-back. Build
  it, but get a Jordanian lawyer's opinion before taking real money. It may
  change the model, for example by adding a skill question or a free entry route.
- **Fairness, provable:** the seed hash is public before anyone buys; the seed
  is revealed after; the beacon round is fixed as "the first round after the
  product filled", so neither you nor a buyer can steer the result. Publish the
  formula on the result screen.
- **Races:** the last spot is a row lock (`select ... for update`) in
  `buy_spots`, not a UI check. Tested: overselling fails.
- **Idempotency:** a client key on every purchase, `external_ref` on payments,
  a unique key on every ledger row. Retries and double webhooks are no-ops.
  Tested.
- **Money precision:** integer fils only. Never floats in the app either:
  format at display time.
- **Time zones:** stored UTC, shown in `Asia/Amman` (no DST since 2022, but use
  the zone name, not +03:00).
- **Arabic / RTL:** every screen in both directions from day one; numbers and
  JD amounts stay LTR inside RTL text.
- **Payment gateway:** hosted checkout, 3-D Secure, and a pending state that
  can last minutes. Never credit on the client's redirect, only on the webhook
  or reconcile job.
- **SMS cost and abuse:** OTP rate limits per phone and per IP; Jordan SMS is
  not free.
- **Privacy:** showcase only with recorded consent; let members delete their
  account (balances must be paid out or written off first, so it is a support
  flow, not a button).
- **Partner side:** store staff need their own login and a scanner screen.
  That is the Next.js portal, not the member app.

## Build order

1. **Vertical slice** (proves the stack): phone OTP -> feed -> product detail ->
   buy with a test top-up -> product fills -> owner picked -> result screen.
   Screens S02, S03, S04, S05, S08, S09. Tables: profiles, products,
   product_seeds, wallets, wallet_txns, participations, spots, assignments,
   notifications. Routes: auth, products, buy_spots, a fake top-up,
   assign-full-products job.
2. **Must-haves:** real top-up through the gateway with webhook + reconcile
   (S06, S07; payments), credit-back and result push (S09, S14), delivery
   address and order status (S10; addresses, orders), admin product + order
   screens, cancel-expired job.
3. **Should-haves:** partner stores, redemption codes and the staff scanner
   (S12, S13; partner_stores, partner_staff, redemptions), owners showcase
   (S11), wallet history, profile/language (S15), help/terms (S16),
   onboarding (S01).
4. **Could-haves:** categories/search, referrals.
5. **Fixes from `/replica-entrepreneur`:** after it reads the original's
   reviews.
