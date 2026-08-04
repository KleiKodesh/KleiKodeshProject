"""
Read-only reader for the C# builder's SQLite association table.

Exposes the same surface as query.AssocIndex (`neighbors`, `similar`, `expand`,
`profile`, `.words`, `.counts`, `.meta`) so evaluate.py and report.py work
against either backend without changes.

Why the table is fast despite being SQLite
------------------------------------------
`assoc` is `WITHOUT ROWID` with `PRIMARY KEY (a, rank)`, so a word's
associations are physically contiguous and already in descending-weight order.
A top-N lookup is one B-tree seek plus a short forward scan of adjacent
pages — the same access pattern as the CSR layout, just with SQLite doing the
paging instead of mmap.
"""

from __future__ import annotations

import sqlite3
from pathlib import Path

FINALS = str.maketrans("ךםןףץ", "כמנפצ")


class AssocDb:
    """Read-only association table. API-compatible with query.AssocIndex."""

    def __init__(self, path: str | Path):
        self.path = str(path)
        self._degrees: list[int] | None = None
        self._con = sqlite3.connect(f"file:{self.path}?mode=ro", uri=True)
        self._con.execute("pragma mmap_size=1073741824")
        self._con.execute("pragma cache_size=-131072")

        raw = dict(self._con.execute("select key, value from meta"))
        # Normalize to the same key names query.AssocIndex/meta.json exposes, so
        # downstream code does not have to know which builder produced this.
        self.meta = dict(raw)
        for k in ("vocab_size", "edge_count", "window", "topk", "min_count",
                  "folded_forms", "books", "min_stem_freq"):
            if k in raw:
                try:
                    self.meta[k] = int(raw[k])
                except ValueError:
                    pass
        for k in ("length_norm_b",):
            if k in raw:
                self.meta[k] = float(raw[k])
        # `units` is this builder's name for what the CSR meta calls `verses`.
        if "units" in raw:
            self.meta["verses"] = int(raw["units"])
        if "tokens" in raw:
            self.meta["tokens"] = int(raw["tokens"])
        self.meta.setdefault("corpus_desc", raw.get("corpus", "unknown"))
        self.meta.setdefault("builder", "AssocBuilder (C#/net10)")
        self.meta.setdefault("offsets_bytes", 0)
        self.meta.setdefault("edges_bytes", Path(self.path).stat().st_size)

        rows = self._con.execute("select term, freq from word order by id").fetchall()
        self.words: list[str] = [r[0] for r in rows]
        self.counts: list[int] = [r[1] for r in rows]
        self.word_id = {w: i for i, w in enumerate(self.words)}

    def close(self) -> None:
        self._con.close()

    # -- lookups -----------------------------------------------------------

    def neighbors(self, word: str, n: int = 20) -> list[tuple[str, float]]:
        wid = self.word_id.get(word.translate(FINALS))
        if wid is None:
            return []
        cur = self._con.execute(
            "select b, w from assoc where a = ? order by rank limit ?", (wid, n))
        W = self.words
        return [(W[b], w) for b, w in cur]

    def profile(self, word: str, n: int = 300) -> dict[int, float]:
        wid = self.word_id.get(word.translate(FINALS))
        if wid is None:
            return {}
        return dict(self._con.execute(
            "select b, w from assoc where a = ? order by rank limit ?", (wid, n)))

    def _profile_by_id(self, wid: int, n: int) -> dict[int, float]:
        return dict(self._con.execute(
            "select b, w from assoc where a = ? order by rank limit ?", (wid, n)))

    # -- CSR-compatibility shims ------------------------------------------
    # report.py measures raw lookup cost and per-word degree via the CSR
    # index's internals. These give the same numbers over the table so the
    # report works unchanged against either backend.

    def _slice(self, wid: int) -> tuple[int, int]:
        """(0, degree) for word `wid`. There are no CSR offsets here, so the
        pair is not a byte range — only `end - start` is meaningful, which is
        all the callers use it for."""
        if self._degrees is None:
            self._load_degrees()
        return 0, self._degrees[wid] if wid < len(self._degrees) else 0

    def _load_degrees(self) -> None:
        """One grouped scan beats 313k individual COUNT queries."""
        self._degrees = [0] * len(self.words)
        for a, c in self._con.execute("select a, count(*) from assoc group by a"):
            if a < len(self._degrees):
                self._degrees[a] = c

    # -- second-order similarity ------------------------------------------

    def similar(self, word: str, n: int = 20, depth: int = 300
                ) -> list[tuple[str, float]]:
        """Words whose association profiles resemble this word's (cosine).

        Candidates are restricted to words reachable in two hops — sharing no
        context word at all means cosine 0, so there is nothing to gain from
        scoring them. Without that restriction this would be all-pairs.
        """
        target = self.profile(word, depth)
        if not target:
            return []
        wid = self.word_id[word.translate(FINALS)]
        tnorm = sum(v * v for v in target.values()) ** 0.5
        if tnorm == 0:
            return []

        # One query for all context words' rows beats `len(target)` round trips.
        ctx = list(target)
        cands: set[int] = set()
        CHUNK = 400
        for i in range(0, len(ctx), CHUNK):
            part = ctx[i:i + CHUNK]
            q = ("select b from assoc where a in (%s) and rank < ?"
                 % ",".join("?" * len(part)))
            cands.update(r[0] for r in self._con.execute(q, (*part, depth)))
        cands.discard(wid)
        if not cands:
            return []

        scored: list[tuple[str, float]] = []
        cl = list(cands)
        for i in range(0, len(cl), CHUNK):
            part = cl[i:i + CHUNK]
            q = ("select a, b, w from assoc where a in (%s) and rank < ? order by a"
                 % ",".join("?" * len(part)))
            cur_a = -1
            dot = nrm = 0.0
            for a, b, w in self._con.execute(q, (*part, depth)):
                if a != cur_a:
                    if cur_a >= 0 and dot > 0:
                        scored.append((self.words[cur_a], dot / (tnorm * nrm ** 0.5)))
                    cur_a, dot, nrm = a, 0.0, 0.0
                nrm += w * w
                tw = target.get(b)
                if tw is not None:
                    dot += w * tw
            if cur_a >= 0 and dot > 0:
                scored.append((self.words[cur_a], dot / (tnorm * nrm ** 0.5)))

        scored.sort(key=lambda t: -t[1])
        return scored[:n]

    # -- query expansion ---------------------------------------------------

    def expand(self, query: str, per_term: int = 5, mode: str = "similar"
               ) -> tuple[list[str], list[tuple[str, float, str]]]:
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
            score = (sum(ws) / len(ws)) * len(ws)
            tag = f"agreed by {len(ws)}" if len(ws) > 1 else "single"
            scored.append((w, score, tag))
        scored.sort(key=lambda t: -t[1])
        return terms, scored[: per_term * max(1, len(known))]


def open_index(path: str | Path):
    """Open either backend: a .db association table or a CSR index directory."""
    p = Path(path)
    if p.is_dir():
        from query import AssocIndex
        return AssocIndex(p)
    return AssocDb(p)
