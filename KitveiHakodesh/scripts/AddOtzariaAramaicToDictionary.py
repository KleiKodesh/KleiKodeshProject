"""
AddOtzariaAramaicToDictionary.py
================================
Imports Otzaria's expanded Aramaic-Hebrew dictionary (the "milon Peshita" list
in Otzaria's assets/dictionary.json, github.com/Otzaria/otzaria branch `dev`,
commit a4c54cd2, 2026-10-27, ~6,300 entries) into the Core Dictionary.db as a
new source_kind.

JSON shape: {dict-name: [{aramaic: meaning}, ...], ...}. Only the first list
(MAIN_LIST_NAME) is imported. A meaning is stored the same way the older
Aramaic source (source_id 1, the earlier version of this list) stores it:
  - `***` separates senses -> one sense row per part
  - a leading `{vocalized form}` goes to sense.nikud, not into the text
  - whitespace collapsed, trailing '.', ',', ';', ':' dropped
  - parentheses / brackets / '=' notes are kept as written (source 1 keeps them)
Headwords are stored without nikud.

Dedupe: a sense whose normalized text (nikud stripped, whitespace collapsed,
yod/vav ignored - same rule as AddOtzariaAcronymsToDictionary.py) already
exists for the same headword in ANY source is skipped; repeats inside the
list are merged.

Re-runnable: senses previously imported for this source are deleted first,
then orphaned words.

Run: python -I AddOtzariaAramaicToDictionary.py <dictionary.json> [--dry-run]
"""

import json
import re
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path

DICT_DB = str(Path(__file__).resolve().parent.parent
              / "CSharpBackend" / "KitveiHakodesh.Core" / "Dictionary" / "Dictionary.db")

# "milon Peshita" (the main Aramaic list in Otzaria's dictionary.json)
MAIN_LIST_NAME = "מילון פשיטא"
# "Otzaria - milon Peshita"
SOURCE_NAME = "אוצריא - " + MAIN_LIST_NAME

NIKUD_RE = re.compile(r'[֑-ׇ]')
LEADING_NIKUD_FORM_RE = re.compile(r'^\{([^{}]*)\}\s*')


def strip_nikud(text: str) -> str:
    return NIKUD_RE.sub('', text)


def normalize_for_dedup(text: str) -> str:
    """Nikud-free, whitespace-collapsed, trailing punctuation dropped, yod/vav
    ignored (same key as AddOtzariaAcronymsToDictionary.py)."""
    text = strip_nikud(text)
    text = re.sub(r'\s+', ' ', text).strip()
    text = text.rstrip('.,;:')
    return text.replace('י', '').replace('ו', '')


def clean_sense(part: str) -> tuple[str | None, str]:
    """One `***` part -> (nikud of headword or None, gloss text)."""
    part = re.sub(r'\s+', ' ', part).strip()
    nikud = None
    m = LEADING_NIKUD_FORM_RE.match(part)
    if m:
        nikud = m.group(1).strip() or None
        part = part[m.end():]
    # any other {..} left inside the gloss: drop the braces, keep the word
    part = part.replace('{', '').replace('}', '')
    part = re.sub(r'\s+', ' ', part).strip().rstrip('.,;:').strip()
    return nikud, part


def load_entries(json_path: str):
    """Returns (headword -> [(nikud, text)] in order, stats)."""
    with open(json_path, encoding='utf-8') as f:
        raw = json.load(f)
    entries = raw[MAIN_LIST_NAME]
    mapping: dict[str, list[tuple[str | None, str]]] = defaultdict(list)
    seen: dict[str, set[str]] = defaultdict(set)
    stats = defaultdict(int)
    for obj in entries:
        for key, meaning in obj.items():
            stats['entries'] += 1
            headword = re.sub(r'\s+', ' ', strip_nikud(key)).strip()
            if not headword or not isinstance(meaning, str):
                stats['skipped_bad_entry'] += 1
                continue
            for part in meaning.split('***'):
                nikud, text = clean_sense(part)
                if not text:
                    continue
                stats['senses_in_source'] += 1
                k = normalize_for_dedup(text)
                if k in seen[headword]:
                    stats['dupes_within_list'] += 1
                    continue
                seen[headword].add(k)
                mapping[headword].append((nikud, text))
    return dict(mapping), stats


def load_existing_senses(cur) -> dict[str, set[str]]:
    existing: dict[str, set[str]] = defaultdict(set)
    for headword, text in cur.execute(
            "SELECT w.headword, s.text FROM word w JOIN sense s ON s.word_id = w.id"):
        if text:
            existing[headword].add(normalize_for_dedup(text))
    return existing


def import_into_dictionary(mapping, dry_run: bool):
    conn = sqlite3.connect(DICT_DB)
    conn.execute("PRAGMA busy_timeout = 10000")
    cur = conn.cursor()

    row = cur.execute("SELECT id FROM source_kind WHERE name = ?", (SOURCE_NAME,)).fetchone()
    if row:
        source_id = row[0]
        print(f"source_kind exists (id={source_id})")
    else:
        cur.execute("INSERT INTO source_kind (name) VALUES (?)", (SOURCE_NAME,))
        source_id = cur.lastrowid
        print(f"inserted source_kind id={source_id}")

    previous = cur.execute("SELECT COUNT(*) FROM sense WHERE source_id = ?", (source_id,)).fetchone()[0]
    if previous:
        # Only words this source created/used and now left empty - never
        # touch pre-existing sense-less words (link-only rows).
        cur.execute("CREATE TEMP TABLE touched AS SELECT DISTINCT word_id AS id FROM sense WHERE source_id = ?",
                    (source_id,))
        cur.execute("DELETE FROM sense WHERE source_id = ?", (source_id,))
        cur.execute("""DELETE FROM word WHERE id IN (SELECT id FROM touched)
                       AND id NOT IN (SELECT word_id FROM sense)
                       AND id NOT IN (SELECT word_id FROM link)
                       AND id NOT IN (SELECT target_id FROM link)""")
        cur.execute("DROP TABLE touched")
        print(f"removed {previous} previously imported senses")

    existing = load_existing_senses(cur)
    words_inserted = senses_inserted = senses_skipped = 0
    for headword, senses in mapping.items():
        new = [(n, t) for n, t in senses
               if normalize_for_dedup(t) not in existing.get(headword, set())]
        senses_skipped += len(senses) - len(new)
        if not new:
            continue
        row = cur.execute("SELECT id FROM word WHERE headword = ?", (headword,)).fetchone()
        if row:
            word_id = row[0]
        else:
            word_id = cur.execute("INSERT INTO word (headword) VALUES (?)", (headword,)).lastrowid
            words_inserted += 1
        for nikud, text in new:
            cur.execute("INSERT INTO sense (word_id, nikud, text, source_id) VALUES (?, ?, ?, ?)",
                        (word_id, nikud, text, source_id))
            senses_inserted += 1

    tag = "[DRY RUN] would insert" if dry_run else "inserted"
    print(f"{tag} {words_inserted} new headwords, {senses_inserted} senses "
          f"(skipped {senses_skipped} already present in the dictionary)")
    if dry_run:
        conn.rollback()
    else:
        conn.commit()
        print("integrity_check:", conn.execute("PRAGMA integrity_check").fetchone()[0])
        conn.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    conn.close()


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if not args:
        sys.exit(__doc__)
    mapping, stats = load_entries(args[0])
    print("=== Otzaria Aramaic list ===")
    for k, v in stats.items():
        print(f"  {k}: {v}")
    print(f"  headwords: {len(mapping)}, unique senses: {sum(len(v) for v in mapping.values())}")
    print("=== importing ===")
    import_into_dictionary(mapping, dry_run="--dry-run" in sys.argv)
    print("Done.")
