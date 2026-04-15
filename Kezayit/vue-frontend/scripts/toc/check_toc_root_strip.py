"""
Validate the fuzzy root-strip rule against the real DB.

For every book, fetch the root tocEntry (parentId IS NULL, lowest id).
Apply the candidate rule and report:
  - TRUE POSITIVES  : root looks like a title variant → would be stripped ✓
  - FALSE POSITIVES : root looks unrelated but rule fires → would be wrongly stripped ✗
  - TRUE NEGATIVES  : root is unrelated, rule correctly leaves it alone ✓
  - FALSE NEGATIVES : root IS a title variant but rule misses it ✗

Since we have no ground-truth labels we print every WOULD-STRIP case so you
can eyeball them and tune RATIO_THRESHOLD.
"""

import sqlite3
import re
import sys

DB_PATH = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"

# ── tuneable ────────────────────────────────────────────────────────────────
RATIO_THRESHOLD = 0.6   # shorter word-set must be >= this fraction of longer
# ────────────────────────────────────────────────────────────────────────────

STRIP_CHARS = re.compile(r'["\'\u05f4\u05f3\u201c\u201d\u2018\u2019״׳\-]')

def normalize(s: str) -> list[str]:
    """Strip quote/punctuation chars, lowercase, split into words."""
    s = STRIP_CHARS.sub('', s)
    return [w for w in s.split() if w]

def would_strip(book_title: str, root_text: str) -> bool:
    bt = normalize(book_title)
    rt = normalize(root_text)
    if not bt or not rt:
        return False
    shorter, longer = (bt, rt) if len(bt) <= len(rt) else (rt, bt)
    if len(shorter) < len(longer) * RATIO_THRESHOLD:
        return False
    return all(w in longer for w in shorter)

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row
cur = conn.cursor()

print("Fetching books and their root TOC entries...", flush=True)

cur.execute("""
    SELECT b.id AS bookId, b.title AS bookTitle, tt.text AS rootText
    FROM book b
    JOIN tocEntry e ON e.bookId = b.id AND e.parentId IS NULL
    JOIN tocText tt ON tt.id = e.textId
    WHERE e.id = (
        SELECT MIN(e2.id) FROM tocEntry e2
        WHERE e2.bookId = b.id AND e2.parentId IS NULL
    )
    ORDER BY b.id
""")
rows = cur.fetchall()
conn.close()

total = len(rows)
print(f"Books with a root TOC entry: {total}\n", flush=True)

would_strip_rows = []
would_keep_rows  = []

for r in rows:
    if would_strip(r['bookTitle'], r['rootText']):
        would_strip_rows.append(r)
    else:
        would_keep_rows.append(r)

# ── exact matches (already handled by current code) ─────────────────────────
exact = [r for r in would_strip_rows if r['bookTitle'].strip() == r['rootText'].strip()]
fuzzy = [r for r in would_strip_rows if r['bookTitle'].strip() != r['rootText'].strip()]

print(f"=== SUMMARY (ratio threshold = {RATIO_THRESHOLD}) ===")
print(f"Total books:              {total}")
print(f"Would strip (total):      {len(would_strip_rows)}")
print(f"  of which exact match:   {len(exact)}")
print(f"  of which fuzzy match:   {len(fuzzy)}")
print(f"Would keep:               {len(would_keep_rows)}")

print(f"\n--- FUZZY MATCHES THAT WOULD BE STRIPPED ({len(fuzzy)}) ---")
print(f"{'bookId':>8}  {'Book Title':<55}  First Root TOC")
print("-" * 110)
for r in fuzzy:
    print(f"{r['bookId']:>8}  {r['bookTitle']:<55}  {r['rootText']}")

# ── spot-check: sample of KEPT rows where root shares words with title ───────
# These are the potential false negatives — rule didn't fire but maybe should have
partial = [
    r for r in would_keep_rows
    if any(w in normalize(r['rootText']) for w in normalize(r['bookTitle']))
    and r['bookTitle'].strip() != r['rootText'].strip()
]
print(f"\n--- KEPT BUT SHARE SOME WORDS (potential false negatives, first 60) ---")
print(f"{'bookId':>8}  {'Book Title':<55}  First Root TOC")
print("-" * 110)
for r in partial[:60]:
    print(f"{r['bookId']:>8}  {r['bookTitle']:<55}  {r['rootText']}")
if len(partial) > 60:
    print(f"  ... and {len(partial) - 60} more")

# ── write full results to markdown ──────────────────────────────────────────
out_path = "scripts/toc_root_strip_validation.md"
with open(out_path, "w", encoding="utf-8") as f:
    f.write(f"# TOC Root Strip Validation\n\n")
    f.write(f"Ratio threshold: `{RATIO_THRESHOLD}`  \n")
    f.write(f"Total books: {total} | Would strip: {len(would_strip_rows)} (exact: {len(exact)}, fuzzy: {len(fuzzy)}) | Would keep: {len(would_keep_rows)}\n\n")

    f.write("## Fuzzy matches that would be stripped\n\n")
    f.write("| bookId | Book Title | Root TOC Entry |\n")
    f.write("|-------:|------------|----------------|\n")
    for r in fuzzy:
        bt = r['bookTitle'].replace('|', '\\|')
        rt = r['rootText'].replace('|', '\\|')
        f.write(f"| {r['bookId']} | {bt} | {rt} |\n")

    f.write("\n## Kept but share some words (potential false negatives)\n\n")
    f.write("| bookId | Book Title | Root TOC Entry |\n")
    f.write("|-------:|------------|----------------|\n")
    for r in partial:
        bt = r['bookTitle'].replace('|', '\\|')
        rt = r['rootText'].replace('|', '\\|')
        f.write(f"| {r['bookId']} | {bt} | {rt} |\n")

print(f"\nFull results written to {out_path}")
