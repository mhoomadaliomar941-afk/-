# Parity report: Spot vs FORTUNEiT

Date: 2026-11-15. Build: dcdea67.

## Verdict: **not shippable yet**

- **Feature parity: 78.6 / 100** (23 features counted; **8 of 12 must-haves done**, 4 partial).
- **Layout parity: not measured.** There are no screenshots of the original
  (`replica/screens/` is empty: the store page was blocked from this session
  and you couldn't send screenshots). No made-up number.
- **Bugs:** no open S1 or S2 (`bugs.md`).
- **Launch blocker outside the score:** the prize-draw licence question
  (`recon.md`). It's a `skip` row, so it doesn't lower the score, but nothing
  launches without a legal answer.

Read the score with one caveat: the original's feature list is itself
**inferred** from its public description (the recon is a draft). So this
measures the clone against our best picture of the original, not against the
original checked by hand. Screenshots or a look at the real app would firm up both.

What "partial" means here: three of the four missing must-haves are **built and
tested but not yet run against the live services** (SMS, HyperPay, push), which
need your accounts. The fourth (delivery) is missing its admin side.

## By area, weakest first
- growth                         0.0  (1 features)
- onboarding                    50.0  (1 features)
- notifications                 50.0  (2 features)
- platform                      50.0  (1 features)
- support                       50.0  (1 features)
- fulfilment                    70.0  (2 features)
- partners                      75.0  (2 features)
- catalog                       85.7  (3 features)
- wallet                        86.4  (4 features)
- participation                100.0  (4 features)
- trust                        100.0  (1 features)
- account                      100.0  (1 features)

## Missing, in build order
- [must] fulfilment: Delivery of the owned product across Amman and governorates, partial  (needs address capture + order tracking; address + status in app (S10); shipping ops and admin side not built)
- [must] notifications: Result notification (you own it / balance credited), partial  (in-app inbox S14 done; push needs /replica-backend; backend: Expo push job written; needs EAS project id + live run)
- [must] onboarding: Sign up / log in (phone number + OTP likely), partial  (login method is a guess; confirm from screenshots; S02 built; fake OTP 123456 until /replica-backend wires Twilio via Supabase Auth; backend: Supabase phone OTP wired; needs your Twilio + a live run)
- [must] wallet: Top up wallet with multiple payment methods, partial  (Jordanian gateways; methods not confirmed; S06 UI done; fake gateway until /replica-backend (HyperPay or similar); backend: HyperPay hosted checkout + webhook + reconcile written and unit-tested; needs sandbox run)
- [should] notifications: Push notifications for new products and closing soon, partial  (assumed; needs /replica-backend (Expo Push); new-product push on open_product; "closing soon" not built)
- [should] partners: Redeem shopping balance at a partner store from the app, partial  (redemption mechanic (QR/code) is a guess; S13 member code + countdown; store-staff scanner portal not built)
- [should] platform: Arabic + English UI with RTL, partial  (reviews are partly Arabic; assumed; Arabic RTL done; English strings not written yet)
- [should] support: Help / FAQ / terms, partial  (support email shown on listing; S16 FAQ + contact; terms page with /replica-launch)
- [could] catalog: Categories / search / filter, no
- [could] growth: Referral / invite friends, no

## Left out on purpose (not scored)
- Partner-store network and agreements: a business network you must build yourself; not software
- Gaming / prize-draw licence in Jordan: the owner-selection model is likely a regulated prize draw; get legal advice before running it
- Authorized-distributor supply deals: their supplier relationships are not part of the product

## Layout per screen

| screen | score | note |
| --- | --- | --- |
| S01–S16 | not measured | no original screenshots. Run when you have them: `python3 imgdiff.py replica/screens/S03.png replica/clone-screens/S03-home.png --out replica/diffs/S03.png` (same 390×844 viewport; the clone's are already in `replica/clone-screens/`) |

## Behaviour differences

The original's side comes from its public description only; "unknown" means we
can't see it without using the app.

| flow | original does | clone does | fix or keep |
| --- | --- | --- | --- |
| F01 take part | ~4 taps with balance (recon estimate) | **3 taps** for 1 spot (card → احجز → ادفع واحجز), measured in e2e | keep |
| F01 take part, low balance | unknown | sheet says why, one tap to top up, nothing charged | keep |
| F02 owner picked | "one member is chosen"; how is not public | provable: published seed hash + public drand round + formula; "verify" screen recomputes it | keep: this is a selling point |
| F02 not picked | spend comes back as shopping balance | same, credited automatically, notified | keep |
| F03 delivery | delivery across Amman and all governorates | address + status timeline in app; **no admin shipping side yet** | fix (top 5 #2) |
| F04 spend balance | redeem at partner stores "from the app" | 15-minute code + QR; **no store-staff scanner yet** | fix (top 5 #3) |
| F05 top up | "multiple payment methods" | card + Apple Pay via HyperPay hosted page; CliQ later | fix after live sandbox run |
| login | unknown (phone assumed) | phone + SMS code, pasted +962 numbers handled | confirm against the real app |
| account deletion | unknown | in-app, blocked while money or an order is open | keep (Apple requires it) |
| remembered between visits | unknown | session kept on device (real backend) | keep |

## Top five to build next

1. **Run the backend live** (your accounts): Supabase project, Twilio for SMS,
   HyperPay sandbox, EAS project id for push. Turns 3 partial must-haves into
   done. Steps in `backend.md`. Then re-run `/replica-test` against it (F01-E7, F05-N1, X-4).
2. **Admin portal: products and delivery** (must): create and open products,
   see owners, mark orders shipped and delivered (sends the "shipped" push).
3. **Store-staff scanner** (should): store login + scan code →
   `confirm_redemption()`. Without it, shopping balance can't actually be spent.
4. **English** (should): `i18n/en.ts` + language switch in Profile.
5. **Terms, privacy and "closing soon" push** (should): legal pages come with
   `/replica-launch`; "closing soon" is one more job task.

Next step: `/replica-entrepreneur`. It finds what FORTUNEiT's users complain
about and turns it into reasons to choose your version. The gaps above are
real, but they're known and listed; build them alongside.
