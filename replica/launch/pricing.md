# Pricing: Nazih

Date: 2026-11-15. **Draft until the legal answer is in.** The model may
have to change (for example a free entry route or a skill question) depending
on what a Jordanian lawyer says.

## What the market charges

| app | price | source | date read |
| --- | --- | --- | --- |
| FORTUNEiT (the original) | spots "starting from 1 JD" | [Google Play listing](https://play.google.com/store/apps/details?id=com.softylus.fortuneit&hl=en) (via search; page itself blocked here) | 2026-11-15 |

No other comparable app in Jordan could be verified from here. Add rows when you find them (name, price, link, date).

## What reviewers said about price (from `feedback.md`, 8 reviews)

- Shopping balance is spent at partner stores with **inflated prices**, with no refunds: 1 review (thin).
- "Pay 2 JD for a lottery ticket": 1 review, 35 people agreed. That's about the
  model, not the price.

## The economics (why this matters)

Every product has: **N** spots at price **p**, so the pool = N·p. The winner
gets the product (it costs us **C**). Everyone else gets their money back as
shopping credit.

```
cash in        = N·p
cash out       = C  +  what partner stores charge us when credit is spent
credit issued  = (N − 1)·p
```

If partners accept credit at face value and charge us all of it, we lose
almost C on every product. The model only works if **partners fund it**: they
accept credit and pay us a commission **k** on what's spent at their store
(like a gift-card program). Then:

```
margin ≈ p + k·(N − 1)·p − C        →   it pays when the pool N·p ≈ C / k
```

| product | cost C | commission k | pool needed ≈ C/k | spots at 1 JD |
| --- | --- | --- | --- | --- |
| earbuds | 150 JD | 20% | 750 JD | ~750 |
| phone | 899 JD | 20% | 4,495 JD | ~4,500 |
| phone | 899 JD | 10% | 8,990 JD | ~9,000 |

So:
1. **The demo data is not realistic** (50 spots for a phone). Real products
   need thousands of spots, so the odds are long. The app must say so plainly.
2. **"Inflated partner prices" is how such a model gets funded silently.** It's the
   exact complaint in the reviews. Nazih should fund it **openly**: a visible
   partner commission, with a fair-price rule for partners (prices no higher
   than the shelf price outside the app).

## The model (proposal)

- **Per-spot price, no subscription, no membership fee.** Spots from 1 JD.
- **On every product page, show the math:** spots × price = pool; product cost;
  "your odds: 1 in N per spot". This is the angle (honest) applied to money.
- **Not picked → full amount as shopping credit**, 1:1, at partners who signed
  the fair-price rule. *Fix from the reviews.*
- **Product doesn't fill by its deadline → full refund in cash balance.**
  Already built (`cancel_product`).
- **Shopping credit can also buy spots** (fix #4 in `fixes.md`). It keeps the
  money moving without forcing a store purchase. Needs a change in `buy_spots`.
- **No tiers.** One price list, the same for everyone.

## Billing promises (built into the product)

- Every fils in a ledger the member can see (built).
- No auto-renewals, nothing recurring, nothing to cancel.
- Top-ups are whole dinars, 1–500 JD, with a clear pending/failed state (built).
- Account deletion in the app (built, blocked only while money or an order is open).

## What you set up

No Stripe (it doesn't serve Jordanian businesses). In HyperPay: nothing per
product. Prices live in the `products` table (`spot_price_fils`, `total_spots`).
Partner agreements (commission, fair-price rule) are contracts, not code.

## Decisions for you

1. Partner commission target **k** (this sets how many spots each product needs).
2. Whether to add an explicit service fee per spot as well, shown openly.
3. Whether shopping credit expires. Recommended: **it doesn't.**
