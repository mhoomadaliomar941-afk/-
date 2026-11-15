# Feedback

**Sample: 0 verified reviews.** `reviews.csv` has no rows yet, so there is
nothing to rank. See `fixes.md` → "Collecting reviews" for how to fill it,
then run:

```bash
python3 reviews.py replica/reviews.csv --themes replica/themes.json --out replica/feedback.md
```

(`replica/themes.json` has themes for this kind of app, with Arabic keywords; checked on test sentences.)
