# -*- coding: utf-8 -*-
import sys, io, sqlite3, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"

HEBREW = re.compile(r'[\u05d0-\u05ea]')

def strip_matres(word):
    """
    Strip yod and vav only when they appear between two Hebrew consonants
    (vowel letters / matres lectionis). Never strip word-initial or word-final.
    Word must be >= 3 chars for any stripping to occur.
    """
    if len(word) < 3:
        return word
    result = []
    chars = list(word)
    for i, ch in enumerate(chars):
        if ch in ('\u05d9', '\u05d5') and 0 < i < len(chars) - 1:
            prev_is_heb = bool(HEBREW.match(chars[i-1]))
            next_is_heb = bool(HEBREW.match(chars[i+1]))
            if prev_is_heb and next_is_heb:
                continue
        result.append(ch)
    return ''.join(result)

con = sqlite3.connect(DB)
rows = con.execute("SELECT title FROM book").fetchall()
con.close()

titles = [r[0] for r in rows]

from collections import defaultdict
groups = defaultdict(list)
for title in titles:
    words = title.split()
    key = ' '.join(strip_matres(w) for w in words)
    groups[key].append(title)

variants = {k: v for k, v in groups.items() if len(v) > 1}
print(f"Groups with spelling variants: {len(variants)}\n")
for key, group_titles in sorted(variants.items())[:60]:
    print(f"  Normalized: {key}")
    for t in group_titles:
        print(f"    - {t}")
    print()

test_words = [
    'nidah', 'nida',
    '\u05e0\u05d9\u05d3\u05d4', '\u05e0\u05d3\u05d4',
    '\u05ea\u05d5\u05e8\u05d4',
    '\u05d9\u05e9\u05e8\u05d0\u05dc',
    '\u05de\u05e1\u05db\u05ea',
    '\u05e9\u05d9\u05e8',
    '\u05d5\u05d9\u05e7\u05e8\u05d0',
    '\u05de\u05d9\u05dc\u05d4', '\u05de\u05dc\u05d4',
    '\u05e6\u05d9\u05d5\u05df',
    '\u05d7\u05d9\u05d9\u05dd',
    '\u05e9\u05d5\u05dc\u05d7\u05df', '\u05e9\u05dc\u05d7\u05df',
    '\u05de\u05d9\u05e9\u05e0\u05d4', '\u05de\u05e9\u05e0\u05d4',
]
print("\nNormalization test:")
for w in test_words:
    print(f"  {w!r:30} -> {strip_matres(w)!r}")
