"""
Hebrew <-> Aramaic bridge lexicon from the Targumim.

The idea (and why it is nearly free)
------------------------------------
seforim.db already contains verse-aligned parallel text: every Tanach verse has
its Aramaic rendering in Targum Onkelos (Torah) and Targum Yonatan (Neviim),
aligned by heRef — `תרגום אונקלוס על בראשית א, א` renders `בראשית א, א`.

That is a parallel corpus of ~14k verse pairs, and word alignment over it yields
Hebrew<->Aramaic translation pairs that co-occurrence inside a mixed corpus can
only find by accident. This is the documented approach for exactly this language
pair (ACL 2020 lt4hala, "Automatic Construction of Aramaic-Hebrew Translation
Lexicon"), whose key finding — string-similarity filtering beats the aligner's
own scores — is used here as a scoring bonus for cognates.

Method
------
Dice over verse-level presence, plus a cognate bonus:

    dice(h, a)  = 2 * verses_containing_both / (df_h + df_a)
    bonus(h, a) = shared-bigram Dice over the letters   (cognates: מלכ/מלכא)
    score       = dice * (1 + bonus)

Extraction keeps a pair when it is the MUTUAL best (h's best a AND a's best h)
and clears a score floor. Mutual-best is the classic cheap high-precision rule.

Output: targum_bridge.csv  (aramaic, hebrew, score, n_verses)

Validation: scored against the hand-made Aramaic->Hebrew CSV (1,309 pairs) where
both sides are in vocabulary, plus spot checks. That CSV is independent of this
corpus, so agreement is meaningful.

Usage:  python targum_bridge.py [--min-score 0.3] [--min-verses 3]
"""

from __future__ import annotations

import argparse
import csv
import re
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from build_index import clean_verse, tokenize  # the same tokenizer as the index
from lexicon import aramaic_to_hebrew

SEFORIM = "C:/ProgramData/otzaria/books/seforim.db"
OUT = Path(__file__).parent / "targum_bridge.csv"

# Trailing "<chapter>, <verse>" — the part shared between a base ref and its
# targum ref. Hebrew numerals, e.g. "בראשית קי, א".
_REF_TAIL = re.compile(r"([\u05D0-\u05EA]{1,4}, [\u05D0-\u05EA]{1,4})\s*$")


def ref_tail(heref: str) -> str | None:
    m = _REF_TAIL.search(heref or "")
    return m.group(1) if m else None


def load_pairs(db: str) -> list[tuple[list[str], list[str]]]:
    """Aligned (hebrew_tokens, aramaic_tokens) verse pairs across all targumim."""
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)

    # Targum book -> the base book it renders, by title suffix match.
    base = {t: i for i, t in con.execute(
        "select id, title from book where id between 1 and 39")}
    targums = []
    for bid, title in con.execute(
            "select id, title from book where title like 'תרגום %על %'"):
        for bt, bi in base.items():
            if title.endswith(" " + bt):
                targums.append((bid, bi))
                break

    pairs: list[tuple[list[str], list[str]]] = []
    for tid, hid in targums:
        heb = {}
        for ref, content in con.execute(
                "select heRef, content from line where bookId=? and heRef is not null",
                (hid,)):
            t = ref_tail(ref)
            if t:
                heb[t] = tokenize(clean_verse(content))
        n = 0
        for ref, content in con.execute(
                "select heRef, content from line where bookId=? and heRef is not null",
                (tid,)):
            t = ref_tail(ref)
            h = heb.get(t) if t else None
            if h:
                a = tokenize(clean_verse(content))
                if a:
                    pairs.append((h, a))
                    n += 1
    con.close()
    return pairs


def letter_bigram_dice(x: str, y: str) -> float:
    """Cognate signal: shared letter bigrams. מלכ/מלכא share strongly."""
    if len(x) < 2 or len(y) < 2:
        return 1.0 if x == y else 0.0
    bx = {x[i:i + 2] for i in range(len(x) - 1)}
    by = {y[i:i + 2] for i in range(len(y) - 1)}
    inter = len(bx & by)
    return 2 * inter / (len(bx) + len(by)) if inter else 0.0


def build_bridge(pairs, min_score: float, min_verses: int,
                 max_verse_len: int = 60):
    """Mutual-best Dice alignment with a cognate bonus."""
    df_h: Counter[str] = Counter()
    df_a: Counter[str] = Counter()
    cooc: dict[str, Counter[str]] = defaultdict(Counter)

    for h_toks, a_toks in pairs:
        # Very long verses generate quadratic pair noise for little signal.
        if len(h_toks) > max_verse_len or len(a_toks) > max_verse_len:
            continue
        hs, as_ = set(h_toks), set(a_toks)
        for h in hs:
            df_h[h] += 1
        for a in as_:
            df_a[a] += 1
        for h in hs:
            for a in as_:
                cooc[h][a] += 1

    # Score every candidate; keep each side's best partner.
    best_for_h: dict[str, tuple[str, float, int]] = {}
    best_for_a: dict[str, tuple[str, float, int]] = {}
    for h, row in cooc.items():
        for a, n in row.items():
            if n < min_verses:
                continue
            dice = 2 * n / (df_h[h] + df_a[a])
            score = dice * (1.0 + letter_bigram_dice(h, a))
            if score < min_score:
                continue
            if h not in best_for_h or score > best_for_h[h][1]:
                best_for_h[h] = (a, score, n)
            if a not in best_for_a or score > best_for_a[a][1]:
                best_for_a[a] = (h, score, n)

    out = []
    for h, (a, score, n) in best_for_h.items():
        if best_for_a.get(a, ("",))[0] == h:          # mutual best
            out.append((a, h, score, n))
    out.sort(key=lambda t: -t[2])
    return out


def validate(bridge, verbose: bool = True) -> None:
    """Score against the independent hand-made Aramaic->Hebrew CSV."""
    gold = aramaic_to_hebrew()
    got = {a: h for a, h, _, _ in bridge}
    common = set(gold) & set(got)
    if not common:
        print("validation: no overlap with the hand-made CSV")
        return
    agree = sum(1 for a in common if gold[a] == got[a])
    print(f"validation vs hand-made CSV: {len(common)} shared Aramaic forms, "
          f"{agree} agree ({agree / len(common) * 100:.0f}%)")
    if verbose:
        wrong = [(a, got[a], gold[a]) for a in common if gold[a] != got[a]][:8]
        for a, mine, ref in wrong:
            print(f"    disagree: {a}  bridge={mine}  csv={ref}")


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--min-score", type=float, default=0.3)
    ap.add_argument("--min-verses", type=int, default=3)
    a = ap.parse_args()

    print("loading aligned verse pairs ...")
    pairs = load_pairs(SEFORIM)
    print(f"  {len(pairs):,} aligned verses")

    bridge = build_bridge(pairs, a.min_score, a.min_verses)
    print(f"  {len(bridge):,} mutual-best pairs (score >= {a.min_score}, "
          f"verses >= {a.min_verses})")

    validate(bridge)

    with OUT.open("w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["aramaic", "hebrew", "score", "verses"])
        for row in bridge:
            w.writerow([row[0], row[1], f"{row[2]:.4f}", row[3]])
    print(f"-> {OUT.name}")

    print("\nspot checks (top by score):")
    for a_, h, s, n in bridge[:15]:
        print(f"  {a_:<12s} <-> {h:<12s} {s:.3f}  ({n} verses)")


if __name__ == "__main__":
    main()
