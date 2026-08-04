# Roadmap

Ordered by leverage. Items 1–3 are quality fixes on the data; items 4–6 make it
a real component; item 7 is the honest exit criterion.

Status legend: **TODO** · **IN PROGRESS** · **DONE**

---

## 1. Hebrew prefix morphology — **DONE (prefixes)** · suffixes still open

Implemented as `--strip-prefixes --min-stem-freq 5`. Strips `ו ה ב ל כ מ ש` and
common two-letter stacks, but only when the remainder is itself a frequent
corpus word — the cheap guard against `משה` → `שה`.

**Measured: first-order P@20 improved 2.9x, recall 2.5x**; second-order gained
too (P@20 +17%, MRR +65%). Full numbers, the frequency sweep, and the
stratification in [FINDINGS.md §9](FINDINGS.md).

Two of the three success criteria above did **not** clear, and the reason is
worth keeping:

| criterion | target | actual |
|---|---|---|
| `מלכ` family collapses | "a handful" | 79 → 37 |
| median associations | "well above 9" | 5 → 6 (p90 22 → 35) |
| `similar(מלך)` returns rulers | rulers | partly |

**The remaining split is carried by suffixes, which prefix stripping cannot
reach by design** — `מלכי`, `מלכימ`, `מלכות`, `מלכו` are inflections, not
particles.

**Still open under this heading:**

- **Suffix/inflection handling.** This is where the rest of the `מלכ` family
  lives. It is genuinely harder than prefixes: Hebrew suffixes are possessive
  and plural markers that interact with the stem, so the "is the remainder a
  corpus word" guard is much weaker. This is the point at which a real
  morphological analyzer starts to earn its cost.
- **The known false positive:** `מלכה`(21) → `לכה`(28) passes every guard.
  A curated exception list is the cheap patch; a root lexicon is the real fix.
- `--stem-ratio` is implemented but **flat across its whole range** — it changes
  109 forms out of 4,679 and moves no metric. Left at 0.25. Do not spend time
  tuning it.

---

## 2. Precompute the similarity graph — **DONE (2026-08-03)** *(design settled by §15)*

**Update 2026-08-03:** the measure to precompute is **`overlap` (N=100), not
cosine** — it scores 2.2x better ([FINDINGS.md §15](FINDINGS.md)) and is a plain
set intersection over the stored rows, so the C# port is trivial. Combine with
Rychlý & Kilgarriff's contexts-first loop (skip contexts with huge membership)
for the offline pass.

**Landed 2026-08-03** as `SimGraph.cs` in AssocBuilder (default ON,
`--no-sim`, `--sim-only <db>` to retrofit): counting pass over an inverted
index of the stored rows writes a `sim` table (a, rank, b, s). Tanach parity
with query-time overlap; full corpus BEATS it (P@20 0.0519 vs 0.0485) at
~375 us lookups. Per-feature cap default 0 — it measured WORSE with
exhaustive candidates (FINDINGS 21). The success test below is met.

### Original item

**Problem.** `similar()` is the one operation that is *not* a lookup: 20–105 ms,
because it does a two-hop candidate sweep and cosine scoring at query time.
That violates the whole premise.

**Approach.** It is a fixed corpus, so compute it offline like everything else.
Emit a **second CSR pair** (`sim_offsets.bin` / `sim_edges.bin`) in exactly the
same format, holding the top-K most similar words per word. Query time becomes
the same two array reads.

All-pairs over 12,638 words is trivially feasible offline; even a naive
implementation is minutes. The two-hop candidate restriction already implemented
in `similar()` makes it faster still.

**Success test.** `similar()` drops to the same ~6 µs as `neighbors()`, and the
`demo` timings show no query-time computation anywhere.

---

## 3. BM25-style normalization — **DONE (v1)** *(user-requested)*

All four knobs are implemented in `build_index.py` and ablated individually via
`compare.py`. **`--length-norm-b 0.75` is now the default** and solved the
burstiness artifact outright: princes in the top-20 of `קרבן` went 11/20 → 0/20,
with genuine associations unchanged or slightly improved.

The surprise, recorded in [FINDINGS.md §6](FINDINGS.md): the IDF-style discount
— the mechanism expected to do the work — had **no effect**, on either basis.
Both were implemented and tested:

- `--idf-basis df` — classic BM25 IDF over verse frequency.
- `--idf-basis degree` — the "discount words that associate with very many other
  terms" reading, i.e. graph degree rather than verse count.

Both returned 11/20 with byte-identical edge counts. The reason is structural:
IDF discounts *high*-frequency/*high*-degree words, and the princes are the
**rarest, lowest-degree** words in the neighbourhood (degree 5-6, versus 200 for
`ליהוה`). IDF therefore gives them the maximum multiplier and shaves their
legitimate competitors instead. **IDF measures breadth; burstiness is
concentration.** Different axis, so no amount of tuning would have helped.

**Still open under this heading:**

- A genuine **concentration** measure, which is what the burstiness intuition
  actually calls for. Candidates: the ratio of a word's occurrences falling in
  its single densest chapter; or entropy of its distribution over books/chapters,
  penalizing low-entropy (concentrated) terms. Length-norm happens to catch the
  Numbers 7 case, but it is a proxy — a *short* formulaic repetition would slip
  through it.
- `--saturate-k` and `--min-ctx-df` remain unproven. Keep them off until item 4
  can measure them.
- `bm25-strict` over-prunes (`שבת` and `מלחמה` both degrade). There is a real
  ceiling on how much normalization this corpus tolerates; find it with numbers,
  not by eye.

---

## 4. Evaluation harness — **DONE** *(`evaluate.py`)*

Gold set comes from the project's dictionary DB `link` table — hand-built from
lexicographic sources, so it knows nothing about co-occurrence and a good score
cannot be an artifact of the method. ~4,000 synonym and ~210 antonym pairs have
both sides in the Tanach vocabulary.

```bash
python evaluate.py                              # score the current index
python evaluate.py --compare index index-morph  # A/B two builds
python evaluate.py --relation antonym
python evaluate.py --min-freq 25 -k 10
```

P@k / MRR / recall@k, always with coverage and a **random baseline** (chance is
P@20 ≈ 0.0025 here, so absolute numbers are unreadable without it).

`כתיב` (spelling) and `נגזרת` (derivation) links are deliberately excluded —
the first scores the tokenizer, the second is circular once morphology lands.

**It earned its cost immediately.** Two confident spot-check readings turned out
backwards under stratified scoring ([FINDINGS.md §9](FINDINGS.md)). Use
`--compare` for every future change; do not trust word lists.

**The antonym run has been done, and it changed the conclusion of the spike.**
Opposites score **4x better than synonyms** even after controlling for gold-set
size and word frequency ([FINDINGS.md §10](FINDINGS.md)). This is a
contrast/semantic-field index, not a synonym index — which is a statement about
what it should be *used for*, and it belongs in item 6's design.

---

## 5. Scale past the Tanach — **DONE, and it answered NO**

Implemented in [build_large.py](build_large.py): external merge sort (bounded
memory), corpus selection, `--base-only`, parallel pass 2. Both the scaling
mechanics and the correctness checks landed:

- **Parity with `build_index.py`** on the Tanach — P@20 0.0487 vs 0.0486.
- **Parallel == serial exactly** — identical edge sets, max weight delta 1e-6.
- The predicted accumulator blowup was real: 65 B/pair measured, so the whole
  DB would need 10-42 GB against 7.2 GB free.

**The measured answer to "does more data fix the sparsity?" is no**
([FINDINGS.md §12](FINDINGS.md)):

```
corpus                   tokens    P@20   vs chance
tanach w8               306,873  0.0510        52x
mishnah w12             192,466  0.0477        12x
yerushalmi base w4      812,293  0.0166         9x
bavli base w12        1,857,496  0.0141        13x
```

Bavli has 6x the tokens and scores 3.6x worse. Register — not volume — is what
governs quality, and the Tanach is where this method works best.

**Two configuration bugs found here, both of which had been silently distorting
every earlier cross-corpus number:**

- The DB is only **4% base text**; `משנה` is 96% commentary. Without
  `--base-only`, a "Mishnah" index measures medieval commentary prose. Fixing
  it more than doubled Mishnah's P@20.
- **`window=4` is a Tanach-specific default.** Optimal window tracks text-unit
  length (Tanach 13 tokens/line → 4-8; rabbinic 36-86 → 12). With the window
  matched, Mishnah reaches Tanach-level quality.

**Still open:** register mixing was only half-tested. On a *shared-register*
gold set the Tanach/Bavli gap narrows from 3.9x to 1.7x, so register explains
roughly half the deficit and something else explains the rest. Per-corpus
indexes with a query-time merge remain the sensible design, and are now cheap
to build.

---

## 6. Integration with the FTS engine — **partly unblocked**

The C# side already exists: [AssocBuilder](AssocBuilder/) builds the index in
net10 and emits a **static SQLite table** (`word` + `assoc`, `WITHOUT ROWID`
clustered on `(a, rank)`), verified against the Python reference and ~10x faster
([FINDINGS.md §13](FINDINGS.md)). So the port question is settled — what remains
is the wiring and the quality bar.

- ~~Port the reader to C#~~ — **done**; a lookup is one indexed `select ... where
  a = ? order by rank limit N`, physically contiguous.
- Wire as an **additional candidate source**, never a replacement: exact matches
  must always rank above expanded ones, and expansion must be visibly labelled
  in the UI.
- **Label it honestly.** [FINDINGS.md §10](FINDINGS.md) shows this ranks
  *opposites* far better than synonyms, so expansion will surface the contrary
  of what the user typed (`ברכה` → `קללה`). That is often useful in this corpus,
  but it must be a stated feature — "related passages", not "similar words" —
  or it will read as a bug.
- Make it toggleable per-query.
- **Expansion artifact LANDED (2026-08-04):** `expansion-tanach.db` via
  `build_expansion_table.py` / `expansion.py` (the audit-hardened guard
  stack, FINDINGS 26): `fold(surface->lemma)` + `exp(lemma, rank, form,
  channel)`. 118,900 rows over 6,030 lemmas (infl 32.6k / syn 86k / bridge
  311). FTS wiring: fold the typed term, add exp rows as labelled OR-terms,
  sweep expansion breadth for the latency knee; exact matches always outrank
  expanded ones. The DISTRIBUTIONAL index is excluded by measurement — no
  score floor separates good from junk neighbors (FINDINGS 26).
- **Scorer split (FINDINGS 20):** build the display rows with `--scorer
  logdice` (+22%/+11% first-order) and the similarity graph from PPMI+LMI
  rows — two channels, two builds or one dual-weight build.
- **Fallback channel (FINDINGS 22):** gloss-text similarity covers 11.6k
  headwords the corpus cannot; serve it only where the corpus row is empty,
  labelled as dictionary-derived.
- **The real budget question is expansion count, not lookup.** Each expanded
  term costs a posting-list read. Measure end-to-end FTS latency against
  expansion breadth and find the knee.

Existing constraint to respect: *FTS results must never be capped.* Expansion
breadth is a separate knob from result count — capping how many *terms* we
expand to is not capping results.

---

## 7. Honest exit criterion

This PoC has already answered the structural question: **the index is small,
the build is fast, lookup is free, and results are explainable.**

The open question is whether **corpus-scale sparsity** can be beaten. If, after
items 1, 3, and 5, `similar()` is still noisy for anything but frequent concrete
nouns, then the conclusion is that a corpus of this size and register does not
support distributional semantics — and the right move is to say so and stop,
rather than to keep tuning scoring functions.

That is a legitimate outcome for a research spike, and cheap to reach from here.
