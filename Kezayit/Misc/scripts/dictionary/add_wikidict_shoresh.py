"""
add_wikidict_shoresh.py
───────────────────────
Integrates שורש and בניין data from wikidictionary.db into the
Kezayit dictionary as entries in the מילון עברי source.

For each sense with a שורש: adds entry "headword → שורש: א-ב-ד"
For each sense with a בניין: adds entry "headword → בניין: קל"
When both exist: adds combined "headword → שורש: א-ב-ד | בניין: קל"

Uses nikud as the headword when available (since wikidict has nikud).

Usage:
    python Misc/scripts/dictionary/add_wikidict_shoresh.py
"""

import sqlite3, unicodedata, re

DICT_DB   = "vue-frontend/public/dictionary/kezayit_dictionary.db"
WIKI_DB   = "Misc/wikidictionary.db"

def strip_nikud(s):
    if not s: return s
    return ''.join(c for c in unicodedata.normalize('NFD', s)
                   if unicodedata.category(c) != 'Mn').strip()

def clean_shoresh(s):
    """Normalize shoresh to א-ב-ג format."""
    if not s: return None
    s = s.strip()
    # Already has dashes — keep as is
    if '-' in s:
        return s
    # Three letters without dashes — add dashes
    letters = [c for c in s if '\u05D0' <= c <= '\u05EA']
    if len(letters) >= 3:
        return '-'.join(letters[:4] if len(letters) == 4 else letters[:3])
    return s if s else None

def clean_binyan(s):
    """Normalize binyan name."""
    if not s: return None
    s = s.strip()
    # Normalize variants
    s = s.replace('פעל (קל)', 'קל').replace('פעל(קל)', 'קל')
    return s if s else None

# ── Load wikidict data ────────────────────────────────────────────────────────
wiki = sqlite3.connect(WIKI_DB)
senses = wiki.execute("""
    SELECT headword, nikud, shoresh, binyan
    FROM sense
    WHERE (shoresh IS NOT NULL AND shoresh != '')
       OR (binyan  IS NOT NULL AND binyan  != '')
""").fetchall()
wiki.close()

print(f"Wikidict senses with shoresh/binyan: {len(senses):,}")

# ── Get מילון עברי source ─────────────────────────────────────────────────────
dict_db = sqlite3.connect(DICT_DB)
source_id = dict_db.execute("SELECT id FROM source WHERE name = 'מילון עברי'").fetchone()[0]

existing = set(
    (r[0], r[1]) for r in dict_db.execute(
        "SELECT headword, definition FROM entry WHERE source_id = ?", (source_id,)
    ).fetchall()
)

# ── Build new rows ────────────────────────────────────────────────────────────
new_rows = []
seen = set()  # (headword, definition) to avoid duplicates within this batch

for hw, nikud, shoresh, binyan in senses:
    shoresh = clean_shoresh(shoresh)
    binyan  = clean_binyan(binyan)

    # Use nikud form as headword if available, else plain
    display_hw = nikud.strip() if nikud and nikud.strip() else hw.strip()
    # Also use plain form as headword for lookup
    plain_hw = strip_nikud(display_hw)

    # Build definition
    parts = []
    if shoresh:
        parts.append(f"שורש: {shoresh}")
    if binyan:
        parts.append(f"בניין: {binyan}")
    if not parts:
        continue

    defn = " | ".join(parts)

    # Add for both nikud form and plain form
    for hw_to_use in set([display_hw, plain_hw]):
        if not hw_to_use:
            continue
        key = (hw_to_use, defn)
        if key not in existing and key not in seen:
            new_rows.append((hw_to_use, None, source_id, defn))
            seen.add(key)

print(f"New rows to insert: {len(new_rows):,}")

dict_db.executemany(
    "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
    new_rows
)
dict_db.commit()

total = dict_db.execute(
    "SELECT COUNT(*) FROM entry WHERE source_id = ?", (source_id,)
).fetchone()[0]
dict_db.close()

print(f"Total מילון עברי rows: {total:,}")

# ── Sample ────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
rows = db.execute("""
    SELECT headword, definition FROM entry
    WHERE source_id = ? AND definition LIKE 'שורש:%'
    ORDER BY RANDOM() LIMIT 15
""", (source_id,)).fetchall()
print("\nSample שורש entries:")
for hw, defn in rows:
    print(f"  '{hw}' -> {defn}")
db.close()
