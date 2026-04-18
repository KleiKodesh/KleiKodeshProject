"""
add_hamichlol_definitions.py
────────────────────────────
Adds Hamichlol definitions as new rows in kezayit_dictionary.db.
Each matched headword gets a new entry row with source = 'המכלול'.

Match strategy: exact match on plain (nikud-stripped) headword.
Multiple Hamichlol matches for the same headword → multiple rows.

Usage:
    python Misc/scripts/add_hamichlol_definitions.py
"""

import sqlite3
import unicodedata

DICT_DB   = "vue-frontend/public/dictionary/kezayit_dictionary.db"
HAMICHLOL = "Misc/hamichlol-dictionary/hamichlol_disambig.db"

def strip_nikud(s):
    if not s: return s
    return ''.join(
        c for c in unicodedata.normalize('NFD', s)
        if unicodedata.category(c) != 'Mn'
    ).strip()

# ── Load Hamichlol ────────────────────────────────────────────────────────────
print("Loading Hamichlol...")
hm = sqlite3.connect(HAMICHLOL)
hamichlol_rows = hm.execute("SELECT headword, definition FROM entry").fetchall()
hm.close()

# Build plain→[definitions] map (keep all matches)
hamichlol = {}
for hw, defn in hamichlol_rows:
    plain = strip_nikud(hw)
    if plain:
        hamichlol.setdefault(plain, []).append(defn)

print(f"Hamichlol: {len(hamichlol_rows):,} rows, {len(hamichlol):,} unique plain headwords")

# ── Get or create המכלול source ───────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)

# Remove any previously added המכלול rows so script is idempotent
existing_source = db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()
if existing_source:
    db.execute("DELETE FROM entry WHERE source_id = ?", (existing_source[0],))
    source_id = existing_source[0]
    print(f"Removed existing המכלול rows, reusing source id={source_id}")
else:
    db.execute("INSERT INTO source (name) VALUES ('המכלול')")
    source_id = db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()[0]
    print(f"Created המכלול source id={source_id}")

# ── Get all unique headwords currently in the dictionary ─────────────────────
existing_headwords = set(
    r[0] for r in db.execute("SELECT DISTINCT headword FROM entry").fetchall()
)
print(f"Unique headwords in dictionary: {len(existing_headwords):,}")

# ── Insert new rows ───────────────────────────────────────────────────────────
new_rows = []
matched_headwords = 0

for headword in existing_headwords:
    plain = strip_nikud(headword)
    definitions = hamichlol.get(plain)
    if definitions:
        matched_headwords += 1
        for defn in definitions:
            new_rows.append((headword, None, source_id, defn))

db.executemany(
    "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
    new_rows
)
db.commit()
db.close()

print(f"Matched headwords: {matched_headwords:,}")
print(f"New rows inserted: {len(new_rows):,}")

# ── Sample ────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
print("\nSample המכלול rows:")
rows = db.execute("""
    SELECT e.headword, e.definition
    FROM entry e
    WHERE e.source_id = ?
    LIMIT 10
""", (source_id,)).fetchall()
for hw, defn in rows:
    print(f"  '{hw}' → '{defn[:80]}'")

total = db.execute("SELECT COUNT(*) FROM entry").fetchone()[0]
print(f"\nTotal entry rows now: {total:,}")
db.close()
