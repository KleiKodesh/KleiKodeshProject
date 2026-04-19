"""
rebuild_dict_flat.py
────────────────────
Rebuilds kezayit_dictionary.db as a clean flat schema.

Schema:
    source(id INTEGER PK, name TEXT UNIQUE NOT NULL)

    entry(id INTEGER PK,
          headword  TEXT NOT NULL,
          nikud     TEXT,           -- vocalized form of headword as given by source
          source_id INTEGER FK → source,
          definition TEXT NOT NULL)

    Indexes:
        idx_entry_headword ON entry(headword)  -- forward lookup
        (reverse lookup works via headword index since reverse rows are inserted)

For every original row where the definition is a single word, a reverse row is
also inserted: (headword=plain_definition, nikud=nikud_of_definition_if_any,
source_id=same, definition=original_headword). This makes reverse lookup
(search by definition) work via the same headword index.

Single-word detection: no spaces, no periods, no commas after stripping nikud.
Nikud is stripped from the definition to get the plain headword for the reverse
row. If the definition itself had nikud, that becomes the nikud of the reverse row.

Multi-word definitions are left as-is — no reverse row, no stripping.

Usage:
    python Misc/scripts/dictionary/rebuild_dict_flat.py
"""

import sqlite3
import subprocess
import tempfile
import unicodedata
import os

DB          = "vue-frontend/public/dictionary/kezayit_dictionary.db"
ORIG_COMMIT = "f88ed51"
ORIG_PATH   = "Kezayit/vue-frontend/public/dictionary/kezayit_dictionary.db"

# ── Helpers ───────────────────────────────────────────────────────────────────

def strip_nikud(s):
    if not s:
        return s
    return ''.join(
        c for c in unicodedata.normalize('NFD', s)
        if unicodedata.category(c) != 'Mn'
    ).strip()

def has_nikud(s):
    if not s:
        return False
    return any(unicodedata.category(c) == 'Mn'
               for c in unicodedata.normalize('NFD', s))

def is_single_word(s):
    if not s:
        return False
    plain = strip_nikud(s).rstrip('.,;:!?')
    return ' ' not in plain and '.' not in plain and ',' not in plain and ';' not in plain

# ── Restore original from git ─────────────────────────────────────────────────

tmp = tempfile.mktemp(suffix='.db')
print(f"Restoring original from git {ORIG_COMMIT}...")
result = subprocess.run(
    ["git", "show", f"{ORIG_COMMIT}:{ORIG_PATH}"],
    capture_output=True
)
if result.returncode != 0:
    raise RuntimeError(f"git show failed: {result.stderr.decode()}")
with open(tmp, 'wb') as f:
    f.write(result.stdout)

src = sqlite3.connect(tmp)
rows = src.execute(
    "SELECT headword, nikud, source_label, definition FROM entry"
).fetchall()
src.close()
os.remove(tmp)

print(f"Read {len(rows):,} rows from original.")

# ── Build source lookup ───────────────────────────────────────────────────────

source_name_to_id = {}
next_source_id = [1]

def get_or_create_source(name):
    if not name:
        return None
    if name not in source_name_to_id:
        source_name_to_id[name] = next_source_id[0]
        next_source_id[0] += 1
    return source_name_to_id[name]

for (_, _, source_label, _) in rows:
    get_or_create_source(source_label)

# Rename source
if 'מילון תורת אמת' in source_name_to_id:
    sid = source_name_to_id.pop('מילון תורת אמת')
    source_name_to_id['מילון ארמי עברי'] = sid

def resolve_source(label):
    if label == 'מילון תורת אמת':
        label = 'מילון ארמי עברי'
    return source_name_to_id.get(label)

print(f"Sources: {list(source_name_to_id.keys())}")

# ── Build entry rows including reverse rows ───────────────────────────────────

entry_rows = []   # (headword, nikud, source_id, definition)
reverse_count = 0

for (headword, nikud, source_label, definition) in rows:
    if not headword or not definition:
        continue

    source_id = resolve_source(source_label)

    # Forward row — always
    # If headword has embedded nikud and nikud column is empty, split them
    hw_plain = headword
    hw_nikud = nikud if nikud else None
    if has_nikud(headword) and not nikud:
        hw_plain = strip_nikud(headword)
        hw_nikud = headword

    entry_rows.append((hw_plain, hw_nikud, source_id, definition))

    # Reverse row — only for single-word definitions
    if is_single_word(definition):
        def_plain  = strip_nikud(definition).rstrip('.,;:!?')
        def_nikud  = definition if has_nikud(definition) else None
        entry_rows.append((def_plain, def_nikud, source_id, hw_plain))
        reverse_count += 1

print(f"Forward rows : {len(rows):,}")
print(f"Reverse rows : {reverse_count:,}")
print(f"Total rows   : {len(entry_rows):,}")

# ── Rewrite DB ────────────────────────────────────────────────────────────────

db = sqlite3.connect(DB)
db.execute("PRAGMA journal_mode=WAL")
db.execute("PRAGMA foreign_keys=OFF")

for t in ("nikud_link", "word_link", "word", "nikud", "entry_link",
          "synonym_link", "entry_meaning", "meaning", "entry", "source"):
    db.execute(f"DROP TABLE IF EXISTS {t}")

# source
db.execute("""
    CREATE TABLE source (
        id   INTEGER PRIMARY KEY,
        name TEXT    NOT NULL UNIQUE
    )
""")
db.executemany(
    "INSERT INTO source (id, name) VALUES (?, ?)",
    ((v, k) for k, v in source_name_to_id.items())
)

# entry
db.execute("""
    CREATE TABLE entry (
        id        INTEGER PRIMARY KEY AUTOINCREMENT,
        headword  TEXT    NOT NULL,
        nikud     TEXT,
        source_id INTEGER REFERENCES source(id),
        definition TEXT   NOT NULL
    )
""")
db.execute("CREATE INDEX idx_entry_headword ON entry(headword)")

db.executemany(
    "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
    entry_rows
)

db.execute("PRAGMA foreign_keys=ON")
db.commit()
db.execute("VACUUM")
db.close()

# ── Verify ────────────────────────────────────────────────────────────────────

db = sqlite3.connect(DB)
ec = db.execute("SELECT COUNT(*) FROM entry").fetchone()[0]
sc = db.execute("SELECT COUNT(*) FROM source").fetchone()[0]

print()
print("Final DB:")
print(f"  source : {sc} rows")
print(f"  entry  : {ec:,} rows")

print()
print("Sample — forward lookup 'קים':")
for r in db.execute("""
    SELECT e.headword, e.nikud, e.definition, s.name
    FROM entry e LEFT JOIN source s ON s.id = e.source_id
    WHERE e.headword = 'קים'
""").fetchall():
    print(f"  '{r[0]}' ({r[1]}) → '{r[2]}'  [{r[3]}]")

print()
print("Sample — reverse lookup 'נדר':")
for r in db.execute("""
    SELECT e.headword, e.nikud, e.definition, s.name
    FROM entry e LEFT JOIN source s ON s.id = e.source_id
    WHERE e.headword = 'נדר'
""").fetchall():
    print(f"  '{r[0]}' ({r[1]}) → '{r[2]}'  [{r[3]}]")

db.close()
print("\nDone.")

# Run Hamichlol enrichment (existing headwords + Aramaic source scans)
print("\nRunning Aramaic Hamichlol enrichment...")
import subprocess, sys
subprocess.run([sys.executable, "Misc/scripts/dictionary/add_aramaic_hamichlol.py"], check=True)
