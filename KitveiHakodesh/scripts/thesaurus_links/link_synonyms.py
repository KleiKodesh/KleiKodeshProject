#!/usr/bin/env python3
r"""Use Microsoft Word's Hebrew thesaurus PURELY AS A LINKER between words that
ALREADY EXIST in the KitveiHakodesh Dictionary.db.

It NEVER adds a new headword or sense from the thesaurus. A thesaurus synonym is
kept only when it is already a resolvable word in the DB, and the sole effect is
a new 'נרדף' (synonym) link. This mirrors the maintainer's WordThesaurusProvider
(CSharpBackend/KitveiHakodeshLib/Dictionary/WordThesaurusProvider.cs) — Word is
used only as a source of candidate synonyms; the DB decides what actually links.

Pipeline (this script writes REVIEW FILES only — it does NOT touch either DB):
  1. read Dictionary.db (service copy),
  2. ask Word's Hebrew thesaurus for synonyms of every real entry,
  3. keep synonyms that already exist in the DB as a resolvable word,
  4. write two files for review:
       proposed_nardaf_links.sql   idempotent INSERT OR IGNORE, both directions
       proposed_nardaf_report.tsv  UTF-8 (source \t synonym), human-readable
  Apply with:  python apply_links.py            (writes BOTH DB copies)

Design rules (agreed with the maintainer):
  * SOURCE words = only words that have a sense of their own (real entries).
    Stubs (spelling-redirect endpoints with no definition) do NOT get synonyms
    of their own — but a stub CAN be a synonym found, because following its
    'כתיב' redirect lands the reader on a real definition.
  * TARGET words = any RESOLVABLE word: has a sense OR a 'כתיב' redirect to one.
  * All thesaurus meaning-groups are flattened into 'נרדף' links (matches the
    frontend Synonyms() query).
  * Links are written BOTH directions (existing נרדף links are 100% symmetric)
    and are idempotent.

Requires: Word installed + pywin32 (win32com). Some Hebrew words reliably throw
a spurious COM error from Word's thesaurus (e.g. שמח, חכם) — those are skipped
and reported; it is a Word bug, not something retries fix.

    python link_synonyms.py                 # full run (all real entries)
    python link_synonyms.py --limit 200     # quick trial on first 200 entries
"""
import argparse
import os
import re
import sqlite3
import sys

import pythoncom
import win32com.client as win32

WD_HEBREW = 1037  # WdLanguageID.wdHebrew

HERE = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.normpath(os.path.join(
    HERE, r"..\..\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db"))
SQL_OUT = os.path.join(HERE, "proposed_nardaf_links.sql")
TSV_OUT = os.path.join(HERE, "proposed_nardaf_report.tsv")

# Hebrew nikud + cantillation (U+0591–U+05C7) and bidi marks. DB headwords are
# unvocalized; the thesaurus sometimes returns vocalized/decorated forms.
_STRIP = re.compile(r"[֑-ׇ‎‏]")


def norm(s):
    return _STRIP.sub("", s or "").strip()


# ── Word thesaurus ───────────────────────────────────────────────────────────
class Thesaurus:
    """Thin wrapper around Word's SynonymInfo (mirrors WordThesaurusProvider).

    IMPORTANT — DO NOT restart Word on a fault. Word's thesaurus reliably throws
    a spurious COMException ("not enough memory") on *certain* Hebrew words (e.g.
    שמח, חכם); this was verified to be fully LOCAL — the very next word succeeds
    with the SAME Word instance and no restart. Re-Dispatching Word was the ONLY
    thing that ever produced a broken late-bound app missing `.SynonymInfo`
    (AttributeError), which crashed two earlier runs. So: one Word instance for
    the whole run, and on any fault just skip that word and continue.

    EnsureDispatch forces early binding from the cached typelib so `.SynonymInfo`
    is a real bound method."""

    def __init__(self):
        self.app = win32.gencache.EnsureDispatch("Word.Application")
        self.app.Visible = False
        self.faults = 0

    def _raw(self, word):
        si = self.app.SynonymInfo(word, WD_HEBREW)
        if not si.Found or si.MeaningCount == 0:
            return []
        out = []
        for i in range(1, si.MeaningCount + 1):
            lst = si.SynonymList(i)  # tuple of strings, or None
            if lst:
                out.extend(lst)
        return out

    def synonyms(self, word):
        """Flattened synonyms, or None if Word faults on this word (skip + report).
        The fault is local to the word — we keep the same Word instance."""
        try:
            return self._raw(word)
        except Exception:
            self.faults += 1
            return None

    def quit(self):
        try:
            self.app.Quit(False)
        except Exception:
            pass


# ── DB load ──────────────────────────────────────────────────────────────────
def load_db():
    con = sqlite3.connect(DB_PATH)
    c = con.cursor()

    id_by_hw = {}
    for wid, hw in c.execute("SELECT id, headword FROM word"):
        h = norm(hw)
        if h and h not in id_by_hw:
            id_by_hw[h] = wid

    has_sense = {r[0] for r in c.execute("SELECT DISTINCT word_id FROM sense")}

    resolvable_stub = {r[0] for r in c.execute(
        "SELECT DISTINCT l.word_id FROM link l "
        "JOIN link_kind lk ON lk.id = l.kind_id AND lk.name = 'כתיב' "
        "WHERE l.target_id IN (SELECT word_id FROM sense)")}

    existing = set()
    for a, b in c.execute(
            "SELECT word_id, target_id FROM link WHERE kind_id="
            "(SELECT id FROM link_kind WHERE name='נרדף')"):
        existing.add((a, b))

    con.close()
    return id_by_hw, has_sense, resolvable_stub, existing


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=0,
                    help="only process the first N source entries (trial run)")
    args = ap.parse_args()

    if not os.path.exists(DB_PATH):
        sys.exit(f"Dictionary.db not found at {DB_PATH}")

    id_by_hw, has_sense, resolvable_stub, existing = load_db()
    print(f"words: {len(id_by_hw)}  with-sense: {len(has_sense)}  "
          f"resolvable-stubs: {len(resolvable_stub)}  existing נרדף: {len(existing)}")

    def resolvable(wid):
        return wid in has_sense or wid in resolvable_stub

    # SOURCE = words with a sense, sorted for a deterministic run/output.
    sources = sorted((h for h, wid in id_by_hw.items() if wid in has_sense))
    if args.limit:
        sources = sources[:args.limit]
    print(f"source entries to query: {len(sources)}")

    th = Thesaurus()
    proposed = set()                 # canonical (min_id, max_id)
    report = []                      # (source_hw, synonym_hw)
    faulted = []

    # Progress is written to a dedicated file with an explicit flush every
    # checkpoint, because a PowerShell `| Tee-Object` pipeline buffers stdout
    # and hides live progress. Read progress.log to watch a run.
    prog_path = os.path.join(HERE, "progress.log")

    def progress(msg):
        print(msg, flush=True)
        with open(prog_path, "a", encoding="utf-8") as pf:
            pf.write(msg + "\n")

    open(prog_path, "w", encoding="utf-8").close()  # truncate at start of run

    try:
        for done, src in enumerate(sources, 1):
            if done % 500 == 0:
                progress(f"  {done}/{len(sources)}  pairs: {len(proposed)}  "
                         f"faults: {th.faults}")
            src_id = id_by_hw[src]
            syns = th.synonyms(src)
            if syns is None:
                faulted.append(src)
                continue
            for raw in syns:
                syn = norm(raw)
                if not syn:
                    continue
                syn_id = id_by_hw.get(syn)
                if syn_id is None or syn_id == src_id or not resolvable(syn_id):
                    continue
                if (src_id, syn_id) in existing or (syn_id, src_id) in existing:
                    continue
                key = (src_id, syn_id) if src_id < syn_id else (syn_id, src_id)
                if key in proposed:
                    continue
                proposed.add(key)
                report.append((src, syn))
    finally:
        th.quit()

    # ── emit review files ──────────────────────────────────────────────────────
    with open(SQL_OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("-- Proposed נרדף (synonym) links from Word's Hebrew thesaurus.\n")
        f.write("-- Generated by link_synonyms.py. Links only pre-existing, resolvable DB words.\n")
        f.write("-- Idempotent: INSERT OR IGNORE + both directions. Safe to re-run.\n")
        f.write("BEGIN;\n")
        for a, b in sorted(proposed):
            f.write(f"INSERT OR IGNORE INTO link(word_id,target_id,kind_id) "
                    f"VALUES({a},{b},(SELECT id FROM link_kind WHERE name='נרדף'));\n")
            f.write(f"INSERT OR IGNORE INTO link(word_id,target_id,kind_id) "
                    f"VALUES({b},{a},(SELECT id FROM link_kind WHERE name='נרדף'));\n")
        f.write("COMMIT;\n")

    with open(TSV_OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("source\tsynonym\n")
        for s, y in sorted(report):
            f.write(f"{s}\t{y}\n")

    print(f"\nWrote {SQL_OUT}")
    print(f"Wrote {TSV_OUT}")
    print(f"Proposed {len(proposed)} new synonym links "
          f"({len(proposed) * 2} directed rows).")
    if faulted:
        print(f"WARNING: {len(faulted)} words faulted twice and were skipped "
              f"(e.g. {', '.join(faulted[:8])}).")


if __name__ == "__main__":
    main()
