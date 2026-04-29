# -*- coding: utf-8 -*-
"""
Analyze common Hebrew word prefixes in the catalog.
Find which prefixes can be safely stripped without causing false positives.
"""
import sys, io, sqlite3, re
from collections import Counter, defaultdict
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
rows = con.execute("""
    SELECT b.id, b.categoryId, b.title, GROUP_CONCAT(a.name,', ') AS authors
    FROM book b
    LEFT JOIN book_author ba ON ba.bookId=b.id
    LEFT JOIN author a ON a.id=ba.authorId
    GROUP BY b.id ORDER BY b.id
""").fetchall()
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

token_set = set(all_tokens)
token_freq = Counter(all_tokens)

print(f"Total tokens: {len(all_tokens)}")
print(f"Unique tokens: {len(token_set)}")

# Hebrew prefixes to analyze (common prepositions/articles attached to words)
# ה = definite article
# ו = conjunction "and"
# ב = preposition "in"
# ל = preposition "to"
# מ = preposition "from" (short form of מן)
# כ = preposition "like"
# ש = conjunction "that/which"
PREFIXES = ['ה', 'ו', 'ב', 'ל', 'מ', 'כ', 'ש', 'הר', 'רב', 'בן']

print(f"\n{'='*70}")
print("PREFIX ANALYSIS")
print(f"{'='*70}")

for prefix in PREFIXES:
    # Find tokens that start with this prefix and whose remainder is also a token
    strippable = []
    false_positive_risk = []
    
    for token in token_set:
        if not token.startswith(prefix):
            continue
        remainder = token[len(prefix):]
        if len(remainder) < 2:  # remainder too short to be meaningful
            continue
        if remainder in token_set:
            # Both הרמבן and רמבן exist — stripping is safe
            strippable.append((token, remainder))
        else:
            # הרמבן exists but רמבן doesn't — stripping would create a ghost token
            false_positive_risk.append((token, remainder))
    
    print(f"\nPrefix '{prefix}' ({len(strippable)} safe strips, {len(false_positive_risk)} risky):")
    if strippable[:8]:
        print(f"  Safe examples: {[f'{t}→{r}' for t,r in strippable[:8]]}")
    if false_positive_risk[:8]:
        print(f"  Risky examples (remainder not in catalog): {[t for t,r in false_positive_risk[:8]]}")

# Special focus: ה prefix (most important for רמבן/הרמבן)
print(f"\n{'='*70}")
print("DETAILED: ה PREFIX")
print(f"{'='*70}")
he_tokens = [(t, t[1:]) for t in token_set if t.startswith('ה') and len(t) > 2]
he_safe = [(t, r) for t, r in he_tokens if r in token_set]
he_risky = [(t, r) for t, r in he_tokens if r not in token_set]

print(f"Tokens starting with ה: {len(he_tokens)}")
print(f"  Where remainder IS also a token (safe to strip): {len(he_safe)}")
print(f"  Where remainder is NOT a token (risky): {len(he_risky)}")
print(f"\nSafe strips (first 20):")
for t, r in sorted(he_safe, key=lambda x: -token_freq[x[0]])[:20]:
    print(f"  {t:20} → {r:20} (freq: {token_freq[t]})")
print(f"\nRisky (first 20 by frequency):")
for t, r in sorted(he_risky, key=lambda x: -token_freq[x[0]])[:20]:
    print(f"  {t:20} → {r:20} (freq: {token_freq[t]})")

# Check: if we normalize ה-prefix tokens to their base form,
# what fraction of the catalog would be affected?
print(f"\n{'='*70}")
print("IMPACT OF ה NORMALIZATION")
print(f"{'='*70}")
he_normalized_tokens = set()
for t in token_set:
    if t.startswith('ה') and len(t) > 2:
        he_normalized_tokens.add(t[1:])
    else:
        he_normalized_tokens.add(t)
print(f"Unique tokens before ה normalization: {len(token_set)}")
print(f"Unique tokens after ה normalization:  {len(he_normalized_tokens)}")
print(f"Reduction: {len(token_set) - len(he_normalized_tokens)} tokens")

# Memory impact of dropping prefix index entirely and using sorted array
print(f"\n{'='*70}")
print("MEMORY: SORTED ARRAY vs PREFIX INDEX")
print(f"{'='*70}")
print(f"Prefix index: 5,412 unique keys × ~250 bytes = ~1.3 MB")
print(f"Sorted token array: {len(token_set)} tokens × ~30 bytes avg = ~{len(token_set)*30//1024} KB")
print(f"Binary search on sorted array: O(log {len(token_set)}) = ~{len(token_set).bit_length()} comparisons per lookup")
