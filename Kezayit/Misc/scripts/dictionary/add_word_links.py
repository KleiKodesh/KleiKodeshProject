"""
add_word_links.py
─────────────────
Extracts word links from wikidictionary.db and adds them to
kezayit_dictionary.db as a new word_link table.

Only adds links where BOTH the source and target headwords exist
in the kezayit dictionary — no dead links.

Link types extracted:
  מילים נרדפות → נרדף (synonym)
  ניגודים       → ניגוד (antonym)
  נגזרות        → נגזרת (derived form)

Schema:
  word_link(from_headword TEXT, to_headword TEXT, link_type TEXT,
            PRIMARY KEY (from_headword, to_headword, link_type))

Usage:
    python Misc/scripts/dictionary/add_word_links.py
"""

import sqlite3, unicodedata, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

DICT_DB  = "vue-frontend/public/dictionary/kezayit_dictionary.db"
WIKI_DB  = "Misc/wikidictionary.db"

def strip_nikud(s):
    if not s: return s
    return ''.join(c for c in unicodedata.normalize('NFD', s)
                   if unicodedata.category(c) != 'Mn').strip()

# ── Load all headwords from kezayit dictionary ────────────────────────────────
dict_db = sqlite3.connect(DICT_DB)
dict_headwords = set(r[0] for r in dict_db.execute("SELECT DISTINCT headword FROM entry").fetchall())
print(f"Dictionary headwords: {len(dict_headwords):,}")

# ── Drop and recreate word_link table ─────────────────────────────────────────
dict_db.execute("DROP TABLE IF EXISTS word_link")
dict_db.execute("""
    CREATE TABLE word_link (
        from_headword TEXT NOT NULL,
        to_headword   TEXT NOT NULL,
        link_type     TEXT NOT NULL,
        PRIMARY KEY (from_headword, to_headword, link_type)
    )
""")
dict_db.execute("CREATE INDEX idx_word_link_from ON word_link(from_headword)")
dict_db.execute("CREATE INDEX idx_word_link_to   ON word_link(to_headword)")

# ── Extract links from wikidict ───────────────────────────────────────────────
SECTION_MAP = {
    'מילים נרדפות': 'נרדף',
    'ניגודים':       'ניגוד',
    'נגזרות':        'נגזרת',
}

wiki = sqlite3.connect(WIKI_DB)
rows = wiki.execute("""
    SELECT se.headword, si.text, sn.name
    FROM section_item si
    JOIN section s ON s.id = si.section_id
    JOIN section_name sn ON sn.id = s.name_id
    JOIN sense se ON se.id = s.sense_id
    WHERE sn.name IN ('מילים נרדפות', 'ניגודים', 'נגזרות')
      AND si.text IS NOT NULL AND si.text != ''
""").fetchall()
wiki.close()

print(f"Wiki link candidates: {len(rows):,}")

# ── Filter to only links where both ends exist in dictionary ──────────────────
new_links = set()
skipped = 0

for hw, target, section_name in rows:
    from_plain = strip_nikud(hw)
    to_plain   = strip_nikud(target)
    link_type  = SECTION_MAP.get(section_name)

    if not from_plain or not to_plain or not link_type:
        continue
    if from_plain == to_plain:
        continue
    if from_plain not in dict_headwords or to_plain not in dict_headwords:
        skipped += 1
        continue

    new_links.add((from_plain, to_plain, link_type))

print(f"Skipped (not in dictionary): {skipped:,}")
print(f"Valid links: {len(new_links):,}")

dict_db.executemany(
    "INSERT OR IGNORE INTO word_link (from_headword, to_headword, link_type) VALUES (?, ?, ?)",
    new_links
)
dict_db.commit()
dict_db.execute("VACUUM")
dict_db.close()

# ── Report ────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
total = db.execute("SELECT COUNT(*) FROM word_link").fetchone()[0]
by_type = db.execute("SELECT link_type, COUNT(*) FROM word_link GROUP BY link_type ORDER BY COUNT(*) DESC").fetchall()
print(f"\nTotal word_link rows: {total:,}")
for lt, cnt in by_type:
    print(f"  {lt}: {cnt:,}")

print("\nSample links:")
for r in db.execute("SELECT from_headword, to_headword, link_type FROM word_link LIMIT 15").fetchall():
    print(f"  '{r[0]}' --{r[2]}--> '{r[1]}'")
db.close()
