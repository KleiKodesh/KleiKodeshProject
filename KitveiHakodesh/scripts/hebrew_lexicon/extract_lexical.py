#!/usr/bin/env python3
"""
Extract candidate spelling-redirects from lexical.db for MANUAL review.

For a slice of source-6 dictionary headwords that are also lexical `base` values,
pull every `surface` form rolling up to that base, drop obvious noise, and emit:
  - a human-readable REVIEW file grouped by base (for hand-vetting), and
  - a candidates JSON (base -> [surface forms]) to import AFTER review.

Noise filters (pre-review):
  - surface must be pure Hebrew letters (drops "(א)", "ב.", latin, spaces, merged tokens with punctuation)
  - surface != base, and surface not already a dictionary headword (those resolve directly)
  - surface not already one of our redirect variants (avoid re-review)
  - surface must be MORPHOLOGICALLY DERIVABLE from the base: after stripping a valid
    proclitic cluster (ו/ה/ש/כ/ל/ב/מ combos), the remainder must begin with the base
    stem (or its male/haser variant). This drops merged tokens (גםשניכם) and stray
    letters (ב) that lexical.db's corpus pipeline left attached to a base.

Usage: python extract_lexical.py <start> <count>   # slice of sorted source6 bases
Outputs (scratchpad): lex_review_<start>.txt , lex_cand_<start>.json
"""
import sqlite3, os, re, sys, json

LEX=r"C:\Users\Public\Documents\Dictionary\Backup\lexical.db"
DICT=r"C:\Users\Public\Documents\KleiKodeshProject\KitveiHakodesh\CSharpBackend\KitveiHakodeshService\Dictionary\Dictionary.db"
# Output dir: set LEXICON_OUT to point this somewhere specific, else a temp dir.
SP=os.environ.get("LEXICON_OUT") or os.path.join(__import__("tempfile").gettempdir(), "hebrew_lexicon")
os.makedirs(SP, exist_ok=True)
HEB=re.compile(r'^[א-ת]+$')
PREFIXES=sorted({'','ו','ה','ש','כ','ל','ב','מ','וה','וש','וכ','ול','וב','ומ','שה','שכ',
  'של','שב','שמ','כש','מה','לכ','וכ','והכ','ולכ','וכש','ושה','ומה','כשה','לכש','מש'}, key=len, reverse=True)

def stems(base):
    st = base[:-1] if base.endswith('ה') else base
    out={st, base}
    # male/haser: remove one internal vav/yod, add none (keep it simple)
    for i,ch in enumerate(st):
        if ch in 'וי' and 0<i: out.add(st[:i]+st[i+1:])
    return {s for s in out if len(s)>=2}

def derivable(surface, base):
    S=stems(base)
    head3={s[:3] for s in S}|{s[:2] for s in S if len(s)==2}
    for p in PREFIXES:
        if surface.startswith(p):
            rem=surface[len(p):]
            if not rem: continue
            if rem in S or any(rem.startswith(s) for s in S): return True
            if any(rem.startswith(h) for h in head3) and len(rem)>=len(min(S,key=len)): return True
    return False

def main():
    start=int(sys.argv[1]); count=int(sys.argv[2])
    d=sqlite3.connect(f"file:{DICT}?mode=ro",uri=True)
    dict_hw={h for (h,) in d.execute("SELECT headword FROM word")}
    src6=sorted({h for (h,) in d.execute("SELECT DISTINCT w.headword FROM sense s JOIN word w ON w.id=s.word_id WHERE s.source_id=6")})
    # existing redirect variants (sense-less כתיב sources) to skip
    existing_variants={h for (h,) in d.execute("""SELECT wv.headword FROM link l JOIN link_kind lk ON lk.id=l.kind_id AND lk.name='כתיב'
       JOIN word wv ON wv.id=l.word_id WHERE (SELECT COUNT(*) FROM sense s WHERE s.word_id=wv.id)=0""")}
    d.close()
    c=sqlite3.connect(f"file:{LEX}?mode=ro",uri=True)
    bid={v:i for i,v in c.execute("SELECT id,value FROM base")}
    sl=src6[start:start+count]
    review=[]; cand={}
    for b in sl:
        if b not in bid: continue
        surs=[r[0] for r in c.execute("SELECT value FROM surface WHERE base_id=?",(bid[b],))]
        keep=[]
        for s in surs:
            if s==b or not HEB.match(s): continue
            if s in dict_hw or s in existing_variants: continue
            if not derivable(s,b): continue
            keep.append(s)
        keep=sorted(set(keep))
        if keep:
            cand[b]=keep
            review.append(f"{b}  ({len(keep)}):  " + "  ".join(keep))
    c.close()
    json.dump(cand, open(os.path.join(SP,f"lex_cand_{start}.json"),"w",encoding="utf-8"), ensure_ascii=False, indent=1)
    open(os.path.join(SP,f"lex_review_{start}.txt"),"w",encoding="utf-8").write(
        f"source6 bases {start}..{start+count} with lexical candidates: {len(cand)}  (total forms: {sum(len(v) for v in cand.values())})\n\n"+"\n".join(review))
    print(f"bases with candidates: {len(cand)}  forms: {sum(len(v) for v in cand.values())}  -> lex_review_{start}.txt / lex_cand_{start}.json")

if __name__=="__main__": main()
