"""
Scalable builder — same index format, corpus-selectable, bounded memory.

Why a second builder
--------------------
`build_index.py` accumulates co-occurrences in a dict-of-dicts. Measured on the
Tanach that costs **65 bytes per pair**, which is fine for 866k pairs (56 MB)
and impossible for the whole DB:

    39 base Tanach books      2,959,879 chars
    whole seforim.db      2,223,713,531 chars      751x
    -> ~231M tokens, ~0.2-0.7B pairs, 10-42 GB accumulator

This machine has 7.2 GB free. So the accumulator has to go to disk.

The approach: **external merge sort**. Emit fixed-width (a, b, weight) records
into sorted runs of bounded size, spill each run to disk, then k-way merge the
runs while summing duplicate keys. Peak memory is one run plus the merge
heap — independent of corpus size. Disk cost is transient.

    verses -> pair records -> sorted runs on disk -> k-way merge (summing)
           -> PMI scoring -> top-K pruning -> the same CSR index

Everything downstream (scoring, pruning, on-disk format, query.py) is reused
from build_index.py unchanged, so results stay comparable across corpora.

Usage:
    python build_large.py --corpus tanach          # parity check vs build_index
    python build_large.py --corpus mishnah
    python build_large.py --corpus bavli
    python build_large.py --corpus all --out index-all
    python build_large.py --list                   # show corpora and sizes
"""

from __future__ import annotations

import argparse
import array
import heapq
import json
import math
import os
import shutil
import sqlite3
import struct
import sys
import tempfile
import time
from collections import Counter
from pathlib import Path

from build_index import (
    FINALS,
    SEFORIM_DB,
    apply_prefix_map,
    build_prefix_map,
    clean_verse,
    score_ppmi,
    tokenize,
    write_csr,
)

# Root categories in seforim.db, by title. Selecting by root category rather
# than by book id keeps this stable if book ids shift.
#
# Register matters here and is the reason these are separate switches rather
# than one --all: Biblical Hebrew, Mishnaic Hebrew and Talmudic Aramaic have
# genuinely different distributions. Blending them may raise coverage and
# lower precision, so they have to be measurable apart before being combined.
CORPORA: dict[str, dict] = {
    "tanach":    {"books": (1, 39), "desc": "39 base Tanach books"},
    "tanach-all":{"cats": ["תנ״ך"], "desc": "Tanach + commentaries + targumim"},
    "mishnah":   {"cats": ["משנה"], "desc": "Mishnah"},
    "bavli":     {"cats": ["תלמוד בבלי"], "desc": "Talmud Bavli"},
    "yerushalmi":{"cats": ["תלמוד ירושלמי"], "desc": "Talmud Yerushalmi"},
    "midrash":   {"cats": ["מדרש"], "desc": "Midrash"},
    "halacha":   {"cats": ["הלכה"], "desc": "Halacha"},
    "kabbalah":  {"cats": ["קבלה"], "desc": "Kabbalah"},
    "chasidut":  {"cats": ["חסידות"], "desc": "Chasidut"},
    "musar":     {"cats": ["ספרי מוסר"], "desc": "Musar"},
    "machshava": {"cats": ["מחשבת ישראל"], "desc": "Jewish thought"},
    "responsa":  {"cats": ["שו״ת"], "desc": "Responsa"},
    "tefila":    {"cats": ["סדר התפילה"], "desc": "Liturgy"},
    "tosefta":   {"cats": ["תוספתא"], "desc": "Tosefta"},
    # Convenience groupings.
    "rabbinic":  {"cats": ["משנה", "תלמוד בבלי", "תלמוד ירושלמי", "תוספתא",
                           "מדרש"], "desc": "Mishnah+Talmud+Tosefta+Midrash"},
    "all":       {"cats": None, "desc": "every book in the DB"},
}

PAIR = struct.Struct("<IIf")          # (word_a, word_b, weight)
PAIR_SIZE = PAIR.size


# ---------------------------------------------------------------------------
# Corpus selection
# ---------------------------------------------------------------------------


def resolve_books(con: sqlite3.Connection, corpus: str,
                  base_only: bool = False) -> list[int]:
    """Book ids for a named corpus.

    `base_only` restricts to `isBaseBook=1`, and it matters far more than it
    looks. Measured: the `משנה` category is **96% commentary** — 217,597 of
    230,300 lines are `dependenceType='commentary'`, averaging 238 chars
    against the base text's own verse-sized lines. Building over the whole
    category therefore measures medieval commentary prose, not Mishnah, and
    it silently breaks the verse-bounded window (66 tokens/line vs the
    Tanach's 13.2). Always state which mode a result came from.
    """
    spec = CORPORA[corpus]
    base_clause = " and b.isBaseBook = 1" if base_only else ""
    if "books" in spec:
        lo, hi = spec["books"]
        ids = list(range(lo, hi + 1))
        if not base_only:
            return ids
        q = (f"select id from book where id between {lo} and {hi}"
             f" and isBaseBook = 1")
        return [r[0] for r in con.execute(q)]
    cats = spec.get("cats")
    if cats is None:
        return [r[0] for r in con.execute(
            f"select id from book b where 1=1{base_clause}")]
    q = """
        select b.id from book b
        where b.categoryId in (
            select cc.descendantId from category_closure cc
            join category rc on rc.id = cc.ancestorId
            where rc.level = 0 and rc.title in (%s))%s
    """ % (",".join("?" * len(cats)), base_clause)
    return [r[0] for r in con.execute(q, cats)]


def iter_verses(db_path: str, book_ids: list[int], batch: int = 20000):
    """Stream tokenized verses. Never materializes the corpus — at 231M tokens
    the token lists alone would not fit in memory."""
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    con.execute("pragma mmap_size=4294967296")
    for i in range(0, len(book_ids), 400):
        chunk = book_ids[i:i + 400]
        ids = ",".join(str(b) for b in chunk)
        rows = con.execute(
            f"""select content from line
                where bookId in ({ids}) and heRef is not null"""
        )
        while True:
            got = rows.fetchmany(batch)
            if not got:
                break
            for (content,) in got:
                toks = tokenize(clean_verse(content))
                if toks:
                    yield toks
    con.close()


# ---------------------------------------------------------------------------
# Pass 1 — vocabulary
# ---------------------------------------------------------------------------


def pass1_vocab(db: str, books: list[int], min_count: int,
                strip_prefixes: bool, min_stem_freq: int, stem_ratio: float):
    """Count token frequency and build the vocabulary + prefix map.

    A full pass just to count is the price of not holding the corpus in
    memory. It is I/O-bound and cheap relative to pass 2.
    """
    freq: Counter[str] = Counter()
    n_tok = n_verse = 0
    for toks in iter_verses(db, books):
        freq.update(toks)
        n_tok += len(toks)
        n_verse += 1
        if n_verse % 500000 == 0:
            print(f"    ... {n_verse:,} verses, {n_tok:,} tokens, "
                  f"{len(freq):,} distinct", flush=True)

    prefix_map: dict[str, str] = {}
    if strip_prefixes:
        prefix_map = build_prefix_map(freq, min_stem_freq, stem_ratio)
        # Fold the counts so the vocabulary reflects post-fold frequency.
        folded: Counter[str] = Counter()
        for w, n in freq.items():
            folded[prefix_map.get(w, w)] += n
        freq = folded

    kept = [(w, n) for w, n in freq.most_common() if n >= min_count]
    vocab = [w for w, _ in kept]
    counts = [n for _, n in kept]
    return vocab, {w: i for i, w in enumerate(vocab)}, counts, prefix_map, n_tok, n_verse


# ---------------------------------------------------------------------------
# Pass 2 — pair emission with external sort
# ---------------------------------------------------------------------------


class RunWriter:
    """Accumulates pairs in a dict, spilling sorted runs to disk when full.

    The in-memory dict is still used — but only up to `max_pairs`, which caps
    peak memory at a known figure regardless of how large the corpus is.
    Aggregating in the dict before spilling matters: within one run, duplicate
    keys are summed rather than written twice, which typically shrinks the
    spilled data several-fold.
    """

    def __init__(self, tmpdir: Path, max_pairs: int, prefix: str = ""):
        self.tmpdir = tmpdir
        self.max_pairs = max_pairs
        self.prefix = prefix
        self.buf: dict[tuple[int, int], float] = {}
        self.runs: list[Path] = []
        self.spilled = 0

    def add(self, a: int, b: int, w: float) -> None:
        k = (a, b)
        self.buf[k] = self.buf.get(k, 0.0) + w
        if len(self.buf) >= self.max_pairs:
            self.flush()

    def flush(self) -> None:
        if not self.buf:
            return
        path = self.tmpdir / f"{self.prefix}run{len(self.runs):04d}.bin"
        pack = PAIR.pack
        with open(path, "wb", buffering=1 << 22) as f:
            for (a, b) in sorted(self.buf):
                f.write(pack(a, b, self.buf[(a, b)]))
        self.runs.append(path)
        self.spilled += len(self.buf)
        self.buf = {}


def _read_run(path: Path, bufsize: int = 1 << 22):
    """Yield (a, b, w) from a sorted run."""
    unpack = PAIR.unpack
    with open(path, "rb", buffering=0) as f:
        while True:
            blk = f.read(bufsize - bufsize % PAIR_SIZE)
            if not blk:
                return
            for i in range(0, len(blk), PAIR_SIZE):
                yield unpack(blk[i:i + PAIR_SIZE])


def merge_runs(runs: list[Path]):
    """K-way merge the sorted runs, summing weights for equal (a,b) keys.

    Yields (a, b, total_weight) in key order. Memory is the merge heap only —
    one record per run.
    """
    iters = [_read_run(p) for p in runs]
    heap = []
    for i, it in enumerate(iters):
        rec = next(it, None)
        if rec is not None:
            heapq.heappush(heap, (rec[0], rec[1], i, rec[2]))

    cur_key = None
    acc = 0.0
    while heap:
        a, b, i, w = heapq.heappop(heap)
        rec = next(iters[i], None)
        if rec is not None:
            heapq.heappush(heap, (rec[0], rec[1], i, rec[2]))
        if cur_key == (a, b):
            acc += w
        else:
            if cur_key is not None:
                yield cur_key[0], cur_key[1], acc
            cur_key, acc = (a, b), w
    if cur_key is not None:
        yield cur_key[0], cur_key[1], acc


def _shard_worker(arg):
    """Count co-occurrences for one shard of books, spilling sorted runs.

    Runs in a separate process. Each shard writes its own run files and returns
    its partial totals/doc_freq/grand, which the parent sums — the counts are
    additive, so sharding by book is exact, not an approximation.

    The `avg_len` used for length normalization is passed in from pass 1 so
    every shard normalizes against the SAME corpus-wide mean. Computing it
    per-shard would make the weights depend on how the work happened to be
    divided, which would be a genuine correctness bug.
    """
    (db, books, word_id, prefix_map, window, length_norm_b, avg_len,
     tmpdir, max_pairs, shard_id) = arg
    rw = RunWriter(Path(tmpdir), max_pairs, prefix=f"s{shard_id:02d}_")
    V = len(word_id)
    totals = [0.0] * V
    doc_freq = [0] * V
    grand = 0.0
    n = 0
    g = prefix_map.get

    for toks in iter_verses(db, books):
        if prefix_map:
            toks = [g(t, t) for t in toks]
        ids = [word_id[w] for w in toks if w in word_id]
        if len(ids) < 2:
            continue
        n += 1
        for i in set(ids):
            doc_freq[i] += 1
        norm = 1.0
        if length_norm_b > 0 and avg_len > 0:
            norm = 1.0 - length_norm_b + length_norm_b * (len(ids) / avg_len)
        local: dict[tuple[int, int], float] = {}
        L = len(ids)
        for i, a in enumerate(ids):
            hi = min(L, i + window + 1)
            for j in range(i + 1, hi):
                b = ids[j]
                if a == b:
                    continue
                k = (a, b) if a < b else (b, a)
                local[k] = local.get(k, 0.0) + 1.0 / (j - i)
        for (a, b), w in local.items():
            w /= norm
            rw.add(a, b, w)
            rw.add(b, a, w)
            totals[a] += w
            totals[b] += w
            grand += 2 * w
    rw.flush()
    return [str(p) for p in rw.runs], totals, doc_freq, grand, n


def pass2_parallel(db: str, books: list[int], word_id: dict[str, int],
                   prefix_map: dict[str, str], window: int, length_norm_b: float,
                   avg_len: float, tmpdir: Path, max_pairs: int, workers: int):
    """Shard pass 2 across processes.

    Sharding is by book, so no verse is split and no window crosses a shard
    boundary — the result is identical to the serial path up to float ordering.
    """
    import multiprocessing as mp

    # Interleave books so each shard gets a mix of large and small works,
    # rather than one worker inheriting all of Halacha.
    shards: list[list[int]] = [[] for _ in range(workers)]
    for i, b in enumerate(books):
        shards[i % workers].append(b)

    args = [(db, sh, word_id, prefix_map, window, length_norm_b, avg_len,
             str(tmpdir), max_pairs // workers, i)
            for i, sh in enumerate(shards) if sh]

    V = len(word_id)
    totals = [0.0] * V
    doc_freq = [0] * V
    grand = 0.0
    runs: list[Path] = []
    n_tot = 0
    done = 0
    with mp.Pool(len(args)) as pool:
        for rs, tot, df, gr, n in pool.imap_unordered(_shard_worker, args):
            runs += [Path(p) for p in rs]
            for i, v in enumerate(tot):
                if v:
                    totals[i] += v
            for i, v in enumerate(df):
                if v:
                    doc_freq[i] += v
            grand += gr
            n_tot += n
            done += 1
            print(f"    shard {done}/{len(args)} done — {n:,} units, "
                  f"{len(rs)} runs", flush=True)
    return runs, totals, grand, doc_freq, n_tot


def pass2_cooccur(db: str, books: list[int], word_id: dict[str, int],
                  prefix_map: dict[str, str], window: int, length_norm_b: float,
                  avg_len: float, tmpdir: Path, max_pairs: int):
    """Emit distance-weighted pairs to sorted runs on disk.

    Semantics are identical to build_index.count_cooccurrences — verse-bounded
    windows, harmonic 1/d weighting, BM25 length normalization — so results are
    comparable. The only difference is where the accumulator lives.
    """
    rw = RunWriter(tmpdir, max_pairs)
    V = len(word_id)
    totals = [0.0] * V
    doc_freq = [0] * V
    grand = 0.0
    n = 0
    g = prefix_map.get

    for toks in iter_verses(db, books):
        if prefix_map:
            toks = [g(t, t) for t in toks]
        ids = [word_id[w] for w in toks if w in word_id]
        if len(ids) < 2:
            continue
        n += 1
        if n % 500000 == 0:
            print(f"    ... {n:,} verses, {rw.spilled + len(rw.buf):,} pairs, "
                  f"{len(rw.runs)} runs", flush=True)

        for i in set(ids):
            doc_freq[i] += 1

        norm = 1.0
        if length_norm_b > 0 and avg_len > 0:
            norm = 1.0 - length_norm_b + length_norm_b * (len(ids) / avg_len)

        local: dict[tuple[int, int], float] = {}
        L = len(ids)
        for i, a in enumerate(ids):
            hi = min(L, i + window + 1)
            for j in range(i + 1, hi):
                b = ids[j]
                if a == b:
                    continue
                k = (a, b) if a < b else (b, a)
                local[k] = local.get(k, 0.0) + 1.0 / (j - i)

        for (a, b), w in local.items():
            w /= norm
            # Both directions, matching build_index's symmetric accumulation.
            rw.add(a, b, w)
            rw.add(b, a, w)
            totals[a] += w
            totals[b] += w
            grand += 2 * w

    rw.flush()
    return rw.runs, totals, grand, doc_freq, n


# ---------------------------------------------------------------------------
# Streaming score + prune
# ---------------------------------------------------------------------------


def score_streaming(runs: list[Path], totals: list[float], grand: float,
                    topk: int, min_cooc: float, shift: float
                    ) -> dict[int, list[tuple[int, float]]]:
    """PMI-score and top-K prune while streaming the merge.

    Because the merge yields keys in (a, b) order, one word's entire row
    arrives contiguously — so only that row is ever in memory, and it can be
    pruned to top-K before moving on. This is what keeps the whole pipeline
    bounded rather than just the counting stage.

    Scoring math is identical to build_index.score_ppmi (PPMI, alpha=0.75
    context smoothing). Kept as a separate function only because that one
    consumes a materialized dict.
    """
    alpha = 0.75
    p_ctx = [(t / grand) ** alpha if t > 0 else 0.0 for t in totals]
    out: dict[int, list[tuple[int, float]]] = {}
    cur_a = -1
    row: list[tuple[int, float]] = []
    log2 = math.log2

    def finish(a: int, row: list[tuple[int, float]]) -> None:
        if not row:
            return
        row.sort(key=lambda t: -t[1])
        out[a] = row[:topk]

    for a, b, c in merge_runs(runs):
        if a != cur_a:
            finish(cur_a, row)
            cur_a, row = a, []
        if c < min_cooc:
            continue
        ta = totals[a]
        if ta <= 0 or p_ctx[b] <= 0:
            continue
        pmi = log2((c / grand) / ((ta / grand) * p_ctx[b])) - shift
        if pmi > 0:
            row.append((b, pmi))
    finish(cur_a, row)
    return out


# ---------------------------------------------------------------------------


def _measure(con: sqlite3.Connection, books: list[int]) -> tuple[int, int]:
    """(lines, chars) over a book list, chunked to stay under SQLite's
    variable/expression limits on the 7,000-book selections."""
    lines = chars = 0
    for i in range(0, len(books), 900):
        ids = ",".join(str(b) for b in books[i:i + 900])
        r = con.execute(
            f"select count(*), coalesce(sum(charCount),0) from line "
            f"where bookId in ({ids}) and heRef is not null").fetchone()
        lines += r[0]
        chars += r[1]
    return lines, chars


def cmd_list(db: str) -> None:
    """Show every corpus in BOTH modes.

    The base-only column is the point of this listing: most categories are
    overwhelmingly commentary, so the default (all books) measures something
    quite different from what the corpus name suggests.
    """
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    print(f"{'corpus':<12s} {'books':>6s} {'chars':>14s} | "
          f"{'base':>5s} {'base chars':>13s} {'base%':>6s}   desc")
    print("-" * 92)
    for name, spec in CORPORA.items():
        allb = resolve_books(con, name)
        base = resolve_books(con, name, True)
        if not allb:
            continue
        _, ca = _measure(con, allb)
        cb = _measure(con, base)[1] if base else 0
        pct = f"{cb / ca * 100:.0f}%" if ca else "-"
        print(f"{name:<12s} {len(allb):>6,d} {ca:>14,d} | "
              f"{len(base):>5,d} {cb:>13,d} {pct:>6s}   {spec['desc']}")
    con.close()


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default="tanach", choices=list(CORPORA))
    ap.add_argument("--base-only", action="store_true",
                    help="only isBaseBook=1 — excludes commentaries, which "
                         "dominate most categories (משנה is 96%% commentary)")
    ap.add_argument("--list", action="store_true", help="list corpora and exit")
    ap.add_argument("--window", type=int, default=4)
    ap.add_argument("--topk", type=int, default=200)
    ap.add_argument("--min-count", type=int, default=3)
    ap.add_argument("--min-cooc", type=float, default=1.0)
    ap.add_argument("--shift", type=float, default=0.0)
    ap.add_argument("--length-norm-b", type=float, default=0.75)
    ap.add_argument("--strip-prefixes", action=argparse.BooleanOptionalAction,
                    default=True)
    ap.add_argument("--min-stem-freq", type=int, default=5)
    ap.add_argument("--stem-ratio", type=float, default=0.25)
    ap.add_argument("--max-pairs", type=int, default=12_000_000,
                    help="pairs held in memory before spilling a run "
                         "(~65 B each, so 12M ~= 780 MB)")
    ap.add_argument("--workers", type=int, default=0,
                    help="parallel pass-2 shards (0/1 = serial). Sharding is by "
                         "book, so results are exact, not approximate")
    ap.add_argument("--rebuild-vocab", action="store_true",
                    help="ignore the cached pass-1 vocabulary")
    ap.add_argument("--db", default=SEFORIM_DB)
    ap.add_argument("--out", default=None)
    ap.add_argument("--keep-tmp", action="store_true")
    args = ap.parse_args()

    if args.list:
        cmd_list(args.db)
        return

    out = Path(args.out or f"index-{args.corpus}")
    t0 = time.perf_counter()

    con = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    books = resolve_books(con, args.corpus, args.base_only)
    con.close()
    print(f"corpus '{args.corpus}'{' [base only]' if args.base_only else ''}: "
          f"{len(books):,} books")

    # Pass 1 is a full scan just to count, so cache it. On a 350M-token corpus
    # it costs ~20 minutes, and re-running it after a pass-2 crash is pure waste.
    cache = out.parent / f".vocab-{args.corpus}{'-base' if args.base_only else ''}" \
                         f"-mc{args.min_count}-sp{int(args.strip_prefixes)}" \
                         f"-msf{args.min_stem_freq}.json"
    if cache.exists() and not args.rebuild_vocab:
        print(f"pass 1/2  vocabulary (cached: {cache.name}) ...", flush=True)
        d = json.loads(cache.read_text(encoding="utf-8"))
        vocab, counts, pmap = d["vocab"], d["counts"], d["pmap"]
        n_tok, n_verse = d["n_tok"], d["n_verse"]
        word_id = {w: i for i, w in enumerate(vocab)}
    else:
        print("pass 1/2  vocabulary ...", flush=True)
        t = time.perf_counter()
        vocab, word_id, counts, pmap, n_tok, n_verse = pass1_vocab(
            args.db, books, args.min_count, args.strip_prefixes,
            args.min_stem_freq, args.stem_ratio)
        cache.write_text(json.dumps(
            {"vocab": vocab, "counts": counts, "pmap": pmap,
             "n_tok": n_tok, "n_verse": n_verse}, ensure_ascii=False),
            encoding="utf-8")
        print(f"  ({time.perf_counter() - t:.0f}s, cached)")
    avg_len = n_tok / max(1, n_verse)
    print(f"  {n_verse:,} units, {n_tok:,} tokens, {len(vocab):,} vocab, "
          f"{len(pmap):,} folded")

    tmpdir = Path(tempfile.mkdtemp(prefix="wassoc_", dir=out.parent))
    try:
        print(f"pass 2/2  co-occurrence -> {tmpdir.name} "
              f"({args.workers or 1} worker(s)) ...", flush=True)
        t = time.perf_counter()
        if args.workers and args.workers > 1:
            runs, totals, grand, doc_freq, n2 = pass2_parallel(
                args.db, books, word_id, pmap, args.window, args.length_norm_b,
                avg_len, tmpdir, args.max_pairs, args.workers)
        else:
            runs, totals, grand, doc_freq, n2 = pass2_cooccur(
                args.db, books, word_id, pmap, args.window, args.length_norm_b,
                avg_len, tmpdir, args.max_pairs)
        spill = sum(p.stat().st_size for p in runs) / 1e9
        print(f"  {len(runs)} runs, {spill:.2f} GB spilled "
              f"({time.perf_counter() - t:.0f}s)")

        print("merge + PMI + prune ...", flush=True)
        t = time.perf_counter()
        assoc = score_streaming(runs, totals, grand, args.topk,
                                args.min_cooc, args.shift)
        kept = sum(len(r) for r in assoc.values())
        print(f"  {kept:,} edges, {len(assoc):,} words "
              f"({time.perf_counter() - t:.0f}s)")

        write_csr(out, vocab, counts, assoc, {
            "corpus": args.corpus,
            "corpus_desc": CORPORA[args.corpus]["desc"],
            "base_only": args.base_only,
            "books": len(books),
            "verses": n_verse,
            "tokens": n_tok,
            "window": args.window,
            "topk": args.topk,
            "min_count": args.min_count,
            "min_cooc": args.min_cooc,
            "shift": args.shift,
            "length_norm_b": args.length_norm_b,
            "strip_prefixes": args.strip_prefixes,
            "min_stem_freq": args.min_stem_freq,
            "stem_ratio": args.stem_ratio,
            "folded_forms": len(pmap),
            "builder": "build_large.py",
            "build_seconds": round(time.perf_counter() - t0, 1),
        })
    finally:
        if not args.keep_tmp:
            shutil.rmtree(tmpdir, ignore_errors=True)

    mb = sum(f.stat().st_size for f in out.iterdir()) / 1e6
    print(f"\ndone in {time.perf_counter() - t0:.0f}s -> {out}  ({mb:.1f} MB)")


if __name__ == "__main__":
    main()
