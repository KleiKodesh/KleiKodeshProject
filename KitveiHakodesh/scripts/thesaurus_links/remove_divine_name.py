#!/usr/bin/env python3
"""Remove the sacred Name from the dictionary (2026-07-21, user-requested).

The Name should not appear as a lookup entry, and should not be written out
letter-by-letter inside sense text. Applied IDENTICALLY to both DB copies.

Scope (user chose "entry + scrub sense text"; theological words like אלהים/אלוה/אל
and homographs like שקי/הויה-as-term are KEPT — only the Name itself is removed):

  1. DELETE entry word id=5440 headword 'י-ה' (sense 7295 = "one of God's names",
     nikud field holds the vocalized Name) + its sense row. 0 links, safe.
  2. sense 29276 (word בילאו"א): 'ברוך יהוה לעולם: אמן ואמן'
        -> 'ברוך ה' לעולם: אמן ואמן'   (יהוה replaced with the reverent ה')
  3. sense 36600 (word הויה): 'שם הוי"ה(י-ה-ו-ה) (נקרא גם...'
        -> 'שם הוי"ה (נקרא גם...'        (drop only the spelled-out (י-ה-ו-ה);
                                          the הוי"ה gershayim form is the reverent
                                          reference and stays)

Idempotent: guards on exact expected text; a second run finds nothing to change.
Transactional per DB (explicit BEGIN + commit/rollback, never executescript).

    python remove_divine_name.py --dry-run
    python remove_divine_name.py
"""
import argparse
import hashlib
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DBS = [
    os.path.normpath(os.path.join(
        HERE, r"..\..\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db")),
    os.path.normpath(os.path.join(
        HERE, r"..\..\vue-frontend\public\dictionary\KitveiHakodesh_dictionary.db")),
]

NAME_WORD_ID = 5440          # headword 'י-ה'
BILAVA_SENSE = 29276
BILAVA_OLD = "ברוך יהוה לעולם: אמן ואמן"
BILAVA_NEW = "ברוך ה' לעולם: אמן ואמן"
HAVAYA_SENSE = 36600
HAVAYA_OLD = 'שם הוי"ה(י-ה-ו-ה) (נקרא גם השם המפורש, השם המיוחד, ושם בן ארבע אותיות).'
HAVAYA_NEW = 'שם הוי"ה (נקרא גם השם המפורש, השם המיוחד, ושם בן ארבע אותיות).'


def content_hash(con):
    h = hashlib.sha256()
    for tbl in ["word", "sense", "link", "link_kind"]:
        ncol = len(con.execute(f"SELECT * FROM {tbl} LIMIT 1").description)
        order = ",".join(str(i + 1) for i in range(ncol))
        for row in con.execute(f"SELECT * FROM {tbl} ORDER BY {order}"):
            h.update(repr(row).encode("utf-8"))
    return h.hexdigest()


def apply(con):
    changes = []
    con.execute("BEGIN")
    # 1. delete the Name entry (senses first for FK cleanliness, then word, then any links)
    if con.execute("SELECT 1 FROM word WHERE id=?", (NAME_WORD_ID,)).fetchone():
        con.execute("DELETE FROM link WHERE word_id=? OR target_id=?",
                    (NAME_WORD_ID, NAME_WORD_ID))
        con.execute("DELETE FROM sense WHERE word_id=?", (NAME_WORD_ID,))
        con.execute("DELETE FROM word WHERE id=?", (NAME_WORD_ID,))
        changes.append("deleted word 5440 (Name entry)")
    # 2. בילאו"א phrase
    n = con.execute("UPDATE sense SET text=? WHERE id=? AND text=?",
                    (BILAVA_NEW, BILAVA_SENSE, BILAVA_OLD)).rowcount
    if n:
        changes.append(f"reworded sense {BILAVA_SENSE}")
    # 3. הויה parenthetical
    n = con.execute("UPDATE sense SET text=? WHERE id=? AND text=?",
                    (HAVAYA_NEW, HAVAYA_SENSE, HAVAYA_OLD)).rowcount
    if n:
        changes.append(f"scrubbed sense {HAVAYA_SENSE}")
    return changes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    for p in DBS:
        if not os.path.exists(p):
            sys.exit(f"missing DB: {p}")

    hashes = []
    for p in DBS:
        con = sqlite3.connect(p)
        try:
            changes = apply(con)
            if a.dry_run:
                con.rollback()
            else:
                con.commit()
                ic = con.execute("PRAGMA integrity_check").fetchone()[0]
                if ic != "ok":
                    sys.exit(f"integrity_check failed on {p}: {ic}")
                con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
            hashes.append(content_hash(con))
            tag = "[dry-run] " if a.dry_run else ""
            print(f"{tag}{os.path.basename(p)}: {changes or 'no change (already clean)'}")
        finally:
            con.close()

    print("content hashes:", [h[:16] for h in hashes])
    print("COPIES IDENTICAL" if len(set(hashes)) == 1 else "COPIES DIFFER")


if __name__ == "__main__":
    main()
