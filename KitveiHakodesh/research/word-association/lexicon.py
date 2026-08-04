"""
Lexical resources as a quality lever — Hebrew AND Aramaic.

Context that shapes every decision here: this is a **Talmudic** search engine.
Passages are pure Hebrew, pure Aramaic, or a mix of both in one line. So Aramaic
is a first-class register, not noise — `מלכ`/`מלכא` linking is a *win*, and
`דאבא` is a legitimate form of `אבא` carrying the Aramaic relative prefix.

What IS noise is different: glued word pairs from print/OCR (`דכשמקדשינ`,
`חייבלקרוע`), which are single tokens that should have been two.

Resources used, in order of value
--------------------------------
`lexical.db`  (C:\\Users\\Public\\Documents\\Dictionary\\Backup)
    base    24,559   lexemes
    surface 137,631  inflected forms -> base_id
    variant 190,151  + 594,428 surface_variant links
    Covers Hebrew and Aramaic with prefixes attached. This is the resource that
    finally reaches the SUFFIX morphology FINDINGS.md §9 could not: `מלכי`,
    `מלכות`, `מלכא` all resolve to one base.

`Dictionary.db` (the app's own dictionary)
    54.5k headwords + a typed `link` table. Used for headword membership and for
    curated `כתיב` spelling variants.

Aramaic CSVs (`Dictionary/Backup/AramaicDictionary/`)
    root -> Aramaic forms -> Hebrew equivalents. Gives cross-language links
    (`אבהתא` <-> `אבות`) that co-occurrence alone would never connect.

Three uses, each measured separately in `improve.py`:

  LEMMA   collapse surface forms onto one base — the real fix for the split-mass
          problem, and it works across Hebrew/Aramaic.
  FILTER  only show words some lexicon knows. Cleans result lists; does not
          touch the statistics.
  BOOST   rank known words above unknown ones instead of dropping them.
"""

from __future__ import annotations

import csv
import re
import sqlite3
import sys
from functools import lru_cache
from pathlib import Path

DICT_DB = (
    "C:/Users/Admin/AppData/Local/KleiKodesh/KitveiHakodesh"
    "/dictionary/KitveiHakodesh_dictionary.db"
)
LEXICAL_DB = "C:/Users/Public/Documents/Dictionary/Backup/lexical.db"
ARAMAIC_DIR = Path("C:/Users/Public/Documents/Dictionary/Backup/AramaicDictionary")
SHORASHIM = Path("C:/Users/Public/Documents/Dictionary/Backup")

FINALS = str.maketrans("ךםןףץ", "כמנפצ")
_NIKUD_RE = re.compile(r"[\u0591-\u05BD\u05BF-\u05C7]")
_NON_HEB = re.compile(r"[^\u05D0-\u05EA]")


def normalize(w: str) -> str:
    """Same normalization the tokenizer applies, so lexicon entries and index
    vocabulary meet in one alphabet."""
    return _NON_HEB.sub("", _NIKUD_RE.sub("", w)).translate(FINALS).strip()


# ---------------------------------------------------------------------------
# Known-word sets
# ---------------------------------------------------------------------------


@lru_cache(maxsize=2)
def dict_headwords() -> frozenset[str]:
    """Dictionary.db headwords (~54.5k), normalized."""
    con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
    try:
        out = {normalize(r[0]) for r in con.execute("select headword from word")}
    finally:
        con.close()
    out.discard("")
    return frozenset(out)


@lru_cache(maxsize=2)
def lexical_forms() -> frozenset[str]:
    """Every surface form and variant lexical.db knows — Hebrew and Aramaic.

    This is the broad "is this a real word" set: ~700k forms including prefixed
    and inflected ones, which is what a Talmudic corpus actually contains.
    """
    con = sqlite3.connect(f"file:{LEXICAL_DB}?mode=ro", uri=True)
    try:
        out = {normalize(r[0]) for r in con.execute("select value from surface")}
        out |= {normalize(r[0]) for r in con.execute("select value from variant")}
        out |= {normalize(r[0]) for r in con.execute("select value from base")}
    finally:
        con.close()
    out.discard("")
    return frozenset(out)


@lru_cache(maxsize=2)
def known_words() -> frozenset[str]:
    """Union of every lexicon: the widest defensible 'a reader would recognize
    this' set. Aramaic included by design."""
    return dict_headwords() | lexical_forms() | aramaic_forms()


# ---------------------------------------------------------------------------
# Lemmatization — the suffix fix
# ---------------------------------------------------------------------------


@lru_cache(maxsize=2)
def lexical_lemmas() -> dict[str, str]:
    """form -> base lexeme, from lexical.db.

    Two joins, both needed:
      surface -> base            137,631 direct inflections
      variant -> surface -> base 594,428 variant links

    Collision policy: when a form maps to several bases (genuinely ambiguous in
    an unvocalized script), prefer the base that equals the form itself, then the
    shortest base. Deterministic and biased toward not inventing a mapping.
    """
    con = sqlite3.connect(f"file:{LEXICAL_DB}?mode=ro", uri=True)
    try:
        pairs: list[tuple[str, str]] = []
        for form, base in con.execute(
                "select s.value, b.value from surface s "
                "join base b on b.id = s.base_id"):
            pairs.append((normalize(form), normalize(base)))
        for form, base in con.execute(
                "select v.value, b.value from surface_variant sv "
                "join variant v on v.id = sv.variant_id "
                "join surface s on s.id = sv.surface_id "
                "join base b on b.id = s.base_id"):
            pairs.append((normalize(form), normalize(base)))
    finally:
        con.close()

    out: dict[str, str] = {}
    for form, base in pairs:
        if not form or not base:
            continue
        prev = out.get(form)
        if prev is None:
            out[form] = base
            continue
        if prev == form:
            continue                       # already identity — best possible
        if base == form or len(base) < len(prev):
            out[form] = base
    return out


@lru_cache(maxsize=2)
def aramaic_forms() -> frozenset[str]:
    """All Aramaic forms from the shorashim CSVs."""
    out: set[str] = set()
    for p in (ARAMAIC_DIR / "aramic_dictionary_shorashim_consolidated.csv",
              SHORASHIM / "merged_pealim_wikiDictinary_aramaic_shorashim.csv"):
        if not p.exists():
            continue
        with p.open(encoding="utf-8-sig", newline="") as f:
            for row in csv.reader(f):
                for cell in row:
                    for w in cell.split(","):
                        n = normalize(w)
                        if n:
                            out.add(n)
    out.discard("")
    return frozenset(out)


@lru_cache(maxsize=2)
def aramaic_to_hebrew() -> dict[str, str]:
    """Aramaic form -> Hebrew equivalent.

    A genuine cross-language bridge: co-occurrence alone can only link two words
    that share contexts, so it will never discover `אבהתא` == `אבות` unless both
    happen to appear in the same passages. This is curated knowledge.
    """
    out: dict[str, str] = {}
    p = ARAMAIC_DIR / "aramic_dictionary_with_hebrew_root.csv"
    if p.exists():
        with p.open(encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                a = normalize(next(iter(row.values())) or "")
                heb = normalize(row.get("hebrew") or "")
                if a and heb and a != heb:
                    out.setdefault(a, heb)
    return out


@lru_cache(maxsize=2)
def shorashim_lemmas() -> dict[str, str]:
    """form -> root, from every *_shorashim.csv (Hebrew and Aramaic).

    Format is one row per root: `root,form1,form2,...`. Coarser than lexical.db
    (a ROOT, not a lexeme) so it is used as a fallback where lexical.db has no
    entry — root-level folding merges more aggressively than is always right.
    """
    out: dict[str, str] = {}
    files = [
        SHORASHIM / "lexical_shorashim.csv",
        SHORASHIM / "merged_pealim_wikiDictinary_shorashim.csv",
        SHORASHIM / "merged_pealim_wikiDictinary_aramaic_shorashim.csv",
        SHORASHIM / "radak_shorashim.csv",
    ]
    for p in files:
        if not p.exists():
            continue
        with p.open(encoding="utf-8-sig", newline="") as f:
            for line in f:
                parts = [normalize(x) for x in line.strip().split(",")]
                parts = [x for x in parts if x]
                if len(parts) < 2:
                    continue
                root, forms = parts[0], parts[1:]
                for w in forms:
                    out.setdefault(w, root)
    return out


def build_lemma_map(vocab: list[str], counts: list[int], min_freq: int = 2,
                    use_shorashim: bool = True) -> dict[str, str]:
    """Vocabulary form -> canonical form, restricted to what this corpus uses.

    Resolution order (most precise first):
      1. lexical.db surface/variant -> base   (lexeme-level, Hebrew + Aramaic)
      2. shorashim CSVs -> root               (coarser fallback)

    The hard guard: the target must ALSO be in this vocabulary above `min_freq`.
    Folding onto a form the corpus never uses would invent a node with no
    statistics behind it, which is worse than leaving the split alone.
    """
    lex = lexical_lemmas()
    sho = shorashim_lemmas() if use_shorashim else {}
    present = {w for w, c in zip(vocab, counts) if c >= min_freq}
    vocab_set = set(vocab)

    out: dict[str, str] = {}
    for w in vocab:
        for src in (lex, sho):
            tgt = src.get(w)
            if tgt and tgt != w and tgt in vocab_set and tgt in present:
                out[w] = tgt
                break
    # Collapse any 2-step chains so every form points at a final target.
    for w, t in list(out.items()):
        seen = {w}
        while t in out and t not in seen:
            seen.add(t)
            t = out[t]
        out[w] = t
    return out


# ---------------------------------------------------------------------------
# Display-time views
# ---------------------------------------------------------------------------


class LexiconView:
    """Applies a lexicon policy to an index's result lists.

    A VIEW, not a rebuild: the counts underneath are untouched, so several
    policies can be compared against one index apples-to-apples.

    mode='off'     pass through
    mode='filter'  drop words no lexicon knows (Aramaic is KEPT — it is known)
    mode='boost'   keep everything, rank known words first
    """

    def __init__(self, ix, mode: str = "filter", boost: float = 2.0,
                 vocabulary: frozenset[str] | None = None):
        self.ix = ix
        self.mode = mode
        self.boost = boost
        self._known = vocabulary if vocabulary is not None else known_words()
        self.words = ix.words
        self.counts = ix.counts
        self.word_id = ix.word_id
        self.meta = dict(ix.meta, lexicon_mode=mode)

    def _apply(self, res, n):
        if self.mode == "off" or not res:
            return res[:n]
        if self.mode == "filter":
            return [(w, s) for w, s in res if w in self._known][:n]
        scored = [(w, s * (self.boost if w in self._known else 1.0)) for w, s in res]
        scored.sort(key=lambda t: -t[1])
        return scored[:n]

    # Over-fetch before filtering, or a filter that drops most candidates would
    # return 2 results where 20 were asked for.
    def neighbors(self, word: str, n: int = 20):
        return self._apply(self.ix.neighbors(word, n if self.mode == "off" else n * 12), n)

    def similar(self, word: str, n: int = 20, depth: int = 300):
        return self._apply(self.ix.similar(word, n if self.mode == "off" else n * 12, depth), n)

    def profile(self, word: str, n: int = 300):
        return self.ix.profile(word, n)

    def _slice(self, wid: int):
        """Passthrough for degree statistics (report.py)."""
        return self.ix._slice(wid)

    def expand(self, query: str, per_term: int = 5, mode: str = "similar"):
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
            scored.append((w, (sum(ws) / len(ws)) * len(ws),
                           f"agreed by {len(ws)}" if len(ws) > 1 else "single"))
        scored.sort(key=lambda t: -t[1])
        return terms, scored[: per_term * max(1, len(known))]

    def close(self) -> None:
        self.ix.close()


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    print(f"Dictionary.db headwords : {len(dict_headwords()):,}")
    print(f"lexical.db forms        : {len(lexical_forms()):,}")
    print(f"lexical.db lemma map    : {len(lexical_lemmas()):,}")
    print(f"Aramaic forms           : {len(aramaic_forms()):,}")
    print(f"Aramaic -> Hebrew       : {len(aramaic_to_hebrew()):,}")
    print(f"shorashim lemma map     : {len(shorashim_lemmas()):,}")
    print(f"known_words (union)     : {len(known_words()):,}")
