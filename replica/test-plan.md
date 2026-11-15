# Test plan: Spot (the FORTUNEiT-style clone)

Build: 7543cf6 (2026-11-15).  Env: web build of `app/` on
the fake data layer with seed data, Chromium at 390×844 (and 1440×900 where noted).
Automated specs: `app/e2e/` (`npm run e2e`). Database rules: `supabase/tests/run.sh`.
Every spec fails on a console error, a page error or any 5xx response, and runs
an axe accessibility scan on each screen it visits.

Flows come from `recon.md`: F01 take part, F02 owner picked, F03 owner receives,
F04 spend balance, F05 top up. F00 (login) and F06 (account) were added.

| case | flow | type | steps | expected | auto | result |
| --- | --- | --- | --- | --- | --- | --- |
| F00-H1 | log in | happy | onboarding → skip → phone 79 123 4567 → send → code 123456 | home feed with products | e2e || pass |
| F00-E1 | | edge: empty phone | open login, nothing typed | send button disabled | e2e || pass |
| F00-E2 | | edge: not a Jordanian mobile (e.g. 61 234 5678) | type, send | clear error under the field, stays on phone step | e2e || pass |
| F00-E3 | | edge: letters, emoji and spaces typed into phone | type `7a9😀1 234 567` | only digits and spaces kept | e2e || pass (after BUG-004) |
| F00-N1 | | negative: wrong code | code 000000 | error, boxes cleared, can retry | e2e || pass |
| F00-E4 | | edge: deep link while signed out | open /wallet directly | sent to onboarding/login, no wallet data | e2e || pass |
| F00-E5 | | edge: sign out then back button | sign out, browser back | stays signed out (login shown) | e2e || pass |
| F00-E6 | | edge: keyboard only | login with Tab/Enter/typing only | reaches the feed | e2e || pass |
| F01-H1 | take part | happy | phone product → + → + → book → pay | spots 48–50 taken, balance −3 JD, product full | e2e || pass |
| F01-E1 | | edge: double click on pay | click pay twice fast | charged once, one participation | e2e || pass |
| F01-E2 | | edge: stepper limits | press + past the max | stops at min(spots left, per-member limit) | e2e || pass (after BUG-003) |
| F01-E3 | | edge: balance too low | console product (2 JD/spot) × 3 with 4 JD | sheet says balance too low, offers top-up, no charge | e2e || pass |
| F01-E4 | | edge: close sheet mid-flow | open confirm, close it | nothing charged, nothing booked | e2e || pass |
| F01-E5 | | edge: last spot taken by someone else | two members race for the last spot | one wins, the other gets "sold out", no double spot | SQL (`run.sh`) + fake unit || pass (SQL) |
| F01-E6 | | edge: desktop width | 1440px feed and product | content centred, max width, nothing stretched | e2e || pass |
| F01-E7 | | edge: reload mid-flow | reload on product page | fake: back to login (by design); real: session kept | manual (real backend) || not run (needs live backend) |
| F02-H1 | owner picked | happy | after F01-H1 wait | result: owner or credit, my spot numbers listed | e2e || pass |
| F02-E1 | | edge: verify result | open "verify" | seed, beacon, formula; check passes ✓ | e2e || pass |
| F02-E2 | | edge: result screen before the pick | open result right after filling | "not picked yet", then updates by itself | e2e || pass |
| F02-E3 | | edge: notification | after the pick | one unread notification for this product | e2e || pass |
| F02-N1 | | negative: wrong beacon round | call assign_owner with another round | refused | SQL (`run.sh`) || pass (SQL) |
| F03-H1 | owner receives | happy | Mine → Won → tablet → address form → confirm | timeline moves to "being prepared" | e2e || pass |
| F03-N1 | | negative: empty form | confirm with nothing filled | required errors on 4 fields, nothing saved | e2e || pass |
| F03-E1 | | edge: bad phone | phone "abc" | error, stays on form | e2e || pass |
| F03-E2 | | edge: emoji, accents, 200 chars in street | fill and confirm | saved, layout intact | e2e || pass |
| F04-H1 | spend balance | happy | get credited → store → 1.5 JD → create code | code + 15:00 countdown, shopping balance −1.5 | e2e || pass |
| F04-N1 | | negative: more than the balance | 999 JD | "not enough shopping balance", nothing held | e2e || pass |
| F04-E2 | | edge: expired code is fetched once (BUG-007) | create code, run the clock past expiry | ≤2 fetches, shows expired | e2e | pass (after BUG-007) |
| F04-E1 | | edge: code expires | create code, advance clock 15 min | "expired", balance returned | e2e || pass (after BUG-006/007) |
| F05-H1 | top up | happy | wallet → top up → 5 JD → card → pay | balance +5, ledger row | e2e || pass |
| F05-E1 | | edge: other amount | type 7 | pays 7 JD | e2e || pass |
| F05-E2 | | edge: unavailable method | tap CliQ | not selectable | e2e || pass (after BUG-003) |
| F05-N1 | | negative: card declined | HyperPay sandbox decline card | no credit, payment marked failed | manual (sandbox) || not run (needs HyperPay sandbox) |
| F05-N2 | | negative: forged webhook | body not encrypted with our key | 401, no credit | unit + local run || pass (unit + local) |
| F06-N1 | account | negative: delete with balance | profile → delete → confirm | blocked with the reason | e2e || pass |
| X-1 | all | second member's data invisible | B queries A's wallet, ledger, spots | nothing returned | SQL (`run.sh`) || pass (SQL) |
| X-2 | all | screen reader labels / contrast | axe on every screen | no violations | e2e || pass (after BUG-001/002/003/005) |
| X-3 | all | time zone | browser in America/New_York | dates still right ("today" means today) | e2e || pass |
| X-4 | all | offline / slow network | real backend with network throttled | errors with retry, no double charge | manual (real backend) || not run (needs live backend) |

## Results (7543cf6)

| suite | ran | passed | failed |
| --- | --- | --- | --- |
| e2e (`cd app && npm run e2e`) | 30 | 30 | 0 |
| unit (`node --test` app + functions) | 14 | 14 | 0 |
| database (`supabase/tests/run.sh`) | 36 | 36 | 0 |
| manual / needs live services | 3 | - | - |

First run: 15 of 28 passed. 9 of the 13 failures came from real bugs
(BUG-001…005); 4 were mistakes in the tests themselves (ambiguous locators,
invisible direction marks around amounts). Once corrected, one of those exposed
BUG-006, and fixing that turned up BUG-007. All 7 fixed, see `bugs.md`; two
tests were added for them (F00-E3b, F04-E2).
