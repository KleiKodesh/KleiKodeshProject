# Dictionary spelling-redirects — RESUME (handoff 2026-07-20)

**Open a FRESH Claude Code chat in this repo and point it here. Do NOT continue the chat that wrote this (it may auto-compact and get NetFree-blocked — see memory note netfree-badwords-block-handling).**

## Where we are
- **Last committed: batch 39** → master `3f15bc46` (`Dictionary: +979 ... pe-tzadi-slice`).
- **כתיב (redirect) links = 15331**, both DBs synced, integrity ok.
- source6 bases swept through **~base 2220** (alef → tzadi-start; done through צוף).
- Batches 30-39 all on master (32-39 via plumbing). A duplicate batch31 commit `0bb8de60` sits harmlessly on branch `fts-tokenizer-unify`.

## Next step = batch 40
Continue the per-batch loop at slice **2220**:
```
cd KitveiHakodesh/scripts/hebrew_lexicon
python extract_lexical.py 2220 120          # -> lex_review_2220.txt / lex_cand_2220.json (in THIS session's scratchpad)
```
Total source6 bases = **2855**, so ~5 slices of 120 remain (2220 → 2855, roughly tzadi → tav).

## The loop (per batch)
1. `extract_lexical.py <start> 120` — writes review + cand JSON to the scratchpad. **Update the `SP=` path in extract_lexical.py to the NEW session's scratchpad dir first.**
2. **Read the review file and hand-vet every base-group** (user rule: "confirm manually, don't be lazy"). Drop:
   - **corrupt OCR tokens**: triple-letter (מחיייה), doubled-letter (המללות), truncated/missing-final (מלאכ, מחיצ), non-final-letter-mid-word (מלחתימ, מותרינ).
   - **genuine different-words / homographs** (no meaning-mixing): e.g. dropped so far מכור, מכלים, הכרתי, שכתב, בלוט, לפתות, המום, מתרים, למידה, מכרה, מנים.
   - **KEEP same-word homographs** (same root/headword spelling: מילה word/circumcision, מטה staff/bed, מלח salt/sailor, כלה bride/consume).
3. Build `redirects/batch_0NN_lexical.json` = `[{"base":..,"variants":[..]}]` from the cand JSON minus drops.
4. `python validate_redirects.py redirects/batch_0NN_lexical.json` — CSV root cross-check. **WARN = likely different word → drop**, EXCEPT documented false-positives: ה/ל-infinitives whose base isn't in CSV (base root reads empty), and forms that clearly fit the base's possessive/inflection paradigm (e.g. לבדה kept). Pipe `| grep -E "WARN|confirmed-real="` to avoid dumping the huge UNLISTED list into context.
5. `python add_ketiv_redirects.py` (imports ALL batches, idempotent → only the new one lands; writes BOTH DBs).
6. Verify: both DBs same כתיב count (rose by the batch's variant count), `PRAGMA integrity_check` = ok, SYNCED true.
7. **Commit to master** — one-line ASCII message only (Hebrew/multiline breaks tool JSON): `Dictionary: +N spelling redirects (batch NN lexical.db source6 <slice>, hand-confirmed)`.

## CRITICAL — shared working tree (see memory concurrent-sessions-shared-worktree)
Another session works FtsLib/tokenizer in this SAME dir → shared git HEAD. A dict commit can land on the wrong branch. Either:
- Ask the user to give this session its own `git worktree`, OR
- Commit to master **via plumbing** (never checkout/reset --hard): `git hash-object -w <db1> <db2>` → temp `GIT_INDEX_FILE` `git read-tree master` + `git update-index --cacheinfo` ×2 → `git write-tree` → `git commit-tree -p master` → `git update-ref refs/heads/master`; then `git reset -q -- <db1> <db2>` to reconcile the index. **Stage ONLY the two .db files; never touch the tokenizer session's FtsLib/*.cs, *.vue, or untracked files.**

The two DBs:
- `CSharpBackend/KitveiHakodeshService/Dictionary/Dictionary.db`
- `vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db`
