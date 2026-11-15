# Components

An original design system for a trick-puzzle game for players under 13.
No screenshots of the original could be captured (store hosts are blocked in
this session), so nothing here is measured from it: the roles and patterns
are the genre's, the values and the look are ours. `accent` is a placeholder
that `/replica-brand` replaces.

Stack is React Native, so tokens ship as `tokens.ts` (generated from
`tokens.json`), not CSS or Tailwind.

## Rules for every component

- **Touch targets are 56px or more**, with 8px between them. Small hands miss.
- **Never colour alone.** Right and wrong, solved and locked always carry an
  icon or shape as well as a colour.
- **Reading level.** Short words, one idea per line, 18px minimum. An icon
  beside every action label.
- **Arabic and English.** Layout mirrors in right-to-left: use start/end,
  never left/right. Numbers and the bulb counter do not mirror.
- **Screen readers.** Every control has a label and a role; scene objects
  have a name ("cat, asleep"). Puzzle answers are never put in a label.
- **Motion.** Honour the system "reduce motion" setting: swap bounces and
  confetti for a fade.
- **Icons:** Phosphor (MIT), "fill" weight. **Illustrations and sounds:**
  made for this game. Nothing traced or taken from the original.
- **Selling to children.** No countdown timers, no "last chance", no
  pre-ticked options, prices always shown before the store sheet. Shop and
  subscription sit behind a parent gate.

## Specs

```
QuestionText
  variants  static, with draggable words
  states    default, word held (lifted, shadow pop), word dropped
  tokens    text on bg, type xl/700, centred, max 3 lines
  a11y      header role; read aloud on level start
  used on   S03

SceneObject
  variants  tappable, draggable, hidden, combinable
  states    idle, pressed (scale .96), held (shadow pop), correct (bounce + tick),
            wrong (shake + cross), disabled after solve
  tokens    sits on scene; focus ring 3px accent
  a11y      hit area padded to 56px even when the sprite is smaller; named
  used on   S03

AnswerInput
  variants  number pad, text
  states    empty, filled, wrong (shake, danger border + cross), disabled
  tokens    surface, border-input 2px, radius md, type lg, height 60
  a11y      labelled; error announced, not only coloured
  used on   S03

BulbCounter
  states    idle, gaining (count up, reward pulse), spending (count down), pressed
  tokens    reward bg, on-reward text, radius pill, type lg/700, height 56
  a11y      "12 bulbs, open shop" button
  used on   S02, S03, S09

IconButton
  variants  hint, skip, retry, settings, map, close
  states    default, pressed (drops 4px onto its shadow), disabled, focus
  tokens    surface, text, border 2px, radius md, 56x56, shadow button
  a11y      label always set; hint and skip also show their bulb cost as text
  used on   S02, S03, S07, S11

PrimaryButton
  variants  accent (Play, Next, Claim), reward (Watch ad), quiet (No thanks)
  sizes     md 56px, lg 60px
  states    default, pressed, disabled, loading (spinner, label kept)
  tokens    accent / on-accent, radius lg, type lg/700, shadow button
  a11y      real button role; "No thanks" is the same size as the yes button
  used on   S02, S04, S05, S06, S08, S09, S10

Modal
  variants  hint, skip confirm, daily gift, parent gate, offer
  states    opening (base motion), open, closing
  tokens    surface, radius lg, shadow pop, padding 24, overlay text at 50%
  a11y      focus trapped, back button closes, title announced
  used on   S04, S05, S08, S09, S10

Feedback
  variants  right (success + tick), wrong (danger + cross)
  states    animating (celebrate / fast), gone
  tokens    on-status on success or danger, haptic to match
  a11y      announced as "Correct" or "Try again"; never blocks input over 600ms
  used on   S03, S06

LevelTile
  states    solved (success tick), skipped (dashed border, arrow icon),
            current (accent ring, gentle pulse), locked (locked bg, padlock)
  tokens    64x64, radius md, type lg/700
  a11y      "Level 12, solved"; locked tiles announce why
  used on   S07

ShopCard
  variants  bulb pack, remove ads, bundle, subscription
  states    default, purchasing (loading), owned (tick, disabled), failed (inline message + retry)
  tokens    surface, border, radius lg, price in text type lg/700
  a11y      price and what you get read together; reached only through ParentGate
  used on   S09, S10

ParentGate   (new: not in the recon, required for a children's game)
  does      a question a young child cannot answer (e.g. a two-digit sum)
            before the shop, any purchase, and any link out of the app
  states    asking, wrong (new question), passed (for this visit only)
  used on   S09, S10, S11 links

Toggle
  states    on (accent + tick), off, disabled, focus
  tokens    56px tall row, label at the start, switch at the end
  used on   S11

AdSlot       (frame only; the ad itself comes from the SDK)
  states    loading, shown, no fill (skip silently between levels; on a
            rewarded ad say "No video right now" and offer bulbs instead)
  rules     never over the scene, never next to a game button, never on S03
  used on   S12
```

## Layout

One column, portrait only. Content max width 520px, centred on tablets.
Screen padding 16. S03 from the top: counter and level number row (56),
question, scene (fills what is left, square where possible), action row
with Hint, Skip and Retry.

## Not done here

The skill's last step is to build these as code on a showcase screen. No
app project exists yet, so that is the first task of `/replica-build`.
