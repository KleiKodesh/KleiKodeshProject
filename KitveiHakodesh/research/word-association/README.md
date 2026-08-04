# Word-Association Index — Proof of Concept

> **Status 2026-08-04 — where the shipping code lives.**
> This folder is the research spike and its record. The part that SHIPPED is the
> query-expansion artifact, and it has moved to
> `CSharpBackend/FtsLib-Csharp/SearchExpansion/` — artifact, generator, and
> regeneration instructions all live there now. Read that README to rebuild or
> tune the shipped expansion; read FINDINGS.md here for why it is built the way
> it is (sections 26-27c) and what was measured and rejected.
>
> The distributional association index described below did NOT ship: it failed
> the end-user test as a silent search-expansion source (no score threshold
> separates good neighbours from junk — FINDINGS 26). It remains useful as a
> browsing/"related words" surface and as the evaluation apparatus that produced
> the shipped channels.
>
> Intermediate `.db` files here are gitignored — regenerate with the scripts.

An explicit word-association index over the **entire Tanach**, built to test one
question:

> Can we get semantic search behaviour from raw co-occurrence statistics —
> kept as an inspectable graph — instead of compressing them into neural
> embeddings?

Short answer from this PoC: **yes, but it is a contrast-and-semantic-field
index rather than a synonym index** — measured against an independent gold set,
it ranks *opposites* four times better than synonyms. Useful, explainable, and
not what "similar words" would lead you to expect. Details in
[FINDINGS.md](FINDINGS.md); next steps in [ROADMAP.md](ROADMAP.md).

---

## The idea

Instead of `cat -> [0.173, -0.821, 0.442, ...]` (a vector nobody can read), keep
the associations themselves:

```
מזבח
 ├── הקטרת    4.74
 ├── קרנת     4.38
 ├── העלה     3.89
 ├── הנחשת    3.76
 └── יסוד     3.70
```

Two words are "similar" when their association profiles overlap — no training,
no vectors. And you can always answer *why* the system linked two words, which
a 768-dimensional vector cannot.

Everything expensive (counting, scoring, pruning) happens **once, offline**. At
query time you do array lookups.

---

## Quick start

```bash
python build_index.py     # ~3 s, writes index/ (1.1 MB)
python query.py demo      # guided tour
python evaluate.py        # score it against the gold set
```

Individual commands:

```bash
python query.py assoc   מזבח            # strongest raw associations
python query.py similar זהב             # words with a similar profile
python query.py expand  "לחם יין"       # query expansion (default: similar)
python query.py expand  "לחם יין" --mode assoc
python query.py bench                   # lookup timing
```

Measuring a change — **always do this rather than reading word lists**
([FINDINGS.md §9](FINDINGS.md) records two confident spot-check readings that
turned out backwards):

```bash
python build_index.py --out index-experiment  <flags>
python evaluate.py --compare index index-experiment
python evaluate.py --relation antonym          # the strongest signal here
python report.py --index index --open          # HTML report
```

## Beyond the Tanach — the C# builder

`build_index.py`'s in-memory accumulator cannot hold a larger corpus
([FINDINGS.md §11](FINDINGS.md)). Use [AssocBuilder](AssocBuilder/):

```bash
cd AssocBuilder && dotnet build -c Release && cd ..

AssocBuilder/bin/Release/net10.0/AssocBuilder.exe --list
AssocBuilder/bin/Release/net10.0/AssocBuilder.exe --corpus all   --out assoc-full.db
AssocBuilder/bin/Release/net10.0/AssocBuilder.exe --corpus bavli --base-only

python evaluate.py --index assoc-full.db       # the readers accept either backend
python report.py   --index assoc-full.db --open
```

It writes a **static SQLite table** (`word` + `assoc`), clustered so one word's
associations are contiguous and pre-sorted by weight:

```sql
select w.term, x.w from assoc x
join word w on w.id = x.b
where x.a = (select id from word where term = 'מזבח')
order by x.rank limit 20;
```

### Measure what the user sees, not just P@k

```bash
python improve.py --index assoc-full.db --aramaic --show
```

Gold-set P@k is blind to what appears *beside* the right answer, and that is most
of perceived quality. Before the lexicon work, `similar()` on the full corpus
returned **24% recognizable words** — a user searching `שבת` saw
`דכשמקדשינ, מבדיליננ, במייאנצא` while P@20 registered nothing wrong.

`improve.py` reports both axes, and prints **probe survival** first: a
lemmatization bug once folded 12 of 16 query terms out of the vocabulary, which
made P@20 *rise* while `similar(שבת)` returned nothing at all.

Two notes on interpretation:

- **Aramaic is a register here, not noise.** This is a Talmudic corpus — passages
  are Hebrew, Aramaic, or mixed. `known%` counts both languages; the real defect
  is `glued%` (print/OCR fusing two words into one token).
- **A filtered P@k is an upper bound**, not a discovery: the gold answers are
  themselves lexicon words, so filtering to lexicon words cannot lose them.

Four flags matter more than they look:

- **`--lemmatize`** folds inflections onto their lexeme via `lexical.db`,
  reaching the suffix morphology the prefix heuristic cannot — and bridging
  Aramaic to Hebrew (`מלכא`, `דמלכא`, `דמלכותא` → `מלכ`). Worth +22% `assoc` and
  +67% `similar` P@20 on the Tanach ([FINDINGS.md §14](FINDINGS.md)).

- **`--base-only`** excludes commentaries. The DB is only **4% base text**, so
  without it `--corpus mishnah` measures medieval commentary, not Mishnah.
- **`--buffer-pairs`** is the aggregation window, **per shard**, not a memory
  cap. Undersizing it does not save anything — it moves the same counting work to
  disk. A 5.7M-per-shard buffer spilled **34 GB** on the full corpus where the
  60M default spills essentially nothing ([FINDINGS.md §13](FINDINGS.md)).
- **`--window`** defaults to 4 and should usually stay there. Longer windows help
  *base-text* rabbinic works (§11) but bought nothing on the full corpus while
  costing 2x the pairs (§13).

---

## What got built

| File | Role |
|---|---|
| [build_index.py](build_index.py) | Offline build: seforim.db → tokens → co-occurrence → PMI → pruned CSR index |
| [query.py](query.py) | Read-only query layer: `neighbors`, `similar`, `expand`, `bench` |
| [compare.py](compare.py) | Builds several scoring configs side by side and diffs their output — how the BM25 knobs were ablated |
| [evaluate.py](evaluate.py) | Scores the index against an independent gold set (the project dictionary's synonym/antonym links). P@k, MRR, recall, vs a random baseline |
| [build_large.py](build_large.py) | Scalable Python builder — external merge sort, corpus selection, parallel pass 2. Reference implementation; superseded by the C# one for real corpora |
| [AssocBuilder/](AssocBuilder/) | **Production builder (C#/net10).** LSM segments like FtsLib; writes a static SQLite table. ~10x faster than Python and the only path that handles the whole DB |
| [assoc_db.py](assoc_db.py) | Reader for the C# SQLite table. Same API as `query.AssocIndex`, so `evaluate.py` / `report.py` work against either backend |
| [lexicon.py](lexicon.py) | Lexical resources — `lexical.db` lemmas (Hebrew **and** Aramaic), Dictionary.db headwords, Aramaic shorashim CSVs. Plus `LexiconView`, a display-time filter/boost |
| [improve.py](improve.py) | Improvement harness. Scores every variant on **two** axes: gold-set P@k *and* what the user actually sees (`known%` / `glued%`). Checks probe survival first |
| [report.py](report.py) | HTML report in the FtsLib house style — build params, lookup timing, scored quality, contrast check, association panels |
| [explore.py](explore.py) | Throwaway scratch script (rewritten for each investigation; not part of the pipeline) |
| `index/` | Build output — the actual index (generated, ~1.3 MB) |
| [FINDINGS.md](FINDINGS.md) | Measured results, quality assessment, and the three real bugs found |
| [ROADMAP.md](ROADMAP.md) | What to do next, in priority order |

### Corpus

`C:\ProgramData\otzaria\books\seforim.db`, book ids **1–39** — the 39 base
Tanach books (`isBaseBook=1` under category `תנ״ך`). Commentaries and targumim
are deliberately excluded so the statistics reflect the biblical text itself.

| | |
|---|---|
| Books | 39 |
| Verses | 23,204 |
| Tokens | 306,873 |
| Vocabulary (freq ≥ 3) | 12,638 |
| Associations kept | 132,030 |
| Build time | **3.5 s** |
| Index size | **1.3 MB** |

---

## The on-disk format (CSR)

Three files. No pointers anywhere — every reference is an integer offset into
the same array, so the on-disk bytes are byte-identical to the in-memory layout.
That is what makes it `mmap`-able with no parse step.

```
vocab.json    word list; a word's id IS its position in the array
offsets.bin   uint32[V+1]
edges.bin     (uint32 word_id, float32 weight)[]  — sorted by weight DESC
```

Lookup for word `i`:

```python
start = offsets[i]              # one array index
end   = offsets[i + 1]          # one array index
edges[start:end]                # one contiguous read
```

Because edges are sorted by weight descending, a top-20 query reads the first
20 entries (160 bytes) and stops — it never touches the rest of the run.

---

## Measured lookup cost

```
offset lookup only     0.48 µs
neighbors(top-20)      6.14 µs      (20,000 random lookups)
```

That is in **Python**, with per-edge `struct.unpack`. A C#/native implementation
over an mmap'd span would be a small fraction of this. The point stands: the
association lookup is free, and it is not where a real system's latency goes.

---

## Two levels of query

The distinction matters more than expected, so both are implemented:

**First-order (`assoc`)** — words that appear *next to* the query word.

```
מזבח  ->  הקטרת, קרנת, העלה, הנחשת, יסוד, ומקטיר
```

**Second-order (`similar`)** — words *used like* the query word, computed by
cosine over the sparse association profiles. This is the `cat ≈ dog` step, done
without embeddings.

```
זהב   ->  כסף, סגור, ועשית, ויצפהו, טהור, שטים, נחשת
```

For search-style query expansion, `similar` is the better default: a user typing
מזבח wants other cultic vocabulary, not the verbs that happen to surround it.

`similar()` avoids an all-pairs comparison by only scoring candidates reachable
in two hops — words sharing no context word at all have cosine 0 and are skipped.
It runs in 20–105 ms in Python, and is the one operation that is *not* a pure
lookup. See [ROADMAP.md](ROADMAP.md) — it should be precomputed offline too.

---

## Design decisions worth knowing

**Verse-bounded windows.** Co-occurrence never crosses a verse boundary. Verses
are the natural sentence unit here; bleeding across them would associate the end
of one verse with the start of the next for no linguistic reason.

**Harmonic distance weighting.** A word 1 token away contributes 1.0, at 2
tokens 0.5, at 3 tokens 0.33. Adjacency should count for more than mere
co-presence.

**PPMI with α=0.75 context smoothing.** Raw counts would rank `את`, `כל`, and
`אשר` at the top of every list. PMI fixes that. The `α=0.75` exponent on the
context probability is the standard correction for PMI's *opposite* bias — its
tendency to over-reward rare words (Levy & Goldberg 2015).

**Final-form normalization** (`ךםןףץ` → `כמנפצ`). Purely orthographic, so that
`מלך` and `מלכים` share an alphabet.

**Prefix folding** (`--strip-prefixes`, on in the recommended build). Hebrew
grammatical particles `ו ה ב ל כ מ ש` are stripped — but *only* when the
remainder is itself a frequent corpus word, which is the cheap guard against
destroying `משה` → `שה`. Worth **2.9x P@20**; see [FINDINGS.md §9](FINDINGS.md).
Suffix inflection (`מלכי`, `מלכות`) is still untouched and is now the largest
remaining quality limitation.

---

## BM25-style normalization

The association layer uses the same normalization ideas as the retrieval layer,
so the two agree about what counts as informative. Four knobs are implemented;
each was **ablated individually** rather than assumed:

| flag | what it does | verdict on this corpus |
|---|---|---|
| `--length-norm-b` | BM25 `b`, verse = document | **ON by default (0.75)** — the only one that worked |
| `--idf-weight` | discount context words spread over many verses | no measurable effect |
| `--saturate-k` | BM25 `tf/(tf+k)` within a verse | negligible |
| `--min-ctx-df` | drop context words confined to few verses | negligible |

The headline result — formulaic repetition (Numbers 7's twelve identical
offering formulas) polluting `קרבן`'s associations:

```
princes in top-20 of קרבן:
  baseline (pure PPMI)     11 / 20
  + idf-weight 0.6         11 / 20      <- no effect
  + saturate-k 1.2         10 / 20
  + length-norm-b 0.75      0 / 20      <- eliminated
```

```
expansion of מזבח קרבן
  before:  מנחתך, אחירע, אבידן, שלמיאל, פגעיאל, אליצור   (tribal princes)
  after:   יקריבו, הקטרת, קרנת, חמשה, ובנית, מכבר, ליהוה  (sacrificial vocabulary)
```

Length normalization wins here because the princes appear **only** inside those
long formulaic verses, so the penalty hits 100% of their co-occurrence mass,
while genuine neighbours occur in verses of varied length and are left alone.
Full mechanism and the per-knob numbers are in [FINDINGS.md §6](FINDINGS.md).

Reproduce the ablation with:

```bash
python compare.py
```

**Do not enable the other three knobs on faith** — `bm25-strict` visibly
over-prunes good results. They stay available for when a proper evaluation
harness ([ROADMAP.md](ROADMAP.md) item 4) can measure them.

---

## Measured quality

Scored by [evaluate.py](evaluate.py) against the project dictionary's link table
— an independent source that knows nothing about co-occurrence.

```
                            P@20      MRR   recall     vs chance
random baseline           0.0025   0.0003   0.0048          1x
synonym  (no prefix fold) 0.0153   0.0079   0.0550          6x
synonym  (prefix fold)    0.0444   0.0242   0.1375         18x
antonym  (prefix fold)    0.1273   0.0280   0.1575         51x
```

Two things to take from this:

**Prefix folding is worth 2.9x** — the single largest quality win measured.

**Opposites score 4x better than synonyms**, and the gap widens to 4x rather
than closing when controlled for gold-set size and word frequency. The
distributional hypothesis predicts exactly this (both sides of a contrast occur
in the same frames), and the hits are unambiguous:

```
בקר <-> ערב     אור <-> חשכ     רשע <-> צדיק     ברכה <-> קללה
```

So this is a **contrast and semantic-field index**, not a synonym index. That
is a statement about what it should be used for — see
[FINDINGS.md §10](FINDINGS.md). Absolute precision is still low; whether that is
fixable is now a corpus-size question ([ROADMAP.md](ROADMAP.md) item 5).

---

## Prior art

This is the **distributional hypothesis**, and the design lands close to
**GloVe**, which also starts from a global word-word co-occurrence matrix — the
difference being that GloVe factorizes the matrix into dense vectors while this
keeps it sparse and readable. The PPMI + α-smoothing choices come from Levy &
Goldberg's work showing that a well-tuned PPMI matrix is competitive with SGNS
(word2vec) on similarity tasks.

The bet here is that for a fixed corpus with a hard explainability requirement,
skipping the factorization step is a feature rather than a compromise.

## Update 2026-08-03 — precomputed similarity, scorer split, safety tooling

- **`sim` table** (a, rank, b, s): AssocBuilder now precomputes the top-K
  overlap neighbors offline (`SimGraph.cs`, on by default). `similar()` is an
  indexed seek like `neighbors()`. FINDINGS §21.
- **Scorer split**: `--scorer logdice` builds better first-order display rows;
  PPMI (+ LMI pruning, now the default) builds better similarity features.
  FINDINGS §18, §20.
- **Targum bridge**: `targum_bridge.py` → `--bridge targum_bridge.csv` folds
  Aramaic onto Hebrew lemmas via the verse-aligned Targumim; clean win on
  Bavli. FINDINGS §19.
- **Gloss channel**: `gloss_channel.py` — dictionary-definition similarity as
  a fallback for words without a corpus profile. FINDINGS §22.
- **`tools/`**: every script here MUST route output through `tools/masked.py`
  (`import masked; masked.install()`), which replaces Hebrew with stable
  `[H:xxxx]` hashes (decode map: `tools/hashmap.tsv`, local only). The network
  content filter scans conversation payloads and corpus vocabulary can be on
  its blocklist — raw corpus words in stdout have already killed a session.
  FINDINGS §25 and `.kiro/steering/agent-behavior.md`.
