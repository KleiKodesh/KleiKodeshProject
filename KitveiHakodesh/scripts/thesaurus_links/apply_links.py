#!/usr/bin/env python3
"""Apply proposed_nardaf_links.sql to BOTH Dictionary.db copies.

Reads the review file link_synonyms.py produced and inserts the new 'נרדף'
links into the service DB and the frontend DB, keeping them identical, then
checkpoints the WAL so the committed .db files are complete.

Idempotent: every statement is INSERT OR IGNORE and the link PK dedups, so
re-running adds nothing new.

    python apply_links.py --dry-run   # show counts, write NOTHING
    python apply_links.py             # apply to both DBs + checkpoint

NOTE: we deliberately execute the INSERT statements one-by-one inside a
transaction WE control (not sqlite3.executescript). executescript() issues an
implicit COMMIT before running, so a --dry-run rollback would NOT undo the
inserts — it would silently write. Parsing + explicit commit/rollback avoids
that trap.
"""
import argparse
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SQL_FILE = os.path.join(HERE, "proposed_nardaf_links.sql")

# Both copies must stay identical (see Dictionary/ADDING_SENSES.md).
DBS = [
    os.path.normpath(os.path.join(
        HERE, r"..\..\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db")),
    os.path.normpath(os.path.join(
        HERE, r"..\..\vue-frontend\public\dictionary\KitveiHakodesh_dictionary.db")),
]

NARDAF_COUNT = ("SELECT COUNT(*) FROM link WHERE kind_id="
                "(SELECT id FROM link_kind WHERE name='נרדף')")

# Pull just the (word_id, target_id) pairs out of the generated INSERTs; we
# re-issue them ourselves against the resolved נרדף kind_id.
_INSERT = re.compile(r"INSERT OR IGNORE INTO link\(word_id,target_id,kind_id\) "
                     r"VALUES\((\d+),(\d+),")


def load_pairs():
    pairs = []
    with open(SQL_FILE, encoding="utf-8") as f:
        for line in f:
            m = _INSERT.search(line)
            if m:
                pairs.append((int(m.group(1)), int(m.group(2))))
    return pairs


def apply_to(db, pairs, dry_run):
    con = sqlite3.connect(db)
    try:
        kid = con.execute(
            "SELECT id FROM link_kind WHERE name='נרדף'").fetchone()[0]
        before = con.execute(NARDAF_COUNT).fetchone()[0]

        con.execute("BEGIN")
        con.executemany(
            "INSERT OR IGNORE INTO link(word_id,target_id,kind_id) VALUES(?,?,?)",
            [(a, b, kid) for a, b in pairs])
        after = con.execute(NARDAF_COUNT).fetchone()[0]

        if dry_run:
            con.rollback()
            print(f"[dry-run] {os.path.basename(db)}: {before} -> {after} "
                  f"(+{after - before}) — rolled back, nothing written")
            return

        con.commit()
        ic = con.execute("PRAGMA integrity_check").fetchone()[0]
        con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        print(f"{os.path.basename(db)}: {before} -> {after} "
              f"(+{after - before}), integrity_check={ic}")
    finally:
        con.close()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(SQL_FILE):
        sys.exit(f"Missing {SQL_FILE} — run link_synonyms.py first.")
    for db in DBS:
        if not os.path.exists(db):
            sys.exit(f"DB not found: {db}")

    pairs = load_pairs()
    print(f"proposed directed rows: {len(pairs)}")

    for db in DBS:
        apply_to(db, pairs, args.dry_run)

    if not args.dry_run:
        print("Done. Both DB copies updated + checkpointed. Verify, then commit "
              "only the two .db files.")


if __name__ == "__main__":
    main()
