# Brand: Nazih (نزيه)

Angle (from `fixes.md`): **trust you can check.** The original's 1★ reviews
call it a scam (4 of 4 negative reviews) and gambling (the most-agreed review,
35 helpful votes). Nazih answers with proof, not promises.

## Name

**Nazih / نزيه**: "honest, impartial, clean of fraud". It names the angle
directly, is short, and is easy to say and spell in Arabic and English.

Watch-outs:
- It's also a common man's name in Arabic. Fine for a brand (personal names
  are registrable), but check the trademark search for conflicts.
- Nothing in it echoes the original (no luck, fortune or prize, and no "-it").

### 20 candidates → 5

| style | candidates |
| --- | --- |
| descriptive | واضح Wadeh (clear), شفاف Shafaf (transparent), شريك Shareek (partner), حصة Hissa (share) |
| metaphor | ميزان Mizan (scale/justice), بيّنة Bayyina (clear proof), أمانة Amana (trust), دليل Daleel (proof) |
| invented / short | Nazih نزيه, Saafi صافي, Sadeed سديد, Proofy |
| compound | ClearShare, FairPool, ProofBuy |
| social | لمّة Lamma (gathering), معاً Ma'an (together), شلّة Shalla (crew), جمعية Jam'iyya (savings circle) |
| verb | شارك Shaarek (take part) |

Cut because: luck or fate words (قسمة Qisma) sound like gambling; Amana and
Jam'iyya are widely used by banks and fintechs; the English compounds lose the
Arabic-first audience.

### Screening checks for the 5

Only the web search could be run from this session (2026-11-15). Everything
else is **to run**; nothing here is "available" until someone checks.

| check | Nazih نزيه | Mizan ميزان | Wadeh واضح | Bayyina بيّنة | Lamma لمّة |
| --- | --- | --- | --- | --- | --- |
| web + app stores (search, 2026-11-15) | no app by that name; nearest is the government Integrity Commission app (النزاهة) | none found in Jordan; common word, likely used elsewhere | none found; generic adjective, weak as a trademark | **crowded**: Quran apps (Bayyinah TV, Ayat Bayyinat) | **taken**: several apps, incl. a fintech wallet |
| Jordan trademark (Ministry of Industry, Trade and Supply) | to run | to run | to run | - | - |
| WIPO Global Brand Database (classes 9, 35, 36, 42) | to run | to run | to run | - | - |
| US / EU / Canada | to run (only if you'll launch there) | to run | to run | - | - |
| domains (nazih.jo, nazih.app, getnazih.com) | to run | to run | to run | - | - |
| App Store + Google Play exact name | to run (on your phone) | to run | to run | - | - |
| handles (Instagram, TikTok, X, Facebook) | to run | to run | to run | - | - |

**Recommendation:** Nazih, then Mizan as a backup. Before paying for anything
under the name, have a Jordanian trademark lawyer do a proper search. These are
screening checks only.

## Palette

The original is **gold #FCC800 on black** (measured from its icon). Nazih is
**deep teal**, a different hue family, never paired with gold:

| role | light | dark |
| --- | --- | --- |
| accent | `#0f6e7a` | `#5ccfdb` |
| accent-strong | `#0a4f58` | `#b3ebf1` |
| accent-soft | `#e2f3f5` | `#10303a` |
| on-accent | `#ffffff` | `#0f1115` |

Teal sits apart from the green used for "success", so the brand colour never
reads as a status. Contrast: `contrast.py` → **21 pairs, 0 AA failures** in
both themes. The original's golds are in `brand.json`, so the sweep catches
them if they ever creep in.

## Logo brief (for a designer or an image tool)

- **Idea:** a result you can check. A simple mark built from a check or a
  seal, honest and calm, not flashy.
- **Mark:** symbol + wordmark (نزيه / Nazih). The symbol must stand alone as the app icon.
- **Must work** at 16px (favicon) and as a 1024px app icon.
- **Deliverables:** SVG (symbol, wordmark, lockup; Arabic and Latin), app icon
  1024×1024 with no transparency (iOS), Android adaptive foreground and
  monochrome, favicon set, social image 1200×630.
- **Must not resemble the original:** no shield, no monogram of two letters,
  no gold, no gold-on-black. Put the drafts next to the original's icon and check.
- **Avoid:** a lone "ن" as the symbol. It was painted on Christians' homes
  in Mosul in 2014 and became a solidarity symbol. Too loaded for a brand mark.
  Also avoid coins, dice, tickets, crowns and confetti: they read as gambling.

Placeholder in the app now: a white check on teal (`app/assets/`). Replace it with the real mark.

## Voice

| word | means | doesn't mean |
| --- | --- | --- |
| **صادق** honest | say exactly what happens to the money | blunt or cold |
| **هادي** calm | short, steady sentences; no hype | flat or passive |
| **واضح** clear | numbers and next steps up front | technical |

| do | don't |
| --- | --- |
| "إذا ما طلع مكانك، المبلغ كامل بيرجعلك رصيد تسوق." | "فرصتك لتربح آيفون!" |
| Say how the winner is picked | "حظك اليوم!" / any luck language |
| Show amounts in full: "1.000 د.ا" | Round, or hide the per-spot price |
| Admit errors plainly: "صار خلل بالاتصال" | Promise what we can't check ("ما انخصم منك شي" after a network error) |
| Promise only what the app does today | "Use your balance on anything" (not built yet) |

Strings rewritten in this voice (`app/src/i18n/ar.ts`): the three onboarding
slides (the third now explains the draw and the refund), the login subtitle,
the pending-result text, the credit message ("كامل المبلغ…"), the sold-out
message, the connection error, and a new line in the confirm sheet saying
what happens if your spot isn't picked, shown before you pay.

## Sweep

`sweep.py . --config replica/brand.json` → **clean** (2026-11-15). The only
hit before the fixes was a comment in the first migration. Also checked by
eye: app name, link scheme (`nazih://`), notification channel, push titles,
icons, favicon, and the web page title (now "Nazih").
