# -*- coding: utf-8 -*-
"""
Analyze Hebrew spelling variants in the catalog.
Find pairs of words that differ only by presence/absence of matres lectionis (י/ו).
Goal: find a rule that matches נידה/נדה and אירוסין/ארוסין
but does NOT match שבועות/שביעית.
"""
import sys, io, sqlite3, re
from collections import defaultdict
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"
HEBREW = re.compile(r'[\u05d0-\u05ea]')
YOD = '\u05d9'
VAV = '\u05d5'

def is_mid_vowel_letter(chars, i):
    """True if chars[i] is a yod or vav between two Hebrew consonants."""
    ch = chars[i]
    if ch not in (YOD, VAV):
        return False
    if i == 0 or i == len(chars) - 1:
        return False
    return bool(HEBREW.match(chars[i-1])) and bool(HEBREW.match(chars[i+1]))

def decompose(word):
    """
    Returns (skeleton, vowel_positions) where:
    - skeleton: word with all mid-word yod/vav removed
    - vowel_positions: list of (position_in_skeleton, letter) for each stripped letter
      position_in_skeleton = index in skeleton where the letter would be inserted
    """
    chars = list(word)
    skeleton = []
    vowel_positions = []
    skel_pos = 0
    for i, ch in enumerate(chars):
        if is_mid_vowel_letter(chars, i):
            vowel_positions.append((skel_pos, ch))
        else:
            skeleton.append(ch)
            skel_pos += 1
    return ''.join(skeleton), vowel_positions

def words_are_variants(w1, w2):
    """
    Two words are spelling variants if:
    1. They have the same consonantal skeleton
    2. One is the חסר (defective) form of the other:
       - The vowel letters in the shorter form are a subset of those in the longer form
       - AND they appear at the same skeleton positions
    """
    if w1 == w2:
        return True
    s1, vp1 = decompose(w1)
    s2, vp2 = decompose(w2)
    if s1 != s2:
        return False
    # One's vowel positions must be a subset of the other's (same positions, same letters)
    vp1_set = set(vp1)
    vp2_set = set(vp2)
    return vp1_set <= vp2_set or vp2_set <= vp1_set

# Load all unique words from book titles
con = sqlite3.connect(DB)
rows = con.execute("SELECT title FROM book").fetchall()
con.close()

all_words = set()
for (title,) in rows:
    for w in title.split():
        if any(HEBREW.match(c) for c in w):
            all_words.add(w)

print(f"Unique Hebrew words in catalog: {len(all_words)}\n")

# Group words by skeleton
by_skeleton = defaultdict(list)
for w in all_words:
    skel, _ = decompose(w)
    by_skeleton[skel].append(w)

# Find skeletons with multiple words
multi = {k: v for k, v in by_skeleton.items() if len(v) > 1}
print(f"Skeletons with multiple spellings: {len(multi)}\n")

# Show all cases, marking which pairs are variants vs false positives
print("=" * 70)
print("ALL SKELETON COLLISIONS")
print("=" * 70)
for skel, words in sorted(multi.items()):
    print(f"\nSkeleton: {skel!r}")
    for w in sorted(words):
        _, vp = decompose(w)
        print(f"  {w:25} vowel_positions={vp}")
    # Check all pairs
    for i in range(len(words)):
        for j in range(i+1, len(words)):
            w1, w2 = words[i], words[j]
            match = words_are_variants(w1, w2)
            print(f"  {'MATCH' if match else 'NO MATCH':10} {w1!r} <-> {w2!r}")

# Specific test cases
print("\n" + "=" * 70)
print("SPECIFIC TEST CASES")
print("=" * 70)
test_pairs = [
    ('\u05e0\u05d9\u05d3\u05d4', '\u05e0\u05d3\u05d4'),           # נידה / נדה
    ('\u05d0\u05d9\u05e8\u05d5\u05e1\u05d9\u05df', '\u05d0\u05e8\u05d5\u05e1\u05d9\u05df'),  # אירוסין / ארוסין
    ('\u05e9\u05d1\u05d5\u05e2\u05d5\u05ea', '\u05e9\u05d1\u05d9\u05e2\u05d9\u05ea'),  # שבועות / שביעית
    ('\u05dc\u05d9\u05e7\u05d5\u05d8\u05d9', '\u05dc\u05e7\u05d5\u05d8\u05d9'),        # ליקוטי / לקוטי
    ('\u05e1\u05d9\u05d3\u05d5\u05e8', '\u05e1\u05d3\u05d5\u05e8'),                    # סידור / סדור
    ('\u05ea\u05d5\u05e8\u05d4', '\u05ea\u05e8\u05d4'),                                # תורה / תרה (should NOT match - תרה is not a word)
    ('\u05de\u05d9\u05e9\u05e0\u05d4', '\u05de\u05e9\u05e0\u05d4'),                    # מישנה / משנה
]
for w1, w2 in test_pairs:
    s1, vp1 = decompose(w1)
    s2, vp2 = decompose(w2)
    match = words_are_variants(w1, w2)
    print(f"\n  {w1:15} skel={s1!r:15} vp={vp1}")
    print(f"  {w2:15} skel={s2!r:15} vp={vp2}")
    print(f"  Result: {'MATCH' if match else 'NO MATCH'}")
