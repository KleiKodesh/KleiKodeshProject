#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')
"""
Phase 1 search accuracy analysis for the query רשי.

Exactly replicates the TypeScript normalization and filtering pipeline
against the real seforim database.
"""

import sqlite3
import re
import sys

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"

# ── Exact replica of the JS normalization pipeline ──────────────────────────

QUOTES_RE = re.compile(r'["\'״׳]')

def normalize(s: str) -> str:
    """normalize() from normalizeText.ts: lowercase + strip quotes"""
    return QUOTES_RE.sub('', s.lower())

TITLE_VARIANTS = [
    (re.compile(r'שו["״]?ע'), 'שלחן ערוך'),
    (re.compile(r'שולחן'),    'שלחן'),
]

def normalize_book_query(text: str) -> str:
    """normalizeBookQuery() from bookQueryNormalizer.ts"""
    for pattern, canonical in TITLE_VARIANTS:
        text = pattern.sub(canonical, text)
    return text

def to_query_words(raw: str) -> list:
    """toQueryWords() from useBookCatalogSearch.ts"""
    normalized = normalize_book_query(normalize(raw.strip()))
    return [w for w in normalized.split() if w]

def make_search_words(full_path: str, authors) -> list:
    """ensureBookSearchMetadata() logic"""
    base = normalize_book_query(normalize(full_path))
    author_part = (' ' + normalize(authors)) if authors else ''
    search_path = base + author_part
    return [w for w in search_path.split() if w]

def filter_books(books, words):
    """
    filterBooksByWords() — exact replica.
    NOTE: JS uses .includes() not .startsWith() for the prefix word.
    This means 'רשי' matches any token that CONTAINS 'רשי' as a substring,
    not just tokens that START with it.
    """
    if not words:
        return []
    exact_words = words[:-1]
    prefix_word = words[-1]
    results = []
    for book in books:
        path_words = book['search_words']
        exact_match = all(
            any(pw == qw for pw in path_words)
            for qw in exact_words
        )
        # JS: pathWord.includes(prefixWord) — substring, not prefix!
        prefix_match = any(prefix_word in pw for pw in path_words)
        if exact_match and prefix_match:
            results.append(book)
    return sorted(results, key=lambda b: b['tree_order'])

def filter_books_startswith(books, words):
    """
    Improved version: uses startsWith() instead of includes() for prefix word.
    """
    if not words:
        return []
    exact_words = words[:-1]
    prefix_word = words[-1]
    results = []
    for book in books:
        path_words = book['search_words']
        path_word_set = set(path_words)
        exact_match = all(qw in path_word_set for qw in exact_words)
        prefix_match = any(pw.startswith(prefix_word) for pw in path_words)
        if exact_match and prefix_match:
            results.append(book)
    return sorted(results, key=lambda b: b['tree_order'])

# ── Load books from DB ───────────────────────────────────────────────────────

con = sqlite3.connect(DB)
con.row_factory = sqlite3.Row

cats = {r['id']: dict(r) for r in con.execute("SELECT id, parentId, title FROM category")}

def build_path(cat_id):
    parts = []
    visited = set()
    cid = cat_id
    while cid is not None and cid not in visited:
        visited.add(cid)
        cat = cats.get(cid)
        if not cat:
            break
        parts.append(cat['title'])
        cid = cat['parentId']
    return ' / '.join(reversed(parts))

rows = con.execute("""
    SELECT b.id, b.categoryId, b.title,
           GROUP_CONCAT(a.name, ', ') AS authors
    FROM book b
    LEFT JOIN book_author ba ON ba.bookId = b.id
    LEFT JOIN author a ON a.id = ba.authorId
    GROUP BY b.id
    ORDER BY b.id
""").fetchall()

books = []
for i, r in enumerate(rows):
    cat_path = build_path(r['categoryId'])
    full_path = f"{cat_path} / {r['title']}" if cat_path else r['title']
    sw = make_search_words(full_path, r['authors'])
    books.append({
        'id': r['id'],
        'title': r['title'],
        'full_path': full_path,
        'authors': r['authors'],
        'search_words': sw,
        'tree_order': i,
    })

con.close()
print(f"Total books loaded: {len(books)}")

# ── Normalization trace ──────────────────────────────────────────────────────

raw_query = 'רשי'
words = to_query_words(raw_query)

print(f"\n{'='*70}")
print("NORMALIZATION TRACE")
print(f"{'='*70}")
print(f"  Input:                    '{raw_query}'")
print(f"  After normalize():        '{normalize(raw_query)}'")
print(f"  After normalizeBookQuery: '{normalize_book_query(normalize(raw_query))}'")
print(f"  Final words:              {words}")
print(f"  exact_words (all but last): {words[:-1]}")
print(f"  prefix_word (last):         '{words[-1]}'")
print()
print("  Comparison: how רש\"י is indexed:")
sample_title = 'רש"י על התורה'
sw = make_search_words(sample_title, None)
print(f"  title '{sample_title}' -> search_words: {sw}")
print()
print("  Key question: does 'רשי' (no quotes) match 'רשי' (stripped from רש\"י)?")
print(f"  normalize('רש\"י') = '{normalize('רש\"י')}'  (quotes stripped -> 'רשי')")
print(f"  So 'רשי' in 'רשי' = {('רשי' in 'רשי')}  MATCH")

# ── Phase 1 results (current .includes() behavior) ──────────────────────────

results_current = filter_books(books, words)
results_improved = filter_books_startswith(books, words)

print(f"\n{'='*70}")
print("PHASE 1 RESULTS")
print(f"{'='*70}")
print(f"  Current (.includes):   {len(results_current)} books")
print(f"  Improved (.startsWith): {len(results_improved)} books")

# ── Categorize results ───────────────────────────────────────────────────────

def has_rashi_in_title(b):
    t = b['title']
    return 'רשי' in normalize(t)  # catches רשי, רש"י, רש׳י etc after normalization

rashi_title = [b for b in results_current if has_rashi_in_title(b)]
path_only   = [b for b in results_current if not has_rashi_in_title(b)]

print(f"\n  Of the {len(results_current)} results:")
print(f"    {len(rashi_title)} have רשי/רש\"י in their own title")
print(f"    {len(path_only)} matched only via category path (potential noise)")

# ── Show all results ─────────────────────────────────────────────────────────

print(f"\n{'='*70}")
print(f"ALL {len(results_current)} RESULTS (current algorithm)")
print(f"{'='*70}")

for b in results_current:
    matching_tokens = [w for w in b['search_words'] if words[-1] in w]
    marker = '✓' if has_rashi_in_title(b) else '⚠ PATH'
    print(f"  {marker} [{b['id']:5}] {b['title']}")
    if not has_rashi_in_title(b):
        print(f"           path: {b['full_path']}")
        print(f"           matched via: {matching_tokens}")

# ── False positives: path-only matches ──────────────────────────────────────

if path_only:
    print(f"\n{'='*70}")
    print(f"PATH-ONLY MATCHES — {len(path_only)} books (noise / false positives)")
    print(f"{'='*70}")
    print("These books don't have רשי in their title but appear because")
    print("their category path contains a word with 'רשי' as a substring.\n")
    for b in path_only:
        matching_tokens = [w for w in b['search_words'] if words[-1] in w]
        print(f"  [{b['id']:5}] {b['title']}")
        print(f"         full path: {b['full_path']}")
        print(f"         matched token(s): {matching_tokens}")
        print()

# ── False negatives: books with רש"י in title that didn't match ─────────────

result_ids = {b['id'] for b in results_current}
missed = [
    b for b in books
    if has_rashi_in_title(b) and b['id'] not in result_ids
]

print(f"\n{'='*70}")
print(f"MISSED MATCHES — {len(missed)} books with רשי/רש\"י in title that did NOT appear")
print(f"{'='*70}")
if missed:
    for b in missed[:30]:
        print(f"  [{b['id']:5}] {b['title']}")
        print(f"         search_words: {b['search_words'][:10]}")
        print()
else:
    print("  None — all רשי/רש\"י books were found.")

# ── .includes() vs .startsWith() difference ─────────────────────────────────

only_in_current  = [b for b in results_current  if b['id'] not in {x['id'] for x in results_improved}]
only_in_improved = [b for b in results_improved if b['id'] not in {b['id'] for b in results_current}]

print(f"\n{'='*70}")
print(".includes() vs .startsWith() DIFFERENCE")
print(f"{'='*70}")
print(f"  Results only in current (.includes):   {len(only_in_current)}")
print(f"  Results only in improved (.startsWith): {len(only_in_improved)}")

if only_in_current:
    print(f"\n  Books found by .includes() but NOT by .startsWith():")
    print("  (These are substring matches where 'רשי' appears mid-token)")
    for b in only_in_current[:20]:
        matching_tokens = [w for w in b['search_words'] if words[-1] in w]
        print(f"    [{b['id']:5}] {b['title']}")
        print(f"           matched token(s): {matching_tokens}")

if only_in_improved:
    print(f"\n  Books found by .startsWith() but NOT by .includes():")
    for b in only_in_improved[:20]:
        matching_tokens = [w for w in b['search_words'] if w.startswith(words[-1])]
        print(f"    [{b['id']:5}] {b['title']}")
        print(f"           matched token(s): {matching_tokens}")

# ── Summary ──────────────────────────────────────────────────────────────────

print(f"\n{'='*70}")
print("SUMMARY")
print(f"{'='*70}")
print(f"""
  Query 'רשי' normalizes to: {words}
  
  The key insight: normalize() strips ALL quote characters (including ״ and ׳),
  so 'רש\"י' → 'רשי' and the query 'רשי' → 'רשי'. They meet at the same token.
  
  Phase 1 results: {len(results_current)} books
    - {len(rashi_title)} are genuine Rashi books (רשי/רש\"י in title)
    - {len(path_only)} are path-only matches (category name contains רשי)
    - {len(missed)} Rashi books were missed (false negatives)
  
  .includes() vs .startsWith():
    - Current uses .includes() — matches 'רשי' anywhere in a token
    - This means 'ישראל' would match query 'ישר' (substring mid-word)
    - .startsWith() is more precise for prefix matching
    - Difference for this query: {len(only_in_current)} extra results from .includes()
""")
