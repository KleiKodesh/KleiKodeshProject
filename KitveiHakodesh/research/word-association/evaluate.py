"""
Evaluation harness for the association index.

Why this exists
---------------
Every claim in FINDINGS.md up to now was judged by reading Hebrew word lists.
That does not scale, and it is far too easy to see structure in noisy output.
This scores the index against a gold set drawn from a source that knows nothing
about co-occurrence, so a good number cannot be an artifact of the method.

The gold set
------------
The project's own dictionary DB carries a typed `link` table built by hand from
lexicographic sources. Two relation kinds are usable here:

    נרדף   synonym       ~4,000 pairs with both sides in the Tanach vocabulary
    ניגוד  antonym         ~210 pairs

Antonyms belong in a *distributional* gold set on purpose. The distributional
hypothesis predicts that opposites share contexts (both sides of a contrast
appear in the same frames), so a distributional index should rank them close.
That is a real prediction, and it is worth testing separately from synonymy.

Two relation kinds are deliberately EXCLUDED:

    כתיב    spelling variants — mostly orthographic, not semantic. Scoring
            against them would reward the tokenizer, not the associations.
    נגזרת   derivations — same root, so a morphology-aware build would score
            well on them by construction. Circular once item 1 lands.

Metrics
-------
    P@k    fraction of gold partners appearing in the top-k
    MRR    mean reciprocal rank of the first gold partner found
    recall fraction of query words with ANY gold partner in the top-k

Coverage is reported alongside every number. A high score over 40 evaluable
words says much less than a mediocre score over 800, and the two are easy to
confuse if coverage is left out.

Usage:
    python evaluate.py                          score the current index
    python evaluate.py --index index-morph      score an alternative build
    python evaluate.py --compare index index-morph
    python evaluate.py --relation antonym
"""

from __future__ import annotations

import argparse
import json
import re
import sqlite3
import sys
import time
from collections import defaultdict
from pathlib import Path

from assoc_db import open_index

DICT_DB = (
    "C:/Users/Admin/AppData/Local/KleiKodesh/KitveiHakodesh"
    "/dictionary/KitveiHakodesh_dictionary.db"
)

# Kind ids in the dictionary's link_kind table.
RELATIONS = {"synonym": 1, "antonym": 4, "seealso": 3}

FINALS = str.maketrans("ךםןףץ", "כמנפצ")
_NIKUD_RE = re.compile(r"[\u0591-\u05BD\u05BF-\u05C7]")


def normalize(w: str) -> str:
    """Match the tokenizer's normalization so dictionary headwords and index
    vocabulary meet in the same alphabet."""
    return _NIKUD_RE.sub("", w).translate(FINALS).strip()


# ---------------------------------------------------------------------------
# Gold set
# ---------------------------------------------------------------------------


def load_gold(
    relation: str, vocab: set[str], min_freq: int, counts: dict[str, int]
) -> dict[str, set[str]]:
    """Gold partners per word, restricted to pairs where BOTH sides are in the
    index vocabulary and clear the frequency floor.

    The frequency floor is not a way to flatter the numbers — it is the
    difference between measuring the method and measuring corpus sparsity. A
    word occurring 3 times has no estimable context distribution, so its rank
    is noise regardless of how good the scoring function is. Report the floor
    with the result and vary it to see the effect (`--min-freq`).
    """
    con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
    kind = RELATIONS[relation]
    rows = con.execute(
        """select w1.headword, w2.headword
           from link l
           join word w1 on w1.id = l.word_id
           join word w2 on w2.id = l.target_id
           where l.kind_id = ?""",
        (kind,),
    )
    gold: dict[str, set[str]] = defaultdict(set)
    for a, b in rows:
        a, b = normalize(a), normalize(b)
        if a == b or a not in vocab or b not in vocab:
            continue
        if counts.get(a, 0) < min_freq or counts.get(b, 0) < min_freq:
            continue
        # The relation is symmetric in meaning even where the table stores it
        # one-way, so score it both directions.
        gold[a].add(b)
        gold[b].add(a)
    con.close()
    return dict(gold)


# ---------------------------------------------------------------------------
# Scoring
# ---------------------------------------------------------------------------


def evaluate(
    ix,
    gold: dict[str, set[str]],
    k: int,
    mode: str,
    limit: int = 0,
) -> dict:
    """Score `mode` ('similar' | 'assoc') over the gold set.

    Reported per-query-word, then averaged — not pooled over pairs. Pooling
    would let a handful of words with many gold partners dominate the number.
    """
    words = sorted(gold, key=lambda w: -len(gold[w]))
    if limit:
        words = words[:limit]

    p_at_k: list[float] = []
    rr: list[float] = []
    hits = 0
    evaluated = 0
    t0 = time.perf_counter()

    for w in words:
        ranked = (ix.similar(w, k) if mode == "similar" else ix.neighbors(w, k))
        if not ranked:
            continue
        evaluated += 1
        got = [r for r, _ in ranked]
        partners = gold[w]
        found = [i for i, r in enumerate(got) if r in partners]
        # P@k is scored against min(|gold|, k): a word with 1 gold partner can
        # never exceed 1/k, and averaging that in raw form would make P@k a
        # measure of how many partners the dictionary happens to list.
        p_at_k.append(len(found) / min(len(partners), k))
        rr.append(1.0 / (found[0] + 1) if found else 0.0)
        if found:
            hits += 1

    n = max(1, len(p_at_k))
    return {
        "mode": mode,
        "k": k,
        "gold_words": len(words),
        "evaluated": evaluated,
        f"P@{k}": sum(p_at_k) / n,
        "MRR": sum(rr) / n,
        f"recall@{k}": hits / n,
        "seconds": round(time.perf_counter() - t0, 1),
    }


def random_baseline(ix, gold: dict[str, set[str]], k: int,
                    seed: int = 7) -> dict:
    """What the metrics look like when the ranking carries no information.

    Without this the absolute numbers are unreadable: P@20 of 0.05 sounds poor
    but is two orders of magnitude above chance on a 12,638-word vocabulary.
    """
    import random

    rng = random.Random(seed)
    V = len(ix.words)
    p_at_k, rr, hits = [], [], 0
    for w in gold:
        partners = gold[w]
        got = [ix.words[rng.randrange(V)] for _ in range(k)]
        found = [i for i, r in enumerate(got) if r in partners]
        p_at_k.append(len(found) / min(len(partners), k))
        rr.append(1.0 / (found[0] + 1) if found else 0.0)
        hits += bool(found)
    n = max(1, len(p_at_k))
    return {"mode": "random", "k": k, "gold_words": len(gold), "evaluated": len(gold),
            f"P@{k}": sum(p_at_k) / n, "MRR": sum(rr) / n,
            f"recall@{k}": hits / n, "seconds": 0.0}


def print_row(label: str, r: dict, k: int) -> None:
    print(f"  {label:<22s} {r[f'P@{k}']:>7.4f}  {r['MRR']:>7.4f}  "
          f"{r[f'recall@{k}']:>7.4f}   {r['evaluated']:>5d}   {r['seconds']:>5.1f}s")


def header(k: int) -> None:
    print(f"  {'run':<22s} {'P@'+str(k):>7s}  {'MRR':>7s}  "
          f"{'recall':>7s}   {'words':>5s}   {'time':>6s}")
    print("  " + "-" * 66)


# ---------------------------------------------------------------------------


def run_one(index_dir: Path, relation: str, k: int, min_freq: int,
            limit: int, with_baseline: bool, label: str = "") -> None:
    ix = open_index(index_dir)
    try:
        vocab = set(ix.words)
        counts = dict(zip(ix.words, ix.counts))
        gold = load_gold(relation, vocab, min_freq, counts)
        pairs = sum(len(v) for v in gold.values()) // 2

        print(f"\nindex    : {index_dir}")
        print(f"  {ix.meta['vocab_size']:,} words, {ix.meta['edge_count']:,} edges"
              f"  (window={ix.meta.get('window')}, b={ix.meta.get('length_norm_b')})")
        print(f"gold     : {relation}, {len(gold):,} words / {pairs:,} pairs"
              f"  (min_freq={min_freq})\n")

        header(k)
        if with_baseline:
            print_row("random baseline", random_baseline(ix, gold, k), k)
        for mode in ("assoc", "similar"):
            print_row(label + mode, evaluate(ix, gold, k, mode, limit), k)
    finally:
        ix.close()


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--index", default="index", help="index directory to score")
    ap.add_argument("--compare", nargs="+", metavar="DIR",
                    help="score several index directories side by side")
    ap.add_argument("--relation", choices=list(RELATIONS), default="synonym")
    ap.add_argument("-k", type=int, default=20)
    ap.add_argument("--min-freq", type=int, default=10,
                    help="skip gold pairs whose words are rarer than this")
    ap.add_argument("--limit", type=int, default=400,
                    help="cap evaluated words (similar() is the slow path); 0 = all")
    ap.add_argument("--no-baseline", action="store_true")
    a = ap.parse_args()

    base = Path(__file__).parent
    dirs = [base / d for d in (a.compare or [a.index])]
    for d in dirs:
        run_one(d, a.relation, a.k, a.min_freq, a.limit, not a.no_baseline)
    print()


if __name__ == "__main__":
    main()
