"""
normalize_dict_sources.py
─────────────────────────
Migrates kezayit_dictionary.db from a denormalized source_label TEXT column
on the entry table to a normalized source lookup table with an integer FK.

Before:
    entry(id, headword, nikud, source_label TEXT, definition)

After:
    source(id INTEGER PK, name TEXT UNIQUE NOT NULL)
    entry(id, headword, nikud, source_id INTEGER FK → source, definition)

The original DB file is left untouched. The output is written to a new file
so the migration can be verified before replacing the original.

Usage:
    python Misc/scripts/dictionary/normalize_dict_sources.py

Output:
    vue-frontend/public/dictionary/kezayit_dictionary_normalized.db

Once verified, rename it over the original:
    kezayit_dictionary_normalized.db → kezayit_dictionary.db
"""

import sqlite3
import shutil
import os

SRC = "vue-frontend/public/dictionary/kezayit_dictionary.db"
DST = "vue-frontend/public/dictionary/kezayit_dictionary_normalized.db"

# ── 1. Copy the original so we start from a clean slate ──────────────────────
if os.path.exists(DST):
    os.remove(DST)
shutil.copy2(SRC, DST)

db = sqlite3.connect(DST)
db.execute("PRAGMA journal_mode=WAL")
db.execute("PRAGMA foreign_keys=ON")

# ── 2. Create the source lookup table ────────────────────────────────────────
db.execute("""
    CREATE TABLE source (
        id   INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT    NOT NULL UNIQUE
    )
""")

# ── 3. Populate source from the distinct values already in entry ──────────────
db.execute("""
    INSERT INTO source (name)
    SELECT DISTINCT source_label
    FROM entry
    WHERE source_label IS NOT NULL
    ORDER BY source_label
""")

# ── 4. Add source_id column to entry ─────────────────────────────────────────
db.execute("ALTER TABLE entry ADD COLUMN source_id INTEGER REFERENCES source(id)")

# ── 5. Fill source_id from the lookup ────────────────────────────────────────
db.execute("""
    UPDATE entry
    SET source_id = (
        SELECT id FROM source WHERE source.name = entry.source_label
    )
""")

# ── 6. Verify no rows were left without a source_id ─────────────────────────
cur = db.execute("SELECT COUNT(*) FROM entry WHERE source_id IS NULL AND source_label IS NOT NULL")
orphans = cur.fetchone()[0]
if orphans:
    db.close()
    os.remove(DST)
    raise RuntimeError(f"{orphans} rows could not be mapped to a source — aborting.")

# ── 7. Rebuild entry without source_label ────────────────────────────────────
db.execute("""
    CREATE TABLE entry_new (
        id        INTEGER PRIMARY KEY,
        headword  TEXT    NOT NULL,
        nikud     TEXT,
        source_id INTEGER REFERENCES source(id),
        definition TEXT   NOT NULL
    )
""")

db.execute("""
    INSERT INTO entry_new (id, headword, nikud, source_id, definition)
    SELECT id, headword, nikud, source_id, definition
    FROM entry
""")

db.execute("DROP TABLE entry")
db.execute("ALTER TABLE entry_new RENAME TO entry")

# ── 8. Recreate any indexes that existed on the original entry table ──────────
# Original had no explicit indexes beyond the PK; add the headword index
# that the frontend queries rely on.
db.execute("CREATE INDEX idx_entry_headword ON entry(headword)")

# ── 9. Verify row counts match ───────────────────────────────────────────────
orig = sqlite3.connect(SRC)
orig_count = orig.execute("SELECT COUNT(*) FROM entry").fetchone()[0]
orig.close()

new_count = db.execute("SELECT COUNT(*) FROM entry").fetchone()[0]
if orig_count != new_count:
    db.close()
    os.remove(DST)
    raise RuntimeError(f"Row count mismatch: original={orig_count}, new={new_count}")

# ── 10. Compact ──────────────────────────────────────────────────────────────
db.commit()
db.execute("VACUUM")
db.close()

# ── 11. Report ───────────────────────────────────────────────────────────────
src_size = os.path.getsize(SRC)
dst_size = os.path.getsize(DST)

print(f"Done.")
print(f"  Original : {SRC} ({src_size:,} bytes)")
print(f"  Normalized: {DST} ({dst_size:,} bytes)")
print(f"  Rows     : {new_count:,} (matches original)")
print()
print("Sources created:")
db2 = sqlite3.connect(DST)
for row in db2.execute("SELECT id, name FROM source ORDER BY id"):
    count = db2.execute("SELECT COUNT(*) FROM entry WHERE source_id=?", (row[0],)).fetchone()[0]
    print(f"  {row[0]:2}  {row[1]}  ({count:,} entries)")
db2.close()
print()
print("Verify the output, then rename kezayit_dictionary_normalized.db → kezayit_dictionary.db")
