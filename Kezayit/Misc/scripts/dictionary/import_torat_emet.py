"""
import_torat_emet.py
────────────────────
Rebuilds the מילון ארמי עברי portion of kezayit_dictionary.db
from the original Torat Emet source files at:
  C:/Users/Admin/Documents/ToratEmetInstall/Dictionaries/

Source file format (FinalDictionary.txt, windows-1255):
  // comment lines — skip
  <code> <headword>[={nikud}] <definition> [*** {nikud2} definition2 ...]

  code 0 — headword may have nikud inline, no braces
  code 1 — headword plain, definition may have nikud inline
  code 3 — headword plain, nikud in {braces} before definition
  code 2 — single special entry

  *** separates multiple meanings for the same headword+nikud

  (=word) prefix in definition = cross-reference, strip and add as synonym

Usage:
  python Misc/scripts/dictionary/import_torat_emet.py
"""

import sqlite3, re, unicodedata, os

SRC  = 'C:/Users/Admin/Documents/ToratEmetInstall/Dictionaries/FinalDictionary.txt'
KZ   = 'vue-frontend/public/dictionary/kezayit_dictionary.db'
WK   = 'vue-frontend/public/dictionary/wikidict.db'
SOURCE_ID = 1  # מילון ארמי עברי

ABBREV_RE  = re.compile(r'["\u05F4\u05F3]')
CROSSREF_RE = re.compile(r'^\(=([^)]+)\)\s*')

def strip_nikud(s):
    return ''.join(
        c for c in unicodedata.normalize('NFD', s)
        if unicodedata.category(c) != 'Mn'
    ).strip()

def has_nikud(s):
    return any(unicodedata.category(c) == 'Mn'
               for c in unicodedata.normalize('NFD', s))

def is_single_word(s):
    plain = strip_nikud(s).rstrip('.,;:!?')
    return bool(plain) and ' ' not in plain and ',' not in plain

def parse_crossrefs(text):
    """Strip leading (=...) cross-references, return (synonyms, remainder)."""
    synonyms = []
    remainder = text
    while True:
        m = CROSSREF_RE.match(remainder)
        if not m:
            break
        for s in m.group(1).split(','):
            s = s.strip().rstrip('.')
            if s:
                synonyms.append(s)
        remainder = remainder[m.end():].strip()
    return synonyms, remainder

def parse_line(line):
    """
    Returns list of (headword, nikud, definition, synonyms) tuples.
    One line can produce multiple tuples via *** separator.
    """
    line = line.strip()
    if not line or line.startswith('//'):
        return []

    parts = line.split(None, 1)
    if len(parts) < 2 or parts[0] not in ('0', '1', '2', '3'):
        return []

    code, rest = parts
    if '=' not in rest:
        return []

    eq = rest.index('=')
    raw_hw  = rest[:eq].strip()
    raw_def = rest[eq+1:].strip()

    # Split on *** for multiple meanings
    segments = [s.strip() for s in raw_def.split('***')]

    results = []
    for seg in segments:
        if not seg:
            continue

        # Extract nikud from {braces} at start of segment (code 3 style)
        nikud = None
        defn  = seg
        m = re.match(r'^\{([^}]+)\}\s*', seg)
        if m:
            nikud = m.group(1).strip()
            defn  = seg[m.end():].strip()

        # For code 0: headword itself may carry nikud
        hw = raw_hw
        if code == '0' and has_nikud(raw_hw):
            nikud = raw_hw
            hw    = strip_nikud(raw_hw)
        elif code == '1' and has_nikud(raw_hw) and not nikud:
            nikud = raw_hw
            hw    = strip_nikud(raw_hw)

        # Strip trailing period
        defn = defn.rstrip('.')

        if not defn:
            continue

        # Extract cross-references from definition
        synonyms, defn = parse_crossrefs(defn)
        defn = defn.strip().rstrip('.')

        if not defn:
            # Whole definition was a cross-ref — treat as synonym only, no def row
            results.append((hw, nikud, None, synonyms))
        else:
            results.append((hw, nikud, defn, synonyms))

    return results


# ── Read source ───────────────────────────────────────────────────────────────

with open(SRC, 'rb') as f:
    text = f.read().decode('windows-1255')

all_entries = []
for line in text.splitlines():
    all_entries.extend(parse_line(line))

print(f"Parsed {len(all_entries)} entries from source")

# ── Update DB ─────────────────────────────────────────────────────────────────

kz = sqlite3.connect(KZ)
wk = sqlite3.connect(WK)

# Delete all existing מילון ארמי עברי entries
deleted = kz.execute("DELETE FROM entry WHERE source_id = ?", (SOURCE_ID,)).rowcount
print(f"Deleted {deleted} existing source_id=1 rows")

# Insert fresh
inserted = 0
synonym_count = 0

for (hw, nikud, defn, synonyms) in all_entries:
    if defn:
        kz.execute(
            "INSERT INTO entry(headword, nikud, source_id, definition) VALUES(?,?,?,?)",
            (hw, nikud, SOURCE_ID, defn)
        )
        inserted += 1

    # Add synonyms to wikidict
    if synonyms:
        sense = wk.execute("SELECT id FROM sense WHERE headword=? LIMIT 1", (hw,)).fetchone()
        if not sense:
            cur = wk.execute(
                "INSERT INTO sense(headword,nikud,pos,binyan,shoresh,ktiv_male) VALUES(?,?,NULL,NULL,NULL,NULL)",
                (hw, nikud)
            )
            sense_id = cur.lastrowid
        else:
            sense_id = sense[0]
        for s in synonyms:
            exists = wk.execute(
                "SELECT 1 FROM related WHERE sense_id=? AND kind='synonym' AND word=?",
                (sense_id, s)
            ).fetchone()
            if not exists:
                wk.execute("INSERT INTO related(sense_id,kind,word) VALUES(?,?,?)",
                           (sense_id, 'synonym', s))
                synonym_count += 1

kz.commit()
wk.commit()

print(f"Inserted {inserted} entry rows")
print(f"Added {synonym_count} synonym links")

# ── Verify ────────────────────────────────────────────────────────────────────
print("\n=== אברא ===")
for r in kz.execute("SELECT headword, nikud, definition FROM entry WHERE headword='אברא' AND source_id=1").fetchall():
    print(' ', r)

print("\n=== איתא ===")
for r in kz.execute("SELECT headword, nikud, definition FROM entry WHERE headword='איתא' AND source_id=1").fetchall():
    print(' ', r)

kz.close()
wk.close()
print("\nDone.")
