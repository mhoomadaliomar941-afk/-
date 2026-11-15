# Recon map: Brain Test: Tricky Puzzles (Android)

Scope: the trick-puzzle game *format* — level player, hint/skip economy, level
map, ads and store — as the shell for a new game with **original puzzles,
characters, art and name**. The original's levels are not inventoried here and
are not part of the clone (see Out of scope).
For: a product to sell (assumed — confirm)
Date: 2026-11-15

> **Evidence warning.** play.google.com and apps.apple.com are blocked by this
> session's network policy, so the store listing itself could not be read and
> no screenshots were captured (`replica/screens/` is empty). Everything below
> comes from search-result summaries of store listings and reviews. Rows
> marked **(inferred)** are typical of the genre and unconfirmed — verify them
> by playing the game on your own device before building.

## Sources

| # | source | URL | notes |
| --- | --- | --- | --- |
| 1 | Google Play listing | https://play.google.com/store/apps/details?id=com.unicostudio.braintest | target; **blocked, not read** |
| 2 | App Store listing | https://apps.apple.com/us/app/-/id1486214495 | read via search summaries only: IAP names, subscription perks, reviews |
| 3 | App Store listing (GB) | https://apps.apple.com/gb/app/id1486214495 | same, regional wording ("lamps" / "bulbs") |
| 4 | Sequel listing (All-Star) | https://apps.apple.com/us/app/-/id6478563961 | bulbs from daily puzzles, hint + skip systems, offline play (different app, same studio) |
| 5 | The Casual App Gamer review | https://thecasualappgamer.com/?p=14406 | 3/5; ambiguous clues frustrate, variety praised |
| 6 | Appy Smarts review | https://www.appysmarts.com/application/brain-test-tricky-puzzles,id_190114.php | 72/100 |
| 7 | Bark app overview | https://www.bark.us/app-overview/brain-test/ | parental-safety view |
| 8 | Review aggregator | https://marlvel.ai/apps/brain-test-tricky-puzzles/reviews | AI-generated, low confidence: "paid ad removal not applied" complaints |
| — | help center, changelog, pricing page, API docs | none found | a casual game; none expected |
| — | your own install | — | **not yet used; the most valuable missing source** |

## Core loop

Read a one-line riddle, poke at the scene until the non-obvious answer works,
get the "aha", go to the next level — and spend bulbs (earned, ad-rewarded or
bought) on a hint or a skip when stuck.

## Screens

| ID | screen | route / how to reach | purpose | key components | states seen |
| --- | --- | --- | --- | --- | --- |
| S01 | Splash / loading | app launch | load, consent | logo, progress | loading (inferred) |
| S02 | Home | after S01 | resume play | Play button, level number, bulb counter, settings, shop, daily gift | first run, returning, gift ready (inferred) |
| S03 | Level player | Play / pick a level | the game | question text, scene canvas, bulb counter, Hint, Skip, Retry, level number | fresh, wrong answer feedback, solved (inferred layout; hint + skip confirmed by #2) |
| S04 | Hint dialog | Hint on S03 | reveal a clue | hint text, cost, "watch ad" option | can afford, cannot afford → S09, revealed (ad-for-hint confirmed by a review in #2) |
| S05 | Skip confirm | Skip on S03 | pass the level | cost, confirm | can afford, cannot afford (skip purchase confirmed by #2) |
| S06 | Level solved | correct answer | reward, explain the trick | praise, explanation line, Next | with / without interstitial ad after (ad after each level per review in #2) |
| S07 | Level map | from S02/S03 | replay, see progress | numbered grid | solved, skipped, current, locked (inferred) |
| S08 | Daily gift | S02, once a day | retention | claim button, amount | ready, claimed, doubled for subscribers (#2) |
| S09 | Shop | bulb counter / cannot afford | sell bulbs, no-ads | bulb packs ("250 Lamps"), "Remove Ads + 250" bundle, restore | loaded, purchasing, failed, owned (#2) |
| S10 | Subscription offer | S09 or prompt | sell VIP | perks: 100 bulbs now, no ads, double daily gift, cheaper skips (-50%) and hints (-40%) | offer, active (#2, one regional listing) |
| S11 | Settings | gear on S02 | preferences | sound, music, vibration, language, restore purchases, privacy, support | (inferred) |
| S12 | Interstitial / rewarded ad | between levels; for hints | revenue | ad SDK view | loaded, no fill, skipped, removed by purchase (#2) |
| S13 | Rate / share prompt | after some levels | growth | stars, share | (inferred) |

## Flows

```
F01 Solve a level
    S03 -> S06 -> (S12) -> S03 next
    happy path clicks: 1 solving interaction + 1 "Next"
    edge: wrong answer feedback, ad has no fill, app killed mid-level

F02 Get unstuck with a hint
    S03 -> S04 -> (S12 rewarded | spend bulbs) -> S03
    happy path clicks: 2
    edge: not enough bulbs -> S09, ad fails, hint already bought (free re-read)

F03 Skip a level
    S03 -> S05 -> S03 next
    happy path clicks: 2
    edge: not enough bulbs, skipped level stays replayable on S07

F04 Buy bulbs / remove ads
    S09 -> store sheet -> S09
    happy path clicks: 3
    edge: cancelled, pending, restore on a new device,
          "paid but ads still show" (reported complaint, #8 — get this right)

F05 Claim the daily gift
    S02 -> S08 -> S02
    happy path clicks: 2
    edge: clock changed on device, subscriber double

F06 Replay an earlier level
    S02 -> S07 -> S03
    happy path clicks: 2

F07 Subscribe
    S09 -> S10 -> store sheet
    edge: expiry, restore, perks removed on lapse
```

## Components

| component | variants | states | used on |
| --- | --- | --- | --- |
| Question text | static, draggable words (inferred) | default | S03 |
| Scene object | tappable, draggable, hidden, combinable | idle, held, correct, wrong | S03 |
| Answer input | number pad, text (inferred) | empty, filled, wrong | S03 |
| Bulb counter | — | idle, increment animation, tap → shop | S02, S03, S09 |
| Icon button | hint, skip, retry, settings, map | default, pressed, disabled | S02, S03 |
| Primary button | Play, Next, Claim, Buy | default, pressed, loading | most |
| Modal | hint, skip, gift, offer | open, closing | S04, S05, S08, S10 |
| Right / wrong feedback | tick, cross | animating | S03 |
| Level tile | — | solved, skipped, current, locked | S07 |
| Shop card | pack, bundle, subscription | default, best-value, owned | S09, S10 |
| Toggle | — | on, off | S11 |

## Inferred data model

All local to the device; no account is confirmed (one low-quality source says
an account is required — unverified, treat as false until checked).

```
Level        id, order, question, scene (objects + positions), solution rule,
             hints[], explanation
             evidence: S03, S04, S06          confidence: medium (format is ours to design)

LevelProgress level_id, status (locked | current | solved | skipped),
             hints_revealed[], attempts
             evidence: S07, F02, F03          confidence: medium

Wallet       bulbs
Transaction  id, type (daily_gift | rewarded_ad | purchase | hint | skip), amount, at
             evidence: #2 IAP list, reviews   confidence: high for the currency, guess for the ledger

Entitlement  no_ads (bool), subscription (active, expires_at)
             evidence: #2                     confidence: high

DailyGift    last_claimed_at, multiplier
             evidence: #2 subscription perks  confidence: medium

Settings     sound, music, vibration, language
             evidence: none                   confidence: guess
```

Relationships: Level 1-1 LevelProgress, Wallet 1-n Transaction.

Puzzle interaction primitives the engine needs (genre-typical, inferred): tap,
drag, drag-onto, long-press, multi-touch, swipe, shake, tilt/rotate device,
typed answer, moving the question's own words, objects hidden off-screen.

## Feature matrix

See `features.csv`. Must: 9, should: 8, could: 4, skip: 4.

## Out of scope (cannot or should not be cloned)

- **The puzzles themselves.** Each level's riddle, scene and solution is the
  game's creative content. Write your own.
- **Characters, art, animation, sound and music.**
- **The name, logo and "Brain Test" branding**, and the look of its store listing.
- **Level order and difficulty curve copied one-for-one.**
- **Its player base and store ranking** (100M+ downloads reported second-hand).

## Size

Screens 13, flows 7, entities 7. Hard parts: (1) a data-driven puzzle engine
flexible enough that each level can break the rules; (2) writing and
illustrating 100+ good original puzzles — the real cost, far more than the
code; (3) ads + IAP + subscription that reliably honour "no ads" and restore.
Size: **M** for the shell and engine with ~30 levels; **L** with a
store-competitive level count.
