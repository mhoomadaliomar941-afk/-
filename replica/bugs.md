# Bugs

Found by `/replica-test` on the web build (fake data layer, seed data),
Chromium 390×844. Only reproduced bugs are listed; ideas go under "To check".
Every fix has a test that failed before it and passes now.

Earlier steps also found and fixed bugs before this report existed (the RLS
leaks and the server-role grants in `/replica-backend`, RTL and layout
problems in `/replica-build`); see `build-log.md`.

| ID | severity | title | status |
| --- | --- | --- | --- |
| BUG-007 | S2 | Expired store code stuck at 0:00, re-requested on every render (real backend) | fixed in 7543cf6 |
| BUG-004 | S3 | Pasting "+962 79 123 4567" into the phone field drops digits | fixed in 7543cf6 |
| BUG-001 | S3 | Progress bars have no name for screen readers | fixed in 7543cf6 |
| BUG-002 | S3 | Selected amount / method / filter / tab not announced | fixed in 7543cf6 |
| BUG-003 | S3 | Disabled buttons announced as enabled | fixed in 7543cf6 |
| BUG-006 | S3 | Balance on screen not restored after a code expires | fixed in 7543cf6 |
| BUG-005 | S4 | Sheet backdrop has a label but no role | fixed in 7543cf6 |

Open: **none**. No S1 was found.

---

### BUG-007: Expired store code stuck at 0:00 and re-requested on every render

- Severity: S2
- Flow / case: F04 / F04-E1, F04-E2
- Screen: S13
- Build: before 7543cf6  Browser / device: Chromium 390px

Steps
1. Stores → any store → amount 1 → "أنشئ رمز".
2. Let 15 minutes pass (test: `page.clock.runFor(901_000)`), before the once-a-minute expiry job runs.

Expected: the code shows "انتهى الرمز" (expired) as soon as time is up; the held balance returns when the job runs.
Actual: the chip stays at "ينتهي خلال 0:00" (pending). Reading the code: every response replaced the state object, which re-ran the effect, which fetched again, an endless request loop against the backend until the job ran.
Evidence: `e2e/f04-spend.spec.ts` › F04-E1 and F04-E2 fail on the old screen code ("انتهى الرمز" never visible) and pass on the fix. (The fake layer used to expire codes on read, which hid this; it now expires them in a minute job like the real backend.)
Fix: the screen treats a pending code past its time as expired, and asks the server once (`app/src/app/redeem/[id].tsx`).
Status: fixed in 7543cf6

### BUG-004: Pasted phone number loses digits

- Severity: S3
- Flow / case: F00 / F00-E3, F00-E3b
- Screen: S02

Steps
1. Login → paste `+962 79 123 4567` (or type `7a9😀1 234 567`).

Expected: field shows `79 123 4567`, sign-in works.
Actual: the 12-character limit applied before non-digits were removed: `791 234 5`, and "send" rejected the number. Workaround: type the 9 digits by hand.
Evidence: F00-E3 received `"791 234 5"`; unit test `lib/money.test.ts` covers 7 formats.
Fix: `normalizeJoMobile()` strips +962 / 00962 / leading 0 and keeps 9 digits.
Status: fixed in 7543cf6

### BUG-001: Progress bars have no accessible name

- Severity: S3. Screens: S03, S04, S05, S08
- Steps: run axe on the feed. Expected: no violations. Actual: `aria-progressbar-name` (serious) ×6.
- Fix: `SpotsProgress` gets `aria-label` "47 من 50 مكان محجوز" and `aria-valuemin/max/now`.
- Status: fixed in 7543cf6

### BUG-002: Selection not announced

- Severity: S3. Screens: S06 amounts and methods, S12 governorate filter, S08 tabs, S16 FAQ
- Steps: axe on S06 / S12. Actual: `aria-required-attr` (critical): radios without `aria-checked`. react-native-web 0.21 ignores `accessibilityState`.
- Fix: `aria-checked` / `aria-selected` / `aria-expanded`, radios wrapped in labelled `radiogroup`s.
- Status: fixed in 7543cf6

### BUG-003: Disabled buttons announced as enabled

- Severity: S3. Screens: S02 send, S04 +/−, S06 CliQ
- Steps: login with an empty phone, check the send button. Expected `aria-disabled="true"`. Actual: missing; still in the tab order.
- Fix: Pressable's own `disabled` prop (it sets `aria-disabled` and removes it from the tab order).
- Status: fixed in 7543cf6

### BUG-006: Balance not restored on screen after a code expires

- Severity: S3. Screen: S13. Case F04-E1
- Actual: after expiry the store screen still showed the reduced shopping balance until you left the screen.
- Fix: the expiry job notifies screens (`emit()`); on the real backend the wallet realtime subscription does the same.
- Status: fixed in 7543cf6

### BUG-005: Sheet backdrop labelled without a role

- Severity: S4. Screens: every bottom sheet (S05, S09, S15)
- Actual: axe `aria-prohibited-attr`: `aria-label` on a plain div.
- Fix: the backdrop is `role="button"` "إلغاء" (it does close the sheet).
- Status: fixed in 7543cf6

---

## To check (not reproduced here)

- Web header back arrow doesn't mirror in RTL (native does). Cosmetic; check on a device.
- Real backend only: reload mid-flow keeps the session (F01-E7), offline / slow network (X-4), card declined in the HyperPay sandbox (F05-N1). Needs the live services.
- Android TalkBack / iOS VoiceOver pass on a device: axe covers the web build only.
