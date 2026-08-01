#!/usr/bin/env python3
"""
Add hand-curated spelling/inflection REDIRECTS to the KitveiHakodesh dictionary.

A redirect makes a typed variant spelling resolve to an EXISTING entry's senses,
WITHOUT copying any definition text. Mechanism = the existing `link` table with
kind 'כתיב' (spelling variant): we add a word row for the variant (with NO senses
of its own) and a link  variant -> base  (word_id = variant, target_id = base).

The dictionary lookup's Exact() follows this link when the typed word has no
senses of its own (see DictionaryService.Exact / dictExact). Because the variant
word has no senses, it is invisible to the prefix/contains/spell tiers (those use
an INNER JOIN on sense) — so there is zero result-list pollution.

SAFETY: every (base, variant) pair is HAND-VERIFIED to be the SAME WORD. This tool
adds nothing on its own; it only wires what the curated batch files list.
  - Skips a variant that is already a headword WITH its own senses (it's a real
    distinct word — never redirect a real entry).
  - Skips a base that is not an existing headword (nothing to point at).
  - Idempotent: re-running never duplicates word rows or links.

Batch files: scripts/hebrew_lexicon/redirects/*.json
  [ {"base": "מדוכה", "variants": ["מדוכות", "מדוכת"]}, ... ]

Usage:
  python add_ketiv_redirects.py [--dry-run]
"""
import json, os, sys, glob, sqlite3

ROOT = os.path.dirname(os.path.abspath(__file__))
REDIR_DIR = os.path.join(ROOT, "redirects")
TARGETS = {
    "service":  r"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db",
    "frontend": r"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\vue-frontend\public\dictionary\KitveiHakodesh_dictionary.db",
}
KETIV = "כתיב"

def load_batches():
    pairs = []  # (base, variant)
    seen = set()
    for f in sorted(glob.glob(os.path.join(REDIR_DIR, "*.json"))):
        for entry in json.load(open(f, encoding="utf-8")):
            base = entry["base"].strip()
            for v in entry.get("variants", []):
                v = v.strip()
                if not base or not v or v == base:
                    continue
                if (base, v) in seen:
                    continue
                seen.add((base, v))
                pairs.append((base, v))
    return pairs

def process(db_path, pairs, dry):
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    kind_row = cur.execute("SELECT id FROM link_kind WHERE name=?", (KETIV,)).fetchone()
    if not kind_row:
        conn.close()
        return f"  {os.path.basename(db_path)}: NO 'כתיב' link_kind — aborted"
    kind_id = kind_row[0]

    added_words = added_links = skip_realword = skip_nobase = already = 0
    for base, variant in pairs:
        brow = cur.execute("SELECT id FROM word WHERE headword=?", (base,)).fetchone()
        if not brow:
            skip_nobase += 1
            continue
        base_id = brow[0]

        vrow = cur.execute("SELECT id FROM word WHERE headword=?", (variant,)).fetchone()
        if vrow:
            var_id = vrow[0]
            has_senses = cur.execute(
                "SELECT 1 FROM sense WHERE word_id=? LIMIT 1", (var_id,)).fetchone()
            if has_senses:
                skip_realword += 1   # variant is a real entry — do NOT redirect it
                continue
        else:
            if not dry:
                var_id = cur.execute(
                    "INSERT INTO word(headword) VALUES(?)", (variant,)).lastrowid
            else:
                var_id = -1
            added_words += 1

        exists = var_id != -1 and cur.execute(
            "SELECT 1 FROM link WHERE word_id=? AND target_id=? AND kind_id=?",
            (var_id, base_id, kind_id)).fetchone()
        if exists:
            already += 1
            continue
        if not dry:
            cur.execute(
                "INSERT INTO link(word_id, target_id, kind_id) VALUES(?,?,?)",
                (var_id, base_id, kind_id))
        added_links += 1

    if not dry:
        conn.commit()
        cur.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        conn.commit()
    conn.close()
    return (f"  {os.path.basename(db_path)}: +{added_words} variant words, "
            f"+{added_links} redirects  (skip real-word={skip_realword}, "
            f"missing-base={skip_nobase}, already={already})")

def main():
    dry = "--dry-run" in sys.argv
    pairs = load_batches()
    print(f"loaded {len(pairs)} (base,variant) redirect pairs from {REDIR_DIR}")
    for _, path in TARGETS.items():
        if not os.path.exists(path):
            print(f"  MISSING DB: {path}")
            continue
        print(process(path, pairs, dry))
    if dry:
        print("(dry-run: no writes)")

if __name__ == "__main__":
    main()
