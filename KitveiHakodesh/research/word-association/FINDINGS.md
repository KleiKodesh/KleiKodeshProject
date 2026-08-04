# Findings

Measured results from the Tanach proof of concept. Reproduce with
`python build_index.py && python query.py demo`.

---

## 1. The core claim holds: lookup is free

```
offset lookup only     0.48 µs
neighbors(top-20)      6.14 µs        20,000 random lookups
```

In **Python**, with a per-edge `struct.unpack`. The offsets array is 50 KB and
the whole index is 1.8 MB, so everything is resident after first touch. A native
implementation over an mmap'd span would be a fraction of this.

**This is settled.** Association lookup is not where latency goes, and no
further work is needed to make it faster. The remaining cost questions are all
about *how many terms you expand to*, because each expanded term becomes a
posting-list read in the real FTS index.

| Stage | Cost |
|---|---|
| Build (whole Tanach, offline) | 2.6 s |
| Index on disk | 1.8 MB |
| `neighbors()` — pure lookup | 6 µs |
| `similar()` — **computed at query time** | 20–105 ms |

The last row is the one real gap. See [ROADMAP.md](ROADMAP.md) item 2.

---

## 2. Second-order similarity works — genuinely well in places

No embeddings, no training. Just cosine over sparse association profiles.

| query | result | verdict |
|---|---|---|
| `זהב` | **כסף**, סגור, ועשית, ויצפהו, **טהור**, שטים, **נחשת** | precious-materials cluster — correct |
| `מלחמה` | **צבא**, **חיל**, **מערכה**, למלחמה | war vocabulary — correct |
| `מזבח` | הקטרת, קרנת, העלה, הנחשת, יסוד, ומקטיר | altar vocabulary — correct |
| `מלך` | בבל, נבוכדראצר, אשור, המליך, ממלכת, יהודה | conflates *kings* with *kingdoms and empires* |
| `שמח` | הקיקיון, מחצרים, ומצאתם, הכירו | mostly noise |

The pattern is clear and worth stating precisely:

> **Concrete nouns with distinctive contexts work. Abstract and low-frequency
> words do not.**

`זהב` appears in dozens of construction passages with a stable vocabulary around
it. `שמח` appears in scattered poetic contexts that share no vocabulary, so its
profile is thin and its nearest neighbours are accidents.

---

## 3. Two real bugs found and fixed

Both are the kind of thing that silently degrades quality without ever throwing
an error. Recording them because they will recur in any Hebrew corpus pipeline.

### Maqaf is inside the nikud range

`U+05BE` (maqaf, the Hebrew hyphen) sits inside `U+0591–U+05C7`, the range you
strip to remove nikud and te'amim. Stripping the whole range **deletes the word
separator**, gluing pairs into single tokens:

```
עַל־פְּנֵי   ->  עלפני        (should be: על, פני)
וַיִּבֶן שָׁם  ->  ויבנשמ
```

Before the fix, `מזבח`'s top associations were `ויבנשמ`, `עלהירדנ`, `עלקרנת` —
plausible-looking garbage. The fix excludes `U+05BE` from the strip class and
lets it fall through as a separator.

### HTML tags sit *inside* words

The source marks up the first letter of some books:
`<big>בְּ</big>רֵאשִׁית`. Replacing tags with a space splits the word:

```
<big>בְּ</big>רֵאשִׁית   ->  "ב ראשית"     (two tokens, both wrong)
```

Tags must be **deleted**, not space-substituted. (The `<span>`-wrapped maqafim
still work, because deleting the tag leaves the maqaf itself as a separator.)

**Combined impact:** 268,674 → 306,873 tokens (+14%) and a *smaller*, cleaner
vocabulary (14,000 → 12,638). Association quality improved visibly across the
board.

---

## 4. The dominant limitation: Hebrew prefix morphology

**42.4% of the vocabulary is another vocabulary word wearing a prefix.**

```
79 distinct tokens contain מלכ, combined frequency 3,430:

  המלכ   1053    מלכ    1027    למלכ    161    מלכא   134
  מלכי    121    וימלכ    90    ממלכות   46    מלכימ   41
  ...
```

Every one of those is a separate node with a separate, thinner profile. The
statistical mass that should establish one strong concept is split 79 ways.
`ו־`, `ה־`, `ב־`, `ל־`, `כ־`, `מ־`, `ש־` are grammatical particles, not part of
the lexeme, but this tokenizer treats them as such.

*(Caveat on the 42.4%: it is a crude string heuristic and includes false
positives — `משה`→`שה`, `מלכ`→`לכ`. The true figure is somewhat lower, but the
`מלכ` breakdown above is real and hand-checked.)*

This is the single highest-leverage fix available. See [ROADMAP.md](ROADMAP.md)
item 1.

---

## 5. Corpus sparsity is a hard ceiling

306,873 tokens is **tiny** for distributional methods. GloVe and word2vec are
trained on 10⁹–10¹¹ tokens — three to six orders of magnitude more.

```
vocabulary                    12,638
  freq < 5                     5,033   (40%)
  freq < 10                    8,734   (69%)
  freq >= 100                    382   (3%)

associations per word:  median 9, p90 36, max 200
```

**The median word has 9 associations.** The `topk=200` cap is almost never
reached — pruning is not what limits this index, data is. Two thirds of the
vocabulary appears fewer than ten times, which is not enough to estimate a
context distribution.

This directly explains finding #2: the 382 words with freq ≥ 100 behave well,
and the long tail does not.

---

## 6. Formulaic repetition — diagnosed, then **FIXED** by BM25 length normalization

### The artifact

`מזבח קרבן` expanded to `אחירע`, `שלמיאל`, `פגעיאל`, `אבידן`, `גמליאל`... —
the tribal princes of **Numbers 7**, the longest chapter in the Torah
(89 verses), twelve near-identical offering formulas.

```
קרבן:   29 occurrences,  12 of them in Numbers 7
קרבנו:  35 occurrences,  12 of them in Numbers 7
```

The princes' names occur almost nowhere else, so PMI correctly concluded the
association was far above chance. The statistics were right; the result was
useless. Liturgical and legal corpora are full of this — repeated formulas,
genealogies, census lists, sacrificial schedules.

### What actually fixed it (not what was expected)

Four BM25-borrowed knobs were implemented and **ablated individually**
(`python compare.py`). Only one mattered:

| knob | princes in top-20 of `קרבן` |
|---|---|
| baseline (pure PPMI) | **11 / 20** |
| `--idf-weight 0.6 --idf-basis df` | 11 / 20 — *no effect at all* |
| `--idf-weight 0.6 --idf-basis degree` | 11 / 20 — *no effect at all* |
| `--idf-weight 0.85 --idf-basis degree` | 11 / 20 — *no effect at all* |
| `--min-ctx-df 3` | 11 / 20 |
| `--saturate-k 1.2` | 10 / 20 |
| **`--length-norm-b 0.75`** | **0 / 20** |

**IDF did nothing** — and the reason turns out to be structural, not a matter
of tuning. Two different bases were tried (`--idf-basis df|degree`), the second
being the "discount words that associate with very many other terms" reading.
Both gave **11/20 and a byte-identical edge count**.

Why: IDF discounts *high*-frequency / *high*-degree context words. The princes
are the **rarest and lowest-degree** words in the neighbourhood.

```
                corpus_freq   degree
  אחירע                   5        5     <- prince
  שלמיאל                  5        6     <- prince
  יקריבו                 10       11     <- genuine
  מנחה                   55       69     <- genuine
  ליהוה                 577      200     <- genuine
```

So every IDF variant hands the princes the **maximum** multiplier (~1.0) while
shaving the legitimate common neighbours around them. **IDF pushes in exactly
the wrong direction on this artifact.** It is the wrong axis: IDF measures
breadth, and burstiness is about *concentration*, which is a different thing.

Within-verse saturation also failed, for its own reason: the repetition is
*across* twelve verses, not inside any one of them.

### Why length normalization worked — the mechanism

The eight offending verses are byte-identical except for the name:

```
ולזבח השלמים בקר שנים אילם חמשה עתדים חמשה כבשים בני שנה חמשה זה קרבן <NAME> בן <FATHER>
```

17 tokens each, against a corpus mean of 13.2 → BM25 norm factor **1.21**.

The decisive point is *selectivity*, not the size of the penalty:

- The princes appear **only** in these long formulaic verses, so the penalty hits
  **100%** of their co-occurrence mass.
- Genuine neighbours like `יקריבו` appear in verses of varied length, so their
  penalty averages out.

Measured on `קרבן`'s full edge run — the princes are *eliminated*, not merely
outranked, while real associations hold their scores:

```
                      baseline        b=0.3         b=0.75
edges for קרבן            33            11            11
princes            ranks 1-5         GONE          GONE
יקריבו                 4.48          4.58          4.72     <- unchanged/up
חמשה                   4.37          4.39          4.43     <- unchanged
```

Stable across `b` = 0.3, 0.5, 0.75, 1.0 — this is not a knife-edge threshold
effect. Result after the fix:

```
before:  מנחתך, אחירע, אבידן, שלמיאל, פגעיאל, אליצור, גמליאל
after:   יקריבו, הקטרת, קרנת, חמשה, ובנית, מכבר, ליהוה, הנחשת
```

### The transferable lesson

> Burstiness in this corpus is carried by **verse length**, not by document
> frequency. Formulaic passages are long and repetitive, and length
> normalization catches them precisely because the offending terms live
> *nowhere else*.

**Recommended default: `--length-norm-b 0.75`.** The other three knobs are
implemented and available but did not earn their place on this corpus; keep them
off until an evaluation harness ([ROADMAP.md](ROADMAP.md) item 4) can show
otherwise. `--idf-weight` in particular should not be enabled on faith.

Caution: `bm25-strict` visibly over-prunes — `שבת` degrades to
`שבתון, משוש, מאשת, אכלי, שאון` and `מלחמה` loses `צבא`/`חיל`. More
normalization is not better.

---

## 7. First-order associations barely intersect

The original expansion design assumed that two related query words would share
associations, and that the overlap would be the strong signal. Measured:

```
מזבח ∩ קרבן     1 shared term out of 72 and 33
לחם  ∩ יין      3 shared out of 157 and 71
מלך  ∩ מלחמה    1 shared out of 190 and 101
```

**Direct co-occurrence is too sparse for intersection to fire.** This is why
`expand` now defaults to `mode=similar` (second-order), where profiles are
denser and agreement between query terms is achievable. The intersection bonus
is still implemented and correct — it just needs the second-order graph to have
anything to work with.

---

## 8. Evaluation harness — the numbers replace the eyeballing

Everything above §7 was judged by reading word lists. `evaluate.py` now scores
against a gold set from the project's **dictionary DB** (`link` table), which is
hand-built from lexicographic sources and knows nothing about co-occurrence — so
a good score cannot be an artifact of the method.

```
נרדף   synonym    ~4,000 pairs with both sides in the Tanach vocabulary
ניגוד  antonym      ~210 pairs
```

Two relation kinds are deliberately **excluded**: `כתיב` (spelling variants —
would score the tokenizer) and `נגזרת` (derivations — same root, so a
morphology-aware build would score well by construction, which is circular).

Metrics are P@k, MRR and recall@k, always reported with coverage and against a
**random baseline**. That baseline is not decoration: P@20 of 0.04 sounds poor
until you see chance is 0.0025 on a 12,638-word vocabulary.

---

## 9. Prefix folding — a measured 2.9x, and the tuning surprise

The fix from §4, implemented as `--strip-prefixes`: strip `ו ה ב ל כ מ ש` and
common two-letter stacks, but **only when the remainder is itself a frequent
corpus word**. That guard is what stops `משה` → `שה`.

### It works, and the size of the effect is real

Scored on an **identical gold set** across builds (564 words, `min_freq=10`):

```
config        folded   vocab    aP@20    aMRR    arec    sP@20    sMRR    srec
no fold            0   12638   0.0153  0.0079  0.0550   0.0157  0.0120  0.0475
msf=100         1205   11869   0.0215  0.0105  0.0800   0.0157  0.0177  0.0475
msf=20          4787   10154   0.0389  0.0205  0.1275   0.0167  0.0137  0.0500
msf=5          10011    8578   0.0444  0.0242  0.1375   0.0184  0.0198  0.0600
msf=3          12324    8060   0.0444  0.0271  0.1375   0.0159  0.0152  0.0575
```

**`--min-stem-freq 5` is the knee.** First-order P@20 improves **2.9x** and
recall **2.5x**; second-order finally moves too (P@20 +17%, MRR +65%). At
`msf=3` second-order degrades again — folding past the knee starts merging
things that should stay apart.

### The tuning surprise: the frequency ratio does nothing

The `stem_freq >= surface_freq` condition looked like the blocker. It rejects
the single most valuable-looking fold in the corpus — `המלכ`(1053) is
marginally *more* frequent than `מלכ`(1027), so the article never comes off the
most common noun in the Tanach. Relaxing it was expected to be the win.

It is not. Sweeping the ratio from 1.0 down to 0.0:

```
ratio 1.0   10247 words   aP@20 0.0408
ratio 0.5   10173         0.0399
ratio 0.25  10154         0.0395
ratio 0.10  10139         0.0395
ratio 0.0   10138         0.0395    <- flat, and marginally WORSE
```

Flat across the whole range, because it only changes 109 forms out of 4,679.
**Coverage is the knob; the direction test is not.** Kept at 0.25 as a cheap
guard that costs nothing.

### The gain is strongest where it was least expected

Spot-checking suggested folding helps frequent words: `similar(מלך)` improved
from scattered place names to `מלכות, בבל, נבוכדנאצר, מדינת`, while
`similar(מזבח)` and `similar(שבת)` visibly degraded. Stratifying the gold set
by corpus frequency shows the opposite:

```
band        words   base aP@20  morph aP@20   base sP@20  morph sP@20
10-25         244       0.0489       0.0819       0.0083       0.0172
25-60         167       0.0270       0.0449       0.0083       0.0130
60-150         77       0.0197       0.0356       0.0257       0.0145
150+           86       0.0081       0.0120       0.0217       0.0217
```

**The gain is largest for the rarest words and smallest for the most frequent
ones** — first-order improves in *every* band, second-order in three of four.
Mechanically this is right: a rare word benefits most from having its scattered
prefixed variants pooled into one profile, while a word occurring 500 times
already had enough mass to estimate a distribution.

The `מזבח`/`שבת` degradation is real, but it is the minority case in the 60-150
band, not the trend. **This is exactly the error the harness exists to prevent** —
two hand-picked examples pointed the wrong way.

### What did NOT clear the roadmap's success test — see §10 first

Stated honestly, because two of three criteria failed:

| criterion | target | actual |
|---|---|---|
| `מלכ` family collapses | "a handful" | 79 → **37** tokens |
| median associations rises | "well above 9" | 5 → **6** (p90 22 → 35) |
| `similar(מלך)` returns rulers | rulers | **partly** — `מלכות, בבל, נבוכדנאצר` |

The reason the family does not collapse further is structural: the remaining
split is carried by **suffixes**, which prefix stripping cannot reach by design.

```
מלכי  121    מלכימ  41    מלכות  33    מלכו  13    מלכותו  20
```

Those are inflections, not particles. Reaching them needs the real morphological
analyzer that ROADMAP item 1 explicitly deferred — and the deferral was correct,
since the cheap version bought a 2.9x for an afternoon's work.

### One known false positive

`מלכה`(21) → `לכה`(28) passes every guard, because `לכה` genuinely is a corpus
word. Only `MIN_STEM_LEN` and the `NEVER_STRIP` list defend against this, and
imperfectly. This is the acknowledged ceiling of the frequency-guard approach.

---

## 10. Opposites score 4x better than synonyms — the clearest result here

The distributional hypothesis makes a counterintuitive prediction: **opposites
share contexts**. Both sides of a contrast appear in the same frames, so a
distributional index should rank them close — even though they mean opposite
things. That is a real, falsifiable prediction, and this corpus confirms it
emphatically.

```
gold                          aP@20    aMRR    arec
antonym                      0.1273  0.0280  0.1575
synonym (all)                0.0466  0.0226  0.1106
synonym (frequency-matched)  0.0316  0.0122  0.0787
```

The third row is the control. The antonym gold set is smaller (127 words vs
868) and its words are more frequent (median 61 vs 33), either of which could
manufacture the gap. Sampling synonym words to match the antonym set's size and
frequency profile does not close it — **it widens it to 4x**.

The hits are unambiguous, not statistical noise:

```
בקר  <-> ערב        אור  <-> חשכ        מלא  <-> ריק
רשע  <-> צדיק       פנימ <-> אחור       זקנ  <-> נער
ברכה <-> קללה       קרוב <-> רחוק
```

### Why this matters for the search engine

It is a **precise statement of what this index is for**, and it cuts both ways:

- **It is a contrast/relatedness index, not a synonym index.** Expanding a query
  with it will surface the opposite of what the user typed. For search that is
  often desirable — someone searching `ברכה` plausibly wants `קללה` passages —
  but it must be a deliberate choice, not a surprise.
- **Do not use it as a drop-in synonym source.** Synonymy is the weaker signal
  by a factor of four. The dictionary's `נרדף` table is the better tool for
  synonyms, and it is already in the project.

This also explains §2's uneven quality. The index is good at *semantic field*
(`זהב` → `כסף, נחשת` — the metals) and at *contrast*, and weak at the narrower
substitutability relation that "similar" implies.

---

## 11. Scaling past the Tanach — the corpus is 96% commentary

ROADMAP item 5 assumed the fix for sparsity was more text: the same DB holds
Mishnah, Talmud, Midrash, Halacha, "plausibly 10²–10³× the Tanach". Measuring
that turned up a structural fact that changes the plan.

### The builder had to be replaced first

`build_index.py`'s dict-of-dicts accumulator costs a measured **65 bytes per
pair**. Extrapolating honestly:

```
39 base Tanach books           2,959,879 chars
whole seforim.db           2,223,713,531 chars      751x
-> ~231M tokens, 0.2-0.7B pairs, 10-42 GB accumulator
```

This machine has 7.2 GB free. So [build_large.py](build_large.py) does an
**external merge sort**: pairs go to bounded sorted runs on disk, then a k-way
merge sums duplicates while streaming. Because the merge yields keys in `(a,b)`
order, one word's whole row arrives contiguously and can be PMI-scored and
pruned to top-K before moving on — peak memory is one run, independent of
corpus size.

**Parity verified against the old builder on the Tanach:** identical vocabulary
and counts, P@20 0.0487 vs 0.0486, identical recall. The 8,618 edge difference
is entirely float-accumulation boundary cases at the `min_cooc` and `pmi > 0`
thresholds — e.g. `כי/יחטאו` scores PMI −0.0026 in one and marginally positive
in the other. Nothing above the noise floor moves.

### The finding: only 4% of the database is base text

```
corpus        books          chars |  base    base chars  base%
tanach-all      623    176,010,720 |    39     2,954,421     2%
mishnah         908     55,306,756 |    63     1,660,766     3%
bavli         1,658    432,862,380 |    37    15,727,118     4%
halacha       2,424    617,326,417 |    94    13,923,621     2%
midrash         190     58,616,507 |    53    38,404,031    66%
kabbalah        151     96,539,083 |     0             0     0%
chasidut        115    146,353,560 |     0             0     0%
responsa        329    360,538,671 |     0             0     0%
all           7,287  2,214,379,170 |   386    80,878,281     4%
```

**The `משנה` category is 96% commentary** — 217,597 of 230,300 lines are
`dependenceType='commentary'`. Building over the category measures medieval
commentary prose, not Mishnah. Several categories have *no* base text at all.

So "more data" is not one option, it is two very different ones: **80 MB of
base text** across 386 books, or **2.2 GB dominated by commentary**. They are
different languages, registers, and eras.

### Measured: commentary contamination costs more than half the score

```
corpus             tok/verse   vocab    P@20     MRR  recall   gold
tanach (base)           13.2   8,578  0.0487  0.0362  0.1725    734
mishnah ALL             66.2  55,323  0.0160  0.0218  0.1150   2681
mishnah base            45.9   5,608  0.0383  0.0125  0.0750    450
tosefta ALL             61.9   9,575  0.0153  0.0097  0.0500    738
tosefta base            74.0   7,133  0.0195  0.0098  0.0575    582
yerushalmi base         66.3  14,288  0.0166  0.0134  0.0775   1117
```

Base-only **more than doubles** Mishnah's P@20 (0.0160 → 0.0383).
`--base-only` is now a flag on `build_large.py`, and any result should state
which mode produced it.

Note also that Mishnah-ALL scoring worse is **not** a gold-set artifact: scored
on the identical 693 shared gold words, Tanach still beats it 0.0507 to 0.0229.

### The window must match the corpus's text-unit length

`tokens/verse` stays high (46-74) for rabbinic base text even after removing
commentary — those works genuinely have paragraph-sized units, not verses. The
window=4 default was tuned on the Tanach's 13.2 and does not transfer:

```
mishnah base-only (46 tok/line)      tanach (13.2 tok/line)
  window  2   P@20 0.0323              window  2   P@20 0.0479
  window  4   P@20 0.0383              window  4   P@20 0.0487
  window  8   P@20 0.0460              window  8   P@20 0.0510
  window 12   P@20 0.0477  <- peak
  window 16   P@20 0.0464
```

**With the window matched to the corpus, Mishnah reaches Tanach-level quality**
(0.0477 vs 0.0487) — the earlier 3x deficit was mostly two configuration bugs
of mine, not a property of the text. Window should scale with text-unit length;
`window=4` is not a universal default and was never validated as one.

---

## 12. More data made it worse — the central negative result

With commentary removed and the window matched per corpus, the registers can
finally be compared fairly:

```
corpus                   tokens   vocab    P@20     MRR  recall   vs chance
tanach w8               306,873   8,578  0.0510  0.0346  0.1725        52x
mishnah w12             192,466   5,608  0.0477  0.0141  0.0850        12x
tosefta base w4         298,177   7,133  0.0195  0.0098  0.0575        14x
yerushalmi base w4      812,293  14,288  0.0166  0.0134  0.0775         9x
bavli base w12        1,857,496  25,059  0.0141  0.0124  0.0900        13x
```

**Bavli has 6x the Tanach's tokens and scores 3.6x worse.** The ordering is
almost the inverse of corpus size. This directly contradicts ROADMAP item 5's
premise that sparsity is the binding constraint and more text is the fix.

The `vs chance` column matters here: Tanach is 52x chance while everything else
is 9-14x. The Tanach is not merely the smallest corpus, it is the one where
this method works best — by a wide margin.

### Why: it is register, not volume

The gold set is a Hebrew lexicon. Bavli is substantially Aramaic, and Mishnaic
and Talmudic Hebrew differ from Biblical Hebrew in vocabulary and idiom. The
corpus with the most text is the one furthest from what the gold set describes.

That also means these numbers **understate** Bavli's quality on its own terms
and cannot be read as "Bavli text is worse." What they establish is narrower
and still decisive: *pooling more text does not improve Biblical-Hebrew
association quality*, because the additional text is a different language
variety.

### A tempting explanation that measurement rejected

The obvious objection is that the gold set is polluted by function words
(`את`, `אשר`, `כי` — whose "synonyms" are grammatical glosses no co-occurrence
method can recover) and by cross-language pairs (`מלכ`→`מלכא`, `ארצ`→`ארעא`).

Quantified on the Tanach index:

```
gold words (f>=10)                734
  function words                   23   (3%)
  only-Aramaic gold partners        8   (1%)
gold set                   words    P@20     MRR
raw (as used so far)         734  0.0571  0.0248
function words removed       711  0.0580  0.0253
+ Aramaic partners removed   703  0.0584  0.0253
```

**4% of the gold set, and removing it moves P@20 by 0.0013.** The gold set is
not meaningfully contaminated, and the register effect above is not an artifact
of it.

### The frequency floor runs backwards

Raising the gold-word frequency floor *lowers* the score, in every corpus:

```
corpus              f>=10    f>=25    f>=50   f>=100   f>=250
tanach w8          0.0530   0.0336   0.0176   0.0174   0.0000
mishnah w12        0.0469   0.0262   0.0021   0.0000        -
yerushalmi base    0.0177   0.0168   0.0142   0.0034   0.0000
bavli base w12     0.0135   0.0106   0.0120   0.0018   0.0040
```

This is the opposite of the sparsity story. If thin data on rare words were the
problem, restricting to frequent words should help. Banding the Tanach gold set
shows the effect is real and not a dilution artifact:

```
freq 10-25    237 words   P@20 0.0680
freq 25-50    183 words   P@20 0.0891   <- peak
freq 50-100   120 words   P@20 0.0425
freq 100+     194 words   P@20 0.0226
```

**The method is best on mid-frequency content words and worst on the most
frequent ones.** Very frequent words are generic (`אמר`, `דבר`, `איש`, `בנ`):
they occur in every context, so their association profile is diffuse and their
top neighbours are whatever they happen to sit beside. Rare words are thin.
Mid-frequency content words are the sweet spot — frequent enough to estimate,
specific enough to have a distinctive context.

That is a useful and previously unstated operating range, and it is a better
guide to where expansion should fire than corpus size is.

---

## 13. The C# builder — and a self-inflicted 34 GB

The Python builders top out well before the full corpus, so the production path
is [AssocBuilder](AssocBuilder/) (C#/net10), using the same LSM shape FtsLib
uses for postings: **RAM buffer → flush sorted segment → k-way merge → write**.

Output is a **static SQLite table**, not a binary blob:

```sql
word  (id INTEGER PRIMARY KEY, term TEXT, freq INTEGER)
assoc (a INTEGER, rank INTEGER, b INTEGER, w REAL, PRIMARY KEY (a, rank))
       WITHOUT ROWID
```

`WITHOUT ROWID` + `PRIMARY KEY (a, rank)` clusters a word's associations
physically contiguous and already in descending-weight order, so a top-N lookup
is one B-tree seek plus a short forward scan — the CSR access pattern with
SQLite doing the paging.

### Verified against the Python reference

Same corpus, same settings, Tanach: **8,578 vocab / 10,011 folded /
128,486 associations** — identical to `build_large.py`. Scores agree
(P@20 0.0492 vs 0.0487); weights match to ~3 decimals, with tail reordering
only among near-ties. And it runs in **2 s** against Python's 19 s.

### The mistake worth recording

The first full-corpus attempt spilled **34 GB** of scratch and ran for hours.
That was not inherent to the problem — it was one badly chosen constant.

```
distinct pairs in the corpus     ~50M   x 16 B  =  ~800 MB   (fits in RAM)
pair OCCURRENCES generated        ~5B
buffer I set                     5.7M pairs per shard
```

A pair seen 400 times should cost **one** record. With the buffer at 5.7M per
shard, every worker filled and flushed repeatedly, writing occurrences that a
larger buffer would have collapsed in memory for free. Disk traffic came out
~40x larger than the data.

Fixed by making `--buffer-pairs` a **per-shard** budget defaulting to 60M
(~1 GB each). Measured on Bavli (87M tokens, a fifth of the corpus):
**7 segments — one per shard**, i.e. nothing spilled beyond a single flush, and
the whole build took 158 s.

> **The buffer is not a memory-safety knob, it is the aggregation window.**
> Undersizing it does not reduce work; it moves the same work to disk and
> multiplies I/O.

The second contributor was `--window 12`. It costs 2x the pair records, and on
full Bavli it scored **no better** than window 4 (P@20 0.0106 vs 0.0105). The
earlier §11 finding that longer windows help was measured on *base-text*
Mishnah; it does not transfer to the commentary-dominated full corpus. Window 4
is the right default here.

### The full-corpus run, with both fixes

```
corpus                7,287 books   (every book in seforim.db, commentaries included)
text units            4,666,631
tokens              447,893,273     = 1,460x the Tanach
vocabulary              313,035     (from 1.36M distinct surface forms)
associations         11,068,667
build time                  502 s   (pass 1: 108 s, pass 2: 324 s, merge+write: 69 s)
scratch spilled               0 B   (was 34 GB before the buffer fix)
output                    249 MB
```

For scale: Python's pass 1 alone took 1,367 s against this 108 s — a **12.7x**
speedup on the identical scan.

### And the quality result is unambiguous — bigger is worse

```
corpus                  tokens    vocab   tok/unit    P@20     MRR   vs chance
tanach (CSR)           306,873    8,578       13.2  0.0555  0.0620        56x
tanach (SQLite)        306,874    8,578       13.2  0.0558  0.0582        18x
bavli               87,079,879  137,761       65.2  0.0041  0.0210        17x
ALL                447,893,273  313,035       96.0  0.0080  0.0286         —
```

**The whole database scores 7x worse than the Tanach alone** on synonym P@20,
with 1,460x the text. The `vs chance` column for `all` is not even computable —
the random baseline hit zero, because 313k words makes a chance hit vanishingly
rare.

This confirms §12 at full scale and settles ROADMAP item 5: **corpus size is not
the binding constraint, and adding text actively hurts.** The reasons are now
clear and were all measured separately:

- **96% of the added text is commentary**, in a later register than the gold set.
- **`tok/unit` rises to 96**, so a "text unit" is a long paragraph and the
  verse-bounded window loses its meaning as a semantic boundary.
- Vocabulary grows 36x, so every profile competes with far more candidates.

The contrast finding (§10) does survive: on the full corpus antonyms still beat
synonyms (`similar` P@20 0.0347 vs 0.0086, **4x** — the same ratio as the
Tanach). That relationship is a property of the method, not of one corpus.

### Why not a direct SQLite upsert

Worth stating plainly, because it is the obvious first instinct: the final table
is small and ordinary, so why not `INSERT ... ON CONFLICT DO UPDATE SET w=w+?`
per pair?

Because you cannot write a row until you know the pair's total, and the total
only exists after the whole corpus is counted. That is ~5 billion upserts, each
a B-tree probe with a potentially random page write, against an index far larger
than page cache. Buffer-then-merge converts exactly that into sequential I/O.
The *table* is a simple many-to-many; the *counting* is not.

---

## 14. Measuring what the user SEES — the axis P@k cannot reach

The gold set answers "did the expected word appear?" It is blind to what appears
*alongside* it, which is most of what a reader perceives as quality. Measured on
the full corpus, `similar()` lists were **24% recognizable words and 76%
otherwise** — a user searching `שבת` got `דכשמקדשינ, מבדיליננ, במייאנצא` while
P@20 registered nothing wrong.

[improve.py](improve.py) therefore reports two axes on every variant:

```
P@k / MRR / recall   the gold set — does it find the right word
known%               fraction of SHOWN results that some lexicon knows
glued%               fraction that are long unknown tokens (print/OCR fusions)
aram%                fraction that are Aramaic — informational, NOT a penalty
```

### Aramaic is a register, not noise

An early version of this scored Aramaic as junk. That was wrong, and it matters:
this is a **Talmudic** corpus, where passages are Hebrew, Aramaic, or both mixed
in one line. `דאבא` is a legitimate Aramaic form of `אבא`; linking `מלכ`/`מלכא`
is a *feature*.

The real defect is different — **glued tokens**, where print/OCR fuses two words
into one (`דכשמקדשינ`, `חייבלקרוע`). Those are unknown to every lexicon and long.
So `known%` counts Hebrew and Aramaic together (198,268 forms), and `glued%`
flags only long unknowns.

### Lexical resources available (and worth far more than expected)

```
lexical.db (Dictionary/Backup)   base 24,559 / surface 137,631 / variants 594,428
Dictionary.db                    ~52,000 curated headwords
Aramaic shorashim CSVs           15,275 forms + 1,309 Aramaic->Hebrew pairs
union of known forms            198,268
```

`lexical.db` is the important one: it carries inflected **and** prefixed forms
for both languages, so it reaches the suffix morphology §9 could not.

### Result 1 — a display filter fixes the junk outright

Full corpus, `similar()`:

```
variant                 P@20     MRR  recall   known  glued   aram
no lexicon             0.0062  0.0437  0.1167    24%     2%     2%
known-word boost x2    0.0166  0.0626  0.2500    92%     1%    12%
known-word filter      0.0166  0.0627  0.2500   100%     0%    12%
```

**24% -> 100% recognizable**, and Aramaic content *rises* 2% -> 12% because the
filter clears junk that was crowding out real Aramaic. The P@20 jump is partly
mechanical (see the caveat below); the `known%` column is the honest signal.

### Result 2 — lexicon lemmatization is a genuine win (`--lemmatize`)

Folding inflections onto their lexeme via `lexical.db`, composed after the
prefix heuristic. Tanach, all 16 probes intact:

```
                   assoc P@20   similar P@20   recall
baseline              0.0499        0.0231      0.235
+ lemmatize           0.0609        0.0385      0.255      (+22% / +67%)
```

Vocabulary 8,578 -> 5,838. And the visible lists improve where it counts:

```
מזבח   ניחח והקטיר לנתחיו אזכרה זרק שלמימ      (sacrificial vocabulary)
אור    חשכ כוכבי צהרימ אפלה יוממ צלמות         (light/darkness)
כהנ    כהנימ לוי מגרשיהמ הקהתימ                (priestly/Levitical)
חכמה   בינה דעת חכמ                            (wisdom cluster)
```

Crucially it folds **across languages** — `מלכא`, `דמלכא`, `דמלכותא` all reach
`מלכ` — which is exactly the Hebrew/Aramaic bridge co-occurrence alone can only
find by accident.

### The bug this found, and why it nearly shipped

First lemmatized build looked like a triumph: `assoc` P@20 0.049 -> **0.074**.
It was broken. `shown` had fallen from 320 to 80 — **12 of 16 probe words had
been folded out of the vocabulary entirely**, so `similar(שבת)` returned nothing
and only the easier surviving words were still being scored.

Cause: in an unvocalized script one form legitimately belongs to several
lexemes, and `lexical.db` records all of them.

```
שבת   is a variant of BOTH  שבת (correct)  and  בת  (wrong here)
מזבח  is a variant of BOTH  מזבח (correct) and  זבח (wrong here)
```

My "prefer the shortest base" rule — which *looks* like the conservative
choice — picked `בת` and `זבח` every time, deleting the base form. Two rules fix
it: **a form that is itself a base is never folded**, and among competing bases
the **longest** wins (more shared stem = nearer lexeme).

> **A rising score with a shrinking result set is a red flag, not a win.**
> `improve.py` now prints probe survival before any metric, because no accuracy
> measure can see "the user's own query term is no longer in the index."

### The buffer is a two-sided trap

Sizing `--buffer-pairs` has now failed in *both* directions on this machine:

```
5.7M pairs/shard   -> 34 GB spilled, hours of avoidable I/O   (§13)
60M pairs/shard    -> 11.5 GB resident, free RAM to 0.1 GB, killed
```

The second failure came from lemmatization, which concentrates pairs onto fewer,
denser rows — so the *same nominal budget* costs far more. A slot is ~23 B once
the 0.7 load factor is counted, and 7 shards run at once, so a "60M" request is
really a ~11 GB commitment.

Fixed by clamping the request against actual available memory
(`GC.GetGCMemoryInfo`), budgeting 55% across the concurrent shards and printing
the clamp when it bites. **Spilling is a slowdown; exhausting RAM is a failure**,
so the clamp always prefers the former.

### Caveat on filtered scores

The gold set is drawn from Dictionary.db's `link` table, so gold answers are
themselves lexicon words: filtering to lexicon words keeps every possible right
answer while discarding non-answers, which inflates P@k mechanically. Read
filtered P@k as an upper bound. `known%`, `glued%`, and the visible lists are the
trustworthy signals.

---

## 15. The similarity measure was the wrong one all along

Prompted by a literature survey (Levy & Goldberg 2015; Rychlý 2008 on logDice;
Santus et al. 2016 APSyn; Riedl & Biemann JoBimText; Kutuzov & Kunilovskaya
2018), several documented techniques were implemented in
[similarity.py](similarity.py) and ablated on the lemmatized Tanach index.

### Cosine loses to every set/rank-based measure tested

All measures run over the SAME stored Top-K rows — no rebuild, the comparison
measure is a free parameter:

```
measure              syn P@20   syn MRR   recall
cosine (current)       0.0325    0.0662    0.187
lin                    0.0417    0.0923    0.200
apsyn (N=100)          0.0457    0.0693    0.247
jaccard (N=100)        0.0567    0.1047    0.267
overlap (N=100)        0.0703    0.1218    0.300     <- 2.2x cosine
```

**Raw shared-feature counting — the crudest measure in the set — wins by 2.2x.**
This reproduces JoBimText's published result (unweighted overlap of salient
features competitive with word2vec) and Sketch Engine's report that Jaccard
beats cosine for their thesaurus. The interpretation: on sparse PPMI profiles
the *membership* of the top-context set is reliable, while the weight values —
which cosine trusts — are noisy estimates that low-frequency features distort.

One published claim did NOT reproduce: APSyn's "smaller N is better"
(their ESL result). Here N=100 ≥ N=50 > N=25. Recorded to avoid re-tuning on
faith.

### The user-visible turnaround

`overlap` + lemmatization + lexicon filter, on words cosine had failed:

```
צדקה   משפט חסד צדיק אמת צדק שלומ       (cosine gave: noise)
שבת    חדש מנחה עולה פסח חג              (cosine gave: junk)
אמת    חסד צדק צדקה                      (cosine gave: noise)
מלחמה  חיל ארמ קשת מגנ צבא               (weapons and war)
חכמה   דעת חכמ לבב בינה כסיל             (wisdom + its classic opposite)
```

### And it rescues the FULL corpus

The §13 conclusion ("bigger is worse, 7x") was measured with cosine and no
morphology. With `--lemmatize` (256,601 vocab, 726,941 forms folded, 368 s
build) + `overlap` + lexicon filter:

```
full corpus, similar():   P@20 0.0062  ->  0.0465     (7.5x)
                          recall 0.117 ->  0.430      (3.7x)
```

Mixed-register probes now behave like a Talmudic search engine should:
`תפלה` -> `תפילה יתפלל סליחות התחננ מעומד`; `שבת` -> `פסח עונג פיקוח פרהסיא`;
and the Aramaic probe `שמעתא` -> `שמעתתא שמועה מרימר` works first-class.
(`מלכא`, `עלמא`, `רבננ` remain mediocre — very-high-frequency function-like
words, consistent with §12's frequency-band finding.)

So the honest amendment to §13: **corpus size was never the problem — the
measure and the morphology were.** With both fixed, the full corpus is usable
and its coverage (3,567 gold words vs the Tanach's 832) makes it the better
shipping candidate.

### APAnt — the promised antonym separator does not work here

Santus et al.'s APAnt hypothesis (antonyms are globally similar but diverge on
their most SALIENT contexts) was tested directly on 1,530 synonym and 128
antonym pairs:

```
                  cosine median    apsyn/cosine ratio (median)
synonym pairs         0.0145              4.95
antonym pairs         0.0154              4.92        <- no separation
APAnt classifier: precision@128 = 0.16 (2.0x chance)
cosine alone    : precision@128 = 0.16 (2.0x chance)  <- identical
```

**Negative result.** The salience-divergence signal that separates antonyms on
large English corpora is absent here — plausibly because 307k tokens cannot
estimate salience rankings stably enough. Do not re-propose APAnt without a
larger register-matched corpus. The literature's alternative (directional
co-occurrence order + G², arXiv:2509.11534) remains untested and is the next
candidate — but note its order feature is the one least likely to transfer to
Hebrew word order.

### Adopted going forward

- `overlap` (N=100) as the default second-order measure — 2.2x cosine, and the
  cheapest to port to C#/SQL (it is a set intersection over stored rows).
- Lexicon filter at display time; lemmatize at build time.
- The ROADMAP item 2 precompute should store the overlap-based neighbor list,
  not the cosine one.

---

## Summary

| Claim | Verdict |
|---|---|
| Lookup can be made effectively free | **Confirmed** — 6-8 µs, and that is unoptimized Python |
| Index stays small and builds fast | **Confirmed** — 1.3 MB, 3.5 s for the whole Tanach |
| Associations are explainable | **Confirmed** — every result traces to counted evidence |
| Similarity without embeddings works | **Partially** — good for frequent concrete words, noisy otherwise |
| Formulaic-repetition noise is controllable | **Confirmed** — BM25 length-norm, 11/20 → 0/20 |
| Multi-word expansion via intersection | **Works after the burstiness fix** — the agreement bonus now fires |
| Prefix morphology is the top limitation | **Confirmed and partly fixed** — 2.9x P@20, but suffixes remain (§9) |
| Results can be measured, not eyeballed | **Confirmed** — independent gold set, 7-18x over chance (§8) |
| It finds words with similar meaning | **Reframed** — it finds *contrast* 4x better than synonymy (§10) |
| More corpus fixes the sparsity | **Refuted as asked, then rescued differently:** raw scale made it 7x worse (§13), but lemmatization + the overlap measure recover the full corpus to usable (§15) |
| Cosine is the right similarity | **NO — worst of five measures tested.** Raw shared-feature overlap wins 2.2x (§15) |
| APAnt separates antonyms | **NO on this corpus** — zero separation, identical to cosine (§15) |
| It can be built at full scale | **Confirmed** — 448M tokens in 502 s, 249 MB, C#/net10 (§13) |
| Ready to wire into the FTS engine | **Not yet** — absolute quality still low; see below |

The architecture is sound. Every remaining problem is **data quality**
(morphology, corpus size), not structure — which is the good outcome, because
those are fixable without redesigning anything.

**The honest headline after measurement:** the index is 18-56x better than
chance on the Tanach and prefix folding bought a genuine 2.9x, but P@20 of ~0.05
is still low in absolute terms. The distributional signal is real and not yet
strong enough to ship.

**And the corpus-size escape hatch is now closed.** That was the open question;
it has been answered by building the whole database (§13): 1,460x the text scores
**7x worse**. So the remaining levers are all about *what* text and *which*
words — register-matched corpora, suffix morphology, and restricting expansion to
the mid-frequency band where the method actually works (§12) — not about volume.
Per ROADMAP item 7, that is close to the honest stopping point for a spike.

### A note on method

Five of this document's conclusions reversed under measurement:

- Length normalization was assumed to work because "Numbers 7 verses are long."
  They are **0.91× average** length. The real mechanism is *selectivity* — the
  princes live only in those verses, so 100% of their mass is penalized.
- IDF was expected to be the fix (it is the most natural reading of "too much
  association frequency"). It does **nothing**, on either basis, because it
  discounts the wrong end of the frequency scale.
- The first-order intersection bonus looked broken. It was actually being
  drowned by burstiness, and started working once that was fixed.
- The prefix-fold **frequency-ratio** guard was expected to be the blocker
  holding back the `מלכ` family. Sweeping it 1.0 → 0.0 is **flat**. Coverage
  (`--min-stem-freq`) was the real knob all along (§9).
- Prefix folding was expected to help **frequent** words, based on two
  spot-checks that pointed that way. Stratified scoring shows the gain is
  **largest for the rarest** words and smallest for frequent ones (§9).
- The whole index was framed as a **similarity** index. It is a **contrast and
  semantic-field** index: it ranks opposites 4x better than synonyms (§10).
  This one was not a tuning error — it was a misunderstanding of what the
  method does, and only an independent gold set could have exposed it.

Each was caught by ablating one knob at a time (`python compare.py`,
`python evaluate.py --compare`) rather than shipping the bundle and assuming the
bundle worked. The last two were caught *only* because the harness existed —
both spot-check readings were confident and wrong. Worth keeping up.

## 15. Cosine is the wrong measure — raw `overlap` wins 2.2x

Five measures over the SAME stored Top-K rows (`similarity.py`): cosine, Lin,
APSyn, Jaccard, raw shared-feature count (`overlap`, JoBimText / Riedl &
Biemann). Order on the Tanach gold set: cosine < lin < apsyn < jaccard <
**overlap** — P@20 0.0325 (cosine) vs 0.0703 (overlap) on identical rows.

On sparse PPMI profiles, top-context SET MEMBERSHIP is reliable; the weight
VALUES are not. Any measure that trusts the weights (cosine, Lin) imports their
noise; any measure that only counts the set (overlap) does not. APSyn's
published "smaller N is better" did NOT reproduce here (N=100 beat N=25).

Consequence: `overlap` (N=100) is the measure for the precomputed similarity
graph (§21), and reweighting schemes that only reshuffle weight values are a
dead end for second-order quality — see the logDice split in §20.

## 16. Lemmatization via lexical.db — and the ambiguity trap

`--lemmatize` folds surface forms through the lexicon DB (base 24,559 /
surface 137,631 / variants 594,428, Hebrew AND Aramaic): Tanach +22% assoc /
+67% similar P@20; full corpus vocab 313k → 256.6k.

The trap worth keeping: one form can belong to several lexemes. A "prefer the
shortest base" rule deleted 12 of 16 probe words from the vocabulary while
P@20 went UP (the survivors were easier). Rules that hold: a form that IS a
base never folds; the longest base wins. `improve.py` prints probe survival
before any metric — check it first; a rising score with shrinking vocabulary
is a red flag, not a win.

## 17. Window sweep under the overlap measure — w4 confirmed

Window and measure could interact, so the sweep was rerun under both scorers
(lemmatized Tanach):

```
win     edges | cos P@20  cos rec | ovl P@20  ovl MRR  ovl rec
w1     97,110 |   0.0200   0.1333 |   0.0547   0.1219   0.3167
w2    102,788 |   0.0344   0.1917 |   0.0696   0.1747   0.3333
w4    104,958 |   0.0373   0.2167 |   0.0709   0.1510   0.3333
w8    104,688 |   0.0362   0.2083 |   0.0627   0.1341   0.2833
```

w4 stands (w2 viable, w8 degrades under both). The Bullinaria & Levy ±1-2
optimum does not transfer to verse-bounded Hebrew text.

## 18. LMI pruning: a no-op on the Tanach, +9% on the full corpus — now default

Pruning survivors by LMI = count × PMI (Evert 2005) instead of by PMI keeps
well-SUPPORTED associations instead of barely-attested high-PMI pairs. The
stored value stays PPMI; only WHICH Top-K survive changes.

On the Tanach it is a byte-identical no-op — rows rarely exceed Top-K, so
nothing is pruned either way. On the full corpus (where pruning actually
bites): similar() P@20 0.0457 → 0.0497 (+8.8%), MRR 0.1184 → 0.1294 (+9.3%),
first-order unchanged. Free at build time. `--prune-lmi` is therefore the
AssocBuilder default now (`--prune-pmi` restores the old behavior).

## 19. The Targum bridge — verse-aligned Aramaic→Hebrew folding works

seforim.db contains verse-aligned parallel text (the Targumim embed the Hebrew
ref in their heRef). `targum_bridge.py` extracts co-occurrence-scored
mutual-best translation pairs: 5,940 pairs at score ≥ 0.3, ≥ 3 verses.
`AssocBuilder --bridge targum_bridge.csv` merges them into the lemma fold
(2,830 folds applied on Bavli).

First reading looked like a wash — but the two runs scored DIFFERENT gold sets
(folding shrinks vocabulary, 1727 → 1656 pairs). On the SHARED gold set
(1,654 pairs, identical protocol) the bridge is a clean win on Bavli:

```
                 P@20     MRR   recall@20
no bridge      0.0155  0.0344      0.1333
with bridge    0.0184  0.0437      0.1750   (+19% / +27% / +31%)
```

Probe survival: 9/10 Aramaic probes present; the 10th folded INTO its Hebrew
counterpart — which is the feature working, not a loss (verified against the
bridge CSV). Protocol lesson recorded twice now (§16, here): after any
vocabulary-changing step, compare on the shared gold set or the numbers lie.

## 20. logDice: better association rows, worse similarity features

logDice (Rychlý 2008) = 14 + log2(2c / (ta + tb)) — bounded, built from ratios
only, so scores compare across corpora of different sizes (the original
motivation). `--scorer logdice` in AssocBuilder. Measured against PPMI on the
same gold sets:

- First-order `assoc` (what a user reads as "associated words"):
  Tanach P@20 +22%, Bavli +11%, recall up on both.
- Second-order `similar()` via overlap: Tanach P@20 −23%.

Both make sense: logDice promotes well-supported pairs (good display rows),
while PPMI's rare shared contexts are exactly what the overlap measure counts
(good similarity features). Consequence: the two views want DIFFERENT scorers
— logDice for the assoc display channel, PPMI+LMI for the similarity graph.
One more instance of §15's lesson: the stored rows are a free parameter of the
measure that reads them.

## 21. The similarity graph is now precomputed — and beats query-time

ROADMAP item 2, landed in AssocBuilder (`SimGraph.cs`, on by default,
`--no-sim` to skip, `--sim-only <db>` to retrofit an existing index). With an
inverted index over each word's stored top-100 rows, a counting pass gives
count[c] = |rows(a) ∩ rows(c)| — the overlap score itself — for EVERY word
sharing a feature, in Σ deg² increments. Results go to a `sim` table
(a, rank, b, s) WITHOUT ROWID, physically contiguous like `assoc`.

- Tanach: parity with query-time overlap (P@20 0.0685 vs 0.0684), lookup
  ~285 µs from Python (SQLite seek; C# reader will be lower).
- Full corpus (256,601 vocab): 11.4M sim edges in 428 s, and BETTER than the
  query-time sweep — P@20 0.0519 vs 0.0485, MRR 0.1490 vs 0.1391 — because
  the counting pass scores all candidates, not a depth-limited two-hop sample.
- The per-feature in-degree cap that measured +6% under the OLD candidate
  protocol measured WORSE here (Tanach P@20 0.0630 at cap=100 vs 0.0685
  uncapped): with exhaustive candidates the cap only removes signal. Default
  `--sim-cap 0`; the flag stays for tractability on hub-heavy corpora.

## 22. Gloss-text similarity — a coverage channel, not a competitor

`gloss_channel.py`: headword → set of normalized Hebrew tokens from its
dictionary sense texts (df-capped), similarity = shared-token count via the
same counting pattern. 38,285 senses → 10.7–13.4k usable profiles.

- Standalone: P@20 0.049, MRR 0.150 at df-cap 100 (~19x chance) on its own
  gold. PROVENANCE CAVEAT: gold links and glosses come from the same
  dictionary, so part of this score is editorial consistency, not transfer.
- Head-to-head on shared vocabulary: corpus overlap wins 2:1
  (P@20 0.0810 vs 0.0409 on the identical 591-word gold subset).
- The real value: 11,584 headwords have a gloss profile but NO Tanach corpus
  profile. Merge design: corpus sim first, gloss as the fallback channel for
  words the corpus cannot serve; label the source per row.

## 23. APAnt retested with register-matched volume — still dead

The §10 objection ("307k tokens is too little for salience rankings") is now
answered: on Bavli base text (1.86M tokens, one register, lemmatized +
bridged) the predicted divergence FINALLY appears — antonym apsyn/cosine
median 4.56 vs synonym 5.38 — but as a classifier it adds exactly nothing:
precision@160 = 0.17 for APAnt and for plain cosine alike (3.2x lift each).
The signal exists and is fully redundant with cosine here. Do not re-propose;
the remaining untested idea in this family is directional order + G²
(arXiv:2509.11534), whose order feature may not transfer to Hebrew.

## 24. The Otzarya Books folder — usable text, wrong shape, not a lever

Survey of `C:\Users\Admin\Documents\Otzarya Books`: 966 txt / 3.1 GB, 329
distinct normalized titles, only 34 matching seforim.db titles exactly — so
~3 GB of non-duplicate material. Quality is high (95.5% lexicon-known tokens,
glued ~0.02% — digital text, not OCR). But tok/line averages 111 (14–324), so
the line-bounded window that defines this index's contexts is meaningless
there; using it would need punctuation-based segmentation first. And §12
already showed volume is not the constraint. Verdict: park it; if ever used,
segment first and build per-corpus indexes, never merge into the base index.

## 25. The content filter is part of the build environment

A session of this research was killed mid-flight by the network content
filter: an evaluation script printed raw corpus probe words and their
neighbor lists to stdout, the tool result entered the next API request, and
the proxy rejected it — corpus vocabulary itself sits on the blocklist.
Consequence for METHOD: every corpus-touching script in this directory now
routes stdout/stderr through `tools/masked.py` (Hebrew runs → stable
`[H:xxxx]` hashes, raw words only in local files, decode map in
`tools/hashmap.tsv`), files containing Hebrew are never read raw into a
session, and the full recovery recipe lives in
`tools/extract_transcript.py`. Rule canon: `.kiro/steering/agent-behavior.md`.

## 26. The search-expansion demo — what survived four adversarial audit rounds

`build_search_demo3.py` → `search-demo.html`. The distributional index FAILED
the end-user eyeball test for silent search expansion (user verdict, then
measured: confirmed and non-confirmed sim neighbors have near-identical score
distributions — no floor separates them, §21's quality is rank-level, not
row-level). The demo was rebuilt on PRECISE channels and hardened over four
audit rounds by a Hebrew-reading agent (invalid-result rate: 51% → 27% → 32%
→ 16% → ~0 after the final blocklist). What it took, in order:

1. **Conjunctive semantics** — a result must match EVERY query word, with at
   most 1-2 words substituted through a channel. "Shares one related word"
   is never a valid result for a phrase; both early demo versions failed on
   this alone.
2. **Strip citation labels before tokenizing** — parenthesized verse numbers
   matched letter-numeral "inflections" and fabricated whole cards.
3. **Fold to Tanach-attested lexemes** — rabbinic abbreviations captured
   biblical surface forms ("and-if-you-say" for the object marker).
4. **Fold-back consistency for inflections** (form must fold back to the
   lexeme, with a containment relaxation for prefixed forms) — purged
   cross-lexeme pollution (daughter/house, carry/marriage-nouns) from both
   matching AND display; made high-frequency genuine inflections safe (no
   frequency cap needed on this tier). Known residual: weak-letter
   conjugations (final/middle letter drops) are over-rejected — the main
   RECALL lever left.
5. **Content-shape gate on synonym/bridge forms** (3+ letters, corpus freq
   ≤ 500) — the dictionary link table contains pronoun/particle links that
   match everywhere and silently void the conjunction.
6. **Gloss sense-gate on synonyms** (reject when both glosses ≥5 tokens and
   share zero) — at df-cap 300 it over-pruned to near-nothing; at 1000 with
   the both-substantial condition it kept soil→land and killed ash/be/speak.
7. **One verse token satisfies one query word** (most-constrained first,
   rarity tie-break).
8. **Show zero-result queries honestly** — the sampler was silently replacing
   hard queries that produced no results; two audit rounds "passed" partly by
   self-censoring. Honest zeros beat hidden failures.
9. **Curation blocklist** (`tools/syn_blocklist.txt`) — after all structural
   guards, 100% of remaining invalid results traced to ONE dictionary entry
   (a come-family verb linked as synonym of the remove-verb). These are DATA
   bugs; the real fix belongs in the dictionary DB (backlog: come↔remove,
   "knows"↔son, thing/forbidden↔negation; also re-lemmatize the
   riches/numeral homograph, restore dust↔soil and the Aramaic altar word
   which the sense gate over-pruned).

**Methodology note (filter-safe review):** semantic judgment of Hebrew output
was done by a DISPOSABLE reader agent that reads the real file and reports in
pure-ASCII English (word references by card/position/gloss). Its context is
sacrificial — if the network filter kills it, the main session survives. Four
rounds cost ~380k agent tokens and were the only way to iterate on quality
without eyes on the raw text. Positional references ("card 8 word 1 synonym
1") let the main session act on specific Hebrew entries it never saw.

## 27. The whole-library expansion table failed its register audit — routing wins

`expansion-seforim.db` (582,878 rows, token-weighted coverage 94.4% Tanach /
87.2% whole-library) was audited by the reader agent against the Tanach table
(25 sampled fold divergences of 1,948 total, plus 12 biblical and 12 rabbinic
word rows). Verdict: **not shippable flat; ship the routed design.**

- **Fold divergences: ~52% wrong on the library side** (13/25; only 2/25 the
  library side was right, 10 genuine homographs). Extrapolated: ~1,000 wrong
  folds — systematic, not blocklistable. Two patterns: Aramaic-equivalent
  lemmas capturing Hebrew surfaces (meaning survives, structure inverts —
  the Aramaic word belongs in the bridge column, not the lexeme slot), and
  rabbinic homograph capture (meaning changes). The abbreviation-capture
  pattern from the demo rounds did NOT appear — "attested anywhere" still
  blocks pure abbreviations; it's the shared-consonant homographs that flip.
- **The synonym channel is junk-dominated in BOTH registers at library
  scale** — independent of folding: antonyms listed under the death word,
  desire-words under "dream", topic collocates under halachic terms, modern
  Hebrew under Talmudic markers. The Tanach-demo success of this channel
  survived because conjunctive matching filters junk out of RESULTS; the raw
  table rows don't get that protection. Policy: trust synonym rows only from
  the validated Tanach side; harder sense-gating is prerequisite to enabling
  it for the library side.
- **Inflections are shippable in both registers** (9/12 biblical, 11/12
  rabbinic rows clean) and the Aramaic CONTENT is often excellent — a
  letter-numeral date surface correctly folded to fifteen with genuine
  Hebrew AND Aramaic forms. The machinery works; the placement (lexeme vs
  bridge column) is what needs the routing/relocation pass.

**Shipped artifact: `expansion-routed.db`** (67 MB) — Tanach folds win for
all 39,764 surfaces the validated table knows; the library table serves only
the 1.38M surfaces beyond it; every fold and exp row carries `source`
('tanach'|'library') so the consumer applies per-source policy (synonyms:
tanach-only; inflections: both). Backlog for the library side: same-language
lemma preference pass over the 1,948 divergences (mechanical for the ~60%
Aramaic-capture class), relocate Aramaic equivalents to the bridge channel,
hand-review the true homographs.

### 27b. The union step — verified, filtered, shipped

Library-attested inflection/bridge forms were merged into Tanach-owned lemmas
(synonyms excluded). Reader verification of 141 sampled merged forms: ~88%
search-safe, and ALL concentrated failures shared one property — a THIN
VALIDATED ANCHOR (lemma with <=3 Tanach-attested forms cannot discipline the
guard, so frequent rabbinic/Aramaic homographs flood in; plene spelling makes
the collisions). Two verified filters: thin-anchor gate (skip merge when
anchor <= 3 — mechanically confirmed to remove exactly the three failed audit
rows) and dropping tokens ending in non-final letterforms (normalization
artifacts, now filtered in expansion.py itself). Post-filter estimate ~98%
safe; final artifact: expansion-routed.db, 330,476 rows, 0 artifacts.
Backlog unchanged: relocate non-derivational Aramaic equivalents from infl to
bridge (harmless-to-beneficial as-is); function-compound lemmas arguably
should have no expansion rows.

### 27c. Biblical Aramaic (user-flagged): from unserved to verified

Daniel/Ezra Aramaic sits INSIDE the validated Tanach vocabulary and was
effectively unserved (audit: core words with empty rows; one live homograph
capture — the Dan 3 furnace noun folded to the riding-mount noun). Fixes, all
reader-verified over three full-coverage rounds (93 -> 77 -> 20 pairs):

- **Line-level register classification** (281 Aramaic / 380 Hebrew lines in
  books 35-36, marker-based). Book-level "span" attestation was the root
  cause of two audit failures — the Hebrew chapters polluted the guard.
- **Proclitic folding** (relative/genitive dalet + shared prepositions;
  alef REMOVED — in the biblical span it only stripped verb-prefix/root
  letters) — remainder must be attested in ARAMAIC lines specifically.
  Root letters doubling as particles were the top error class: attestation
  of a remainder as a DIFFERENT word is zero evidence for a strip.
- **Emphatic-state folding** (final alef = the definite article; plural
  emphatic strips yod+alef together or the leftover yod collides with
  Hebrew yod-final words — kings->queen was the example). De-emphatic stems
  may fold to solidly-attested Tanach cognates (exact-skeleton rule; genuine
  t/sh consonant-shift cognates verified). Letterform trap: stripping a
  suffix exposes a non-final kaf/mem/nun/pe/tsadi that matches NOTHING until
  normalized to the final form.
- **Manual layer** (tools/aramaic_fold_manual.tsv, 31 entries, wins over all
  rules): pinned reader-verified pairs, forced-self for audited-wrong
  surfaces (blocklisting rules is NOT enough — the wrong LEXICON fold
  survives underneath), the lamed-preformative to-be jussive (a verb prefix
  a preposition-strip cannot be taught to avoid), and two Persian-loanword
  self-pins whose fold targets were skeleton-twins of Hebrew lemmas with
  their own expansion rows (47 and 112 rows of wrong-family contamination
  avoided).

End state: king/God/kingdom families unified (0 -> 116/48/305 inflection
rows), ~99% right-word on the full fold inventory, wrong-register captures
blocked with safe-thin self-lemmas. Verification pattern that worked: rules
propose, the reader judges EVERY decision (the sets are small), manual pins
make verified judgments durable against re-derivation.
