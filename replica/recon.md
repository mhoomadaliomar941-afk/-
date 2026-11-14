# Recon map: FORTUNEiT: Own it for less (Android)

Scope: the core loop: browse a product, buy spots, product closes, an owner is
picked, everyone else gets shopping balance, owner gets delivery. Plus wallet
and partner-store redemption.
For: the user's own product (confirm: own business / niche / to sell)
Date: 2026-11-14
Status: **DRAFT from public search results only.** The Play listing, its
screenshots and fortuneit.app could not be opened from this session (network
blocked), so every screen below is inferred, not seen. Re-run with access or
with screenshots to firm it up.

## Sources

| # | source | URL | notes |
| --- | --- | --- | --- |
| 1 | Play Store listing | https://play.google.com/store/apps/details?id=com.softylus.fortuneit&hl=en | read via search summaries only; 2.6-3.2 stars, 21-94 reviews; "35,000+ members" |
| 2 | Play data safety | https://play.google.com/store/apps/datasafety?id=com.softylus.fortuneit&hl=en | developer says no data collected or shared |
| 3 | Developer page | https://play.google.com/store/apps/dev?id=5545759754606650959&hl=en_US | Softylus Technologies (UK-registered) |
| 4 | Marketing site | https://fortuneit.app/ | "14,000+ members"; authentic products from authorized distributors |
| 5 | Features page | https://fortuneit.app/features | browse -> participate -> own; refund as shopping balance |
| 6 | user's own account | - | not used |

## Core loop

Pay a small amount (from 1 JD) for a spot in a premium product; when every spot
is taken one member becomes the owner and gets it delivered, and everyone else
gets their money back as in-app shopping balance spendable at partner stores.

## Screens

All rows are **inferred** (no screenshots seen).

| ID | screen | route / how to reach | purpose | key components | states seen |
| --- | --- | --- | --- | --- | --- |
| S01 | Splash / onboarding | first launch | explain browse -> participate -> own | carousel, CTA | (inferred) |
| S02 | Log in / OTP | after onboarding | account by phone (guess) | phone input, OTP input | error, resend |
| S03 | Home feed | tab: Home | browse open products | product card, progress bar, banner | empty, loading, filled |
| S04 | Product detail | tap a card | price per spot, spots filled/total, specs | gallery, progress, qty stepper, CTA | open, almost full, closed |
| S05 | Participate / confirm | S04 CTA | choose spots, pay from wallet | summary, wallet balance, confirm | insufficient balance |
| S06 | Top up wallet | S05 or Wallet | add money | amount chips, payment method list | pending, failed, success |
| S07 | Wallet | tab: Wallet | balance + history | balance card, transaction list | empty, filled |
| S08 | My participations | tab: Mine | track spots | segmented tabs (active/closed/owned), list | empty, filled |
| S09 | Result | push / S08 | outcome of a closed product | result banner, credited amount, CTA | owner, not selected |
| S10 | Delivery address / order | S09 (owner) | ship the product | address form, order status timeline | pending, shipped, delivered |
| S11 | Owners showcase | tab or home section | social proof | owner card (photo, product, date) | empty, filled |
| S12 | Partner stores | Wallet / tab | where to spend balance | store list, map/city filter | empty, filled |
| S13 | Redeem at store | S12 store | pay at a store with balance | QR or code, amount | expired, used (guess) |
| S14 | Notifications | bell icon | results, new products | notification list | empty, filled |
| S15 | Profile / settings | tab: Profile | account, language, logout | list rows, language toggle | - |
| S16 | Help / FAQ / terms | S15 | rules and support | accordion, contact | - |

## Flows

```
F01 Member takes part in a product
    S03 -> S04 -> S05 -> (S06 if balance low) -> S05 confirm -> S08
    happy path clicks: ~4 (with balance), ~7 (with top-up)
    edge: product fills while paying, payment fails, last spot race, balance short

F02 Product closes and an owner is picked
    (system) all spots filled -> owner selected -> S14 push -> S09
    edge: spots never fill (deadline? refund?), selected member unreachable

F03 Owner receives the product
    S09 (owner) -> S10 address -> order tracking -> delivered -> S11 showcase
    happy path clicks: ~3
    edge: address outside coverage, delivery failed, owner declines showcase

F04 Not selected: spend the balance
    S09 (not selected) -> S07 -> S12 -> S13 redeem
    happy path clicks: ~4
    edge: partial redemption, store offline, balance expiry (unknown)

F05 Top up wallet
    S07 -> S06 -> gateway -> S07
    edge: gateway timeout, double charge, pending state
```

## Components

| component | variants | states | used on |
| --- | --- | --- | --- |
| Button | primary, secondary, ghost | default, pressed, disabled, loading | all |
| Product card | feed, compact | open, almost full, closed | S03, S08 |
| Spots progress bar | thin, large | 0-100%, full | S03, S04 |
| Quantity stepper | - | min, max, disabled | S04, S05 |
| Wallet balance card | large, inline | normal, low | S05, S07 |
| Transaction row | top-up, spend, credit, redeem | pending, done, failed | S07 |
| Payment method row | card, local wallet | selected, unavailable | S06 |
| Result banner | owner, not selected | - | S09 |
| Owner card | - | - | S11 |
| Store row | - | open, closed | S12 |
| OTP input | - | typing, error | S02 |
| Bottom tab bar | 4-5 tabs | active | app shell |
| Toast / snackbar | success, error | - | all |
| Empty state | per list | - | S03, S07, S08, S12, S14 |

## Inferred data model

```
User         id, phone, name, language, created_at
             evidence: account needed to pay and deliver; confidence: high (fields guess)
Product      id, title, brand, images, specs, retail_price, spot_price (>=1 JD),
             total_spots, filled_spots, status (open | full | assigned | delivered), closes_at?
             evidence: listing "participation details", "starting from 1 JD"; confidence: high
Participation id, user_id, product_id, spots, amount, created_at
             evidence: "secure a spot"; confidence: high
Assignment   id, product_id, owner_user_id, assigned_at, method, audit_ref
             evidence: "one member is chosen as owner when all spots fill"; confidence: high (method unknown)
Wallet       user_id, balance
WalletTxn    id, user_id, type (topup | spend | credit | redeem | refund), amount, ref, status
             evidence: "top up your wallet", "balance goes back automatically"; confidence: high
Payment      id, user_id, gateway, amount, status, external_ref; confidence: medium
Order        id, assignment_id, address, status (pending | shipped | delivered); confidence: medium
PartnerStore id, name, city, logo, active; confidence: medium
Redemption   id, user_id, store_id, amount, code, status; confidence: guess
Notification id, user_id, type, payload, read_at; confidence: medium
```

Relationships: User 1-n Participation, Product 1-n Participation, Product 1-1
Assignment, Assignment 1-1 Order, User 1-1 Wallet 1-n WalletTxn, PartnerStore
1-n Redemption.

## Feature matrix

See `features.csv`. Must: 13, should: 9, could: 2, skip: 3.

## Out of scope (cannot or should not be cloned)

- **The licence.** Many people pay, one gets the product: that is very likely a
  prize draw / lottery under Jordanian law, even with the shopping-balance
  refund. Building the app is fine; running it for money needs legal advice
  and probably a licence. Treat this as a launch blocker, not a detail.
- The partner-store network and its agreements.
- Supply deals with authorized distributors.
- Their brand, name, logo, copy, screenshots and member showcase content.

## Size

Screens 16, flows 5, entities 11. Hard parts: (1) a provably fair, auditable
owner-selection with a race-safe "last spot" close; (2) wallet ledger with
local payment gateways, refunds-as-credit and no double spend; (3) partner-store
redemption on the store side (a second app or portal). Size: **M** for the app
and admin; **L** once partner redemption, delivery ops and compliance are in.
