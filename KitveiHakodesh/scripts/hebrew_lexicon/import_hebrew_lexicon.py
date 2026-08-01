"""
import_hebrew_lexicon.py
========================
Adds curated GENERAL Hebrew vocabulary (everyday nouns, verbs, adjectives,
nature, science, household, etc.) to the KitveiHakodesh dictionary under the
existing source_kind "מילון עברי" (source id 6).

Why this exists
---------------
The bundled "מילון עברי" source is Torah-centric and thin on plain, everyday
Hebrew vocabulary. This importer fills that gap so a reader can look up ordinary
words (שולחן, נהר, מהר, כחול …) and get a short gloss + nikud.

Scope guardrails (per request)
------------------------------
* ONLY general/secular vocabulary.
* NO religious / halachic / theological terms, names of God, mitzvot,
  festivals-as-observances, prayer/Temple terms, or biblical figures.
* NO vulgar / adult / violent / otherwise inappropriate content.
A defensive BLOCKLIST below rejects anything that slips into the batch files;
the real gate is careful curation of the JSON, this is only a backstop.

Safety / idempotency
--------------------
* PURELY ADDITIVE. It inserts a sense only when no sense with the same
  (word_id, text, source_id=6) already exists, so re-running never duplicates
  and NEVER deletes the pre-existing curated entries.
* Before the first insert into a DB it snapshots the pre-existing source-6
  (headword, text) pairs to baseline_source6/<db>.json — the "protected"
  set — so a future cleanup can remove exactly my additions and nothing else.

Data
----
Reads every terms/batch_*.json. Each file is a JSON array of:
    {"hw": "שולחן", "nikud": "שֻׁלְחָן", "defs": ["רהיט בעל משטח ורגליים"]}
`nikud` is optional (null/omit when unsure). `defs` is a list of short glosses.

Targets both committed copies:
    CSharpBackend/KitveiHakodeshService/Dictionary/Dictionary.db      (service)
    vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db       (frontend)
A rebuild/restart of the service is needed for a running dev instance to pick
up the change (the service reads its bin copy).

Run:
    python import_hebrew_lexicon.py --dry-run     # report only, no writes
    python import_hebrew_lexicon.py               # write both DBs
    python import_hebrew_lexicon.py --db service  # write one target only
"""

import glob
import json
import os
import re
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
TERMS_DIR = os.path.join(HERE, "terms")
BASELINE_DIR = os.path.join(HERE, "baseline_source6")
EXCLUDE_PATH = os.path.join(HERE, "exclude.json")

SOURCE_NAME = "מילון עברי"

TARGETS = {
    "service": os.path.join(
        REPO, "CSharpBackend", "KitveiHakodeshService", "Dictionary", "Dictionary.db"),
    "frontend": os.path.join(
        REPO, "vue-frontend", "public", "dictionary", "KitveiHakodesh_dictionary.db"),
}

NIKUD_RE = re.compile(r"[֑-ׇ]")

# ── Defensive blocklist (backstop only; curation is the real filter) ──────────
# Terms that, if present as a WHOLE WORD in a headword or gloss, reject the entry
# as out-of-scope (religious/halachic/inappropriate). Matching is whole-word
# (bounded by non-Hebrew-letters) so short words like כשר don't false-match
# inside unrelated words (e.g. כשרץ "when it crawls"). Curation is the real
# filter; this is only a backstop.
BLOCK_TERMS = [
    "הקב\"ה", "הקדוש ברוך הוא", "אלוקים", "אלהים", "יתברך",
    "מצווה", "מצוות", "הלכה", "הלכות", "כשר", "תפילה", "תפילין",
    "בית המקדש", "קרבן", "קרבנות", "כהן גדול", "תשובה שלמה",
]
_BLOCK_RE = re.compile(
    r"(?<![א-ת])(?:" + "|".join(re.escape(t) for t in BLOCK_TERMS) + r")(?![א-ת])")
BLOCK_HEADWORDS = set()  # exact headwords to reject if ever added


def strip_nikud(s: str) -> str:
    return NIKUD_RE.sub("", s)


def norm_ws(s: str) -> str:
    return re.sub(r"\s+", " ", s or "").strip()


def is_blocked(hw: str, text: str) -> str | None:
    if hw in BLOCK_HEADWORDS:
        return "blocked headword"
    m = _BLOCK_RE.search(hw + " ¦ " + text)
    if m:
        return f"blocked term {m.group(0)!r}"
    return None


def load_exclude() -> tuple[set, set]:
    """exclude.json = {"headwords":[...], "pairs":[[hw,text],...]}. Entries whose
    headword is in `headwords`, or whose (hw,text) is in `pairs`, are dropped from
    the dataset — and, with --prune, deleted from the DB. Used to purge
    over-elementary ("4-year-old lexicon") entries."""
    if not os.path.exists(EXCLUDE_PATH):
        return set(), set()
    with open(EXCLUDE_PATH, encoding="utf-8") as f:
        data = json.load(f)
    hws = {strip_nikud(norm_ws(h)).strip() for h in data.get("headwords", [])}
    pairs = {(strip_nikud(norm_ws(hw)).strip(), norm_ws(t)) for hw, t in data.get("pairs", [])}
    return hws, pairs


# ── Load & validate batch files ──────────────────────────────────────────────

def load_terms() -> tuple[list[dict], list[tuple], dict]:
    """Returns (entries, skipped, tally). Each entry: {hw, nikud|None, defs:[...]}.
    De-duplicates (hw, text) across all batch files; keeps nikud from the first
    occurrence that carries one.

    Returns a FLAT list of senses [{hw, nikud, text}], deduped by (hw, text).
    nikud is stored PER SENSE (not per headword) so homographs with different
    vocalization are preserved — e.g. מַעֲלָה (degree) vs מַעְלָה (upward),
    שֶׁבַע (seven) vs שָׂבֵעַ (sated).

    `tally` accounts for every gloss read off disk, so the caller can prove
    nothing vanished silently:
        drafted = kept + blocked + excluded + dup_in_batches + empty
    """
    files = sorted(glob.glob(os.path.join(TERMS_DIR, "batch_*.json")))
    excl_hw, excl_pairs = load_exclude()
    senses: list[dict] = []
    seen: dict[tuple[str, str], dict] = {}   # (hw, text) -> sense
    skipped: list[tuple] = []
    tally = dict(drafted=0, blocked=0, excluded=0, dup_in_batches=0, empty=0)

    for path in files:
        with open(path, encoding="utf-8") as f:
            arr = json.load(f)
        for raw in arr:
            hw = strip_nikud(norm_ws(raw.get("hw", ""))).strip()
            nikud = norm_ws(raw.get("nikud") or "") or None
            defs = [d for d in (raw.get("defs") or []) if norm_ws(d)]
            tally["drafted"] += max(len(defs), 1)
            if not hw or not defs:
                tally["empty"] += max(len(defs), 1)
                skipped.append((path, hw, "empty hw/defs"))
                continue
            if hw in excl_hw:
                tally["excluded"] += len(defs)
                continue
            for d in defs:
                text = norm_ws(d)
                if (hw, text) in excl_pairs:
                    tally["excluded"] += 1
                    continue
                reason = is_blocked(hw, text)
                if reason:
                    tally["blocked"] += 1
                    skipped.append((path, hw, reason))
                    continue
                key = (hw, text)
                if key in seen:
                    # same gloss twice: fill in nikud if the first lacked it
                    tally["dup_in_batches"] += 1
                    if nikud and not seen[key]["nikud"]:
                        seen[key]["nikud"] = nikud
                    continue
                s = {"hw": hw, "nikud": nikud, "text": text}
                senses.append(s)
                seen[key] = s

    if tally["excluded"]:
        print(f"Excluded {tally['excluded']} over-elementary entries (exclude.json)")
    return senses, skipped, tally


def report_gloss_collisions(db_path: str, senses: list[dict], source_id: int = 6):
    """Warn when a drafted gloss is already carried by a DIFFERENT headword.

    The importer dedupes on (word_id, text), so such an entry inserts fine — but
    an identical gloss on two unrelated words is nearly always a drafting slip,
    and the near-miss case (same word, same gloss) is dropped SILENTLY as a dup.
    Surfacing both is the point: reword the gloss, don't lose the entry."""
    if not os.path.exists(db_path):
        return
    conn = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    hits = []
    for s in senses:
        # Only entries not yet landed matter — an already-imported gloss that
        # matches another headword was reviewed on the round that added it.
        if conn.execute(
                "SELECT 1 FROM sense s JOIN word w ON w.id=s.word_id "
                "WHERE w.headword=? AND s.text=? AND s.source_id=? LIMIT 1",
                (s["hw"], s["text"], source_id)).fetchone():
            continue
        rows = conn.execute(
            "SELECT w.headword FROM sense s JOIN word w ON w.id=s.word_id "
            "WHERE s.text=? AND s.source_id=? AND w.headword<>?",
            (s["text"], source_id, s["hw"])).fetchall()
        if rows:
            hits.append((s["hw"], s["text"], [r[0] for r in rows]))
    conn.close()
    if hits:
        print(f"\n!! {len(hits)} gloss(es) already carried by another headword "
              f"— reword before committing:")
        for hw, text, others in hits:
            print(f"     {hw} | {text}   (also on: {', '.join(others)})")


# ── DB write ─────────────────────────────────────────────────────────────────

def ensure_source(cur) -> int:
    row = cur.execute("SELECT id FROM source_kind WHERE name=?", (SOURCE_NAME,)).fetchone()
    if row:
        return row[0]
    cur.execute("INSERT INTO source_kind (name) VALUES (?)", (SOURCE_NAME,))
    return cur.lastrowid


def load_baseline(db_tag: str) -> set:
    path = os.path.join(BASELINE_DIR, f"{db_tag}.json")
    if not os.path.exists(path):
        return set()
    with open(path, encoding="utf-8") as f:
        return {(hw, t) for hw, t in json.load(f)}


def snapshot_baseline(cur, source_id: int, db_tag: str):
    os.makedirs(BASELINE_DIR, exist_ok=True)
    path = os.path.join(BASELINE_DIR, f"{db_tag}.json")
    if os.path.exists(path):
        return  # already captured — do not overwrite (would swallow my adds)
    rows = cur.execute(
        "SELECT w.headword, s.text FROM sense s JOIN word w ON w.id=s.word_id "
        "WHERE s.source_id=?", (source_id,)).fetchall()
    with open(path, "w", encoding="utf-8") as f:
        json.dump([[hw, t] for hw, t in rows], f, ensure_ascii=False, indent=0)
    print(f"    baseline snapshot: {len(rows)} pre-existing pairs -> {os.path.basename(path)}")


def import_into(db_path: str, db_tag: str, senses: list[dict], dry_run: bool,
                prune: bool = False):
    if not os.path.exists(db_path):
        print(f"  !! missing DB: {db_path}")
        return
    conn = sqlite3.connect(db_path)
    conn.execute("PRAGMA busy_timeout=15000")
    cur = conn.cursor()
    source_id = ensure_source(cur)
    snapshot_baseline(cur, source_id, db_tag)
    baseline = load_baseline(db_tag)  # (hw, text) pairs that pre-existed — never touch

    word_cache: dict[str, int] = {}
    words_added = senses_added = senses_dup = nikud_fixed = 0
    for s in senses:
        hw, nikud, text = s["hw"], s["nikud"], s["text"]
        word_id = word_cache.get(hw)
        if word_id is None:
            row = cur.execute("SELECT id FROM word WHERE headword=?", (hw,)).fetchone()
            if row:
                word_id = row[0]
            else:
                if dry_run:
                    word_id = -1
                else:
                    cur.execute("INSERT INTO word (headword) VALUES (?)", (hw,))
                    word_id = cur.lastrowid
                words_added += 1
            word_cache[hw] = word_id

        existing = None
        if word_id != -1:
            existing = cur.execute(
                "SELECT id, nikud FROM sense WHERE word_id=? AND text=? AND source_id=? LIMIT 1",
                (word_id, text, source_id)).fetchone()

        if existing:
            senses_dup += 1
            # Correct nikud on rows I own (in my dataset, not in the protected
            # baseline). Fixes homograph mistakes from the old merge-by-headword.
            sid, cur_nikud = existing
            if nikud and cur_nikud != nikud and (hw, text) not in baseline:
                nikud_fixed += 1
                if not dry_run:
                    cur.execute("UPDATE sense SET nikud=? WHERE id=?", (nikud, sid))
            continue

        if not dry_run:
            cur.execute(
                "INSERT INTO sense (word_id, nikud, text, source_id) VALUES (?,?,?,?)",
                (word_id, nikud, text, source_id))
        senses_added += 1

    # ── Reconcile deletions: with --prune, make my source-6 rows match the
    # dataset exactly. Deletes only rows I own — i.e. source-6 senses whose
    # (headword,text) is NOT in the dataset AND NOT in the protected baseline.
    senses_deleted = words_deleted = 0
    if prune:
        want = {(s["hw"], s["text"]) for s in senses}
        rows = cur.execute(
            "SELECT s.id, w.headword, s.text FROM sense s JOIN word w ON w.id=s.word_id "
            "WHERE s.source_id=?", (source_id,)).fetchall()
        to_del = [sid for sid, hw, text in rows
                  if (hw, text) not in want and (hw, text) not in baseline]
        senses_deleted = len(to_del)
        if not dry_run and to_del:
            cur.executemany("DELETE FROM sense WHERE id=?", [(i,) for i in to_del])
            # Remove words left with no senses AND not referenced by any link,
            # excluding headwords that pre-existed (in the baseline).
            base_hw = {hw for hw, _ in baseline}
            orphan_rows = cur.execute(
                "SELECT w.id, w.headword FROM word w "
                "WHERE NOT EXISTS (SELECT 1 FROM sense s WHERE s.word_id=w.id) "
                "AND NOT EXISTS (SELECT 1 FROM link l WHERE l.word_id=w.id OR l.target_id=w.id)"
            ).fetchall()
            orphan_ids = [wid for wid, hw in orphan_rows if hw not in base_hw]
            words_deleted = len(orphan_ids)
            if orphan_ids:
                cur.executemany("DELETE FROM word WHERE id=?", [(i,) for i in orphan_ids])

    if dry_run:
        conn.rollback()
    else:
        conn.commit()
    total = cur.execute("SELECT COUNT(*) FROM sense WHERE source_id=?", (source_id,)).fetchone()[0]
    conn.close()
    tag = "[dry] " if dry_run else ""
    fixed = f", {nikud_fixed} nikud corrected" if nikud_fixed else ""
    pruned = f", -{senses_deleted} pruned (-{words_deleted} headwords)" if prune else ""
    print(f"  {tag}{db_tag}: +{senses_added} senses "
          f"(+{words_added} new headwords, {senses_dup} already present{fixed}{pruned}) "
          f"-> source now {total if not dry_run else str(total)+'~'} senses")
    return dict(added=senses_added, dup=senses_dup, words=words_added)


def main():
    dry_run = "--dry-run" in sys.argv
    prune = "--prune" in sys.argv
    which = None
    if "--db" in sys.argv:
        which = sys.argv[sys.argv.index("--db") + 1]

    senses, skipped, tally = load_terms()
    headwords = {s["hw"] for s in senses}
    print(f"Loaded {len(headwords)} headwords / {len(senses)} glosses "
          f"from {TERMS_DIR}")
    if skipped:
        # ALL of them, not a sample: a silently-dropped entry is the one bug
        # this tool can't otherwise show you.
        print(f"Skipped {len(skipped)} (blocked/empty):")
        for path, hw, reason in skipped:
            print(f"    {os.path.basename(path)}: {hw}  — {reason}")
    # Homographs carrying more than one vocalization — informational.
    nikud_by_hw: dict[str, set] = {}
    for s in senses:
        if s["nikud"]:
            nikud_by_hw.setdefault(s["hw"], set()).add(s["nikud"])
    multi = {h: v for h, v in nikud_by_hw.items() if len(v) > 1}
    if multi:
        print(f"Homographs with multiple nikud ({len(multi)}): " +
              ", ".join(f"{h}={'/'.join(sorted(v))}" for h, v in list(multi.items())[:12]))
    if not senses:
        print("No senses — add terms/batch_*.json first.")
        return

    # A drafted gloss that already sits on another headword inserts fine, but is
    # nearly always a slip — and its same-headword twin is dropped silently.
    report_gloss_collisions(TARGETS["service"], senses)

    targets = TARGETS if not which else {which: TARGETS[which]}
    print(f"\n{'DRY RUN — ' if dry_run else ''}Importing into: {', '.join(targets)}"
          f"{'  [PRUNE on]' if prune else ''}")
    results = {}
    for tag, path in targets.items():
        results[tag] = import_into(path, tag, senses, dry_run, prune=prune)

    # ── Accounting: every gloss drafted on disk must be accounted for ─────────
    t = tally
    reconciled = (len(senses) + t["blocked"] + t["excluded"]
                  + t["dup_in_batches"] + t["empty"])
    print(f"\nBatch files: {t['drafted']} glosses drafted = {len(senses)} kept "
          f"+ {t['blocked']} blocked + {t['excluded']} excluded "
          f"+ {t['dup_in_batches']} dup-in-batches + {t['empty']} empty")
    if reconciled != t["drafted"]:
        print(f"!! ACCOUNTING MISMATCH: {reconciled} accounted for vs "
              f"{t['drafted']} drafted — investigate before committing.")
    for tag, r in results.items():
        if r and r["added"] + r["dup"] != len(senses):
            print(f"!! {tag}: {r['added']} inserted + {r['dup']} already present "
                  f"= {r['added'] + r['dup']}, but {len(senses)} were offered. "
                  f"{len(senses) - r['added'] - r['dup']} entr(ies) vanished — "
                  f"find them before committing.")
    print("\nDone." + ("" if not dry_run else "  (dry run — nothing written)"))


if __name__ == "__main__":
    main()
