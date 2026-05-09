# -*- coding: utf-8 -*-
"""
Measure actual memory usage of every data structure in the search system.
"""
import sys, io, sqlite3, re, sys as _sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"
QUOTES_RE = re.compile(r'["\'״׳]')
TITLE_VARIANTS = [
    (re.compile(r'שו["״]?ע'), 'שלחן ערוך'),
    (re.compile(r'שולחן'), 'שלחן'),
]
HE = 'ה'

def normalize(s): return QUOTES_RE.sub('', s.lower())
def normalize_book_path(t):
    for p, c in TITLE_VARIANTS: t = p.sub(c, t)
    return t

HEBREW_RE = re.compile(r'[\u05d0-\u05ea]')
YOD, VAV = '\u05d9', '\u05d5'

def decompose(word):
    chars = list(word)
    skeleton, vowel_set = [], set()
    si = 0
    for i, ch in enumerate(chars):
        if ch in (YOD, VAV) and 0 < i < len(chars)-1 and HEBREW_RE.match(chars[i-1]) and HEBREW_RE.match(chars[i+1]):
            vowel_set.add(f"{si}:{ch}")
        else:
            skeleton.append(ch)
            si += 1
    return ''.join(skeleton), frozenset(vowel_set)

con = sqlite3.connect(DB)
cats = {r[0]: (r[1], r[2]) for r in con.execute("SELECT id, parentId, title FROM category")}
rows = con.execute("""
    SELECT b.id, b.categoryId, b.title, GROUP_CONCAT(a.name,', ') AS authors
    FROM book b LEFT JOIN book_author ba ON ba.bookId=b.id
    LEFT JOIN author a ON a.id=ba.authorId GROUP BY b.id ORDER BY b.id
""").fetchall()
con.close()

def build_path(cid):
    parts, visited = [], set()
    while cid and cid not in visited:
        visited.add(cid); cat = cats.get(cid)
        if not cat: break
        parts.append(cat[1]); cid = cat[0]
    return ' / '.join(reversed(parts))

# Build book data
books = []
all_tokens_per_book = []
for r in rows:
    path = build_path(r[1])
    full = f"{path} / {r[2]}" if path else r[2]
    auth = f" {normalize_book_path(normalize(r[3]))}" if r[3] else ''
    s = normalize_book_path(normalize(full)) + auth
    tokens = [w for w in s.split() if w]
    books.append({'id': r[0], 'tokens': tokens})
    all_tokens_per_book.append(tokens)

all_tokens_flat = [t for tokens in all_tokens_per_book for t in tokens]
unique_tokens = set(all_tokens_flat)

print(f"Books: {len(books)}")
print(f"Total tokens: {len(all_tokens_flat)}")
print(f"Unique tokens: {len(unique_tokens)}")
print()

# ── Current memory usage ──────────────────────────────────────────────────────

import sys as sys2

def str_bytes(s): return sys2.getsizeof(s)
def set_bytes(s): return sys2.getsizeof(s) + sum(sys2.getsizeof(x) for x in s)

# searchWords: list of strings per book
search_words_mem = sum(
    sys2.getsizeof(tokens) + sum(sys2.getsizeof(t) for t in tokens)
    for tokens in all_tokens_per_book
)

# searchWordDecompositions: (skeleton_str, frozenset) per token
decomp_mem = 0
for tokens in all_tokens_per_book:
    for t in tokens:
        skel, vow = decompose(t)
        decomp_mem += sys2.getsizeof(skel) + sys2.getsizeof(vow)
        decomp_mem += sum(sys2.getsizeof(k) for k in vow)

# exact index: Map<string, Set<int>>
exact_index = {}
for bi, tokens in enumerate(all_tokens_per_book):
    for t in tokens:
        exact_index.setdefault(t, set()).add(bi)
        stripped = t[1:] if t.startswith(HE) and len(t) > 2 else None
        if stripped: exact_index.setdefault(stripped, set()).add(bi)

exact_mem = sys2.getsizeof(exact_index)
for k, v in exact_index.items():
    exact_mem += sys2.getsizeof(k) + sys2.getsizeof(v) + len(v) * 28  # 28 bytes per int in set

# skeleton index
skel_index = {}
for bi, tokens in enumerate(all_tokens_per_book):
    for t in tokens:
        skel, _ = decompose(t)
        skel_index.setdefault(skel, set()).add(bi)

skel_mem = sys2.getsizeof(skel_index)
for k, v in skel_index.items():
    skel_mem += sys2.getsizeof(k) + sys2.getsizeof(v) + len(v) * 28

# sorted tokens array
sorted_tokens = sorted(unique_tokens)
sorted_mem = sys2.getsizeof(sorted_tokens) + sum(sys2.getsizeof(t) for t in sorted_tokens)
# parallel sets
sorted_token_books = {t: set() for t in unique_tokens}
for bi, tokens in enumerate(all_tokens_per_book):
    for t in tokens:
        sorted_token_books[t].add(bi)
sorted_sets_mem = sum(sys2.getsizeof(s) + len(s)*28 for s in sorted_token_books.values())

print("=" * 60)
print("CURRENT MEMORY BREAKDOWN")
print("=" * 60)
print(f"  searchWords (strings per book):      {search_words_mem/1024:8.1f} KB")
print(f"  searchWordDecompositions:            {decomp_mem/1024:8.1f} KB  ← can FREE after index build")
print(f"  exact index (Map<str,Set<int>>):     {exact_mem/1024:8.1f} KB")
print(f"  skeleton index:                      {skel_mem/1024:8.1f} KB")
print(f"  sortedTokens array:                  {sorted_mem/1024:8.1f} KB")
print(f"  sortedTokenBooks sets:               {sorted_sets_mem/1024:8.1f} KB")
total = search_words_mem + decomp_mem + exact_mem + skel_mem + sorted_mem + sorted_sets_mem
print(f"  TOTAL:                               {total/1024:8.1f} KB  ({total/1024/1024:.2f} MB)")

print()
print("=" * 60)
print("OPTIMIZATION OPPORTUNITIES")
print("=" * 60)

# 1. Free decompositions after index build
print(f"\n1. Free searchWordDecompositions after index build:")
print(f"   Saves: {decomp_mem/1024:.1f} KB")

# 2. Replace Set<int> with Uint16Array (2 bytes per book index vs 28 bytes in Set)
# Book indices are 0..6854, fit in Uint16
set_entries = sum(len(v) for v in exact_index.values())
set_entries_skel = sum(len(v) for v in skel_index.values())
set_entries_sorted = sum(len(v) for v in sorted_token_books.values())
total_set_entries = set_entries + set_entries_skel + set_entries_sorted
current_set_mem = total_set_entries * 28  # 28 bytes per int in JS Set
typed_array_mem = total_set_entries * 2   # 2 bytes per Uint16
print(f"\n2. Replace Set<number> with Uint16Array (book indices 0..6854 fit in 16 bits):")
print(f"   Total set entries: {total_set_entries:,}")
print(f"   Current (Set):     {current_set_mem/1024:.1f} KB")
print(f"   Optimized (Uint16):{typed_array_mem/1024:.1f} KB")
print(f"   Saves: {(current_set_mem-typed_array_mem)/1024:.1f} KB")

# 3. Merge exact + skeleton into one lookup (skeleton of exact token = itself when no vowels stripped)
# Many tokens have no mid-word yod/vav, so skeleton == original
no_vowel_tokens = sum(1 for t in unique_tokens if decompose(t)[0] == t)
print(f"\n3. Merge exact+skeleton: {no_vowel_tokens}/{len(unique_tokens)} tokens have skeleton==original")
print(f"   Could skip separate skeleton lookup for those tokens")

# 4. Drop searchWords from BookRow after index is built
# searchWords is only needed for ensureBookSearchMetadata guard and for display
# The guard can use a boolean flag instead
sw_strings_mem = sum(sum(sys2.getsizeof(t) for t in tokens) for tokens in all_tokens_per_book)
sw_lists_mem = sum(sys2.getsizeof(tokens) for tokens in all_tokens_per_book)
print(f"\n4. Drop searchWords from BookRow after index build:")
print(f"   String data: {sw_strings_mem/1024:.1f} KB (shared with exact index strings — no real saving)")
print(f"   List objects: {sw_lists_mem/1024:.1f} KB  ← can free")

print(f"\nTotal achievable savings:")
savings = decomp_mem + (current_set_mem - typed_array_mem) + sw_lists_mem
print(f"  {savings/1024:.1f} KB  ({savings/1024/1024:.2f} MB)")
print(f"  New total: {(total-savings)/1024:.1f} KB  ({(total-savings)/1024/1024:.2f} MB)")
