"""
Build a word-association index over the whole Tanach.

Pipeline
--------
  seforim.db  ->  verses  ->  tokens  ->  co-occurrence counts
              ->  PMI scoring  ->  top-K pruning  ->  CSR arrays on disk

The output is the "association index" described in the design: two flat arrays
(offsets + edges) plus a vocabulary, all immutable and read-only after the build.

Usage:  python build_index.py [--window 4] [--topk 200] [--min-count 3]
"""

from __future__ import annotations

import argparse
import array
import json
import math
import re
import sqlite3
import struct
import sys
import time
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

SEFORIM_DB = "C:/ProgramData/otzaria/books/seforim.db"
OUT_DIR = Path(__file__).parent / "index"

# Tanach base books are ids 1..39 in this DB (verified: 39 books, 24174 lines).
TANACH_BOOK_IDS = range(1, 40)


# ---------------------------------------------------------------------------
# Text extraction
# ---------------------------------------------------------------------------

_TAG_RE = re.compile(r"<[^>]+>")
_ENTITY_RE = re.compile(r"&[a-zA-Z]+;|&#\d+;")
# Verse numbers "(א)" and parasha markers "{פ}"/"{ס}" are editorial, not text,
# and they occur mid-line as well as at the start.
_MARKER_RE = re.compile(r"[({\[][^)}\]]{0,4}[)}\]]")
# Hebrew points (nikud) and cantillation (te'amim) live in U+0591..U+05C7 — but
# that range ALSO contains the maqaf U+05BE, the hyphen joining words such as
# `al-pnei`. Deleting it silently glues two words into one token, so U+05BE is
# excluded from this class and falls through to _NON_HEBREW_RE as a separator.
_NIKUD_RE = re.compile(r"[\u0591-\u05BD\u05BF-\u05C7]")
# Everything that is not a Hebrew letter becomes a separator.
_NON_HEBREW_RE = re.compile(r"[^\u05D0-\u05EA]+")

# Final-form letters normalized to their base form so that e.g. מלך / מלכים
# share a consistent alphabet. (Purely orthographic; not stemming.)
FINALS = str.maketrans("ךםןףץ", "כמנפצ")


def clean_verse(html: str) -> str:
    """Strip HTML, editorial markers, nikud and te'amim. Maqaf survives as a
    separator (see _NIKUD_RE) so hyphenated pairs stay two tokens."""
    # Tags are deleted, NOT replaced by a space: they are inline formatting
    # (`<big>B</big>ereshit`, `<span>` maqafim) that sits *inside* words, so
    # substituting a space would split the word in two.
    s = _TAG_RE.sub("", html)
    s = _ENTITY_RE.sub(" ", s)
    s = _MARKER_RE.sub(" ", s)
    s = _NIKUD_RE.sub("", s)
    return unicodedata.normalize("NFC", s)


def tokenize(text: str) -> list[str]:
    """Hebrew-letter tokens, final forms normalized. No stemming, no stopping."""
    parts = _NON_HEBREW_RE.split(text)
    return [p.translate(FINALS) for p in parts if p]


# ---------------------------------------------------------------------------
# Prefix normalization (Hebrew grammatical particles)
# ---------------------------------------------------------------------------
#
# 42% of the vocabulary is another vocabulary word wearing a prefix, so the
# statistical mass of one concept is split across dozens of tokens
# (FINDINGS.md §4: 79 distinct tokens carry מלכ). Every split thins the
# profiles that similarity depends on.
#
# This is NOT a morphological analyzer. It is the cheap version: strip a
# candidate prefix only when the remainder is ITSELF a frequent corpus word.
# That guard is what stops the obvious destruction (משה -> שה, מלכ -> לכ),
# and it costs one dict lookup.

# Single-letter particles: vav (and), he (the), bet (in), lamed (to),
# kaf (like), mem (from), shin (that). Then the common two-letter stacks,
# which must be tried FIRST — longest match wins, or `וה` strips as bare `ו`
# and leaves the article behind.
PREFIXES = ("וכש", "ולכ", "ובכ", "וה", "ול", "וב", "וכ", "ומ", "וש",
            "כש", "לכ", "בכ", "מה", "שה", "הל", "הב",
            "ו", "ה", "ב", "ל", "כ", "מ", "ש")

# Words that must never be stripped: high-frequency function words whose own
# first letter coincides with a particle. Stripping these is always wrong and
# the frequency guard alone will not catch them, because the remainder is also
# a real word (של -> ל, מה -> ה).
NEVER_STRIP = {
    "ואת", "ויהי", "והוא", "והיה", "כי", "כל", "כה", "כן", "כאשר", "כמו",
    "לא", "לו", "לה", "לכ", "למה", "מה", "מי", "מנ", "משה", "מאד", "מאה",
    "בנ", "בת", "בית", "בא", "בו", "הוא", "היא", "הנה", "הימ", "המ", "הנ",
    "של", "שמ", "שנה", "שני", "שמע", "שר", "שלמה", "ולא", "ואמר",
    "ו", "ה", "ב", "ל", "כ", "מ", "ש",
}

# The remainder must be at least this long. Two-letter Hebrew roots exist but
# stripping down to them is where nearly all the false positives live.
MIN_STEM_LEN = 3


def build_prefix_map(freq: Counter[str], min_stem_freq: int,
                     stem_ratio: float = 0.25) -> dict[str, str]:
    """Map surface form -> stripped lexeme, for the forms where stripping is safe.

    A prefix is removed only when ALL of the following hold:
      - the remainder is itself a corpus word occurring >= min_stem_freq times
      - the remainder is at least MIN_STEM_LEN letters
      - neither the surface form nor the remainder is on NEVER_STRIP
      - the remainder is at least `stem_ratio` as frequent as the surface form

    That last condition sets the direction of the fold, and its threshold is
    the whole tuning surface of this function.

    Requiring `stem_freq >= surface_freq` (ratio 1.0) is the safe reading, but
    measurement showed it rejects the single most valuable fold in the corpus:
    `המלכ`(1053) is marginally MORE frequent than `מלכ`(1027), so the definite
    article never comes off the most common noun in the Tanach. A ratio well
    below 1 accepts that fold while still refusing to collapse a common word
    into a genuinely rare one.

    The ratio does NOT protect against a frequent-but-wrong stem — `מלכה`(21)
    folding to `לכה`(28) passes any ratio test, because `לכה` really is a
    corpus word. Only MIN_STEM_LEN and NEVER_STRIP guard that case, and
    imperfectly. This is the known ceiling of the cheap approach.
    """
    mapping: dict[str, str] = {}
    for w, n in freq.items():
        if w in NEVER_STRIP or len(w) < MIN_STEM_LEN + 1:
            continue
        for p in PREFIXES:
            if not w.startswith(p):
                continue
            stem = w[len(p):]
            if len(stem) < MIN_STEM_LEN or stem in NEVER_STRIP:
                continue
            sf = freq.get(stem, 0)
            if sf >= min_stem_freq and sf >= stem_ratio * n:
                mapping[w] = stem
                break
    return mapping


def apply_prefix_map(verses: list[list[str]], mapping: dict[str, str]) -> list[list[str]]:
    """Rewrite tokens to their lexeme. Position is preserved, so the distance
    weighting and verse-length normalization are unaffected."""
    if not mapping:
        return verses
    g = mapping.get
    return [[g(t, t) for t in v] for v in verses]


def load_verses(db_path: str) -> tuple[list[list[str]], list[str]]:
    """Return (tokenized verses, book titles). Only real verses — heRef IS NOT NULL
    skips the <h1>/<h2> chapter-heading lines."""
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    ids = ",".join(str(i) for i in TANACH_BOOK_IDS)
    titles = {
        bid: t for bid, t in con.execute(f"select id, title from book where id in ({ids})")
    }
    verses: list[list[str]] = []
    rows = con.execute(
        f"""select bookId, content from line
            where bookId in ({ids}) and heRef is not null
            order by bookId, lineIndex"""
    )
    for _bid, content in rows:
        toks = tokenize(clean_verse(content))
        if toks:
            verses.append(toks)
    con.close()
    return verses, [titles[i] for i in sorted(titles)]


# ---------------------------------------------------------------------------
# Co-occurrence counting
# ---------------------------------------------------------------------------


def build_vocab(verses: list[list[str]], min_count: int) -> tuple[list[str], dict[str, int], list[int]]:
    """Assign an integer id to every word occurring >= min_count times.

    Ids are assigned in descending-frequency order, so the hottest words get the
    smallest ids — their offset entries land on the same cache lines.
    """
    freq: Counter[str] = Counter()
    for v in verses:
        freq.update(v)
    kept = [(w, n) for w, n in freq.most_common() if n >= min_count]
    vocab = [w for w, _ in kept]
    counts = [n for _, n in kept]
    word_id = {w: i for i, w in enumerate(vocab)}
    return vocab, word_id, counts


def count_cooccurrences(
    verses: list[list[str]], word_id: dict[str, int], window: int,
    saturate_k: float = 0.0, length_norm_b: float = 0.0,
) -> tuple[dict[int, dict[int, float]], list[float], float, list[int]]:
    """Distance-weighted co-occurrence counts, harmonic weighting (1/d).

    Co-occurrence never crosses a verse boundary — a verse is the natural
    sentence unit here, and bleeding across them would associate the last word
    of one verse with the first word of the next for no linguistic reason.

    Two BM25-borrowed normalizations are available, both applied PER VERSE
    (the verse being our document unit) before the counts are accumulated:

    `saturate_k`  BM25's tf saturation, tf/(tf+k). Within one verse, the 5th
                  repetition of a pair should count far less than the 1st.
                  This is what stops a formulaic passage (Numbers 7's twelve
                  identical offering formulas) from dominating a term's
                  profile. k=0 disables it.

    `length_norm_b` BM25's `b`: divide by (1-b + b*len/avglen). Long verses
                  otherwise contribute more co-occurrence mass simply by being
                  long. b=0 disables, b=1 fully normalizes.

    Returns (cooc, context_totals, grand_total, doc_freq) where cooc[a][b] is
    the weighted count of b near a, and doc_freq[i] counts the DISTINCT verses
    word i appears in (needed for the IDF-style discount at scoring time).
    """
    cooc: dict[int, dict[int, float]] = defaultdict(lambda: defaultdict(float))
    ctx_total: dict[int, float] = defaultdict(float)
    V = max(word_id.values()) + 1
    doc_freq = [0] * V
    grand = 0.0

    lengths = [sum(1 for w in v if w in word_id) for v in verses]
    avg_len = (sum(lengths) / len(lengths)) if lengths else 1.0

    for v, vlen in zip(verses, lengths):
        ids = [word_id[w] for w in v if w in word_id]
        n = len(ids)

        for i in set(ids):
            doc_freq[i] += 1

        # Length normalization is a per-verse constant (BM25's denominator).
        norm = 1.0
        if length_norm_b > 0 and avg_len > 0:
            norm = 1.0 - length_norm_b + length_norm_b * (vlen / avg_len)

        # Accumulate this verse's pairs separately so saturation can be applied
        # to the within-verse total before it reaches the global counts.
        local: dict[tuple[int, int], float] = defaultdict(float)
        for i, a in enumerate(ids):
            hi = min(n, i + window + 1)
            for j in range(i + 1, hi):
                b = ids[j]
                if a == b:
                    continue
                local[(a, b) if a < b else (b, a)] += 1.0 / (j - i)

        for (a, b), w in local.items():
            if saturate_k > 0:
                w = w * (saturate_k + 1.0) / (w + saturate_k)
            w /= norm
            cooc[a][b] += w
            cooc[b][a] += w
            ctx_total[a] += w
            ctx_total[b] += w
            grand += 2 * w

    totals = [0.0] * V
    for k, v in ctx_total.items():
        totals[k] = v
    return cooc, totals, grand, doc_freq


# ---------------------------------------------------------------------------
# PMI scoring + pruning
# ---------------------------------------------------------------------------


def score_ppmi(
    cooc: dict[int, dict[int, float]],
    totals: list[float],
    grand: float,
    topk: int,
    min_cooc: float,
    shift: float,
    doc_freq: list[int] | None = None,
    n_docs: int = 0,
    idf_weight: float = 0.0,
    min_ctx_df: int = 0,
    idf_basis: str = "df",
) -> dict[int, list[tuple[int, float]]]:
    """Positive PMI with context smoothing and an optional BM25-style IDF discount.

    PMI(a,b) = log2( P(a,b) / (P(a) * P(b)^alpha) )

    The alpha=0.75 exponent on the context probability is the standard fix for
    PMI's bias toward rare words (Levy & Goldberg 2015): it inflates P(b) for
    rare b, damping their scores. `shift` subtracts a constant (shifted PMI,
    equivalent to SGNS's negative-sampling k) which prunes weak pairs.

    `idf_weight` adds the retrieval-side discount BM25 relies on, so the
    association layer and the search layer agree about what is informative.
    PMI already normalizes by how OFTEN a context word occurs, but not by how
    BROADLY it is spread: a word appearing in many distinct verses discriminates
    little, exactly as a high-document-frequency term does in BM25. We scale
    each score by

        idf(b) = log(1 + (N - df_b + 0.5) / (df_b + 0.5))          [BM25 IDF]

    normalized to [0,1] and mixed in via `idf_weight` (0 = pure PMI,
    1 = fully IDF-scaled).

    `min_ctx_df` is the complementary guard in the other direction: a context
    word confined to too few distinct verses is bursty (one formulaic passage)
    rather than genuinely associated, so it is dropped outright.
    """
    alpha = 0.75
    p_ctx_a = [(t / grand) ** alpha if t > 0 else 0.0 for t in totals]

    # Precompute the normalized IDF-style discount per context word.
    #
    #   idf_basis='df'     classic BM25 IDF over VERSE frequency — how many
    #                      distinct verses the word appears in.
    #   idf_basis='degree' IDF over ASSOCIATION frequency — how many distinct
    #                      words it co-occurs with (its degree in the graph).
    #                      A word that sits next to everything discriminates
    #                      nothing, regardless of how many verses it spans.
    #                      These are genuinely different quantities: a word can
    #                      be confined to few verses yet touch many neighbours.
    idf: list[float] | None = None
    if idf_weight > 0:
        if idf_basis == "degree":
            V = len(totals)
            degree = [0] * V
            for a, row in cooc.items():
                degree[a] = len(row)
            raw = [
                math.log(1.0 + (V - d + 0.5) / (d + 0.5)) if d > 0 else 0.0
                for d in degree
            ]
        elif doc_freq and n_docs > 0:
            raw = [
                math.log(1.0 + (n_docs - df + 0.5) / (df + 0.5)) if df > 0 else 0.0
                for df in doc_freq
            ]
        else:
            raw = []
        if raw:
            hi = max(raw) or 1.0
            idf = [r / hi for r in raw]

    out: dict[int, list[tuple[int, float]]] = {}
    for a, row in cooc.items():
        ta = totals[a]
        if ta <= 0:
            continue
        p_a = ta / grand
        scored: list[tuple[int, float]] = []
        for b, c in row.items():
            if c < min_cooc:
                continue
            if min_ctx_df and doc_freq and doc_freq[b] < min_ctx_df:
                continue
            denom = p_a * p_ctx_a[b]
            if denom <= 0:
                continue
            pmi = math.log2((c / grand) / denom) - shift
            if pmi <= 0:
                continue
            if idf is not None:
                pmi *= (1.0 - idf_weight) + idf_weight * idf[b]
            scored.append((b, pmi))
        if not scored:
            continue
        scored.sort(key=lambda t: -t[1])
        out[a] = scored[:topk]
    return out


# ---------------------------------------------------------------------------
# CSR serialization
# ---------------------------------------------------------------------------


def write_csr(out_dir: Path, vocab: list[str], counts: list[int],
              assoc: dict[int, list[tuple[int, float]]], meta: dict) -> None:
    """Write the immutable on-disk index.

      vocab.json    word list (id = array position) + corpus frequency
      offsets.bin   uint32[V+1]  — offsets[i]..offsets[i+1] is word i's slice
      edges.bin     (uint32 id, float32 weight)[]  — sorted by weight desc

    Lookup for word i is: read offsets[i], offsets[i+1], then read that
    contiguous run of edges. No pointers, no parsing, no traversal.
    """
    out_dir.mkdir(parents=True, exist_ok=True)
    V = len(vocab)

    offsets = array.array("I", [0] * (V + 1))
    edge_buf = bytearray()
    pack = struct.Struct("<If").pack

    cursor = 0
    for i in range(V):
        offsets[i] = cursor
        for b, w in assoc.get(i, ()):
            edge_buf += pack(b, w)
            cursor += 1
    offsets[V] = cursor

    (out_dir / "offsets.bin").write_bytes(offsets.tobytes())
    (out_dir / "edges.bin").write_bytes(bytes(edge_buf))
    (out_dir / "vocab.json").write_text(
        json.dumps({"words": vocab, "counts": counts}, ensure_ascii=False),
        encoding="utf-8",
    )
    meta = dict(meta, vocab_size=V, edge_count=cursor,
                offsets_bytes=(V + 1) * 4, edges_bytes=cursor * 8)
    (out_dir / "meta.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8"
    )


# ---------------------------------------------------------------------------


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--window", type=int, default=4, help="context window (tokens each side)")
    ap.add_argument("--topk", type=int, default=200, help="max associations kept per word")
    ap.add_argument("--min-count", type=int, default=3, help="min corpus frequency for vocab")
    ap.add_argument("--min-cooc", type=float, default=1.0, help="min weighted co-occurrence")
    ap.add_argument("--shift", type=float, default=0.0, help="PMI shift (higher = stricter)")
    # --- BM25-style normalization (see score_ppmi / count_cooccurrences) ---
    ap.add_argument("--idf-weight", type=float, default=0.0,
                    help="0..1 — discount uninformative context words "
                         "(BM25 IDF). 0 = pure PMI")
    ap.add_argument("--idf-basis", choices=["df", "degree"], default="df",
                    help="'df' = verse frequency (classic BM25); 'degree' = number "
                         "of distinct words it associates with")
    ap.add_argument("--saturate-k", type=float, default=0.0,
                    help="BM25 tf saturation k applied within a verse; 0 = off")
    # Default ON: the one normalization that measurably earned its place on this
    # corpus — it eliminates the Numbers-7 formulaic artifact without touching
    # genuine associations. See FINDINGS.md §6. Pass 0 to disable.
    ap.add_argument("--length-norm-b", type=float, default=0.75,
                    help="BM25 length normalization b (verse = document); 0 = off")
    ap.add_argument("--min-ctx-df", type=int, default=0,
                    help="drop context words appearing in fewer than N distinct "
                         "verses (anti-burstiness)")
    # --- Hebrew prefix morphology (ROADMAP item 1) ---
    # Default ON: measured 2.9x P@20 / 2.5x recall against the gold set, the
    # largest quality win in this PoC. See FINDINGS.md §9.
    ap.add_argument("--strip-prefixes", action=argparse.BooleanOptionalAction,
                    default=True,
                    help="fold grammatical prefixes onto the lexeme when the "
                         "remainder is itself a frequent corpus word "
                         "(--no-strip-prefixes to disable)")
    # 5 is the measured knee: quality rises monotonically down to it and
    # second-order degrades below it.
    ap.add_argument("--min-stem-freq", type=int, default=5,
                    help="a stripped remainder must occur at least this often "
                         "to be accepted as the lexeme")
    ap.add_argument("--stem-ratio", type=float, default=0.25,
                    help="remainder must be >= this fraction of the surface "
                         "form's frequency; 1.0 = strictly more frequent")
    ap.add_argument("--db", default=SEFORIM_DB)
    ap.add_argument("--out", default=str(OUT_DIR))
    args = ap.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")
    t0 = time.perf_counter()

    print("reading Tanach ...", flush=True)
    verses, titles = load_verses(args.db)
    n_tokens = sum(len(v) for v in verses)
    print(f"  {len(titles)} books, {len(verses):,} verses, {n_tokens:,} tokens "
          f"({time.perf_counter() - t0:.1f}s)")

    prefix_map: dict[str, str] = {}
    if args.strip_prefixes:
        freq: Counter[str] = Counter()
        for v in verses:
            freq.update(v)
        before_vocab = len(freq)
        prefix_map = build_prefix_map(freq, args.min_stem_freq, args.stem_ratio)
        verses = apply_prefix_map(verses, prefix_map)
        after = len({t for v in verses for t in v})
        print(f"prefix normalization: {len(prefix_map):,} forms folded, "
              f"vocabulary {before_vocab:,} -> {after:,}")

    print("building vocabulary ...", flush=True)
    vocab, word_id, counts = build_vocab(verses, args.min_count)
    print(f"  {len(vocab):,} words (min-count={args.min_count})")

    print(f"counting co-occurrences (window={args.window}, k={args.saturate_k}, "
          f"b={args.length_norm_b}) ...", flush=True)
    t = time.perf_counter()
    cooc, totals, grand, doc_freq = count_cooccurrences(
        verses, word_id, args.window, args.saturate_k, args.length_norm_b)
    pairs = sum(len(r) for r in cooc.values())
    print(f"  {pairs:,} raw pairs ({time.perf_counter() - t:.1f}s)")

    print(f"scoring PMI (idf_weight={args.idf_weight}, "
          f"min_ctx_df={args.min_ctx_df}) + pruning ...", flush=True)
    t = time.perf_counter()
    assoc = score_ppmi(cooc, totals, grand, args.topk, args.min_cooc, args.shift,
                       doc_freq=doc_freq, n_docs=len(verses),
                       idf_weight=args.idf_weight, min_ctx_df=args.min_ctx_df,
                       idf_basis=args.idf_basis)
    kept = sum(len(r) for r in assoc.values())
    print(f"  {kept:,} edges kept, {len(assoc):,} words with associations "
          f"({time.perf_counter() - t:.1f}s)")

    print("writing CSR index ...", flush=True)
    write_csr(Path(args.out), vocab, counts, assoc, {
        "corpus": "Tanach (39 base books)",
        "books": titles,
        "verses": len(verses),
        "tokens": n_tokens,
        "window": args.window,
        "topk": args.topk,
        "min_count": args.min_count,
        "min_cooc": args.min_cooc,
        "shift": args.shift,
        "idf_weight": args.idf_weight,
        "idf_basis": args.idf_basis,
        "saturate_k": args.saturate_k,
        "length_norm_b": args.length_norm_b,
        "min_ctx_df": args.min_ctx_df,
        "strip_prefixes": args.strip_prefixes,
        "min_stem_freq": args.min_stem_freq,
        "stem_ratio": args.stem_ratio,
        "folded_forms": len(prefix_map),
        "build_seconds": round(time.perf_counter() - t0, 1),
    })

    out = Path(args.out)
    total_mb = sum(f.stat().st_size for f in out.iterdir()) / 1e6
    print(f"\ndone in {time.perf_counter() - t0:.1f}s -> {out}  ({total_mb:.1f} MB total)")
    for f in sorted(out.iterdir()):
        print(f"  {f.name:14s} {f.stat().st_size / 1e6:7.2f} MB")


if __name__ == "__main__":
    main()
