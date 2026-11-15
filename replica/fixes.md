# Fixes and angle

Date: 2026-11-15.

## Sample size: 0 verified reviews

Nothing below is based on what FORTUNEiT's users said, because we haven't
been able to read a single review word for word. What was tried:

| source | result |
| --- | --- |
| Google Play listing (the only place with reviews: ~94 on one regional listing, ~21 on another) | blocked from this session; seen only through search summaries, which paraphrase and can't be quoted |
| App Store | no FORTUNEiT listing found |
| Reddit, Hacker News | nothing about the app |
| Jordanian news / forums (Arabic search) | nothing about the app |
| fortuneit.app, fortuneit.net | blocked from this session |
| its own feature board / changelog | none found |

Rules of this step: no invented reviews, no paraphrases passed off as quotes,
every quote verbatim with a link. So the three lists are empty until reviews
come in.

## The three lists

1. **What they hate:** no evidence yet.
2. **What is missing:** no evidence yet.
3. **What is unsolved:** no evidence yet.

## What we do know (from the original's public pages, not from users)

These are facts about how the original presents itself, read through search
results. They are not complaints.

- Its two pages state different member counts (35,000+ on the store listing,
  14,000+ on the website). [Play listing](https://play.google.com/store/apps/details?id=com.softylus.fortuneit&hl=en),
  [website](https://fortuneit.app/)
- Its public pages don't explain how the owner of a product is chosen.
  [features page](https://fortuneit.app/features)
- Store rating around 3.2 (94 reviews) on one regional listing and 2.6 (21) on
  another, as shown in search snippets. Not checked on the page itself.

## Hypotheses to test (not added to `features.csv` until reviews back them)

| # | hypothesis | theme it would show up in | if true, the fix | size | already in the clone? |
| --- | --- | --- | --- | --- | --- |
| H1 | People doubt anyone really wins | fairness, transparency | provable draw + public winners list | - | **yes**: verify screen, owners wall |
| H2 | Shopping balance is hard to spend (few stores) | shopping_balance, refund | more partner stores; or cash refund option where legal | M / business | partly: store list |
| H3 | Products take long to fill | slow_fill | smaller products, a fill deadline with automatic refund, "closing soon" push | S–M | partly: refund on deadline exists in the backend |
| H4 | Payment methods are limited | payment_methods | CliQ, Zain Cash, eFAWATEERcom | M | planned: CliQ |
| H5 | Support is slow | support | in-app help with order status, WhatsApp support | S | partly: help + order timeline |

## Angle options (pending evidence)

Each angle should rest on a review theme. None can yet, so these are
**hypotheses**, ranked by how well the clone already supports them.

1. **Provable fairness** (recommended, provisionally)
   For people in Jordan who wonder whether these draws are real,
   *Spot* shows you the proof: the number that picks the winner comes from a
   public source nobody controls, and you can check every result yourself.
   Evidence: 0 reviews. Rests on: the original's pages don't explain selection;
   "is it rigged?" is the first question in this category. Already built.
2. **Your money is never stuck**
   For people whose balance gets tied up, *Spot* refunds automatically when a
   product doesn't fill by its deadline, and shows every fils in a clear ledger.
   Evidence: 0 reviews. Partly built.
3. **Pay the way Jordan pays**
   For people without a card, *Spot* takes CliQ and local wallets.
   Evidence: 0 reviews. Not built.

Recommendation: start `/replica-brand` with angle 1. It's the one the clone can
already prove on screen, and it needs no claims about the original. Re-rank
when the reviews are in. Never put the original's name in your name, ads or
store listing.

## Collecting reviews (5–10 minutes, on your phone)

Google Play works from Jordan, so you can read them directly:

1. Open the app's page on Google Play → **Ratings and reviews** → the arrow → **See all reviews**.
2. Sort by **Most recent**. Read the 1, 2 and 3-star ones first, then the 4-star "good, but…" ones.
3. For each review, send me: **stars, date, and the text copied exactly** (Arabic or English, as written).
   Copy and paste. Don't summarise.
4. 30–50 reviews is enough to see patterns; all ~94 is better.

Google Play has no link per review, so I'll use the listing URL as the link for
each row. I'll put them in `reviews.csv`, run the tool and rewrite this page
with real counts and quotes.
