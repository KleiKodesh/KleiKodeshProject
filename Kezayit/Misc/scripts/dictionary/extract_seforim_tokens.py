"""
extract_seforim_tokens.py
─────────────────────────
Scans all lines from base books in the seforim DB, tokenizes the text,
matches tokens against Hamichlol, and inserts new rows into the dictionary.

Strategy:
- Process lines in chunks of 10,000 to keep memory bounded
- Strip HTML tags with a fast regex (no parser overhead)
- Tokenize: Hebrew word chars + " in middle of word only
- Deduplicate tokens globally and against existing dictionary headwords
- Match against Hamichlol (loaded fully into memory — 97k rows, ~10MB)
- Insert only new matches not already in the dictionary

Usage:
    python Misc/scripts/dictionary/extract_seforim_tokens.py
"""

import sqlite3
import re
import unicodedata
import time

SEFORIM_DB  = r"C:\Users\Public\Documents\seforim-db\seforim.db"
DICT_DB     = "vue-frontend/public/dictionary/kezayit_dictionary.db"
HAMICHLOL   = "Misc/hamichlol-dictionary/hamichlol_disambig.db"
CHUNK_SIZE  = 10_000

# ── Helpers ───────────────────────────────────────────────────────────────────

TAG_RE   = re.compile(r'<[^>]+>')
# Hebrew Unicode block: \u05D0-\u05EA, geresh/gershayim: \u05F3\u05F4, ASCII "
# A token is one or more Hebrew chars, optionally with " or ' in the middle
TOKEN_RE = re.compile(r'[\u05D0-\u05EA\u05F3\u05F4]+'
                      r'(?:["\'״׳][\u05D0-\u05EA\u05F3\u05F4]+)*')

def strip_nikud(s):
    if not s: return s
    return ''.join(
        c for c in unicodedata.normalize('NFD', s)
        if unicodedata.category(c) != 'Mn'
    ).strip()

def tokenize(html):
    text = TAG_RE.sub(' ', html)
    return TOKEN_RE.findall(text)

# ── Load Hamichlol into memory ────────────────────────────────────────────────
print("Loading Hamichlol...")
hm = sqlite3.connect(HAMICHLOL)
hamichlol_rows = hm.execute("SELECT headword, definition FROM entry").fetchall()
hm.close()

# plain → list of definitions
hamichlol = {}
for hw, defn in hamichlol_rows:
    plain = strip_nikud(hw)
    if plain:
        hamichlol.setdefault(plain, []).append(defn)
print(f"  {len(hamichlol):,} unique Hamichlol headwords")

# ── Get or create המכלול source ───────────────────────────────────────────────
dict_db = sqlite3.connect(DICT_DB)
dict_db.execute("PRAGMA journal_mode=WAL")

src = dict_db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()
if src:
    source_id = src[0]
else:
    dict_db.execute("INSERT INTO source (name) VALUES ('המכלול')")
    source_id = dict_db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()[0]
    dict_db.commit()
print(f"המכלול source id={source_id}")

# ── Load existing headwords that already have a המכלול row ───────────────────
existing = set(
    r[0] for r in dict_db.execute(
        "SELECT DISTINCT headword FROM entry WHERE source_id = ?", (source_id,)
    ).fetchall()
)
print(f"  Already have {len(existing):,} headwords with המכלול rows")

# ── Count base book lines ─────────────────────────────────────────────────────
seforim = sqlite3.connect(SEFORIM_DB)
total_lines = seforim.execute(
    "SELECT COUNT(*) FROM line l JOIN book b ON b.id = l.bookId WHERE b.isBaseBook = 1"
).fetchone()[0]
print(f"\nBase book lines to scan: {total_lines:,}")

# ── Scan lines in chunks ──────────────────────────────────────────────────────
seen_tokens = set()   # all tokens seen so far (dedup across chunks)
new_rows    = []      # (headword, None, source_id, definition)
offset      = 0
t0          = time.time()
chunks_done = 0

while True:
    rows = seforim.execute(
        """SELECT l.content FROM line l
           JOIN book b ON b.id = l.bookId
           WHERE b.isBaseBook = 1
           LIMIT ? OFFSET ?""",
        (CHUNK_SIZE, offset)
    ).fetchall()

    if not rows:
        break

    for (content,) in rows:
        if not content:
            continue
        for token in tokenize(content):
            plain = strip_nikud(token)
            if not plain or plain in seen_tokens or plain in existing:
                continue
            seen_tokens.add(plain)
            definitions = hamichlol.get(plain)
            if definitions:
                for defn in definitions:
                    new_rows.append((plain, None, source_id, defn))
                existing.add(plain)  # don't add again from future chunks

    offset      += CHUNK_SIZE
    chunks_done += 1
    elapsed      = time.time() - t0
    pct          = min(offset / total_lines * 100, 100)
    print(f"  {offset:,}/{total_lines:,} lines ({pct:.0f}%)  "
          f"tokens={len(seen_tokens):,}  new_rows={len(new_rows):,}  "
          f"elapsed={elapsed:.0f}s", end='\r')

    # Flush to DB every 50 chunks to keep memory bounded
    if chunks_done % 50 == 0 and new_rows:
        dict_db.executemany(
            "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
            new_rows
        )
        dict_db.commit()
        new_rows = []

seforim.close()
print()  # newline after progress line

# ── Final flush ───────────────────────────────────────────────────────────────
if new_rows:
    dict_db.executemany(
        "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
        new_rows
    )
    dict_db.commit()

total_hamichlol = dict_db.execute(
    "SELECT COUNT(*) FROM entry WHERE source_id = ?", (source_id,)
).fetchone()[0]
dict_db.close()

elapsed = time.time() - t0
print(f"\nDone in {elapsed:.0f}s")
print(f"Unique tokens seen   : {len(seen_tokens):,}")
print(f"Total המכלול rows now: {total_hamichlol:,}")
