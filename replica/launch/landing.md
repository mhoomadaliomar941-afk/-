# Landing page

Built: `landing/` (static HTML, Arabic, right-to-left, same tokens as the app,
light and dark). Open `landing/index.html`, or serve the folder.

Checked (2026-11-15): axe WCAG 2.1 AA **clean** at 390px light, 1440px light
and 390px dark; no horizontal scroll at 390px; no console errors; waitlist form
validates the number, requires consent, and says "soon" until Supabase is set
in `landing/config.js`. Contrast comes from the tokens (21 pairs, 0 AA failures).

| # | section | content | tied to |
| --- | --- | --- | --- |
| 1 | Hero | "احجز مكان بمنتج، والنتيجة بتقدر تتحقق منها بنفسك." + one line on drand and the full credit + waitlist button; two real screenshots of the app | angle (`fixes.md`) |
| 2 | The problem | the two "hate" themes in our own words: you can't see how the winner is picked; you don't know where your money goes | scam ×4, balance trap |
| 3 | How it works | choose → book → verify | core flow F01–F02 |
| 4 | Features | proof for every result; full amount back; every fils visible; your spot numbers; delivery; delete your account | fixes 2, 3 and built features |
| 5 | Pricing | from 1 JD per spot, odds shown per product, no subscription, refund rules | `pricing.md` |
| 6 | FAQ | how do I know it isn't rigged; **is this gambling (answer pending the lawyer)**; where does my money go; what if it doesn't fill; where do you deliver | the review objections |
| 7 | Call to action | waitlist: phone + explicit consent; same answer whether new or already listed | `waitlist` table |

What's deliberately missing:
- **No proof row.** No user counts, ratings, logos or testimonials. There are no users yet, and reviewers of the original are research, not quotes.
- **No mention of the original.**
- **No claims about features not built.** Shopping credit buying spots and the fair-price pledge are in the plan, not on the page.

**Do not publish** until the FAQ answer on gambling and the terms page are
written from the legal opinion. Both are marked in yellow on the page.
