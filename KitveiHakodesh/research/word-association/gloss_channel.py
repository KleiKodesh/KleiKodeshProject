"""Gloss-text similarity channel from the dictionary's sense texts.

Idea: two headwords are similar if their DEFINITIONS share content words —
a completely independent signal from corpus co-occurrence, available for every
word the dictionary covers (including rare corpus words whose distributional
profile is noise).

Profile: headword -> set of normalized Hebrew tokens from all its sense texts,
with a document-frequency cap (a token appearing in very many glosses is
lexicographic boilerplate, the gloss equivalent of a hub context).
Similarity: shared-token count (overlap — the measure that won on the corpus
side), served by an inverted index + counting pass.

PROVENANCE CAVEAT, recorded in FINDINGS: the gold set is the SAME dictionary's
link table. Glosses and links were written by the same lexicographers, so a
good score here partially measures editorial consistency, not transfer. The
honest read is the head-to-head on shared vocab vs the corpus index, plus the
coverage delta (words only the gloss channel can serve).

Output is masked ([H:xxxx]); raw words never reach stdout.
"""
import os
import re
import sqlite3
import sys
from collections import defaultdict

sys.path.insert(0, "tools")
sys.path.insert(0, ".")
import masked  # noqa: E402

masked.install()

from assoc_db import open_index  # noqa: E402
from evaluate import evaluate, load_gold, normalize  # noqa: E402

DICT_DB = os.path.expandvars(
    r"%LOCALAPPDATA%\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db")
HEB_TOKEN = re.compile(r"[א-ת]{2,}")
NIKUD = re.compile(r"[֑-ֽֿ-ׇ]")
TAG = re.compile(r"<[^>]+>")


def build_profiles(df_cap: int):
    con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
    texts = defaultdict(list)
    for hw, txt in con.execute(
            "select w.headword, s.text from sense s join word w on w.id = s.word_id"
            " where s.text is not null"):
        texts[normalize(hw)].append(txt)
    con.close()

    prof = {}
    df = defaultdict(int)
    for hw, ts in texts.items():
        toks = set()
        for t in ts:
            t = NIKUD.sub("", TAG.sub(" ", t))
            toks.update(HEB_TOKEN.findall(t))
        toks.discard(hw)          # a gloss usually repeats its headword
        if len(toks) >= 3:
            prof[hw] = toks
            for tk in toks:
                df[tk] += 1
    if df_cap:
        for hw in prof:
            prof[hw] = {t for t in prof[hw] if df[t] <= df_cap}
        prof = {hw: p for hw, p in prof.items() if len(p) >= 3}
    return prof


class GlossView:
    """similar() over gloss-token overlap, evaluate.py-compatible."""

    def __init__(self, prof):
        self.prof = prof
        self.inv = defaultdict(list)
        for hw, toks in prof.items():
            for t in toks:
                self.inv[t].append(hw)

    def similar(self, w, n=20, depth=300):
        p = self.prof.get(w)
        if not p:
            return []
        count = defaultdict(int)
        for t in p:
            for c in self.inv[t]:
                count[c] += 1
        count.pop(w, None)
        out = sorted(count.items(), key=lambda kv: (-kv[1], kv[0]))
        return [(c, float(s)) for c, s in out[:n]]

    def neighbors(self, w, n=20):
        return self.similar(w, n)


def main():
    tanach = open_index("assoc-tanach-sim0.db")
    tv = set(tanach.words)
    tc = dict(zip(tanach.words, tanach.counts))

    for cap in (0, 1000, 300, 100):
        prof = build_profiles(cap)
        gv = GlossView(prof)
        V = set(prof)

        g_all = load_gold("synonym", V, 0, {})
        r_all = evaluate(gv, g_all, 20, "similar", 300)

        shared = V & tv
        g_sh = load_gold("synonym", shared, 10, tc)
        r_sh = evaluate(gv, g_sh, 20, "similar", 300)

        print(f"cap {cap or 'none':>5}: profiles {len(prof):,}  "
              f"gold-all {len(g_all)} P@20 {r_all['P@20']:.4f} MRR {r_all['MRR']:.4f} | "
              f"shared-w-tanach gold {len(g_sh)} P@20 {r_sh['P@20']:.4f} MRR {r_sh['MRR']:.4f}")

    # corpus baseline on the SAME shared gold (protocol: identical limit)
    from similarity import SimilarityView
    prof = build_profiles(300)
    shared = set(prof) & tv
    g_sh = load_gold("synonym", shared, 10, tc)
    corp = evaluate(SimilarityView(tanach, "overlap", top_n=100), g_sh, 20, "similar", 300)
    print(f"\ncorpus overlap on same shared gold ({len(g_sh)}): "
          f"P@20 {corp['P@20']:.4f} MRR {corp['MRR']:.4f}")
    only = len(set(prof) - tv)
    print(f"coverage: gloss-only headwords not in Tanach vocab: {only:,}")
    tanach.close()


if __name__ == "__main__":
    main()
