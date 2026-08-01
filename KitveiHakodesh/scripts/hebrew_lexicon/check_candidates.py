#!/usr/bin/env python
"""
check_candidates.py
===================
Pre-flight a drafted word list BEFORE it becomes a terms/batch_*.json file.

Why: roughly a fifth of any freshly-drafted loanword list is already in the
dictionary. Importing anyway is not harmless — the importer dedupes on
(word_id, text), so a near-identical gloss on a word that already exists lands
as a SECOND, redundant sense rather than being rejected. Catching those here
keeps the batch files honest and the entry counts meaningful.

It also catches drafting slips the importer would happily accept:
stray Latin characters mid-word, and the same headword drafted twice.

Usage:
    python check_candidates.py mydraft.json
    python check_candidates.py mydraft.json --out terms/batch_150_loan.json

Input is the same shape as a batch file:
    [ {"hw": "...", "nikud": "..."|null, "defs": ["..."]}, ... ]

With --out, the entries that survive are written there, ready to import.
Without it, the report is printed and nothing is written.

Console note: this box is cp1252, so Hebrew prints as `?`. The report is
written to <input>.report.txt in UTF-8 — read that file, not the terminal.
"""

import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DB = os.path.join(REPO, "CSharpBackend", "KitveiHakodeshService",
                  "Dictionary", "Dictionary.db")

NIKUD_RE = re.compile(r"[֑-ׇ]")
# Hebrew letters, nikud, space, geresh/gershayim, hyphen, and the parens and
# periods that real headwords carry (e.g. abbreviations, disambiguators).
# The point is to catch Latin characters typed by accident, not to police
# punctuation the dictionary already uses.
NON_HEBREW_RE = re.compile(r"[^א-ת֑-ׇ '\"()\.\-]")


def strip_nikud(s: str) -> str:
    return NIKUD_RE.sub("", s or "").strip()


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        return 1
    src = args[0]
    out = None
    if "--out" in sys.argv:
        out = sys.argv[sys.argv.index("--out") + 1]

    with open(src, encoding="utf-8") as f:
        cand = json.load(f)

    conn = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    existing = {r[0] for r in conn.execute("SELECT headword FROM word")}
    glosses = {}
    for hw, text in conn.execute(
            "SELECT w.headword, s.text FROM sense s JOIN word w ON w.id=s.word_id "
            "WHERE s.source_id=6"):
        glosses.setdefault(text, []).append(hw)
    conn.close()

    keep, in_db, malformed, dup, collide = [], [], [], [], []
    seen = set()
    for e in cand:
        hw = strip_nikud(e.get("hw", ""))
        if not hw or not e.get("defs"):
            malformed.append((hw, "empty hw/defs")); continue
        if NON_HEBREW_RE.search(hw):
            malformed.append((hw, "non-Hebrew character in headword")); continue
        if hw in seen:
            dup.append(hw); continue
        seen.add(hw)
        if hw in existing:
            in_db.append(hw); continue
        for d in e["defs"]:
            others = [o for o in glosses.get(d.strip(), []) if o != hw]
            if others:
                collide.append((hw, d, others))
        keep.append(e)

    report = src + ".report.txt"
    with open(report, "w", encoding="utf-8") as f:
        f.write("drafted %d -> keep %d | already in db %d | malformed %d | "
                "dup-in-draft %d\n\n" % (len(cand), len(keep), len(in_db),
                                         len(malformed), len(dup)))
        if malformed:
            f.write("MALFORMED (fix these):\n")
            for hw, why in malformed:
                f.write("  %s  - %s\n" % (hw, why))
            f.write("\n")
        if collide:
            f.write("GLOSS ALREADY ON ANOTHER WORD (reword or drop):\n")
            for hw, d, others in collide:
                f.write("  %s | %s   (also on: %s)\n" % (hw, d, ", ".join(others)))
            f.write("\n")
        if dup:
            f.write("DUPLICATED WITHIN DRAFT:\n  " + "\n  ".join(dup) + "\n\n")
        f.write("ALREADY IN DB (%d):\n  " % len(in_db) + "\n  ".join(in_db))
        f.write("\n\nKEEP (%d):\n" % len(keep))
        for e in keep:
            f.write("  %s | %s\n" % (strip_nikud(e["hw"]), " / ".join(e["defs"])))

    print("drafted %d -> keep %d | in-db %d | malformed %d | dup %d | "
          "gloss-collisions %d" % (len(cand), len(keep), len(in_db),
                                   len(malformed), len(dup), len(collide)))
    print("report: " + report)
    if malformed or collide:
        print("!! review the report before importing — see sections above")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(keep, f, ensure_ascii=False, indent=2)
        print("wrote %d entries -> %s" % (len(keep), out))
    else:
        print("(no --out given; nothing written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
