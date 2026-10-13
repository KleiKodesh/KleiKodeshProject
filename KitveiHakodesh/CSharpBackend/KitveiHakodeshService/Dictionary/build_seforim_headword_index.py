#!/usr/bin/env python3
"""
Builds the seforim_headword table that the dictionary feature looks words up in, and
writes it into every tracked copy of the dictionary database.

WHY THIS EXISTS
---------------
Five reference works (Metzudat, Malbim, Machberet Menachem, Aruch, a micropedia) are not
separate dictionary files - they are ordinary books inside seforim.db. The dictionary
feature used to find an entry with `content LIKE '%<b>TERM</b>%'` against those books.

From SeforimLibrary schema 6 on, line content is zstd-compressed, so LIKE matches nothing
at all - silently, with no error. Decompressing all 49 books on every lookup would be
54,000 lines of work per word.

So the headwords are extracted once, here, and shipped as an index. A lookup then reads
this small plain table, and only the one line it actually needs is fetched and
decompressed from seforim.db.

WHAT IS STORED, AND WHAT IS NOT
-------------------------------
Only the headword and where to find it. No definition text: that keeps the dictionary
database small, and it means the shipped index cannot go stale in a way that shows the
reader the wrong words - the definition always comes from the live seforim.db.

Books are identified by TITLE, not by bookId, and lines by lineIndex, not by lineId.
Ids are assigned at library-build time and change between library versions; titles and
line numbering survive a rebuild.

The headword itself is stored twice over: `headword` is the full text inside the tag, and
one row per `word` token exists for multi-word headwords ("ALEPH, BET" is two lookup
keys). Matching splits on commas and whitespace, which is what the frontend's
headerMatchesExact does today, so the index has to be able to answer the same question.

USAGE
    cd CSharpBackend/KitveiHakodeshService/Dictionary
    python build_seforim_headword_index.py [--seforim PATH] [--dry-run]
"""

import argparse
import hashlib
import os
import re
import shutil
import sqlite3
import sys

try:
    import zstandard
except ImportError:
    sys.exit("zstandard is required: pip install zstandard")


# This script lives beside the dictionary database it ships with:
#   CSharpBackend/KitveiHakodeshService/Dictionary/
# so the repository root is four levels up.
REPOSITORY_ROOT = os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
)

# Every tracked copy of the dictionary database. They are byte-identical today and must
# stay that way; shipping the index to one and not the other is exactly the drift this
# script exists to prevent.
DICTIONARY_DATABASE_PATHS = [
    os.path.join(REPOSITORY_ROOT, "CSharpBackend", "KitveiHakodeshService", "Dictionary", "Dictionary.db"),
]

# The plain (uncompressed) Zayit library. Its titles are the shorter spellings, which the
# frontend can widen to a prefix match on an Otzaria library; the reverse is not possible.
DEFAULT_SEFORIM_DATABASE_PATH = os.path.join(
    os.environ.get("APPDATA", ""), "io.github.kdroidfilter.seforimapp", "databases", "seforim.db"
)

# The five sources, each with how its books are found and how a headword is marked.
#
# `title_match` is 'like' or 'exact', mirroring getBookIdsByTitlePattern /
# getBookIdByExactTitle in dictionarySeforimDb.ts.
#
# `headword_pattern` must capture the headword in group 1. These mirror the parse
# functions in dictionarySeforimDb.ts (parseBoldLine, parseBigWord, the h2 patterns) -
# if one changes there, it changes here.
SOURCES = [
    {
        "name": "metzudat",
        "title_match": "like",
        "title": "%\u05de\u05e6\u05d5\u05d3\u05ea \u05e6\u05d9\u05d5\u05df%",
        "headword_pattern": re.compile(r"^<b>([^<]+?)</b>"),
    },
    {
        "name": "malbim",
        "title_match": "like",
        "title": "%\u05de\u05dc\u05d1\u05d9%\u05d1\u05d0\u05d5\u05e8 \u05d4\u05de\u05d9\u05dc\u05d5\u05ea%",
        "headword_pattern": re.compile(r"^<b>([^<]+?)</b>"),
    },
    {
        "name": "menachem",
        "title_match": "like",
        "title": "%\u05de\u05d7\u05d1\u05e8\u05ea \u05de\u05e0\u05d7\u05dd%",
        "headword_pattern": re.compile(r"<big>\s*‎?\s*([^\s<]+)\s*</big>"),
    },
    {
        "name": "micropedia",
        # The frontend asks for this title EXACTLY, but the library ships it with a second
        # word appended, so the frontend's own lookup finds nothing (see the docstring).
        # Matched loosely here so the index covers the book either way.
        "title_match": "like",
        "title": "%\u05de\u05d9\u05e7\u05e8\u05d5\u05e4\u05d3\u05d9\u05d4%",
        "headword_pattern": re.compile(r"<h2>([^<]+?)</h2>"),
    },
    {
        "name": "aruch",
        "title_match": "exact",
        "title": "\u05e1\u05e4\u05e8 \u05d4\u05e2\u05e8\u05d5\u05da",
        "headword_pattern": re.compile(r"<big>([^<]+?)</big>"),
    },
]

# Two tables, because the 49 book titles would otherwise be repeated on all 44,000 rows -
# nearly a megabyte of duplicated text in a database whose whole point is to stay small.
CREATE_TABLE_SQL = [
    """
    CREATE TABLE seforim_headword_book (
        id    INTEGER PRIMARY KEY,
        -- The book's title exactly as seforim.db spells it. Resolved to a bookId at lookup
        -- time, because ids are not stable across library rebuilds but titles are.
        title TEXT NOT NULL UNIQUE
    )
    """,
    """
    CREATE TABLE seforim_headword (
        -- Which reference work this entry belongs to; each one is parsed differently.
        source    TEXT    NOT NULL,
        bookId    INTEGER NOT NULL REFERENCES seforim_headword_book(id),
        -- Position of the headword line within its book. Stable across rebuilds in a way
        -- line.id is not.
        lineIndex INTEGER NOT NULL,
        -- One row per token of the headword, so a lookup on any single word of a
        -- multi-word headword finds the entry. The full headword is not stored: the
        -- caller re-reads it from the line it fetches, which also catches a stale index.
        word      TEXT    NOT NULL
    )
    """,
]

CREATE_INDEX_SQL = [
    # Every lookup is "this word, in this source" - exact, prefix (word >= t AND word < t+1)
    # and contains (LIKE '%t%') all drive off it. Scanning it is cheap because it holds
    # short headwords rather than the books' text.
    "CREATE INDEX index_seforim_headword_source_word ON seforim_headword(source, word)",
]


def open_seforim_database(path):
    """Opens seforim.db read-only, and returns the connection plus a content decoder."""
    if not os.path.exists(path):
        sys.exit(f"seforim.db not found at {path}")

    uri = "file:///" + path.replace("\\", "/") + "?mode=ro"
    connection = sqlite3.connect(uri, uri=True)

    decompressor = None
    has_dictionary_table = connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name='zstd_dict'"
    ).fetchone()
    if has_dictionary_table:
        dictionary_bytes = connection.execute("SELECT dict FROM zstd_dict").fetchone()[0]
        decompressor = zstandard.ZstdDecompressor(
            dict_data=zstandard.ZstdCompressionDict(dictionary_bytes)
        )

    has_split_content = connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name='line_content'"
    ).fetchone()
    line_columns = [row[1] for row in connection.execute("PRAGMA table_info(line)")]
    is_split = bool(has_split_content) and "content" not in line_columns

    return connection, decompressor, is_split


def read_book_lines(connection, decompressor, is_split, book_id):
    """Every line of a book as (lineIndex, text), decompressing when the database is compressed."""
    if is_split:
        sql = ("SELECT l.lineIndex, lc.content FROM line l "
               "LEFT JOIN line_content lc ON lc.id = l.id "
               "WHERE l.bookId = ? ORDER BY l.lineIndex")
    else:
        sql = "SELECT lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex"

    for line_index, value in connection.execute(sql, (book_id,)):
        if value is None:
            continue
        if isinstance(value, bytes):
            if decompressor is None:
                sys.exit("Found compressed content but the database has no zstd_dict table.")
            text = decompressor.decompress(value).decode("utf-8")
        else:
            text = value
        yield line_index, text


def tokenise_headword(headword):
    """
    The lookup keys for a headword.

    Mirrors normalizeHeaderWord + headerMatchesExact in dictionarySeforimDb.ts: strip the
    punctuation those functions strip, then split on commas and whitespace. A multi-word
    headword is findable by any of its words, which is the behaviour the LIKE queries had.
    """
    normalized = re.sub(r"[.,;:?\"״׳]", " ", headword).strip()
    tokens = {token for token in re.split(r"[,\s]+", normalized) if token}
    if normalized:
        tokens.add(normalized)
    return tokens


def extract_headwords(seforim_path):
    """
    Walks every source's books and returns (book_titles, rows).

    `rows` are (source, bookTitle, lineIndex, word) - the title is resolved to an id when
    the index is written. Duplicate (source, title, lineIndex, word) tuples are collapsed:
    a headword whose tokens repeat needs only one lookup row.
    """
    connection, decompressor, is_split = open_seforim_database(seforim_path)
    print(f"seforim.db: split={is_split} compressed={decompressor is not None}")

    rows = set()
    book_titles = set()
    for source in SOURCES:
        operator = "LIKE" if source["title_match"] == "like" else "="
        books = connection.execute(
            f"SELECT id, title FROM book WHERE title {operator} ?", (source["title"],)
        ).fetchall()

        if not books:
            # Shipping an index that silently omits a whole reference work is the exact
            # failure this file exists to prevent, so a title matching nothing is fatal
            # rather than a warning. If a source really is gone from the library, remove it
            # from SOURCES deliberately.
            sys.exit(f"{source['name']}: no books matched its title. Either the library no "
                     f"longer ships it, or the title in SOURCES is wrong.")

        before = len(rows)
        for book_id, book_title in books:
            book_titles.add(book_title)
            for line_index, text in read_book_lines(connection, decompressor, is_split, book_id):
                match = source["headword_pattern"].search(text)
                if not match:
                    continue
                headword = match.group(1).strip()
                if not headword:
                    continue
                for word in tokenise_headword(headword):
                    rows.add((source["name"], book_title, line_index, word))

        print(f"  {source['name']:<12} {len(books):3} book(s) -> {len(rows) - before} index rows")

    connection.close()
    return sorted(book_titles), sorted(rows)


def write_index(dictionary_path, book_titles, rows, dry_run):
    """Replaces the headword index in one dictionary database."""
    if not os.path.exists(dictionary_path):
        print(f"  MISSING: {dictionary_path}")
        return False

    if dry_run:
        print(f"  would write {len(rows)} rows / {len(book_titles)} books -> {dictionary_path}")
        return True

    connection = sqlite3.connect(dictionary_path)
    try:
        connection.execute("DROP TABLE IF EXISTS seforim_headword")
        connection.execute("DROP TABLE IF EXISTS seforim_headword_book")
        for statement in CREATE_TABLE_SQL:
            connection.execute(statement)

        connection.executemany(
            "INSERT INTO seforim_headword_book (title) VALUES (?)",
            [(title,) for title in book_titles],
        )
        book_ids = {
            title: identifier
            for identifier, title in connection.execute(
                "SELECT id, title FROM seforim_headword_book"
            )
        }

        connection.executemany(
            "INSERT INTO seforim_headword (source, bookId, lineIndex, word) VALUES (?, ?, ?, ?)",
            [(source, book_ids[title], line_index, word)
             for source, title, line_index, word in rows],
        )
        for statement in CREATE_INDEX_SQL:
            connection.execute(statement)
        connection.commit()
        # The database ships in the installer, so leave no free pages behind. VACUUM also
        # rewrites it out of WAL mode, which keeps a -wal sidecar from travelling with it.
        connection.execute("PRAGMA journal_mode=DELETE")
        connection.execute("VACUUM")
        connection.commit()
    finally:
        connection.close()

    print(f"  wrote {len(rows)} rows / {len(book_titles)} books -> {dictionary_path}"
          f"  ({os.path.getsize(dictionary_path)} bytes)")
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seforim", default=DEFAULT_SEFORIM_DATABASE_PATH,
                        help="Path to seforim.db (default: the Otzaria library location)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Extract and report, but do not write to any dictionary database")
    arguments = parser.parse_args()

    print("Extracting headwords...")
    book_titles, rows = extract_headwords(arguments.seforim)
    print(f"TOTAL: {len(rows)} index rows across {len(book_titles)} books")

    if not rows:
        sys.exit("No headwords extracted - refusing to write an empty index.")

    print("\nWriting to every tracked dictionary database...")

    # Build into the FIRST database, then copy that file to the rest. Building each one
    # separately produced files of the same size but different bytes - SQLite does not
    # guarantee a reproducible page layout - and two dictionary databases that differ by a
    # byte are exactly the drift this script exists to prevent.
    primary_path = DICTIONARY_DATABASE_PATHS[0]
    if not write_index(primary_path, book_titles, rows, arguments.dry_run):
        sys.exit(f"Could not write the primary dictionary database at {primary_path}.")

    for path in DICTIONARY_DATABASE_PATHS[1:]:
        if not os.path.exists(path):
            sys.exit(f"Missing dictionary database: {path}. Every copy must stay in sync - "
                     f"fix the path and re-run.")
        if arguments.dry_run:
            print(f"  would copy  -> {path}")
            continue
        shutil.copyfile(primary_path, path)
        print(f"  copied      -> {path}  ({os.path.getsize(path)} bytes)")

    if arguments.dry_run:
        return

    digests = {file_digest(path) for path in DICTIONARY_DATABASE_PATHS}
    if len(digests) != 1:
        sys.exit("The dictionary databases differ after writing - they must be identical.")

    print(f"\nDone. {len(DICTIONARY_DATABASE_PATHS)} dictionary database(s), byte-identical.")


def file_digest(path):
    """SHA-256 of a file, used to prove the copies really are identical."""
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


if __name__ == "__main__":
    main()
