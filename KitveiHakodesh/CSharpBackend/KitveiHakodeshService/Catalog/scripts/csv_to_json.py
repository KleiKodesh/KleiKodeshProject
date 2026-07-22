# -*- coding: utf-8 -*-
"""Audited CSV -> final production JSON, with validation.

CSV (UTF-8 BOM): abbreviation, expands_to, ambiguous, n_words, keep
  - abbreviation kept VERBATIM (carries ' or " exactly as the auditor typed)
  - expands_to : alternatives split on ' | '; words split on whitespace
  - n_words / ambiguous : IGNORED (recomputed / derived — auditor edits made them stale)
  - keep : ignored per the user ("i ignored the keep field, i simply deleted")

JSON out:
  { "abbreviations": { "<key>": [ [w,w,...], [w,w,...] ], ... } }
"""
import csv, io, json, sys
from collections import OrderedDict

SRC = "catalog_abbreviations.csv"
OUT = "catalog_abbreviations.json"

problems = []
final = OrderedDict()

with io.open(SRC, encoding="utf-8-sig", newline="") as f:
    reader = csv.DictReader(f)
    for lineno, row in enumerate(reader, start=2):  # header = line 1
        key = (row.get("abbreviation") or "").strip()
        expands = (row.get("expands_to") or "").strip()

        if not key and not expands:
            continue  # blank trailing line
        if not key:
            problems.append(f"line {lineno}: empty abbreviation (expands_to={expands!r})")
            continue
        if not expands:
            problems.append(f"line {lineno}: '{key}' has empty expands_to")
            continue

        alts = []
        for alt in expands.split("|"):
            words = alt.split()          # collapse any stray double spaces
            if not words:
                problems.append(f"line {lineno}: '{key}' has an empty alternative (check ' | ' usage)")
                continue
            alts.append(words)
        if not alts:
            problems.append(f"line {lineno}: '{key}' produced no alternatives")
            continue

        if key in final:
            problems.append(f"line {lineno}: DUPLICATE key '{key}' (already defined) — later one wins")
        final[key] = alts

obj = {"abbreviations": final}

# --- JSON correctness: serialize then re-parse to prove validity ---
text = json.dumps(obj, ensure_ascii=False, indent=2)
try:
    json.loads(text)
except json.JSONDecodeError as e:
    print("FATAL: produced invalid JSON:", e, file=sys.stderr)
    sys.exit(1)

io.open(OUT, "w", encoding="utf-8").write(text)

# --- report ---
n = len(final)
amb = sum(1 for a in final.values() if len(a) > 1)
print(f"OK: wrote {OUT}  ({n} entries, {amb} ambiguous/OR-union)")
print("JSON re-parse: VALID")

# sanity flags (not fatal) — things worth an eyeball
for key, alts in final.items():
    for words in alts:
        for w in words:
            if any(c in w for c in ('"', "'", "|", ",")):
                problems.append(f"'{key}': target word {w!r} still contains a quote/sep — likely a split mistake")

if problems:
    print(f"\n{len(problems)} thing(s) to review:")
    for p in problems:
        print("  -", p)
else:
    print("no structural problems detected")
