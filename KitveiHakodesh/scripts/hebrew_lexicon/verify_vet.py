#!/usr/bin/env python3
"""
Verify an agent's lexN_vet_<start>.json and emit an importable redirect batch.

Steps:
  1. shape-check the vet JSON ({keep, dropped_bases, dropped_forms})
  2. confirm every kept variant exists in the original lexN_cand_<start>.json (no invented forms)
  3. auto-flag kept forms whose Aramaic-CSV gloss points to a DIFFERENT Hebrew word than base
  4. write redirects/batch_from_vet_<start>.json  (list of {base,variants})
     plus print a report of auto-flags for hand review before import.

Usage: python verify_vet.py <start>
"""
import json, os, sys, re

# Output dir: set LEXICON_OUT to point this somewhere specific, else a temp dir.
SP=os.environ.get("LEXICON_OUT") or os.path.join(__import__("tempfile").gettempdir(), "hebrew_lexicon")
os.makedirs(SP, exist_ok=True)
HERE=os.path.dirname(os.path.abspath(__file__))
PRE=sorted(['','ו','ה','ש','כ','ל','ב','מ','וה','וש','וכ','ול','וב','ומ','שה','כש','מה','שב','של','שמ','ובכ','ולכ'],key=len,reverse=True)

def main():
    start=sys.argv[1]
    vet=json.load(open(os.path.join(SP,f"lexN_vet_{start}.json"),encoding="utf-8"))
    cand=json.load(open(os.path.join(SP,f"lexN_cand_{start}.json"),encoding="utf-8"))
    amap=json.load(open(os.path.join(SP,"aram_ref.json"),encoding="utf-8"))
    assert isinstance(vet.get("keep"),list), "keep missing"
    # 2. no invented forms
    cand_forms={b:set(fs) for b,fs in cand.items()}
    invented=[]
    for e in vet["keep"]:
        b=e["base"]
        for v in e["variants"]:
            if b not in cand_forms or v not in cand_forms[b]:
                invented.append(f"{b}/{v}")
    # 3. aramaic cross-check
    flags=[]
    for e in vet["keep"]:
        b=e["base"]
        for v in e["variants"]:
            for p in PRE:
                if v.startswith(p) and v[len(p):] in amap:
                    bare=v[len(p):]; gl=amap[bare]
                    if not any(b[:3] in g or g[:3]==b[:3] or b in g for g in gl):
                        flags.append(f"{v} (bare {bare}) base={b} CSV={gl}")
                    break
    # 4. emit batch
    batch=[{"base":e["base"],"variants":e["variants"]} for e in vet["keep"] if e["variants"]]
    outp=os.path.join(HERE,"redirects",f"batch_from_vet_{start}.json")
    json.dump(batch,open(outp,"w",encoding="utf-8"),ensure_ascii=False,indent=1)
    nb=len(batch); nv=sum(len(e['variants']) for e in batch)
    print(f"[{start}] keep {nb} bases / {nv} variants; dropped_bases={len(vet.get('dropped_bases',[]))} dropped_forms={len(vet.get('dropped_forms',[]))}")
    print(f"  invented(must be 0): {len(invented)}", invented[:10])
    print(f"  aramaic-mismatch flags (hand-review): {len(flags)}")
    for f in flags[:60]: print("   FLAG", f)

if __name__=="__main__": main()
