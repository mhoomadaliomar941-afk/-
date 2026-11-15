# Launch plan: Nazih

## Gate 0: before anything public (blockers)

1. **Legal opinion in Jordan** on the prize-draw question (the original's
   most-agreed review calls the model gambling). It decides the model, the
   terms, the FAQ and the store listing. Nothing below starts before it.
2. **Store policies, checked against the current text** (from memory,
   not re-read here): Apple 5.3 (gaming, gambling and lotteries) and Google
   Play's real-money gambling, games and contests policy may treat this as a
   lottery, which needs licence proof and country approval. If so, plan for
   the web app first.
3. **Backend live** (`backend.md`), then `/replica-test` re-run against it.
4. **Admin portal + store scanner** (`parity.md` top 5, #2 and #3). Without them
   nobody can ship a product or spend a credit.
5. **Partner agreements** with the fair-price rule (`pricing.md`).

## Before launch (2–4 weeks)

- **Waitlist** on the landing page (built; switch it on in `landing/config.js`).
- **Analytics + errors** (your accounts): privacy-friendly analytics
  (Plausible or PostHog EU) and Sentry for the app and Edge Functions. Add both
  to the privacy page's processor table.
- **Store assets:** screenshots at the sizes App Store Connect and Play
  Console ask for at upload time (they change); lead with the "verify the
  result" screen and the money-path line, since that's what gets through review
  as *not* a copy (Apple 4.1).
- **Privacy labels / data safety:** phone number (account, sign-in), name +
  address (delivery, owners only), purchase history (wallet), device push
  token. No tracking, no data sold. Must match `landing/privacy.html`.
- **Review notes:** a demo account the reviewer can use (a test number with a
  fixed code in Supabase Auth's test OTP settings), with a product that fills
  quickly in the review build.
- **Age rating:** 18+ if the lawyer treats it as a prize draw.

## Where the original's unhappy users are

- Its Google Play reviews: the 1★ reviewers are the audience. Don't reply to
  them, contact them, or quote them. Just make sure people searching the
  store find an honest alternative (listing keywords: تحقق، شفافية).
- Jordanian Facebook groups and Instagram/TikTok, where these apps are
  advertised. A short video of the "verify the result" screen recomputing a
  real draw beats any claim.
- Product Hunt / Hacker News are a poor fit for a Jordan-only consumer app; skip.

## The first 10 users, by hand

1. Pick one small, cheap product (earbuds) sized honestly (the odds shown).
2. Invite 10–20 people you know (not reviewers of the original), with
   their consent, on the waitlist.
3. Sit with 3 of them while they book. Watch where they hesitate. Does the
   money-path line answer "where does my money go"?
4. After the draw, ask the non-winners to spend their credit at a partner,
   and watch that too. That's the part the original's users hated.
5. Fix, then open the waitlist.

## Launch day

- Publish the store listings (`listing.json`, checked with `listing.py`, 0 issues).
- First public product: small, honest odds, deadline with automatic refund.
- Post the first draw's proof publicly (seed, beacon round, winning spot) the moment it's assigned.
