"""
Query the association index.

This is the whole point of the design: at query time we do lookups, not
computation. `AssocIndex.neighbors()` is two array reads and a slice.

Usage:
  python query.py assoc  <word> [-n 20]      strongest associations of a word
  python query.py similar <word> [-n 20]     words with a similar association
                                             profile (second-order / distributional)
  python query.py expand "<query>" [-n 5]    query expansion, intersection-first
  python query.py bench                      timing
  python query.py demo                       a guided tour of all of the above
"""

from __future__ import annotations

import argparse
import json
import mmap
import struct
import sys
import time
from pathlib import Path

INDEX_DIR = Path(__file__).parent / "index"
FINALS = str.maketrans("ךםןףץ", "כמנפצ")


class AssocIndex:
    """Read-only CSR association index.

    offsets: uint32[V+1]              — offsets[i]..offsets[i+1] is word i's run
    edges:   (uint32 id, float32 w)[] — sorted by weight descending
    """

    EDGE = struct.Struct("<If")
    EDGE_SIZE = 8

    def __init__(self, index_dir: Path = INDEX_DIR):
        self.meta = json.loads((index_dir / "meta.json").read_text(encoding="utf-8"))
        v = json.loads((index_dir / "vocab.json").read_text(encoding="utf-8"))
        self.words: list[str] = v["words"]
        self.counts: list[int] = v["counts"]
        self.word_id = {w: i for i, w in enumerate(self.words)}

        self._foff = open(index_dir / "offsets.bin", "rb")
        self._fedg = open(index_dir / "edges.bin", "rb")
        self.offsets = mmap.mmap(self._foff.fileno(), 0, access=mmap.ACCESS_READ)
        self.edges = mmap.mmap(self._fedg.fileno(), 0, access=mmap.ACCESS_READ)

    def close(self) -> None:
        self.offsets.close(); self.edges.close()
        self._foff.close(); self._fedg.close()

    # -- the hot path ------------------------------------------------------

    def _slice(self, wid: int) -> tuple[int, int]:
        base = wid * 4
        start, end = struct.unpack_from("<II", self.offsets, base)
        return start, end

    def neighbors(self, word: str, n: int = 20) -> list[tuple[str, float]]:
        """Top-n associations. Because edges are sorted by weight descending we
        read only the first n entries and stop — never the whole run."""
        wid = self.word_id.get(word.translate(FINALS))
        if wid is None:
            return []
        start, end = self._slice(wid)
        end = min(end, start + n)
        unpack, W = self.EDGE.unpack_from, self.words
        return [
            (W[i], w)
            for i, w in (unpack(self.edges, o * self.EDGE_SIZE) for o in range(start, end))
        ]

    def profile(self, word: str, n: int = 300) -> dict[int, float]:
        """The word's association profile as a sparse id->weight map."""
        wid = self.word_id.get(word.translate(FINALS))
        if wid is None:
            return {}
        start, end = self._slice(wid)
        end = min(end, start + n)
        unpack = self.EDGE.unpack_from
        return dict(unpack(self.edges, o * self.EDGE_SIZE) for o in range(start, end))

    # -- second-order similarity ------------------------------------------

    def similar(self, word: str, n: int = 20, depth: int = 300) -> list[tuple[str, float]]:
        """Words whose association profiles resemble this word's.

        This is the "cat ≈ dog" step, done WITHOUT embeddings: two words are
        similar if their sparse profiles have high cosine overlap. We only
        consider candidates that share at least one association, which keeps
        this from being an all-pairs comparison.
        """
        target = self.profile(word, depth)
        if not target:
            return []
        wid = self.word_id[word.translate(FINALS)]
        tnorm = sum(v * v for v in target.values()) ** 0.5

        # Candidates = words reachable in two hops. Sharing no context word at
        # all means cosine 0, so there is nothing to gain from checking them.
        candidates: set[int] = set()
        for ctx in target:
            s, e = self._slice(ctx)
            e = min(e, s + depth)
            for o in range(s, e):
                candidates.add(self.EDGE.unpack_from(self.edges, o * self.EDGE_SIZE)[0])
        candidates.discard(wid)

        scored = []
        for cid in candidates:
            s, e = self._slice(cid)
            e = min(e, s + depth)
            dot = 0.0
            nrm = 0.0
            for o in range(s, e):
                i, w = self.EDGE.unpack_from(self.edges, o * self.EDGE_SIZE)
                nrm += w * w
                tw = target.get(i)
                if tw is not None:
                    dot += w * tw
            if dot > 0:
                scored.append((self.words[cid], dot / (tnorm * nrm ** 0.5)))
        scored.sort(key=lambda t: -t[1])
        return scored[:n]

    # -- query expansion ---------------------------------------------------

    def expand(self, query: str, per_term: int = 5, mode: str = "similar"
               ) -> tuple[list[str], list[tuple[str, float, str]]]:
        """Expand a multi-word query.

        mode='assoc'   — expand along first-order associations (co-occurrence).
                         Gives words that appear NEXT TO the query term.
        mode='similar' — expand along second-order similarity (profile overlap).
                         Gives words USED LIKE the query term. This is the
                         better default for search: a user typing מזבח wants
                         other cultic-object words, not the verbs around it.

        Terms that several query words agree on are boosted — agreement across
        query terms is a far stronger signal than any single association.

        Capping `per_term` is the latency control. Association lookup is ~6 µs;
        it is the downstream posting-list reads for each expanded term that cost,
        so the number of terms we emit is the number that matters.
        """
        terms = [t.translate(FINALS) for t in query.split() if t.strip()]
        known = [t for t in terms if t in self.word_id]
        if not known:
            return terms, []

        votes: dict[str, list[float]] = {}
        for t in known:
            src = (self.similar(t, per_term * 6) if mode == "similar"
                   else self.neighbors(t, per_term * 6))
            for w, weight in src:
                if w in known:
                    continue
                votes.setdefault(w, []).append(weight)

        scored = []
        for w, ws in votes.items():
            # Multiplicative agreement bonus: a term voted for by n query words
            # scales by n. Single-source terms keep their raw weight.
            score = (sum(ws) / len(ws)) * len(ws)
            tag = f"agreed by {len(ws)}" if len(ws) > 1 else "single"
            scored.append((w, score, tag))
        scored.sort(key=lambda t: -t[1])
        return terms, scored[: per_term * max(1, len(known))]


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def cmd_assoc(ix: AssocIndex, word: str, n: int) -> None:
    res = ix.neighbors(word, n)
    if not res:
        print(f"'{word}' not in vocabulary")
        return
    freq = ix.counts[ix.word_id[word.translate(FINALS)]]
    print(f"\n{word}  (appears {freq:,}x in Tanach)\n")
    for w, s in res:
        bar = "█" * int(s * 2.2)
        print(f"  {w:<14s} {s:6.2f}  {bar}")


def cmd_similar(ix: AssocIndex, word: str, n: int) -> None:
    t = time.perf_counter()
    res = ix.similar(word, n)
    el = (time.perf_counter() - t) * 1000
    if not res:
        print(f"'{word}' not in vocabulary")
        return
    print(f"\nwords with an association profile similar to '{word}'  ({el:.0f} ms)\n")
    for w, s in res:
        bar = "█" * int(s * 60)
        print(f"  {w:<14s} {s:6.3f}  {bar}")


def cmd_expand(ix: AssocIndex, query: str, n: int, mode: str = "similar") -> None:
    t0 = time.perf_counter()
    terms, exp = ix.expand(query, per_term=n, mode=mode)
    el = (time.perf_counter() - t0) * 1000
    print(f"\nquery: {query}   [mode={mode}, {el:.0f} ms]")
    print(f"terms: {terms}\n")
    if not exp:
        print("  (no expansion — none of the terms are in the vocabulary)")
        return
    print("  expanded to:")
    for w, s, src in exp:
        mark = " <--" if src != "single" else ""
        print(f"    {w:<14s} {s:6.2f}   {src}{mark}")


def cmd_bench(ix: AssocIndex) -> None:
    import random
    random.seed(7)
    sample = [ix.words[random.randrange(len(ix.words))] for _ in range(20000)]

    t = time.perf_counter()
    for w in sample:
        ix.neighbors(w, 20)
    el = time.perf_counter() - t
    print(f"\nneighbors(top-20)  {len(sample):,} lookups in {el * 1000:.0f} ms"
          f"  ->  {el / len(sample) * 1e6:.2f} µs each")

    t = time.perf_counter()
    for w in sample[:200000]:
        ix._slice(ix.word_id[w])
    el = time.perf_counter() - t
    print(f"offset lookup only  {len(sample):,} in {el * 1000:.0f} ms"
          f"  ->  {el / len(sample) * 1e6:.2f} µs each")

    m = ix.meta
    print(f"\nindex: {m['vocab_size']:,} words, {m['edge_count']:,} edges, "
          f"{(m['offsets_bytes'] + m['edges_bytes']) / 1e6:.2f} MB mapped")


def cmd_demo(ix: AssocIndex) -> None:
    print("=" * 72)
    print("  WORD-ASSOCIATION INDEX OVER THE ENTIRE TANACH")
    m = ix.meta
    print(f"  {m['verses']:,} verses · {m['tokens']:,} tokens · "
          f"{m['vocab_size']:,} words · {m['edge_count']:,} associations")
    print(f"  built in {m['build_seconds']}s · "
          f"{(m['offsets_bytes'] + m['edges_bytes']) / 1e6:.1f} MB on disk")
    print("=" * 72)

    print("\n\n### 1. Raw associations — what does the corpus put near this word?")
    for w in ["שבת", "מזבח", "מלך", "אויב"]:
        cmd_assoc(ix, w, 8)

    print("\n\n### 2. Distributional similarity — no embeddings, just profile overlap")
    for w in ["מלך", "כהן", "זהב", "שמח"]:
        cmd_similar(ix, w, 8)

    print("\n\n### 3. Query expansion")
    print("\n--- mode=assoc: words that appear NEXT TO the query terms ---")
    cmd_expand(ix, "מזבח קרבן", 5, mode="assoc")
    print("\n--- mode=similar: words USED LIKE the query terms ---")
    for q in ["מזבח קרבן", "מלך מלחמה", "לחם יין"]:
        cmd_expand(ix, q, 5, mode="similar")

    print("\n\n### 4. Lookup cost")
    cmd_bench(ix)


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["assoc", "similar", "expand", "bench", "demo"])
    ap.add_argument("arg", nargs="?", default="")
    ap.add_argument("-n", type=int, default=20)
    ap.add_argument("--mode", choices=["assoc", "similar"], default="similar",
                    help="expand only: which graph to expand along")
    a = ap.parse_args()

    ix = AssocIndex()
    try:
        if a.cmd == "assoc":
            cmd_assoc(ix, a.arg, a.n)
        elif a.cmd == "similar":
            cmd_similar(ix, a.arg, a.n)
        elif a.cmd == "expand":
            cmd_expand(ix, a.arg, a.n, a.mode)
        elif a.cmd == "bench":
            cmd_bench(ix)
        else:
            cmd_demo(ix)
    finally:
        ix.close()


if __name__ == "__main__":
    main()
