"""
Check whether the first tocEntry of every book matches the book's title.
Full scan — no sampling.
"""

import sqlite3
import sys

DB_PATH = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row
cur = conn.cursor()

print("Fetching all books...", flush=True)
cur.execute("SELECT id, title FROM book ORDER BY id")
books = cur.fetchall()
total = len(books)
print(f"Total books: {total}", flush=True)

mismatches = []
no_toc = []
match_count = 0

for i, book in enumerate(books):
    book_id = book["id"]
    book_title = book["title"]

    # Get the first tocEntry for this book (lowest id among level-0 entries, or just lowest id)
    cur.execute("""
        SELECT t.text
        FROM tocEntry e
        JOIN tocText t ON t.id = e.textId
        WHERE e.bookId = ?
        ORDER BY e.id ASC
        LIMIT 1
    """, (book_id,))
    row = cur.fetchone()

    if row is None:
        no_toc.append({"bookId": book_id, "bookTitle": book_title})
    else:
        first_toc_text = row["text"]
        if first_toc_text.strip() == book_title.strip():
            match_count += 1
        else:
            mismatches.append({
                "bookId": book_id,
                "bookTitle": book_title,
                "firstTocEntry": first_toc_text,
            })

    if (i + 1) % 500 == 0:
        print(f"  Processed {i + 1}/{total}...", flush=True)

conn.close()

print(f"\n=== RESULTS ===")
print(f"Total books:          {total}")
print(f"Exact matches:        {match_count}")
print(f"Mismatches:           {len(mismatches)}")
print(f"Books with no TOC:    {len(no_toc)}")

if mismatches:
    print(f"\n--- MISMATCHES (first 50 shown) ---")
    for m in mismatches[:50]:
        print(f"  bookId={m['bookId']}  book='{m['bookTitle']}'  firstToc='{m['firstTocEntry']}'")
    if len(mismatches) > 50:
        print(f"  ... and {len(mismatches) - 50} more")

if no_toc:
    print(f"\n--- BOOKS WITH NO TOC ENTRIES (first 20 shown) ---")
    for b in no_toc[:20]:
        print(f"  bookId={b['bookId']}  title='{b['bookTitle']}'")
    if len(no_toc) > 20:
        print(f"  ... and {len(no_toc) - 20} more")

# Write full markdown table
out_path = "scripts/toc_mismatches.md"
with open(out_path, "w", encoding="utf-8") as f:
    f.write(f"# TOC First-Entry Mismatches\n\n")
    f.write(f"Full scan of {total} books — {len(mismatches)} mismatches, {match_count} exact matches, {len(no_toc)} with no TOC.\n\n")
    f.write("| bookId | Book Title | First TOC Entry |\n")
    f.write("|-------:|------------|------------------|\n")
    for m in mismatches:
        book = m['bookTitle'].replace('|', '\\|')
        toc  = m['firstTocEntry'].replace('|', '\\|')
        f.write(f"| {m['bookId']} | {book} | {toc} |\n")

print(f"\nFull markdown table written to {out_path}")
