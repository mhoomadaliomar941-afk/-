# Architecture: an original trick-puzzle game (same format as Brain Test, own puzzles)

Reads `recon.md` and `features.csv`. The recon found no accounts and no server
features, so this is a **local-first mobile game with no backend**. That is the
biggest decision here: it removes auth, hosting, email and jobs entirely.
Roughly half the recon is still marked "inferred"; nothing below depends on
those rows being exact.

## Stack

| layer | choice | why |
| --- | --- | --- |
| app | Expo (React Native) + TypeScript | one codebase for Android first, iOS later; you named Android as the target |
| scene and gestures | react-native-gesture-handler + Reanimated | tap, drag, long-press and multi-touch on the same object without fighting each other |
| device input | expo-sensors (shake, tilt), expo-haptics | the genre's "use the phone itself" puzzles |
| level content | JSON files in the bundle, validated with Zod | puzzles are data, so adding a level needs no new code |
| database | SQLite on the device (expo-sqlite) | progress, wallet and settings; works offline, no server bill |
| state | Zustand | small, and stores map one-to-one onto the tables |
| auth | none | no account in the original; nothing to log into |
| payments | RevenueCat over Google Play Billing | bulb packs, remove-ads and the subscription with restore handled; Stripe is not allowed for in-app digital goods |
| ads | Google AdMob (react-native-google-mobile-ads) + UMP consent | interstitial and rewarded formats the flows need |
| email, jobs, files | none | nothing in the recon needs them |
| build and release | EAS Build + EAS Submit | Play Store builds without a local Android toolchain |
| crash and analytics | Sentry + a per-level funnel event | you must know which level players quit on |
| tests | Jest for the engine, Maestro for flows | the rule engine is pure logic and cheap to test |

Not chosen: a game engine (Unity, Godot). The scenes are flat 2D objects with
simple motion, the hard part is the shop, ads and UI around them, and that is
where React Native is strongest. Revisit only if puzzles need physics.

## Schema

Tables: 5. Access rules: none needed — one install, one local file, no server.
SQL is in `replica/schema.sql` (SQLite, migration 001).

| table | holds | from recon |
| --- | --- | --- |
| level_progress | status, hints revealed, attempts per level | LevelProgress |
| wallet_ledger | every bulb earned or spent; balance is the sum | Wallet + Transaction |
| entitlements | cached no-ads and subscription state | Entitlement |
| daily_gift | last claim day and streak | DailyGift |
| settings | sound, music, vibration, language, consent | Settings |

`Level` is not a table. Levels are files:

```jsonc
// levels/0001.json — an original example, to show the format
{
  "id": "lv_a1f3",                 // stable forever; order lives in levels/index.json
  "question": { "en": "Wake the cat up.", "ar": "أيقظ القطة." },
  "scene": [
    { "id": "cat",   "sprite": "cat_asleep", "x": 0.5, "y": 0.6, "w": 0.4 },
    { "id": "clock", "sprite": "alarm_clock", "x": 0.2, "y": 0.3, "w": 0.15 }
  ],
  "solution": { "any": [ { "on": "shake", "minForce": 1.8 } ] },
  "onSolve":  [ { "set": "cat.sprite", "to": "cat_awake" } ],
  "wrong":    [ { "on": "tap", "target": "cat" }, { "on": "tap", "target": "clock" } ],
  "hints": [
    { "en": "Tapping is too gentle.", "ar": "اللمس لطيف جداً." },
    { "en": "Shake your phone.",      "ar": "هزّ هاتفك." }
  ],
  "explanation": { "en": "A good shake wakes anyone.", "ar": "الهزّ يوقظ أي أحد." }
}
```

Positions are fractions of the scene, so one file fits every screen size.
Trigger types for v1: `tap`, `drag_onto`, `drag_out` (off the screen),
`long_press`, `multi_touch`, `shake`, `tilt`, `answer` (typed), `drag_word`
(a word of the question is itself an object). A level that needs something
new gets a small custom component named in the file; expect about one in ten.

## API

No server, so no HTTP routes. These are the in-app actions, one module each,
and the only code allowed to write to the database.

| action | does | input | output | flow |
| --- | --- | --- | --- | --- |
| levels.load(id) | read + validate a level file | level id | Level | F01 |
| engine.dispatch(event) | match an input against solution / wrong rules | gesture or sensor event | solved, wrong or nothing | F01 |
| progress.solve(id) | mark solved, unlock and set next as current | level id | next level id | F01 |
| progress.skip(id) | charge bulbs, mark skipped, advance | level id | next id or not-enough-bulbs | F03 |
| progress.list() | all levels with status | — | rows for the map | F06 |
| hints.reveal(id, via) | charge bulbs or consume an ad reward, reveal next hint | level id, bulbs or ad | hint text | F02 |
| wallet.balance() | sum the ledger | — | number | all |
| wallet.grant(kind, amount, dedupeKey) | add bulbs exactly once | kind, amount, key | new balance | F04 F05 |
| gift.status() / gift.claim() | once per local day; doubled for subscribers | — | amount, next time | F05 |
| store.offerings() | products and local prices from RevenueCat | — | packs, bundle, subscription | F04 F07 |
| store.purchase(productId) | buy, then grant bulbs and refresh entitlements | product id | ok, cancelled, pending | F04 F07 |
| store.restore() | re-read what the player owns | — | entitlements | F04 |
| ads.maybeInterstitial() | show between levels unless no-ads; capped | — | shown or skipped | F01 |
| ads.rewarded() | show a rewarded ad | — | reward id or failed | F02 |
| settings.get / set | preferences | key, value | — | S11 |

Webhooks in: none. Webhooks out: none. Jobs: none.
Prices (hint, skip, gift size, ad frequency) live in one `economy.ts` file so
they can be tuned without touching logic.
External services, all official SDKs with your own keys: Google Play Billing
(through RevenueCat), AdMob, Sentry.

## The parts that bite

- **"I paid and still see ads."** The recon's loudest purchase complaint about
  the original. Check the cached `entitlements` row *before* every ad request,
  refresh it on launch and after every purchase or restore, and never show an
  interstitial while a purchase is pending.
- **Idempotency.** Store callbacks and ad rewards can fire twice, and the app
  can be killed mid-purchase. Every grant carries a `dedupe_key` (store
  transaction id, reward id, gift date) with a unique index, so replays are no-ops.
- **Races.** Double-tapping Hint or Skip must charge once: the charge and the
  progress update go in one SQLite transaction, and the button disables on press.
- **Time and the daily gift.** With no server, a player can move the clock
  forward. Accept it, but refuse a claim if the clock is earlier than the last
  claim, and key the gift on the local calendar day so travel does not double it.
- **Offline.** Everything except ads and purchases must work with no network.
  A rewarded ad that will not load must say so and offer bulbs instead, never
  spin forever.
- **Sensors and multi-touch.** Some devices have no gyroscope and cheap screens
  mis-read three fingers. Every such level needs a fallback, or gets a free skip.
- **Level ids.** Progress is keyed by a stable id, not by position, so levels
  can be reordered or inserted in an update without corrupting saves.
- **Translation.** Word-play does not translate. Each level lists the languages
  it works in; the index skips it elsewhere. Arabic needs right-to-left layout
  from day one, not as a retrofit.
- **Children (decided: the game is for under-13s).** This is now a rule, not a
  risk. Join Google Play's Families programme and declare the target age.
  Ads: AdMob with child-directed treatment on, non-personalised only, content
  rating capped at G; no ad on the level screen. No advertising ID is read.
  Analytics: no third-party SDK that profiles the player; count level
  funnel events without a device identifier, and configure Sentry to send no
  personal data, or drop it if it cannot be certified for Families.
  Purchases and every link out of the app sit behind a parent gate. Publish
  a privacy policy that says all of this. Have the COPPA and GDPR-K position
  checked by someone qualified before launch.
- **Lost progress.** A local-only save dies with the phone. Turn on Android
  auto-backup for the database file; real cloud save is a later, optional step.
- **Not applicable:** multi-tenancy, email, search, realtime, rate limits.

## Build order

1. **Vertical slice.** S03 level player and S06 solved screen with 3 original
   levels (one tap, one drag, one shake), read from JSON. Tables:
   level_progress. Actions: levels.load, engine.dispatch, progress.solve.
   Proves the level format, which everything else rests on.
2. **Must-haves.** Wrong-answer feedback; S02 home; bulb counter and
   wallet_ledger; S04 hints and S05 skip paid in bulbs; saved progression.
   Then write levels to about 30.
3. **Should-haves.** S09 shop with packs, remove-ads and restore
   (entitlements); S12 interstitial and rewarded ads; S08 daily gift; S07
   level map; S11 settings; remaining trigger types.
4. **Could-haves.** S10 subscription, more languages, S13 rate and share.
5. **Fixes from replica-entrepreneur**, once it has run — likely fewer forced
   ads, given the reviews already seen.

Content runs in parallel from milestone 1: the code is weeks, the puzzles and
art are the schedule.
