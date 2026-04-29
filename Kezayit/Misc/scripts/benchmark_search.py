# -*- coding: utf-8 -*-
"""
Benchmark the Phase 1 search to find where time is actually spent.
"""
import sys, io, sqlite3, re, time
from collections import defaultdict
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"

HEBREW_RE = re.compile(r'[\u05d0-\u05ea]')
YOD = '\u05d9'
VAV = '\u05d5'
QUOTES_RE = re.compile(r'["\'״׳]')

def normalize(s):
    return QUOTES_RE.sub('', s.lower())

TITLE_VARIANTS = [
    (re.compile(r'שו["״]?ע'), 'שלחן ערוך'),
    (re.compile(r'שולחן'), 'שלחן'),
]

def normalize_book_path(text):
    for pattern, canonical in TITLE_VARIANTS:
        text = pattern.sub(canonical, text)
    return text

def decompose(word):
    chars = list(word)
    skeleton = []
    vowel_set = set()
    skel_idx = 0
    for i, ch in enumerate(chars):
        if ch in (YOD, VAV) and 0 < i < len(chars)-1 and HEBREW_RE.match(chars[i-1]) and HEBREW_RE.match(chars[i+1]):
            vowel_set.add(f"{skel_idx}:{ch}")
        else:
            skeleton.append(ch)
            skel_idx += 1
    return ''.join(skeleton), frozenset(vowel_set)

def are_variants(a_skel, a_vow, b_skel, b_vow):
    if a_skel != b_skel:
        return False
    return a_vow <= b_vow or b_vow <= a_vow

def score_word(query_raw, query_skel, query_vow, tokens, decomps):
    for i, token in enumerate(tokens):
        if token == query_raw:
            return 3
        t_skel, t_vow = decomps[i]
        if are_variants(t_skel, t_vow, query_skel, query_vow):
            return 3
    best = 0
    for token in tokens:
        if best < 2 and token.startswith(query_raw):
            best = 2
        elif best < 1 and query_raw in token:
            best = 1
    return best

# Load books
con = sqlite3.connect(DB)
con.row_factory = sqlite3.Row
cats = {r['id']: dict(r) for r in con.execute("SELECT id, parentId, title FROM category")}
rows = con.execute("""
    SELECT b.id, b.categoryId, b.title,
           GROUP_CONCAT(a.name, ', ') AS authors
    FROM book b
    LEFT JOIN book_author ba ON ba.bookId = b.id
    LEFT JOIN author a ON a.id = ba.authorId
    GROUP BY b.id ORDER BY b.id
""").fetchall()
con.close()

def build_path(cat_id):
    parts, visited, cid = [], set(), cat_id
    while cid is not None and cid not in visited:
        visited.add(cid)
        cat = cats.get(cid)
        if not cat: break
        parts.append(cat['title'])
        cid = cat['parentId']
    return ' / '.join(reversed(parts))

# Build index
t0 = time.perf_counter()
books = []
for r in rows:
    cat_path = build_path(r['categoryId'])
    full_path = f"{cat_path} / {r['title']}" if cat_path else r['title']
    author_part = f" {normalize_book_path(normalize(r['authors']))}" if r['authors'] else ''
    search_str = normalize_book_path(normalize(full_path)) + author_part
    words = [w for w in search_str.split() if w]
    decomps = [decompose(w) for w in words]
    books.append({'id': r['id'], 'words': words, 'decomps': decomps})
t_index = time.perf_counter() - t0
print(f"Index build: {t_index*1000:.1f}ms for {len(books)} books")

# Benchmark a single search
def run_search(query_raw):
    query_words = [w for w in normalize_book_path(normalize(query_raw)).split() if w]
    if not query_words:
        return []

    # Prepare query decompositions
    prepared = [(w, *decompose(w)) for w in query_words]

    # Pass 1: catalog best
    catalog_best = [0] * len(query_words)
    t_p1_start = time.perf_counter()
    for book in books:
        for wi, (qraw, qskel, qvow) in enumerate(prepared):
            if catalog_best[wi] == 3:
                continue
            tier = score_word(qraw, qskel, qvow, book['words'], book['decomps'])
            if tier > catalog_best[wi]:
                catalog_best[wi] = tier
    t_p1 = time.perf_counter() - t_p1_start

    if any(b == 0 for b in catalog_best):
        return []

    # Pass 2: filter
    t_p2_start = time.perf_counter()
    scored = []
    for book in books:
        total, ok = 0, True
        for wi, (qraw, qskel, qvow) in enumerate(prepared):
            tier = score_word(qraw, qskel, qvow, book['words'], book['decomps'])
            if tier < catalog_best[wi]:
                ok = False
                break
            total += tier
        if ok:
            scored.append((book['id'], total))
    t_p2 = time.perf_counter() - t_p2_start

    scored.sort(key=lambda x: -x[1])
    return scored, t_p1, t_p2

queries = ['רשי', 'תלמוד', 'בבלי', 'שלחן ערוך', 'רמבם', 'ב']
print()
for q in queries:
    result = run_search(q)
    if not result:
        print(f"  '{q}': no results")
        continue
    scored, t_p1, t_p2 = result
    total_ms = (t_p1 + t_p2) * 1000
    print(f"  '{q}': {len(scored)} results | pass1={t_p1*1000:.1f}ms pass2={t_p2*1000:.1f}ms total={total_ms:.1f}ms")

# Profile where time goes in score_word
print("\nProfiling score_word breakdown for query 'רשי':")
query_raw = 'רשי'
qskel, qvow = decompose(query_raw)

t_exact = 0
t_variant = 0
t_prefix = 0
N = 100

for _ in range(N):
    for book in books:
        for i, token in enumerate(book['words']):
            t0 = time.perf_counter()
            exact = token == query_raw
            t_exact += time.perf_counter() - t0

            if not exact:
                t0 = time.perf_counter()
                t_skel, t_vow = book['decomps'][i]
                _ = are_variants(t_skel, t_vow, qskel, qvow)
                t_variant += time.perf_counter() - t0

total_tokens = sum(len(b['words']) for b in books)
print(f"  Total tokens across all books: {total_tokens}")
print(f"  Per-token exact check:   {t_exact/N*1e6/total_tokens:.3f}µs avg")
print(f"  Per-token variant check: {t_variant/N*1e6/total_tokens:.3f}µs avg")
print(f"  Variant check is {t_variant/t_exact:.1f}x slower than exact check")
