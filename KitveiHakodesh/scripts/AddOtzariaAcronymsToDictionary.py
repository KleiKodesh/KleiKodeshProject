"""
AddOtzariaAcronymsToDictionary.py
=================================
Reads the Otzaria app's ראשי תיבות asset (Acronyms.json — ~13,100 abbreviations,
~29,000 expansions) and inserts them into KitveiHakodesh_dictionary.db as a new
source type.

The JSON maps each abbreviation headword (e.g. א"א) to a list of expansion
strings. A few dozen keys carry nikud (e.g. הַגְּרָ"א) — nikud is stripped and
the key merged with its plain form.

The dictionary already contains three ראשי תיבות sources (ויקיפדיה, קיצור ראשי
תיבות, ויקי ספרי יהדות) that overlap heavily with the Otzaria list, so unlike
the לעז import, expansions whose normalized text already exists as a sense of
the same headword (from ANY source) are skipped. Text comparison is
spelling-tolerant: nikud stripped, whitespace collapsed, י/ו (matres lectionis)
ignored — so תורתינו and תורתנו count as the same expansion.

Prefix-redundancy filter: the book-view abbreviation tooltip strips attached
prefix letters (ו, ה, ב, כ, ל, מ, ש, ד) when looking up a selection, so an
expansion is NOT imported when it is just a prefix letter glued onto an
expansion the stripped base entry already has (בתוה"ק → "בתורתינו הקדושה" is
skipped because תוה"ק → "תורתנו הקדושה" exists; the tooltip resolves בתוה"ק by
stripping). Entries whose expansion carries content the base entry does not
have are kept even when the headword looks prefixed (בא"ח → "בן איש חי",
בא"ש → "באר שבע").

Re-runnable: previously imported senses for this source are deleted first.

Run: python AddOtzariaAcronymsToDictionary.py [--test] [--dry-run]
"""

import json
import re
import sqlite3
import sys
from collections import defaultdict

ACRONYMS_JSON = r'C:\Program Files\אוצריא\data\flutter_assets\assets\Acronyms.json'
DICT_DB = r'c:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\vue-frontend\public\dictionary\KitveiHakodesh_dictionary.db'

SOURCE_NAME = "אוצריא - ראשי תיבות"

NIKUD_RE = re.compile(r'[֑-ׇ]')
# Standard abbreviation headword: Hebrew letters with a single gershayim inside.
HEADWORD_RE = re.compile(r'^[א-ת]+"[א-ת]+$')

# Prefix letters the book-view tooltip strips before lookup
# (useBookViewAbbrevTooltip.ts PREFIX_LETTERS — keep in sync).
PREFIX_LETTERS = {'ו', 'ה', 'ב', 'כ', 'ל', 'מ', 'ש', 'ד'}


def strip_nikud(text: str) -> str:
    return NIKUD_RE.sub('', text)


def normalize_for_dedup(text: str) -> str:
    """Comparison key for duplicate detection: nikud-free, whitespace-collapsed,
    trailing punctuation dropped, י/ו (matres lectionis) ignored so that מלא and
    חסר spellings of the same expansion compare equal (תורתינו == תורתנו)."""
    text = strip_nikud(text)
    text = re.sub(r'\s+', ' ', text).strip()
    text = text.rstrip('.,;:')
    return text.replace('י', '').replace('ו', '')


def prefix_redundant_keys(headword: str, expansion_norms_of) -> set[str]:
    """Normalized expansion forms that would make an expansion of `headword`
    redundant: for every prefix-stripped base entry that exists (up to 3
    letters, mirroring the tooltip's stripping depth), the base's expansions
    with and without the accumulated prefix glued on."""
    keys: set[str] = set()
    for strip in range(1, 4):
        if strip >= len(headword):
            break
        prefix, base = headword[:strip], headword[strip:]
        if not all(ch in PREFIX_LETTERS for ch in prefix):
            break
        if not HEADWORD_RE.match(base):
            continue
        base_norms = expansion_norms_of(base)
        if not base_norms:
            continue
        prefix_norm = normalize_for_dedup(prefix)
        for norm in base_norms:
            keys.add(prefix_norm + norm)
            keys.add(norm)
    return keys


def filter_prefix_redundant(
    mapping: dict[str, list[str]],
    existing: dict[str, set[str]],
) -> tuple[dict[str, list[str]], int, list[tuple[str, str]]]:
    """Drops expansions the tooltip's prefix-stripping already derives from a
    base entry (in the Otzaria data itself or already in the dictionary).
    Returns (filtered mapping, removed count, sample removals)."""

    def expansion_norms_of(base: str) -> set[str]:
        norms = {normalize_for_dedup(e) for e in mapping.get(base, [])}
        norms |= existing.get(base, set())
        return norms

    filtered: dict[str, list[str]] = {}
    removed = 0
    samples: list[tuple[str, str]] = []
    for headword, expansions in mapping.items():
        redundant = prefix_redundant_keys(headword, expansion_norms_of)
        kept = [e for e in expansions if normalize_for_dedup(e) not in redundant]
        removed += len(expansions) - len(kept)
        if len(kept) < len(expansions) and len(samples) < 10:
            dropped = next(e for e in expansions if normalize_for_dedup(e) in redundant)
            samples.append((headword, dropped))
        if kept:
            filtered[headword] = kept
    return filtered, removed, samples


def load_acronyms() -> tuple[dict[str, list[str]], list[str]]:
    """Returns (headword -> ordered unique expansions, skipped keys)."""
    with open(ACRONYMS_JSON, encoding='utf-8') as f:
        raw = json.load(f)

    mapping: dict[str, list[str]] = defaultdict(list)
    seen_per_word: dict[str, set[str]] = defaultdict(set)
    skipped_keys: list[str] = []

    for key, expansions in raw.items():
        headword = strip_nikud(key).strip()
        if not HEADWORD_RE.match(headword):
            skipped_keys.append(key)
            continue
        for expansion in expansions:
            expansion = re.sub(r'\s+', ' ', str(expansion)).strip()
            if not expansion:
                continue
            dedup_key = normalize_for_dedup(expansion)
            if dedup_key in seen_per_word[headword]:
                continue
            seen_per_word[headword].add(dedup_key)
            mapping[headword].append(expansion)

    return dict(mapping), skipped_keys


# ── Shared DB helpers ─────────────────────────────────────────────────────────

def load_existing_senses(cur, exclude_source_name: str | None = None) -> dict[str, set[str]]:
    """Normalized sense texts per headword, optionally excluding one source
    (used in test mode where a previous import may still be present)."""
    existing: dict[str, set[str]] = defaultdict(set)
    if exclude_source_name:
        cur.execute(
            """SELECT w.headword, s.text FROM word w JOIN sense s ON s.word_id = w.id
               WHERE s.source_id IS NULL OR s.source_id NOT IN
                 (SELECT id FROM source_kind WHERE name = ?)""",
            (exclude_source_name,),
        )
    else:
        cur.execute(
            "SELECT w.headword, s.text FROM word w JOIN sense s ON s.word_id = w.id"
        )
    for headword, text in cur.fetchall():
        existing[headword].add(normalize_for_dedup(text))
    return existing


# ── Test mode ─────────────────────────────────────────────────────────────────

def run_tests(mapping: dict[str, list[str]], skipped_keys: list[str]):
    total_expansions = sum(len(v) for v in mapping.values())
    print(f"Headwords:  {len(mapping)}")
    print(f"Expansions: {total_expansions}")
    print(f"Skipped non-standard keys: {len(skipped_keys)}")
    if skipped_keys:
        print(f"  e.g. {skipped_keys[:8]}")
    print()
    print("=== Samples ===")
    for headword in list(mapping)[:5]:
        print(f"  {headword!r} → {mapping[headword][:4]}")

    conn = sqlite3.connect(DICT_DB)
    cur = conn.cursor()
    existing = load_existing_senses(cur, exclude_source_name=SOURCE_NAME)
    conn.close()

    filtered, prefix_removed, samples = filter_prefix_redundant(mapping, existing)
    print()
    print(f"Prefix-redundant expansions removed: {prefix_removed}")
    print(f"Headwords emptied by the filter:     {len(mapping) - len(filtered)}")
    print("Removal samples:")
    for headword, expansion in samples:
        print(f"  {headword!r} dropped {expansion!r}")

    already_present = 0
    genuinely_new = 0
    new_headwords = 0
    for headword, expansions in filtered.items():
        if headword not in existing:
            new_headwords += 1
        for expansion in expansions:
            if normalize_for_dedup(expansion) in existing.get(headword, set()):
                already_present += 1
            else:
                genuinely_new += 1
    print()
    print(f"Headwords not yet in dictionary:      {new_headwords}")
    print(f"Expansions already present (skipped): {already_present}")
    print(f"Expansions genuinely new (inserted):  {genuinely_new}")


# ── Import mode ────────────────────────────────────────────────────────────────

def import_into_dictionary(mapping: dict[str, list[str]], dry_run: bool = False):
    conn = sqlite3.connect(DICT_DB)
    conn.execute("PRAGMA busy_timeout = 10000")
    cur = conn.cursor()

    cur.execute("SELECT id FROM source_kind WHERE name = ?", (SOURCE_NAME,))
    row = cur.fetchone()
    if row:
        source_id = row[0]
        print(f"source_kind '{SOURCE_NAME}' already exists (id={source_id})")
    else:
        cur.execute("INSERT INTO source_kind (name) VALUES (?)", (SOURCE_NAME,))
        source_id = cur.lastrowid
        print(f"Inserted source_kind '{SOURCE_NAME}' with id={source_id}")

    # Idempotent re-run: drop previously imported senses, then orphaned words
    cur.execute("SELECT COUNT(*) FROM sense WHERE source_id = ?", (source_id,))
    previous = cur.fetchone()[0]
    if previous:
        cur.execute("DELETE FROM sense WHERE source_id = ?", (source_id,))
        cur.execute(
            "DELETE FROM word WHERE id NOT IN (SELECT DISTINCT word_id FROM sense)"
        )
        print(f"Removed {previous} previously imported senses")

    # Existing sense texts per headword (any source) — the cross-source dedup set.
    # Previous Otzaria senses were just deleted, so this reflects the other sources.
    existing = load_existing_senses(cur)

    mapping, prefix_removed, _ = filter_prefix_redundant(mapping, existing)
    print(f"Prefix-redundant expansions filtered out: {prefix_removed}")

    words_inserted = 0
    senses_inserted = 0
    senses_skipped = 0

    for headword, expansions in mapping.items():
        new_expansions = [
            e for e in expansions
            if normalize_for_dedup(e) not in existing.get(headword, set())
        ]
        senses_skipped += len(expansions) - len(new_expansions)
        if not new_expansions:
            continue

        if dry_run:
            words_inserted += headword not in existing
            senses_inserted += len(new_expansions)
            continue

        cur.execute("SELECT id FROM word WHERE headword = ?", (headword,))
        row = cur.fetchone()
        if row:
            word_id = row[0]
        else:
            cur.execute("INSERT INTO word (headword) VALUES (?)", (headword,))
            word_id = cur.lastrowid
            words_inserted += 1

        for expansion in new_expansions:
            cur.execute(
                "INSERT INTO sense (word_id, nikud, text, source_id) VALUES (?, ?, ?, ?)",
                (word_id, None, expansion, source_id),
            )
            senses_inserted += 1

    if dry_run:
        conn.rollback()
        print(f"[DRY RUN] Would insert {words_inserted} headwords, "
              f"{senses_inserted} senses (skipping {senses_skipped} duplicates)")
    else:
        conn.commit()
        print(f"Inserted {words_inserted} new headwords, {senses_inserted} senses "
              f"(skipped {senses_skipped} duplicates)")
    conn.close()


# ── Entry point ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    test_only = "--test" in sys.argv

    mapping, skipped_keys = load_acronyms()

    print("=== Otzaria Acronyms.json analysis ===")
    run_tests(mapping, skipped_keys)
    print()

    if test_only:
        print("Test-only mode — not writing to dictionary DB.")
        sys.exit(0)

    print(f"=== {'DRY RUN: ' if dry_run else ''}Importing into dictionary DB ===")
    import_into_dictionary(mapping, dry_run=dry_run)
    print()
    print("Done.")
