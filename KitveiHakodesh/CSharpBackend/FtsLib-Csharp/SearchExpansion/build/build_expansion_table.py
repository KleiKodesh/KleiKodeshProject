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

from expansion import (  # noqa: E402
    Expander, corpus_freq, SEFARIA_DEFAULT_LEXICONS)

CORPUS = "all" if "--corpus" in sys.argv and "all" in sys.argv else "tanach"
OUT = f"expansion-{'seforim' if CORPUS == 'all' else 'tanach'}.db"


def command_line_value(option):
    """The argument after `option`, or None when the option is absent."""
    if option in sys.argv[:-1]:
        return sys.argv[sys.argv.index(option) + 1]
    return None


# Optional rabbinic-forms input (see expansion.py). Absent -> the build is
# exactly the pre-Sefaria build.
SEFARIA_LEXICON = command_line_value("--sefaria-lexicon")
SEFARIA_LEXICON_IDS = SEFARIA_DEFAULT_LEXICONS + (
    (3,) if "--sefaria-include-klein" in sys.argv else ())


def write_sefaria_lemma_sources(con, ex, lemmas):
    """sefaria_lemma(lemma, source): which lemmas' infl rows are Sefaria
    forms, so the routed merge can tag them. Written only when the channel
    ran, so a plain build's file is unchanged."""
    con.execute("drop table if exists sefaria_lemma")
    if not ex.sefaria_source_of:
        return
    con.execute("create table sefaria_lemma (lemma TEXT PRIMARY KEY, "
                "source TEXT) WITHOUT ROWID")
    with con:
        con.executemany("insert into sefaria_lemma values (?, ?)",
                        ((lm, ex.sefaria_source_of[lm]) for lm in lemmas
                         if lm in ex.sefaria_source_of))


def print_sefaria_drop_counts(ex):
    print(f"sefaria groups admitted: {len(ex.sefaria_forms_of):,} "
          f"({len(ex.sefaria_headword_of):,} forms)")
    for reason, count in sorted(ex.sefaria_drop_counts.items()):
        print(f"  sefaria dropped [{reason}]: {count:,}")


def main():
    freq = corpus_freq(CORPUS)
    print(f"{CORPUS} surface vocab: {len(freq):,} "
          f"({sum(freq.values())/1e6:.0f}M tokens)")
    ex = Expander(freq, SEFARIA_LEXICON, SEFARIA_LEXICON_IDS)
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
    write_sefaria_lemma_sources(con, ex, lemmas)
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
    if SEFARIA_LEXICON:
        print_sefaria_drop_counts(ex)
    print(f"wrote {OUT} ({os.path.getsize(OUT)/1e6:.1f} MB)")


if __name__ == "__main__":
    main()
