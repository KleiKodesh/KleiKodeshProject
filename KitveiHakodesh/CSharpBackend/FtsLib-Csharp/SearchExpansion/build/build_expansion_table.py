"""Build the FTS-consumable expansion artifact -> expansion-tanach.db.

Schema (static, read-only after build — same conventions as the assoc DBs):

  meta (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID
  fold (surface TEXT PRIMARY KEY, lemma TEXT) WITHOUT ROWID
       -- query-term normalization: surface -> lexeme
  exp  (lemma TEXT, rank INTEGER, form TEXT, channel TEXT,
        PRIMARY KEY (lemma, rank)) WITHOUT ROWID
       -- guarded expansions per lexeme, contiguous per lemma

FTS integration sketch (ROADMAP 6): fold the typed term, read its exp rows,
add forms as labelled OR-terms; expansion count is the knob to sweep, and
exact matches must always outrank expanded ones. Expansion breadth is NOT a
result cap.

Output is masked; only ASCII counts reach stdout.
"""
import sqlite3
import sys

sys.path.insert(0, "tools")
sys.path.insert(0, ".")
import masked

masked.install()

from expansion import Expander, corpus_freq  # noqa: E402

CORPUS = "all" if "--corpus" in sys.argv and "all" in sys.argv else "tanach"
OUT = f"expansion-{'seforim' if CORPUS == 'all' else 'tanach'}.db"


def main():
    freq = corpus_freq(CORPUS)
    print(f"{CORPUS} surface vocab: {len(freq):,} "
          f"({sum(freq.values())/1e6:.0f}M tokens)")
    ex = Expander(freq)
    print(f"match-frequency ceiling for this corpus: {ex.match_max:,}")

    lemmas = sorted({ex.lemma_of(t) for t in freq})
    print(f"distinct lemmas: {len(lemmas):,}")

    con = sqlite3.connect(OUT)
    con.executescript("""
        pragma journal_mode=OFF; pragma synchronous=OFF;
        drop table if exists meta; drop table if exists fold;
        drop table if exists exp;
        create table meta (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID;
        create table fold (surface TEXT PRIMARY KEY, lemma TEXT) WITHOUT ROWID;
        create table exp  (lemma TEXT, rank INTEGER, form TEXT, channel TEXT,
                           PRIMARY KEY (lemma, rank)) WITHOUT ROWID;
        """)
    with con:
        con.executemany("insert into fold values (?, ?)",
                        ((t, ex.lemma_of(t)) for t in sorted(freq)))

    rows = 0
    stats = {"infl": 0, "syn": 0, "bridge": 0}
    lemmas_with = 0
    with con:
        for lm in lemmas:
            ranked = ex.expand(lm)
            if not ranked:
                continue
            lemmas_with += 1
            for i, (f, ch, _) in enumerate(ranked):
                con.execute("insert into exp values (?, ?, ?, ?)",
                            (lm, i, f, ch))
                stats[ch] += 1
                rows += 1
    with con:
        for k, v in [("corpus", CORPUS), ("surfaces", len(freq)),
                     ("lemmas", len(lemmas)), ("lemmas_with_exp", lemmas_with),
                     ("exp_rows", rows)] + list(stats.items()):
            con.execute("insert into meta values (?, ?)", (k, str(v)))
    con.execute("analyze")
    con.execute("vacuum")
    con.close()

    import os
    print(f"lemmas with expansions: {lemmas_with:,} "
          f"({lemmas_with/len(lemmas)*100:.0f}%)")
    print(f"exp rows: {rows:,}  by channel: {stats}")
    print(f"wrote {OUT} ({os.path.getsize(OUT)/1e6:.1f} MB)")


if __name__ == "__main__":
    main()
