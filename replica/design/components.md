# Components: "Spot" design system

Source: the component list in `replica/recon.md`. No screenshots of the
original were available, so these are designed fresh from the flows, not
measured. Tokens: `tokens.json` / `tokens.dark.json`. Classes are role
classes from `tailwind.tokens.js`.

Global rules
- **Arabic first, RTL by default.** Layout uses logical sides (`ps-`, `pe-`,
  `start`, `end`), never left/right. Icons that point (chevrons, back arrows)
  mirror; icons that don't (search, wallet) don't.
- **Money:** always `formatJD(fils)`: "3.500 د.ا" in Arabic, "JD 3.500" in
  English, 3 decimals, tabular numerals, wrapped in an LTR isolate so digits
  never reorder inside Arabic text.
- **Touch targets** at least 44x44. Focus ring: 2px `focus`, 2px offset.
- **Icons:** Lucide (ISC licence), 20px in lists and 24px in the tab bar, stroke 1.75.
- **Motion:** `fast` for presses, `base` for sheets and toasts; respect
  reduce-motion (no confetti, no spring).
- Every list has **loading (skeleton), empty, error (with retry)** states.

---

```
Button
  variants  primary (bg accent, text on-accent), secondary (bg accent-soft, text accent-strong),
            ghost (transparent, text accent), danger (bg danger, text on-danger)
  sizes     sm 36px, md 44px, lg 52px (full-width CTAs on mobile)
  states    default, pressed (opacity .85, scale .98), focus-visible (ring), disabled (40% opacity,
            no press), loading (spinner replaces icon; label stays, aria-busy)
  tokens    radius md, font base/600, padding-x 16 (md)
  a11y      accessibilityRole="button", label stays when loading, disabled announced
  used on   all
```

```
ProductCard
  variants  feed (image 4:3, title, spot price, progress, spots left), compact (row: thumb 64px,
            title, status chip) for My participations
  states    open, almost-full (<=10% left: warning chip "باقي ٣ أماكن"), full (chip "اكتمل"),
            assigned (chip "تم اختيار المالك"), cancelled; loading = skeleton
  tokens    bg surface-raised, radius lg, shadow card, gap 12, padding 12
  a11y      whole card is one button; label reads "title, price per spot, X of Y spots taken"
  used on   S03, S08
```

```
SpotsProgress
  variants  thin (6px, on cards), large (10px + "٤٢ من ٥٠ مكان" label, never "42 / 50", which reads both ways in RTL; on detail)
  states    0-99% (fill progress-fill on progress-track), 100% (fill success), closed (muted)
  tokens    radius pill; fill animates with motion base
  a11y      accessibilityRole="progressbar" with min/max/now; text label always visible,
            never colour alone
  used on   S03, S04, S08
```

```
QuantityStepper
  variants  default
  states    default, at-min (minus disabled), at-max (plus disabled, helper "الحد الأقصى ٥"),
            disabled (product full)
  tokens    44px buttons, radius md, border border, number font xl tabular
  a11y      role "adjustable" with increment/decrement actions; value announced
  used on   S04, S05
```

```
WalletBalanceCard
  variants  large (Wallet: cash and shopping side by side + "اشحن" button),
            inline (Checkout: "رصيدك: 4.000 د.ا", turns danger when below the total)
  states    normal, low (inline: danger text + "اشحن المحفظة" link), loading
  tokens    large: bg accent, text on-accent, radius xl, padding 20; inline: bg surface
  a11y      amounts read as words by screen readers ("أربعة دنانير")
  used on   S05, S07
```

```
TransactionRow
  variants  topup (+ cash), spend (- cash), credit (+ shopping), redeem (- shopping),
            refund (+), adjust
  states    pending (warning chip), done, failed (danger chip)
  tokens    icon in a 36px soft circle; amount end-aligned, success for +, text for -
  a11y      one label: "type, amount, date, status"
  used on   S07
```

```
PaymentMethodRow
  variants  card, Apple Pay, CliQ (later)
  states    default, selected (border accent 2px + check), unavailable (muted + reason)
  tokens    radius md, border border, height 56
  a11y      radio group; selected announced
  used on   S06
```

```
AmountChips
  variants  preset (1, 5, 10, 20 JD) + "مبلغ آخر" input
  states    default, selected (bg accent-soft, text accent-strong), disabled
  a11y      radio group
  used on   S06, S13
```

```
ResultBanner
  variants  owner (success-soft bg, large check, "مبروك! صار المنتج إلك" + "أدخل العنوان"),
            not-selected (accent-soft bg, "رجعنا لك ٢.٠٠٠ د.ا رصيد تسوق" + "تسوّق الآن")
  states    -
  extra     "تحقق من النتيجة" link opens the fairness sheet: winning spot, seed, beacon
            round, the formula
  a11y      announced as a live region when it appears
  used on   S09
```

```
OrderTimeline
  variants  vertical
  states    awaiting address, pending, shipped (tracking), delivered, failed
  tokens    done steps success, current accent, future border
  used on   S10
```

```
OwnerCard
  variants  grid tile
  states    with photo, without photo (initials on accent-soft)
  content   first name + initial only, product, date
  used on   S11
```

```
StoreRow
  variants  list row (logo 40px, name, city), with distance (later)
  states    open, inactive (hidden), loading
  used on   S12
```

```
RedeemCode
  variants  QR + 10-char code below (monospace, grouped 5-5)
  states    pending (15:00 countdown), confirmed (success), expired (muted + "أنشئ رمز جديد")
  a11y      code readable and copyable; countdown announced each minute, not each second
  used on   S13
```

```
TextField / PhoneField / OTPInput
  variants  text, phone (fixed +962 prefix, LTR digits), OTP (6 boxes, auto-advance, paste)
  states    default, focus (border accent 2px), error (border danger + message below),
            disabled, read-only
  tokens    height 52, radius md, border border-input, label above (font sm/600)
  a11y      label linked to input; error announced; OTP uses one-time-code autofill
  used on   S02, S10, S06
```

```
StatusChip
  variants  neutral, accent, success, warning, danger (soft bg + strong text)
  tokens    height 24, radius pill, font xs
  used on   S03, S04, S07, S08, S10
```

```
BottomTabBar
  tabs      الرئيسية (Home), مشاركاتي (Mine), المحفظة (Wallet), المتاجر (Stores), حسابي (Profile)
  states    active (accent icon + label), inactive (text-muted), badge (danger dot)
  tokens    height 64 + safe area, bg surface-raised, top border border
  a11y      role tablist; labels always shown (no icon-only tabs)
  used on   app shell
```

```
Header
  variants  large title (tab roots), standard with back (mirrors in RTL), transparent over image (S04)
  used on   all
```

```
BottomSheet
  variants  confirm (S05 checkout), info (fairness), picker
  states    open, closing; scrim at 50%
  tokens    radius xl top corners, shadow sheet
  a11y      focus moves into the sheet and returns on close; Back/escape closes it
  used on   S05, S09, S12
```

```
Toast
  variants  success, error, info
  states    enter, auto-hide 4s (errors stay until dismissed)
  a11y      live region, polite (assertive for errors)
  used on   all
```

```
EmptyState
  variants  per list: feed, participations, wallet history, notifications, stores
  content   own simple line illustration (to be made), one sentence, one action
  used on   S03, S07, S08, S12, S14
```

```
Skeleton
  variants  card, row, text line
  states    shimmer (static if reduce-motion)
  used on   all lists
```

```
Onboarding carousel
  variants  3 slides: browse -> take part -> own it or get credit
  states    swipe, dots, skip
  a11y      each slide one heading + one sentence; "skip" always reachable
  used on   S01
```

## Copy

All labels above are first drafts written for this app, not taken from the
original. `/replica-brand` sets the voice; `/replica-build` writes the final
Arabic + English strings in `i18n/ar.json` and `i18n/en.json`.
