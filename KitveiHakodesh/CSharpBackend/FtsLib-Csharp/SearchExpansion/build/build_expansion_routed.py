"""Build the ROUTED expansion artifact -> expansion-routed.db.

Audit verdict (FINDINGS 27): the flat whole-library table re-introduces the
register-capture class the Tanach table already eliminated (~52% of fold
divergences wrong on the library side), while its inflection content for
seforim-only vocabulary is good. So: route.

  fold (surface, lemma, source)  — Tanach-table folds win wherever the
        surface exists there; library folds serve only the rest.
  exp  (lemma, rank, form, channel, source) — rows come from whichever table
        owns the lemma's fold side.

Consumer policy encoded in meta and enforced downstream:
  - synonyms: trust only source='tanach' rows (the library-side synonym
    channel failed audit in both registers — antonyms, collocates, modern
    register — see FINDINGS 27); inflections are shippable from both.
  - optional Sefaria rabbinic forms (build_expansion_table.py
    --sefaria-lexicon): 'infl' rows tagged source='sefaria-<lexicon>', folds
    likewise; they only fill surfaces/lemmas that folded to themselves.
"""
import os
import sqlite3
import sys
from collections import Counter

sys.path.insert(0, "tools")
sys.path.insert(0, ".")
import masked

masked.install()

from expansion import tokens, finalize_last_letter, SEFORIM  # noqa: E402

OUT = "expansion-routed.db"
# optional: `--sefaria-review <path.tsv>` writes the Sefaria rows for review
SEFARIA_REVIEW_TSV = (sys.argv[sys.argv.index("--sefaria-review") + 1]
                      if "--sefaria-review" in sys.argv[:-1] else None)

# Biblical-Aramaic register guard (FINDINGS 27c). Books 35/36 carry the
# Daniel/Ezra Aramaic spans. A surface that is DISTINCTIVE to those spans
# (frequent there, near-absent elsewhere in Tanach) must not fold to a lemma
# that never occurs there — that is the furnace->riding-mount homograph
# capture, which the containment guard is structurally blind to.
ARAMAIC_BOOKS = (35, 36)
SPAN_MIN, OUTSIDE_MAX = 3, 1


def is_aramaic_line(tks):
    """Daniel/Ezra mix Hebrew and Aramaic CHAPTERS — classify per line.
    Markers: the standalone relative particle, dalet-proclitic tokens, and
    the emphatic-state alef-ending rate. The registers are starkly different,
    so a simple marker vote separates them reliably."""
    if not tks:
        return False
    di = sum(1 for t in tks if t == "די")
    dpref = sum(1 for t in tks if len(t) >= 4 and t.startswith("ד"))
    alef = sum(1 for t in tks if t.endswith("א"))
    score = (2 * di + dpref + alef) / len(tks)
    return score >= 0.15


def register_freqs():
    con = sqlite3.connect(f"file:{SEFORIM}?mode=ro", uri=True)
    span, outside = {}, {}
    n_span = n_heb = 0
    for bid in range(1, 40):
        for (c,) in con.execute("select content from line where bookId=?", (bid,)):
            tks = tokens(c)
            if bid in ARAMAIC_BOOKS and is_aramaic_line(tks):
                tgt = span
                n_span += 1
            else:
                tgt = outside
                if bid in ARAMAIC_BOOKS:
                    n_heb += 1
            for t in tks:
                tgt[t] = tgt.get(t, 0) + 1
    con.close()
    print(f"  aramaic-line classifier: {n_span} Aramaic lines, "
          f"{n_heb} Hebrew lines in Daniel/Ezra")
    return span, outside


# ── optional Sefaria rabbinic-forms rows (absent in a plain build) ─────────
# The per-corpus tables mark which lemmas' 'infl' rows are Sefaria forms
# (sefaria_lemma). Those rows keep channel 'infl' but carry their own source
# tag (e.g. 'sefaria-jastrow'): the runtime gates only syn rows by source, so
# they are served on every search while staying identifiable here.

def read_sefaria_lemma_sources(*tables):
    """lemma -> source tag; {} when no per-corpus table ran the channel."""
    sources = {}
    for con in tables:
        if con.execute("select 1 from sqlite_master "
                       "where name = 'sefaria_lemma'").fetchone():
            sources.update(con.execute("select lemma, source from sefaria_lemma"))
    return sources


def withhold_sefaria_folds(tfold, sefaria_source_of):
    """Turn Sefaria folds back into self-folds so the Aramaic register rules
    run on exactly the pre-Sefaria table; returns them for re-application.
    (Lexical folds never target a Sefaria headword — the loader admits only
    headwords lexical.db does not know.)"""
    withheld = {sf: lm for sf, lm in tfold.items()
                if lm in sefaria_source_of and sf != lm}
    for sf in withheld:
        tfold[sf] = sf
    return withheld


def reapply_sefaria_folds(tfold, withheld):
    """Re-apply a Sefaria fold only where the Aramaic rules left the surface
    self-folding — a rule-made or hand-pinned fold always wins."""
    applied = set()
    for sf, lm in withheld.items():
        if tfold.get(sf, sf) == sf:
            tfold[sf] = lm
            applied.add(sf)
    return applied


def drop_refolded_sefaria_rows(rows_of_lemma, sefaria_source_of, final_lemma,
                               sefaria_stats):
    """A Sefaria form whose final fold went elsewhere must not be offered as
    this lemma's inflection — the fold and the paradigm would disagree."""
    for lm in set(rows_of_lemma) & sefaria_source_of.keys():
        kept = [(f, ch) for f, ch in rows_of_lemma[lm]
                if ch != "infl" or final_lemma(f) == lm]
        sefaria_stats["exp row dropped: form folds elsewhere"] += \
            len(rows_of_lemma[lm]) - len(kept)
        rows_of_lemma[lm] = kept


def write_sefaria_summary(con, sefaria_stats):
    """Print + record in meta the Sefaria rows that reached the artifact."""
    rows_by_source = dict(con.execute(
        "select source, count(*) from exp where source like 'sefaria-%' "
        "group by source"))
    lemmas_gaining = con.execute(
        "select count(distinct lemma) from exp "
        "where source like 'sefaria-%'").fetchone()[0]
    folds_by_source = dict(con.execute(
        "select source, count(*) from fold where source like 'sefaria-%' "
        "group by source"))
    print(f"sefaria exp rows by source: {rows_by_source}")
    print(f"sefaria fold rows by source: {folds_by_source}")
    print(f"lemmas gaining sefaria rows: {lemmas_gaining:,}")
    for reason, count in sorted(sefaria_stats.items()):
        print(f"  sefaria routed [{reason}]: {count:,}")
    with con:
        con.execute("insert into meta values (?, ?)",
                    ("exp_rows_sefaria", str(rows_by_source)))
        con.execute("insert into meta values (?, ?)",
                    ("policy_sefaria", "rabbinic inflections from Sefaria "
                     "lexicons; channel infl, used like any infl row"))


def write_sefaria_review_tsv(con, path):
    """UTF-8 TSV for a human reviewer: one line per lemma that gained Sefaria
    rows — lemma, its forms in rank order (space-separated), source tag."""
    forms_of = {}
    for lemma, form, source in con.execute(
            "select lemma, form, source from exp where source like 'sefaria-%' "
            "order by lemma, rank"):
        forms_of.setdefault((lemma, source), []).append(form)
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("lemma\tforms\tsource\n")
        for (lemma, source), forms in forms_of.items():
            f.write(f"{lemma}\t{' '.join(forms)}\t{source}\n")
    print(f"wrote sefaria review file {path} ({len(forms_of):,} lemmas)")


def main():
    t = sqlite3.connect("file:expansion-tanach.db?mode=ro", uri=True)
    s = sqlite3.connect("file:expansion-seforim.db?mode=ro", uri=True)

    tfold = dict(t.execute("select surface, lemma from fold"))
    sfold = dict(s.execute("select surface, lemma from fold"))
    sefaria_source_of = read_sefaria_lemma_sources(t, s)
    withheld_sefaria_folds = withhold_sefaria_folds(tfold, sefaria_source_of)
    sefaria_stats = Counter()

    span, outside = register_freqs()
    distinctive = {w for w, f in span.items()
                   if f >= SPAN_MIN and outside.get(w, 0) <= OUTSIDE_MAX}

    # Aramaic proclitics: ד (relative/genitive) plus the shared conjunction/
    # preposition set. א was REMOVED after full-fold verification — in the
    # biblical span it stripped verb-prefix and root letters, never a true
    # proclitic (audit rows 1, 4).
    ARAM_PREFIXES = ("וד", "דב", "דל", "דכ", "דמ", "ד",
                     "ו", "ב", "ל", "כ", "מ")
    # candidates: every span surface that is absent from the Hebrew books —
    # the fold itself stays guarded (remainder must be well-attested in the
    # spans), so widening candidates only adds guarded folds
    span_only = {w for w, f in span.items() if outside.get(w, 0) <= OUTSIDE_MAX}
    finalize = finalize_last_letter

    no_fold = set()
    bl_path = os.path.join("tools", "aramaic_fold_blocklist.txt")
    if os.path.exists(bl_path):
        with open(bl_path, encoding="utf-8") as f:
            no_fold = {l.strip() for l in f if l.strip()}

    prefix_folds = {}
    # Guards per full-fold verification (93/93 judged, FINDINGS 27c):
    # - proclitic strips fire ONLY on span-attested remainders — a remainder
    #   attested merely in Hebrew Tanach proves nothing about the strip being
    #   morphological (root letters doubling as particles was the top error)
    # - the Tanach-cognate fallback is allowed ONLY for the de-emphatic case,
    #   where the stem is the word's exact skeleton (it verified well)
    # - plural emphatic ends in yod+alef: strip BOTH, or the leftover yod
    #   collides with Hebrew yod-final words (kings -> queen etc.)
    # two passes so proclitic+emphatic chains settle
    for _ in range(2):
        for w in span_only:
            if w in no_fold:
                continue
            cur = tfold.get(w, w)
            if cur != w and span.get(cur, 0) > 0:
                continue                     # already folds to a span word
            rests = [(finalize(w[len(p):]), False)
                     for p in ARAM_PREFIXES if w.startswith(p)]
            if w.endswith("יא"):
                rests.append((finalize(w[:-2]), True))
            if w.endswith("א"):
                rests.append((finalize(w[:-1]), True))
            for rest, cognate_ok in rests:
                span_hit = span.get(rest, 0) >= SPAN_MIN
                cog_hit = cognate_ok and outside.get(rest, 0) >= 25
                if len(rest) >= 3 and (span_hit or cog_hit):
                    tgt = tfold.get(rest, rest)
                    if span.get(tgt, 0) == 0 and outside.get(tgt, 0) < 25:
                        tgt = rest           # keep the fold register-plausible
                    prefix_folds[w] = tgt
                    break
        tfold.update(prefix_folds)

    overrides = {w for w in distinctive
                 if tfold.get(w, w) != w and span.get(tfold[w], 0) == 0}
    for w in overrides:
        tfold[w] = w                         # self-lemma beats wrong-register capture

    # human-verified manual folds apply LAST and win over everything —
    # pinned reader-verified pairs, forced-self for audited-wrong surfaces,
    # and hand lemma corrections (tools/aramaic_fold_manual.tsv)
    manual_path = os.path.join("tools", "aramaic_fold_manual.tsv")
    n_manual = 0
    if os.path.exists(manual_path):
        with open(manual_path, encoding="utf-8") as f:
            for line in f:
                if "\t" in line:
                    w, lm = line.rstrip("\n").split("\t")
                    tfold[w] = lm
                    if lm != w:
                        prefix_folds[w] = lm     # keep paradigm building aware
                    else:
                        prefix_folds.pop(w, None)
                    n_manual += 1
    print(f"  manual fold entries applied: {n_manual}")

    # give span lemmas their own paradigm as inflection rows: every span
    # surface that folds to the lemma becomes an 'infl' form of it
    span_infl = {}
    for w in span_only:
        lm = tfold.get(w, w)
        if lm != w:
            span_infl.setdefault(lm, set()).add(w)

    with open(os.path.join("tools", "aramaic_fold_overrides.txt"),
              "w", encoding="utf-8") as f:
        f.write("# self-lemma overrides (wrong-register capture blocked)\n")
        f.write("\n".join(sorted(overrides)) + "\n")
        f.write("# proclitic folds (surface -> lemma)\n")
        for w, lm in sorted(prefix_folds.items()):
            f.write(f"{w}\t{lm}\n")
    print(f"aramaic register guard: {len(distinctive)} distinctive surfaces, "
          f"{len(prefix_folds)} proclitic folds, {len(overrides)} self-lemma "
          f"overrides, {len(span_infl)} lemmas gain span paradigms")
    sefaria_tanach_folds = reapply_sefaria_folds(tfold, withheld_sefaria_folds)
    sefaria_stats["fold lost to aramaic rules"] = \
        len(withheld_sefaria_folds) - len(sefaria_tanach_folds)

    con = sqlite3.connect(OUT)
    con.executescript("""
        pragma journal_mode=OFF; pragma synchronous=OFF;
        drop table if exists meta; drop table if exists fold;
        drop table if exists exp;
        create table meta (key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID;
        create table fold (surface TEXT PRIMARY KEY, lemma TEXT,
                           source TEXT) WITHOUT ROWID;
        create table exp  (lemma TEXT, rank INTEGER, form TEXT, channel TEXT,
                           source TEXT, PRIMARY KEY (lemma, rank)) WITHOUT ROWID;
        """)

    lib_only = {sf: lm for sf, lm in sfold.items() if sf not in tfold}

    def fold_source(surface, lemma, side):
        is_sefaria_fold = (surface in sefaria_tanach_folds if side == "tanach"
                           else surface != lemma and lemma in sefaria_source_of)
        return sefaria_source_of[lemma] if is_sefaria_fold else side

    def row_source(lemma, channel, side):
        if channel == "infl" and lemma in sefaria_source_of:
            return sefaria_source_of[lemma]
        return side

    def final_lemma(form):
        return tfold.get(form) or lib_only.get(form) or form

    with con:
        con.executemany("insert into fold values (?, ?, ?)",
                        ((sf, lm, fold_source(sf, lm, "tanach"))
                         for sf, lm in tfold.items()))
        con.executemany("insert into fold values (?, ?, ?)",
                        ((sf, lm, fold_source(sf, lm, "library"))
                         for sf, lm in lib_only.items()))

    t_lemmas = set(tfold.values())
    lib_lemmas = {lm for lm in lib_only.values() if lm not in t_lemmas}
    rows = {"tanach": 0, "library": 0, "merged_forms": 0}
    merged_lemmas = set()

    # library-side rows grouped by lemma (one pass)
    from collections import defaultdict
    lib_rows = defaultdict(list)
    for lm, r, f, ch in s.execute(
            "select lemma, rank, form, channel from exp order by lemma, rank"):
        lib_rows[lm].append((f, ch))
    drop_refolded_sefaria_rows(lib_rows, sefaria_source_of, final_lemma,
                               sefaria_stats)

    with con:
        # Tanach-owned lemmas: validated rows first, then UNION in the
        # library-attested inflection/bridge forms (audit: those channels are
        # shippable from the library side; synonyms are NOT — FINDINGS 27).
        # This closes the recall gap where a rabbinic inflection of a shared
        # Hebrew word was missing because only Tanach-attested forms were kept.
        cur_lemma_rows = defaultdict(list)
        for lm, r, f, ch in t.execute(
                "select lemma, rank, form, channel from exp order by lemma, rank"):
            cur_lemma_rows[lm].append((f, ch))
        drop_refolded_sefaria_rows(cur_lemma_rows, sefaria_source_of,
                                   final_lemma, sefaria_stats)
        # Verified merge filters (reader audit 2026-08-04, ~88% -> ~98% safe):
        # - thin-anchor gate: a lemma with <=3 validated inflection forms
        #   cannot discipline the guard against high-frequency rabbinic/
        #   Aramaic homographs (all three failed audit rows were thin-anchor)
        # - non-final-letter endings are normalization artifacts, never
        #   legitimate word-final orthography
        NONFINAL_END = tuple("כמנפצ")  # kaf mem nun pe tsadi
        done_lemmas = set(cur_lemma_rows) | set(span_infl)
        for lm in done_lemmas:
            trows = cur_lemma_rows.get(lm, [])
            have = {f for f, _ in trows}
            anchor = sum(1 for _, ch in trows if ch == "infl")
            rank = 0
            for f, ch in trows:
                con.execute("insert into exp values (?, ?, ?, ?, ?)",
                            (lm, rank, f, ch, row_source(lm, ch, "tanach")))
                rank += 1
                rows["tanach"] += 1
            # biblical-Aramaic paradigm forms (register-guarded, span-attested)
            for f in sorted(span_infl.get(lm, ()), key=lambda x: -span.get(x, 0)):
                if f not in have and f != lm and not f.endswith(NONFINAL_END):
                    con.execute("insert into exp values "
                                "(?, ?, ?, 'infl', 'tanach')", (lm, rank, f))
                    have.add(f)
                    rank += 1
                    anchor += 1
                    rows["tanach"] += 1
            if anchor <= 3:
                continue                     # thin anchor: no library merge
            for f, ch in lib_rows.get(lm, ()):
                if ch != "syn" and f not in have and not f.endswith(NONFINAL_END):
                    con.execute("insert into exp values (?, ?, ?, ?, ?)",
                                (lm, rank, f, ch, row_source(lm, ch, "library")))
                    have.add(f)
                    rank += 1
                    rows["merged_forms"] += 1
                    merged_lemmas.add(lm)
        # library-only lemmas: all channels stored, synonyms policy-flagged
        for lm in lib_lemmas - done_lemmas:
            r = 0
            for f, ch in lib_rows.get(lm, ()):
                if f.endswith(NONFINAL_END):
                    continue                 # normalization artifacts
                con.execute("insert into exp values (?, ?, ?, ?, ?)",
                            (lm, r, f, ch, row_source(lm, ch, "library")))
                r += 1
                rows["library"] += 1
    print(f"tanach-owned lemmas gaining library forms: {len(merged_lemmas):,}")
    with con:
        for k, v in [("design", "routed: tanach folds win; library backstop"),
                     ("surfaces_tanach", len(tfold)),
                     ("surfaces_library_only", len(lib_only)),
                     ("exp_rows_tanach", rows["tanach"]),
                     ("exp_rows_library", rows["library"]),
                     ("policy_synonyms", "trust source='tanach' only (FINDINGS 27)"),
                     ("policy_inflections", "both sources shippable")]:
            con.execute("insert into meta values (?, ?)", (k, str(v)))
    if sefaria_source_of:
        write_sefaria_summary(con, sefaria_stats)
        if SEFARIA_REVIEW_TSV:
            write_sefaria_review_tsv(con, SEFARIA_REVIEW_TSV)
    con.execute("analyze")
    con.execute("vacuum")
    con.close()
    print(f"fold: {len(tfold):,} tanach + {len(lib_only):,} library-only")
    print(f"exp rows: {rows}")
    print(f"wrote {OUT} ({os.path.getsize(OUT)/1e6:.1f} MB)")


if __name__ == "__main__":
    main()
