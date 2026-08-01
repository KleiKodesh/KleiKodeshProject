#!/usr/bin/env python3
"""
Safety validator for a redirect batch BEFORE import. For each (base, variant):
 - CROSS-CHECK against the pealim/wiki CSV: if the variant is a LISTED real word
   whose root(s) do NOT overlap the base's root(s), it's almost certainly a
   DIFFERENT word -> WARN (drop it). Same-root or unlisted -> ok.
 - Flag variants that are already dictionary entries (importer will skip them).
Usage: python validate_redirects.py redirects/batch_00N.json
"""
import csv, sqlite3, sys, json, os
CSV=r"C:\Users\Public\Documents\Dictionary\Backup\merged_pealim_wikiDictinary_shorashim.csv"
DB=r"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db"

form_roots={}
with open(CSV,encoding="utf-8",newline="") as f:
    r=csv.reader(f); next(r,None)
    for row in r:
        row=[x.strip() for x in row if x.strip()]
        if not row: continue
        for fm in row: form_roots.setdefault(fm,set()).add(row[0])

def main(path):
    c=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
    def has_sense(w): return bool(c.execute("SELECT 1 FROM sense s JOIN word w ON w.id=s.word_id WHERE w.headword=? LIMIT 1",(w,)).fetchone())
    warns=confirmed=unlisted=skips=0
    unlisted_list=[]
    for e in json.load(open(path,encoding="utf-8")):
        base=e["base"]; bR=form_roots.get(base,set())
        for v in e["variants"]:
            if has_sense(v):
                print(f"  SKIP  {v} -> {base}   (variant already a real entry)"); skips+=1; continue
            vR=form_roots.get(v)
            if vR is None:
                unlisted+=1; unlisted_list.append(f"{v} -> {base}")   # NOT in CSV — must be hand-verified as a REAL word
            elif vR & bR:
                confirmed+=1   # in CSV + same root  => attested real word, same family
            else:
                print(f"  WARN  {v} -> {base}   variant roots {sorted(vR)} NOT in base roots {sorted(bR)} — likely DIFFERENT word, DROP"); warns+=1
    c.close()
    if unlisted_list:
        print("  UNLISTED (not in CSV — confirm each is a genuine attested word, else drop):")
        for s in unlisted_list: print("     "+s)
    print(f"\n{os.path.basename(path)}: CSV-confirmed-real={confirmed}  UNLISTED(verify)={unlisted}  WARN(drop)={warns}  skip={skips}")

if __name__=="__main__":
    main(sys.argv[1])
