"""
Improvement harness — measures BOTH axes that matter.

Why two axes
------------
Gold-set P@k answers "did the expected word appear?" It is blind to what appears
*alongside* it, and that is most of what a user perceives as quality. Measured on
the full corpus: only **6%** of the words shown in a `similar()` list are real
dictionary headwords. A user reading `שבת -> דכשמקדשינ, מבדיליננ, במייאנצא` sees a
broken feature, whatever P@20 says.

So every candidate improvement is scored on:

  P@k / MRR / recall   the gold set (evaluate.py) — does it find the right word
  clean%               fraction of SHOWN results that are real lexicon words
  ood%                 fraction that look like Aramaic / glued / foreign junk

`clean%` uses Dictionary.db headword membership. Caveat, stated plainly: the gold
set is drawn from the same DB's `link` table, so a filter that keeps only
headwords keeps every possible gold answer while dropping non-answers — that
inflates P@k mechanically. The honest reading is: **trust clean% as the quality
signal, and treat a filtered P@k as an upper bound, not a discovery.**

Usage:
    python improve.py --index assoc-tanach.db
    python improve.py --index assoc-full.db --limit 60
"""

from __future__ import annotations

import argparse
import re
import statistics
import sys
from pathlib import Path

from assoc_db import open_index
from evaluate import evaluate, load_gold, random_baseline
from lexicon import (LexiconView, aramaic_forms, build_lemma_map,
                     dict_headwords, known_words, lexical_forms)

# Terms a user of this corpus would plausibly search. A Talmudic engine gets
# Hebrew, Aramaic, and mixed queries, so the probe set includes all three.
PROBES = ["שבת", "מזבח", "זהב", "מלכ", "תפלה", "חכמה", "צדקה", "טהרה",
          "ברכה", "אור", "צדיק", "מלחמה", "כהנ", "לחמ", "שמחה", "אמת"]

# Aramaic probes — this corpus is substantially Aramaic and the engine must serve
# those queries as first-class, not as an afterthought.
PROBES_ARAMAIC = ["מלכא", "גברא", "מילתא", "עלמא", "רבננ", "אבוה", "ארעא", "יומא"]

# Glued-token detector. This is the ACTUAL junk in this corpus: two words fused
# by print/OCR into one token (`דכשמקדשינ`, `חייבלקרוע`). It is NOT about
# Aramaic — `דאבא` is a legitimate Aramaic form and must never be penalized.
_LONG_UNKNOWN = 10


def looks_glued(w: str, known: frozenset[str]) -> bool:
    """'No lexicon knows this AND it is long enough to be two words fused.'

    Deliberately conservative: only unknown words, only long ones. An unknown
    5-letter Aramaic form is far more likely to be a real word missing from the
    lexicon than a glued pair, so it is not counted.
    """
    if w in known:
        return False
    return len(w) >= _LONG_UNKNOWN


def shown_quality(ix, mode: str, n: int = 20, probes=None) -> dict:
    """What fraction of the results a user would SEE is a recognizable word?

    `known` is the UNION of every lexicon — Hebrew, Aramaic, inflected forms.
    An Aramaic result counts as clean, because in a Talmudic corpus it is.
    """
    known = known_words()
    aram = aramaic_forms()
    clean = glued = total = aramaic_hits = 0
    empty = 0
    for w in (probes or PROBES):
        res = (ix.similar(w, n) if mode == "similar" else ix.neighbors(w, n))
        if not res:
            empty += 1
            continue
        for x, _ in res:
            total += 1
            if x in known:
                clean += 1
                if x in aram:
                    aramaic_hits += 1
            elif looks_glued(x, known):
                glued += 1
    return {
        "clean": clean / total if total else 0.0,
        "ood": glued / total if total else 0.0,
        "aramaic": aramaic_hits / total if total else 0.0,
        "shown": total,
        "empty_probes": empty,
    }


EMPTY_Q = {"clean": 0.0, "ood": 0.0, "aramaic": 0.0, "shown": 0}


def row(label: str, r: dict | None, q: dict, k: int) -> str:
    nums = (f"{r[f'P@{k}']:>7.4f} {r['MRR']:>7.4f} {r[f'recall@{k}']:>7.4f}"
            if r else f"{'—':>7s} {'—':>7s} {'—':>7s}")
    return (f"  {label:<24s} {nums}  {q['clean']*100:>6.0f}% "
            f"{q['ood']*100:>5.0f}% {q['aramaic']*100:>6.0f}%  {q['shown']:>5d}")


def header(k: int) -> None:
    print(f"  {'variant':<24s} {'P@'+str(k):>7s} {'MRR':>7s} {'recall':>7s}  "
          f"{'known':>7s} {'glued':>5s} {'aram':>6s}  {'shown':>5s}")
    print("  " + "-" * 82)


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--index", default="assoc-tanach.db")
    ap.add_argument("-k", type=int, default=20)
    ap.add_argument("--limit", type=int, default=150)
    ap.add_argument("--min-freq", type=int, default=10)
    ap.add_argument("--show", action="store_true",
                    help="print the actual result lists per variant")
    ap.add_argument("--aramaic", action="store_true",
                    help="also report on Aramaic probe terms")
    a = ap.parse_args()

    base = Path(__file__).parent
    ix = open_index(base / a.index)
    vocab, counts = ix.words, ix.counts

    heads, lexf, aram, known = (dict_headwords(), lexical_forms(),
                                aramaic_forms(), known_words())
    V = set(vocab)
    print(f"index      : {a.index}")
    print(f"  {ix.meta['vocab_size']:,} words, {ix.meta['edge_count']:,} edges, "
          f"window={ix.meta.get('window')}")
    print(f"  vocabulary coverage — Dictionary.db {len(V & heads)/len(V)*100:>3.0f}%"
          f"   lexical.db {len(V & lexf)/len(V)*100:>3.0f}%"
          f"   Aramaic {len(V & aram)/len(V)*100:>3.0f}%"
          f"   ANY {len(V & known)/len(V)*100:>3.0f}%")

    lemmas = build_lemma_map(vocab, counts)
    print(f"lexicon    : {len(known):,} known forms; "
          f"{len(lemmas):,} of this vocabulary is lemmatizable "
          f"({len(lemmas)/len(V)*100:.0f}%)")

    gold = load_gold("synonym", V, a.min_freq, dict(zip(vocab, counts)))
    print(f"gold       : synonym, {len(gold):,} words (min_freq={a.min_freq})\n")

    # PROBE SURVIVAL — check this before reading any score.
    #
    # A lemmatization bug once folded 12 of 16 probe words out of the vocabulary
    # entirely. `similar(שבת)` returned NOTHING, yet gold-set P@20 went UP,
    # because only the easier surviving words were still being scored. No
    # accuracy metric can see that failure; only asking "is the user's own query
    # term still in the index?" can.
    allp = PROBES + PROBES_ARAMAIC
    missing = [w for w in allp if w not in ix.word_id]
    if missing:
        print(f"  !! {len(missing)}/{len(allp)} probe terms ABSENT from the "
              f"vocabulary: {' '.join(missing)}")
        print("     A query term that is not in the index returns nothing — treat "
              "any score below as unreliable.\n")
    else:
        print(f"  probe survival: {len(allp)}/{len(allp)} query terms present\n")

    probe_sets = [("Hebrew probes", PROBES)]
    if a.aramaic:
        probe_sets.append(("Aramaic probes", PROBES_ARAMAIC))

    for mode in ("assoc", "similar"):
        for pname, probes in probe_sets:
            print(f"=== {mode}  /  {pname} ===")
            header(a.k)
            print(row("random baseline", random_baseline(ix, gold, a.k),
                      EMPTY_Q, a.k))
            for label, m in (("no lexicon (current)", "off"),
                             ("known-word boost x2", "boost"),
                             ("known-word filter", "filter")):
                v = LexiconView(ix, m)
                r = evaluate(v, gold, a.k, mode, a.limit)
                q = shown_quality(v, mode, probes=probes)
                print(row(label, r, q, a.k))
            print()

    if a.show:
        print("=== what the user actually sees (similar, top 8) ===")
        for m in ("off", "filter"):
            print(f"\n--- lexicon={m} ---")
            v = LexiconView(ix, m)
            for w in PROBES[:8] + (PROBES_ARAMAIC[:4] if a.aramaic else []):
                res = [x for x, _ in v.similar(w, 8)]
                print(f"  {w:<8s} {' '.join(res) if res else '(none)'}")

    print("\nNOTE  'known' counts Hebrew AND Aramaic — this is a Talmudic corpus,")
    print("      so an Aramaic result is a correct result, not noise. 'glued' is")
    print("      the real defect: long unknown tokens that are two words fused.")
    print("      A filtered P@k is an upper bound, not a discovery — gold answers")
    print("      are themselves lexicon words, so filtering cannot lose them.")
    ix.close()


if __name__ == "__main__":
    main()
