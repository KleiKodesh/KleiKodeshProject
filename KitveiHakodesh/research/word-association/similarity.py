"""
Alternative similarity measures over the sparse association profiles.

All of these operate on the Top-K ranked feature lists the index ALREADY stores,
so none of them needs a new corpus pass. That is the point: the index is a ranked
sparse matrix, and the measure used to compare two rows is a free parameter.

Measures
--------
cosine    the current baseline — geometric, over PPMI values.

APSyn     Santus et al. 2016 (arXiv:1603.09054). Rank-overlap rather than
          geometry: for every feature in the intersection of the two words'
          top-N, add the reciprocal of its AVERAGE rank across the two.

              APSyn(w1,w2) = sum over f in topN(w1) ∩ topN(w2) of
                             1 / ((rank_w1(f) + rank_w2(f)) / 2)

          Reported: 58.33% vs cosine's 49.44% on ESL synonym choice (N=100),
          with no tuning. Their finding that SMALLER N works better is
          reproduced in the sweep below — it matters.

APAnt     Santus et al. 2014, the inverse of APSyn. The hypothesis is precise
          and testable: antonyms are GLOBALLY distributionally similar (they
          share broad context) but DIVERGE on their most salient contexts,
          whereas synonyms agree there. So

              high cosine + low APSyn  =>  contrast, not equivalence

          This is the direct attack on the 4x antonym-over-synonym result in
          FINDINGS.md §10, and it costs one extra rank-overlap computation.

jaccard   |intersection| / |union| over the top-N feature sets, unweighted.
          Lexical Computing report Jaccard beating cosine for their Sketch
          Engine thesaurus, with the advantage GROWING on larger corpora.

overlap   Raw count of shared features (JoBimText / Riedl & Biemann). Reported
          competitive with skip-gram despite discarding all weights.

Each takes two `{feature_id: (rank, weight)}` profiles so the caller can build
them once per candidate and reuse across measures.
"""

from __future__ import annotations

import math


def build_ranked(pairs: list[tuple[int, float]], n: int
                 ) -> dict[int, tuple[int, float]]:
    """feature_id -> (0-based rank, weight), truncated to top-n.

    Input must already be sorted by weight descending — which the index
    guarantees, since that is how the rows are stored.
    """
    return {f: (i, w) for i, (f, w) in enumerate(pairs[:n])}


# ---------------------------------------------------------------------------


def cosine(a: dict[int, tuple[int, float]], b: dict[int, tuple[int, float]]) -> float:
    """Standard cosine over the weights. The baseline."""
    if not a or not b:
        return 0.0
    dot = sum(w * b[f][1] for f, (_, w) in a.items() if f in b)
    if dot == 0.0:
        return 0.0
    na = math.sqrt(sum(w * w for _, w in a.values()))
    nb = math.sqrt(sum(w * w for _, w in b.values()))
    return dot / (na * nb) if na and nb else 0.0


def apsyn(a: dict[int, tuple[int, float]], b: dict[int, tuple[int, float]]) -> float:
    """APSyn — reciprocal average rank over shared features.

    Uses ONLY ranks, never weights. That is what makes it robust for
    low-frequency words: a rare word's PPMI values are unstable estimates, but
    the ORDER of its top contexts is comparatively reliable.
    """
    if not a or not b:
        return 0.0
    total = 0.0
    # Iterate the smaller side; membership test on the larger.
    small, large = (a, b) if len(a) <= len(b) else (b, a)
    for f, (ra, _) in small.items():
        hit = large.get(f)
        if hit is not None:
            total += 2.0 / (ra + hit[0] + 2)   # 1 / ((r1+1 + r2+1)/2)
    return total


def jaccard(a: dict[int, tuple[int, float]], b: dict[int, tuple[int, float]]) -> float:
    """Unweighted set overlap over the top-N features."""
    if not a or not b:
        return 0.0
    inter = len(a.keys() & b.keys())
    return inter / (len(a) + len(b) - inter) if inter else 0.0


def overlap(a: dict[int, tuple[int, float]], b: dict[int, tuple[int, float]]) -> float:
    """Raw shared-feature count (JoBimText)."""
    if not a or not b:
        return 0.0
    return float(len(a.keys() & b.keys()))


def lin(a: dict[int, tuple[int, float]], b: dict[int, tuple[int, float]]) -> float:
    """Lin (1998): shared weight mass over total weight mass.

    Closer to Dice than to cosine — it rewards a large shared FRACTION rather
    than aligned direction, so it is less forgiving of one word having many
    strong features the other lacks.
    """
    if not a or not b:
        return 0.0
    shared = sum(a[f][1] + b[f][1] for f in a.keys() & b.keys())
    if shared == 0.0:
        return 0.0
    tot = sum(w for _, w in a.values()) + sum(w for _, w in b.values())
    return shared / tot if tot else 0.0


MEASURES = {
    "cosine": cosine,
    "apsyn": apsyn,
    "jaccard": jaccard,
    "overlap": overlap,
    "lin": lin,
}


# ---------------------------------------------------------------------------
# Contrast detection
# ---------------------------------------------------------------------------


def apant(cos: float, aps: float, aps_max: float) -> float:
    """Contrast score: distributionally similar but NOT on salient contexts.

    `aps_max` normalizes APSyn into [0,1] over the candidate set being ranked,
    since APSyn is an unbounded sum whose scale depends on N and on how many
    features overlap.

    High when cosine is high and normalized APSyn is low — Santus et al.'s
    antonymy signal. Zero when either condition fails, so it never promotes a
    pair that was not globally similar to begin with.
    """
    if cos <= 0 or aps_max <= 0:
        return 0.0
    return cos * (1.0 - min(1.0, aps / aps_max))


class SimilarityView:
    """Recomputes second-order similarity under a chosen measure.

    Wraps any index exposing `neighbors`, so it composes with LexiconView.

    The candidate set is words reachable in two hops (sharing at least one
    context), exactly as the index's own `similar()` does — a word sharing no
    context scores 0 under every measure here, so there is nothing to gain from
    scoring it.
    """

    def __init__(self, ix, measure: str = "apsyn", top_n: int = 100,
                 candidate_depth: int = 60, contrast: bool = False):
        self.ix = ix
        self.measure = measure
        self.fn = MEASURES[measure]
        self.top_n = top_n
        self.candidate_depth = candidate_depth
        self.contrast = contrast
        self.words = ix.words
        self.counts = ix.counts
        self.word_id = ix.word_id
        self.meta = dict(ix.meta, similarity=measure, top_n=top_n,
                         contrast=contrast)

    def neighbors(self, word: str, n: int = 20):
        return self.ix.neighbors(word, n)

    def profile(self, word: str, n: int = 300):
        return self.ix.profile(word, n)

    def _slice(self, wid: int):
        """Passthrough for degree statistics (report.py)."""
        return self.ix._slice(wid)

    def similar(self, word: str, n: int = 20, depth: int = 300):
        # neighbors() returns (word, weight) pairs; ranks come from position.
        target_pairs = self.ix.neighbors(word, self.top_n)
        if not target_pairs:
            return []
        tgt = {w: (i, s) for i, (w, s) in enumerate(target_pairs)}

        # Two-hop candidates. `candidate_depth` caps how many words a single
        # context contributes: a context shared by thousands of words carries
        # almost no information about any of them, and letting it in makes the
        # candidate set enormous for nothing. (Rychlý & Kilgarriff skip contexts
        # with >10k members for the same reason.)
        cands: set[str] = set()
        for f in tgt:
            for w2, _ in self.ix.neighbors(f, self.candidate_depth):
                cands.add(w2)
        cands.discard(word)
        if not cands:
            return []

        scored: list[tuple[str, float, float]] = []
        for c in cands:
            cp = self.ix.neighbors(c, self.top_n)
            if not cp:
                continue
            prof = build_ranked(cp, self.top_n)
            s = self.fn(tgt, prof)
            if s > 0:
                scored.append((c, s, cosine(tgt, prof) if self.contrast else 0.0))

        if not scored:
            return []

        if self.contrast:
            # APAnt: rank by "globally similar, salient-context divergent".
            amax = max(s for _, s, _ in scored) or 1.0
            out = [(c, apant(cos, s, amax)) for c, s, cos in scored]
        else:
            out = [(c, s) for c, s, _ in scored]
        out.sort(key=lambda t: -t[1])
        return out[:n]

    def expand(self, query: str, per_term: int = 5, mode: str = "similar"):
        return self.ix.expand(query, per_term, mode)

    def close(self) -> None:
        self.ix.close()
