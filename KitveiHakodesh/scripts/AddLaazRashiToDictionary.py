"""
AddLaazRashiToDictionary.py
===========================
Reads לעזי רש"י entries from the seforim database (book id 5747, "אוצר לעזי רש"י")
and inserts them into KitveiHakodesh_dictionary.db as a new source type.

For each entry the headword is the לעז word (Hebrew letters with " before the last
letter, e.g. פריי"ר, גלצ"א) and the sense text is the bolded Hebrew translation
that follows the Latin transliteration.

Run: python AddLaazRashiToDictionary.py
"""

import sqlite3
import re
import sys

SEFORIM_DB = r'C:\ProgramData\otzaria\books\seforim.db'
DICT_DB = r'c:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\vue-frontend\public\dictionary\KitveiHakodesh_dictionary.db'

LAAZ_RASHI_SOURCE_NAME = "לעזי רש\"י"

# ── Regex helpers ─────────────────────────────────────────────────────────────

# Matches a single לעז token: Hebrew characters, possibly containing " (gershayim)
# e.g. פריי"ר, גלצ"א, בו"ן, מלנ"ט
# A לעז token must contain at least one ", otherwise it is plain Hebrew text.
LAAZ_TOKEN_RE = re.compile(
    r'[\u05d0-\u05ea]+'        # one or more Hebrew letters
    r'"'                       # gershayim (the defining mark of a לעז)
    r'[\u05d0-\u05ea]+'        # one or more Hebrew letters after the mark
)

# Matches the entry header: "N / (reference) / <b>HEBREW_WORD</b><br>..."
ENTRY_HEADER_RE = re.compile(
    r'^\d+\s*/\s*\([^)]+\)\s*/\s*<b>(.+?)</b><br>(.+)',
    re.DOTALL,
)

# Standard layout after the <br>:
#   LAAZ / transliteration / <b>HEBREW_TRANSLATION</b>
STANDARD_RE = re.compile(
    r'^(.+?)'               # לעז with geresh (greedy-minimal, group 1)
    r'\s*/\s*'              # separator
    r'[^/]+?'               # transliteration — skip
    r'\s*/\s*'              # separator
    r'<b>(.+?)</b>',        # Hebrew translation (group 2)
    re.DOTALL,
)

# Inverted layout after the <br>:
#   LAAZ + HEBREW_DESCRIPTION / <b>LATIN_TRANSLITERATION</b>
# In this case the first <b>…</b> is Latin, not Hebrew.
# The Hebrew translation is the text between the לעז token(s) and the first "/".
INVERTED_RE = re.compile(
    r'^((?:[\u05d0-\u05ea]+"[\u05d0-\u05ea]+\s*)+)'   # one or more לעז tokens (group 1)
    r'(.*?)'                                             # optional Hebrew description (group 2)
    r'\s*/\s*'                                           # separator
    r'<b>',                                              # start of (Latin) bold
    re.DOTALL,
)


def strip_html(text: str) -> str:
    """Remove all HTML tags from a string."""
    return re.sub(r'<[^>]+>', '', text).strip()


def extract_laaz_words(raw_laaz: str) -> list[str]:
    """
    Given the raw לעז segment (which may contain multiple tokens separated by spaces,
    e.g. 'בו"ן מלנ"ט'), return a list of individual לעז tokens that contain ".
    Only tokens that actually contain a " are real לעז words — the rest are plain
    Hebrew descriptive text and are skipped.
    """
    tokens = []
    for token in raw_laaz.split():
        clean = strip_html(token)
        if LAAZ_TOKEN_RE.search(clean):
            tokens.append(clean)
    return tokens


def parse_entry(content: str):
    """
    Parse a single entry line into a list of (laaz_word, hebrew_translation) pairs.
    Returns an empty list if the line is not a parseable entry.
    Each entry may yield multiple pairs when the לעז segment contains multiple tokens.
    """
    if not re.match(r'^\d+\s*/\s*\(', content):
        return []

    header_match = ENTRY_HEADER_RE.match(content)
    if not header_match:
        return []

    # rest is everything after the opening <b>HEBREW_WORD</b><br>
    rest = header_match.group(2).strip()

    # Determine whether the first <b>…</b> in rest is Hebrew or Latin.
    first_bold_match = re.search(r'<b>(.*?)</b>', rest, re.DOTALL)
    first_bold_is_hebrew = (
        first_bold_match is not None
        and bool(re.search(r'[\u05d0-\u05ea]', first_bold_match.group(1)))
    )

    hebrew_translation = None
    raw_laaz = None

    if first_bold_is_hebrew:
        # Standard layout: LAAZ / latin / <b>HEBREW</b>
        standard_match = STANDARD_RE.match(rest)
        if standard_match:
            raw_laaz = strip_html(standard_match.group(1)).strip()
            hebrew_translation = strip_html(standard_match.group(2)).strip()
    else:
        # Inverted layout: LAAZ + HEBREW_DESCRIPTION / <b>LATIN</b>
        inverted_match = INVERTED_RE.match(rest)
        if inverted_match:
            raw_laaz = inverted_match.group(1).strip()
            # The description text between the לעז tokens and the "/" is also the translation
            description = strip_html(inverted_match.group(2)).strip().lstrip(',').strip()
            hebrew_translation = description if description else None

    if not raw_laaz or not hebrew_translation:
        return []

    laaz_words = extract_laaz_words(raw_laaz)
    if not laaz_words:
        return []

    return [(word, hebrew_translation) for word in laaz_words]


# ── Test mode ─────────────────────────────────────────────────────────────────

def run_tests():
    seforim_conn = sqlite3.connect(SEFORIM_DB)
    cur = seforim_conn.cursor()
    cur.execute(
        "SELECT id, lineIndex, content FROM line WHERE bookId=5747 ORDER BY lineIndex"
    )
    lines = cur.fetchall()
    seforim_conn.close()

    total_entry_lines = 0
    parsed_count = 0
    skipped_count = 0
    all_pairs = []

    for (_, line_index, content) in lines:
        if not re.match(r'^\d+\s*/\s*\(', content):
            continue
        total_entry_lines += 1
        pairs = parse_entry(content)
        if pairs:
            parsed_count += 1
            all_pairs.extend(pairs)
        else:
            skipped_count += 1

    print(f"Total entry lines:   {total_entry_lines}")
    print(f"Successfully parsed: {parsed_count}  ({len(all_pairs)} word-translation pairs)")
    print(f"Skipped (no לעז+translation): {skipped_count}")
    print()

    print("=== Sample pairs (first 20) ===")
    for laaz, translation in all_pairs[:20]:
        print(f"  לעז: {laaz!r:30s}  →  תרגום: {translation!r}")
    print()

    # Check for duplicate לעז headwords (same word, multiple translations — expected)
    from collections import Counter
    headword_counts = Counter(laaz for laaz, _ in all_pairs)
    duplicates = [(w, c) for w, c in headword_counts.items() if c > 1]
    print(f"לעז words with multiple translations: {len(duplicates)}")
    for word, count in sorted(duplicates, key=lambda x: -x[1])[:10]:
        print(f"  {word!r} → {count} senses")

    return all_pairs


# ── Import mode ────────────────────────────────────────────────────────────────

def import_into_dictionary(all_pairs: list[tuple[str, str]], dry_run: bool = False):
    dict_conn = sqlite3.connect(DICT_DB)
    cur = dict_conn.cursor()

    # Add new source_kind if it doesn't exist yet
    cur.execute(
        "SELECT id FROM source_kind WHERE name = ?",
        (LAAZ_RASHI_SOURCE_NAME,),
    )
    existing_source = cur.fetchone()
    if existing_source:
        source_id = existing_source[0]
        print(f"source_kind '{LAAZ_RASHI_SOURCE_NAME}' already exists (id={source_id})")
    else:
        cur.execute(
            "INSERT INTO source_kind (name) VALUES (?)",
            (LAAZ_RASHI_SOURCE_NAME,),
        )
        source_id = cur.lastrowid
        print(f"Inserted source_kind '{LAAZ_RASHI_SOURCE_NAME}' with id={source_id}")

    # Remove any previously imported entries for this source (idempotent re-run)
    cur.execute("SELECT id FROM sense WHERE source_id = ?", (source_id,))
    existing_sense_ids = [row[0] for row in cur.fetchall()]
    if existing_sense_ids:
        placeholders = ",".join("?" * len(existing_sense_ids))
        cur.execute(f"DELETE FROM sense WHERE id IN ({placeholders})", existing_sense_ids)
        # Remove orphaned words (words with no senses left)
        cur.execute("""
            DELETE FROM word
            WHERE id NOT IN (SELECT DISTINCT word_id FROM sense)
              AND id IN (
                  SELECT DISTINCT word_id FROM sense WHERE source_id = ?
              )
        """, (source_id,))
        # Re-fetch after deletion to find truly orphaned words
        cur.execute("""
            DELETE FROM word
            WHERE id NOT IN (SELECT DISTINCT word_id FROM sense)
        """)
        print(f"Removed {len(existing_sense_ids)} previously imported senses")

    # Group all pairs by headword so we can insert one word row + multiple sense rows
    from collections import defaultdict
    word_to_senses: dict[str, list[str]] = defaultdict(list)
    for laaz_word, translation in all_pairs:
        word_to_senses[laaz_word].append(translation)

    words_inserted = 0
    senses_inserted = 0

    for headword, translations in word_to_senses.items():
        if not dry_run:
            # Check if this headword already exists (from another source)
            cur.execute("SELECT id FROM word WHERE headword = ?", (headword,))
            existing_word = cur.fetchone()
            if existing_word:
                word_id = existing_word[0]
            else:
                cur.execute("INSERT INTO word (headword) VALUES (?)", (headword,))
                word_id = cur.lastrowid
                words_inserted += 1

            for translation in translations:
                cur.execute(
                    "INSERT INTO sense (word_id, nikud, text, source_id) VALUES (?, ?, ?, ?)",
                    (word_id, None, translation, source_id),
                )
                senses_inserted += 1
        else:
            words_inserted += 1
            senses_inserted += len(translations)

    if not dry_run:
        dict_conn.commit()
        print(f"Inserted {words_inserted} new headwords, {senses_inserted} senses")
    else:
        print(f"[DRY RUN] Would insert {words_inserted} headwords, {senses_inserted} senses")

    dict_conn.close()


# ── Entry point ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    test_only = "--test" in sys.argv

    print("=== Test run: parsing לעזי רש\"י entries ===")
    print()
    all_pairs = run_tests()
    print()

    if test_only:
        print("Test-only mode — not writing to dictionary DB.")
        sys.exit(0)

    print(f"=== {'DRY RUN: ' if dry_run else ''}Importing into dictionary DB ===")
    import_into_dictionary(all_pairs, dry_run=dry_run)
    print()
    print("Done.")
