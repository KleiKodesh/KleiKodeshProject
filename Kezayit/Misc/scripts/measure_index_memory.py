# -*- coding: utf-8 -*-
import sys, io, sqlite3, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"
QUOTES_RE = re.compile(r'["\'״׳]')
TITLE_VARIANTS = [
    (re.compile(r'שו["״]?ע'), 'שלחן ערוך'),
    (re.compile(r'שולחן'), 'שלחן'),
]

def normalize(s): return QUOTES_RE.sub('', s.lower())
def normalize_book_path(t):
    for p, c in TITLE_VARIANTS: t = p.sub(c, t)
    return t

con = sqlite3.connect(DB)
cats = {r[0]: (r[1], r[2]) for r in con.execute("SELECT id, parentId, title FROM category")}
rows = con.execute("SELECT b.id, b.categoryId, b.title, GROUP_CONCAT(a.name,', ') FROM book b LEFT JOIN book_author ba ON ba.bookId=b.id LEFT JOIN author a ON a.id=ba.authorId GROUP BY b.id").fetchall()
con.close()

def build_path(cid):
    parts, visited = [], set()
    while cid and cid not in visited:
        visited.add(cid)
        cat = cats.get(cid)
        if not cat: break
        parts.append(cat[1])
        cid = cat[0]
    return ' / '.join(reversed(parts))

all_tokens = []
for r in rows:
    path = build_path(r[1])
    full = f"{path} / {r[2]}" if path else r[2]
    auth = f" {normalize_book_path(normalize(r[3]))}" if r[3] else ''
    s = normalize_book_path(normalize(full)) + auth
    all_tokens.extend(w for w in s.split() if w)

print(f"Total tokens: {len(all_tokens)}")
print(f"Unique tokens (exact index size): {len(set(all_tokens))}")

prefix_keys = set()
for t in all_tokens:
    for l in range(1, len(t)+1):
        prefix_keys.add(t[:l])
print(f"Unique prefix keys: {len(prefix_keys)}")

contains_keys = set()
for t in set(all_tokens):
    for start in range(len(t)):
        for length in range(2, len(t)-start+1):
            contains_keys.add(t[start:start+length])
print(f"Unique contains keys: {len(contains_keys)}")

avg_set_overhead = 200
avg_entry = 50
exact_mem = len(set(all_tokens)) * (avg_set_overhead + avg_entry * 3)
prefix_mem = len(prefix_keys) * (avg_set_overhead + avg_entry * 5)
contains_mem = len(contains_keys) * (avg_set_overhead + avg_entry * 10)
print(f"\nEstimated memory:")
print(f"  Exact index:    ~{exact_mem//1024} KB")
print(f"  Prefix index:   ~{prefix_mem//1024} KB")
print(f"  Contains index: ~{contains_mem//1024} KB")
print(f"  Total:          ~{(exact_mem+prefix_mem+contains_mem)//1024} KB")
