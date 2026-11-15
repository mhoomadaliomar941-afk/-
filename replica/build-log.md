# Build log

App: `app/` (Expo SDK 58, Expo Router, TypeScript). Arabic, right-to-left.
Data: fake in-memory layer (`app/src/data/fake.ts`) behind the `Api` interface
in `app/src/data/types.ts`. Same function names and rules as the SQL in
`replica/schema.sql`. `/replica-backend` swaps in Supabase without touching screens.

Checked on every screen: web build at 390px, light and dark, no console errors,
`tsc` clean, `expo lint` clean. Screenshots: `replica/clone-screens/` (+ `dark/`).
Keyboard-only: login -> open product -> choose quantity -> pay works.

| ID | date | status | missing | harder than expected |
| --- | --- | --- | --- | --- |
| shell | 2026-11-15 | done | - | Expo docs site and SDK version API blocked; installed with Expo's offline version map. `tsc` crashed until `dist/` was excluded |
| S01 onboarding | 2026-11-15 | done | "seen" flag is in memory (storage comes with backend) | - |
| S02 login + OTP | 2026-11-15 | partial | real SMS (fake code 123456), rate limiting | - |
| S03 home feed | 2026-11-15 | done | product photos (icons for now), categories/search (could) | - |
| S04 product detail | 2026-11-15 | done | photo gallery | - |
| S05 confirm sheet | 2026-11-15 | done | - | a fresh idempotency key per checkout: the sheet now mounts only while open |
| S06 top-up | 2026-11-15 | partial | real gateway + webhook (fake succeeds after 250ms) | - |
| S07 wallet | 2026-11-15 | done | - | "+"/"−" landed on the wrong side of numbers in RTL; the sign now sits inside the LTR isolate (checked in the DOM) |
| S08 my participations | 2026-11-15 | done | - | - |
| S09 result + verify | 2026-11-15 | done | real public beacon (drand) in the job | - |
| S10 delivery | 2026-11-15 | done (member side) | admin shipping screens | - |
| S11 owners | 2026-11-15 | done | owner photos with consent | - |
| S12 partner stores | 2026-11-15 | done | map / distance | - |
| S13 pay at store | 2026-11-15 | partial | store-staff scanner (Next.js portal), real QR (icon stands in) | - |
| S14 notifications | 2026-11-15 | partial | push delivery | - |
| S15 profile | 2026-11-15 | done | English + language switch, account deletion flow | - |
| S16 help | 2026-11-15 | partial | terms and privacy pages (with /replica-launch) | - |

Not built, bigger than a screen (say so, not quietly half-ship):
- **Admin + partner portal** (Next.js): add products, open them (seed + hash),
  ship orders, store staff scanning codes. Needed before any real launch.
- **Real owner selection job** with drand, push notifications, payment gateway:
  all `/replica-backend`.

Known rough edges:
- Web header back arrow does not mirror for RTL (native does via I18nManager).
- Reloading the web page resets the fake data layer (by design until backend).
- One end-to-end run in dark mode timed out once and passed on rerun: test timing, watch it in `/replica-test`.

## Backend (2026-11-15)

| part | status | note |
| --- | --- | --- |
| migrations + RLS | done, tested locally | 36 SQL checks incl. a second user; found and fixed: members could read winners' ids, anon could read results, server role had lost access to the money functions |
| Edge Functions | written, type-checked, partly run locally | not yet run against live Supabase/HyperPay (unreachable from here, and the accounts are yours) |
| app → Supabase | written, type-checked, mappers tested | switches on with `app/.env.local`; fake layer unchanged and still passes the full flow |
| account deletion | done (fake + backend) | blocked while a balance, an open product or an open delivery exists |
