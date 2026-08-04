"""Search-expansion demo v3 -> search-demo.html (real Hebrew, LOCAL ONLY).

Correct relevance semantics this time (v2's flaw: it showed verses sharing ONE
expansion word with the query — no phrase is served by that):

  query   = 2-3 CONTENT words sampled from a real verse (what a user types)
  result  = a verse matching EVERY query word — literally, or through an
            expansion channel (inflection / dictionary synonym / Targum
            bridge) — with at least ONE non-literal match. Verses matching
            everything literally are excluded: literal search already finds
            them; we show only the ADDED results.

Every substituted word is tagged with its channel and highlighted, so each
result is fully traceable. Exact token matching only.
"""
import csv
import html
import os
import random
import re
import sqlite3
import sys
from collections import defaultdict
from datetime import datetime

sys.path.insert(0, "tools")
sys.path.insert(0, ".")
import masked

masked.install()

from assoc_db import open_index  # noqa: E402

SEFORIM = "C:/ProgramData/otzaria/books/seforim.db"
LEXICAL = "C:/Users/Public/Documents/Dictionary/Backup/lexical.db"
DICT_DB = r"C:\Users\Admin\AppData\Local\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db"
BRIDGE = "targum_bridge.csv"
OUT = "search-demo.html"

TAG = re.compile(r"<[^>]+>")
NIKUD = re.compile(r"[\u0591-\u05BD\u05BF-\u05C7]")
TOK = re.compile(r"[\u05D0-\u05EA]{2,}")
MAQAF = "\u05BE"


PAREN_REF = re.compile(r"\([^()]{1,6}\)")   # verse-number labels like (12)


def clean(t):
    t = TAG.sub("", t)
    t = PAREN_REF.sub(" ", t)                # never match citation metadata
    return NIKUD.sub("", t).replace(MAQAF, " ")


def tokens(t):
    return TOK.findall(clean(t))


def norm(t):
    return "".join(TOK.findall(clean(t)))


def load_lexicon():
    con = sqlite3.connect(f"file:{LEXICAL}?mode=ro", uri=True)
    bases = {r[0] for r in con.execute("select value from base")}
    fold_cands = defaultdict(set)            # surface -> ALL candidate bases
    surfaces_of = defaultdict(set)
    q = """select s.value, b.value from surface s join base b on b.id = s.base_id
           union
           select v.value, b.value
           from surface_variant sv
           join variant v on v.id = sv.variant_id
           join surface s on s.id = sv.surface_id
           join base b on b.id = s.base_id"""
    for surf, base in con.execute(q):
        surfaces_of[base].add(surf)
        if surf in bases:
            continue
        fold_cands[surf].add(base)
    con.close()
    return bases, fold_cands, surfaces_of


def load_synonyms():
    con = sqlite3.connect(f"file:{DICT_DB}?mode=ro", uri=True)
    syn = defaultdict(set)
    for a, b in con.execute(
            """select w1.headword, w2.headword from link l
               join word w1 on w1.id = l.word_id
               join word w2 on w2.id = l.target_id where l.kind_id = 1"""):
        a, b = norm(a), norm(b)
        if a and b and a != b:
            syn[a].add(b)
            syn[b].add(a)
    con.close()
    return syn


def load_bridge():
    br = defaultdict(set)
    with open(BRIDGE, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            ak = [k for k in row if "aram" in k.lower()][0]
            hk = [k for k in row if "heb" in k.lower()][0]
            a, h = norm(row[ak]), norm(row[hk])
            if a and h:
                br[h].add(a)
                br[a].add(h)
    return br


def main():
    bases, fold_cands, surfaces_of = load_lexicon()
    syn = load_synonyms()
    bridge = load_bridge()

    from gloss_channel import build_profiles
    gloss = build_profiles(1000)             # sense gate for the synonym channel

    # curation blocklist: known-bad dictionary synonym entries, identified by
    # human/agent review of the rendered demo (tools/syn_blocklist.txt, one
    # normalized term per line, local only). These are DATA bugs pending a fix
    # in the dictionary itself.
    blocklist = set()
    bl_path = os.path.join("tools", "syn_blocklist.txt")
    if os.path.exists(bl_path):
        with open(bl_path, encoding="utf-8") as f:
            blocklist = {norm(l.strip()) for l in f if l.strip()}
        print(f"synonym blocklist: {len(blocklist)} entries")

    con = sqlite3.connect(f"file:{SEFORIM}?mode=ro", uri=True)
    lines = [(set(tks), tks, c, r) for tks, c, r in
             ((tokens(c), c, r) for c, r in con.execute(
                 "select content, heRef from line where bookId between 1 and 39"))]
    freq = defaultdict(int)
    for _, tks, _, _ in lines:
        for t in tks:
            freq[t] += 1
    print(f"tanach verses: {len(lines):,}, surface vocab {len(freq):,}")

    ld = open_index("assoc-tanach-ld.db")

    _lemma_cache = {}

    def lemma_of(t):
        """Prefer a base that itself occurs in the Tanach — blocks rabbinic
        abbreviations / named entities from capturing biblical forms."""
        got = _lemma_cache.get(t)
        if got is not None:
            return got
        if t in bases:
            out = t
        else:
            cands = fold_cands.get(t)
            if not cands:
                out = t
            else:
                biblical = [b for b in cands if freq.get(b)]
                pool = biblical or list(cands)
                out = max(pool, key=len)     # longest base wins within the pool
        _lemma_cache[t] = out
        return out

    # Two guards, different jobs:
    # - INFLECTIONS: fold-back consistency — a form counts as an inflection of
    #   a lexeme only if folding it returns THAT lexeme. Purges cross-lexeme
    #   pollution in the surface table, and makes genuine high-frequency
    #   inflections safe to use (no frequency cap needed on this tier).
    # - SYNONYM/BRIDGE forms: content-shaped only (3+ letters, corpus freq
    #   <= 500) — the dictionary link table contains pronoun/particle entries
    #   that would otherwise match everywhere and void the conjunction.
    MATCH_MAX_FREQ = 500

    def matchable(f):
        return len(f) >= 3 and 0 < freq.get(f, 0) <= MATCH_MAX_FREQ

    def same_lexeme(f, base):
        """Fold-back with a containment relaxation: the strict argmax check
        purged cross-lexeme pollution but also genuine prefixed/conjugated
        forms. A form also counts if the lexeme appears contiguously inside it
        (Hebrew prefixes prepend; pollution pairs differ internally)."""
        if lemma_of(f) == base:
            return True
        return len(base) >= 3 and base in f

    def in_corpus_forms(base, exclude):
        return {s for s in surfaces_of.get(base, ())
                if freq.get(s) and same_lexeme(s, base)} - {exclude}

    def matchers(t):
        """word -> {form: (tier, channel)}; tier 0 literal, 1 infl, 2 syn, 3 bridge."""
        base = lemma_of(t)
        m = {t: (0, "lit")}
        for f in in_corpus_forms(base, t):
            m.setdefault(f, (1, "infl"))     # fold-back verified, no freq cap
        syns = set()
        for s in (syn.get(t, set()) | syn.get(base, set())):
            if len(s) < 3 or s in blocklist:
                continue
            # sense gate: a real synonym shares defining vocabulary with the
            # query word in the dictionary's own glosses. Kills junk links
            # (verbs-of-being, unrelated nouns) without a POS tagger. Words
            # without a gloss profile pass — absence of data is not evidence.
            # reject only on strong evidence: both glosses substantial yet
            # sharing not a single defining token
            pa, pb = gloss.get(base) or gloss.get(t), gloss.get(s)
            if pa and pb and len(pa) >= 5 and len(pb) >= 5 and not (pa & pb):
                continue
            used = False
            for f in {s} | in_corpus_forms(lemma_of(s), t):
                if matchable(f):
                    m.setdefault(f, (2, "syn"))
                    used = True
            if used:
                syns.add(s)                  # display only matcher-usable synonyms
        for f in (bridge.get(t, set()) | bridge.get(base, set())) - {t, base}:
            if matchable(f):
                m.setdefault(f, (3, "bridge"))
        return base, syns, m

    random.seed(20260805)
    pool = lines[:]
    random.shuffle(pool)

    demos = []
    used = set()
    for _, tks, content, ref in pool:
        if len(demos) >= 8:
            break
        # content words a user would type: mid-frequency, 3+ letters
        cand = [t for t in tks if 5 <= freq[t] <= 3000 and len(t) >= 3]
        seen = set()
        cand = [t for t in cand if not (t in seen or seen.add(t))]
        if len(cand) < 2 or ref in used:
            continue
        qwords = cand[:3]
        info = [(t, *matchers(t)) for t in qwords]   # (t, base, syns, m)
        if not any(len(m) > 1 for _, _, _, m in info):
            continue

        results = []
        for vset, vtks, c2, r2 in pool:
            if r2 == ref:
                continue
            # each verse token may satisfy ONE query word only; assign the
            # most constrained query word first; break tier ties by rarity
            options = []
            ok = True
            for t, _, _, m in info:
                opts = sorted(((m[f][0], freq.get(f, 0), f, m[f][1])
                               for f in vset if f in m))
                if not opts:
                    ok = False
                    break
                options.append((len(opts), t, opts))
            if not ok:
                continue
            consumed = set()
            per_word = []
            for _, t, opts in sorted(options, key=lambda x: x[0]):
                pick = next(((f, ch) for _, _, f, ch in opts
                             if f not in consumed), None)
                if pick is None:
                    ok = False
                    break
                consumed.add(pick[0])
                per_word.append((t, pick[0], pick[1]))
            nonlit = sum(1 for *_, ch in per_word if ch != "lit")
            if ok and nonlit >= 1:
                results.append((nonlit, r2, c2, per_word))
        results.sort(key=lambda x: x[0])   # fewest substitutions first
        # zero-result queries are SHOWN, not skipped — hiding the hard cases
        # would make the demo self-censoring
        used.add(ref)
        demos.append((" ".join(qwords), ref, info, results[:5],
                      len(results)))

    print(f"built {len(demos)} query demos, "
          f"{sum(len(d[3]) for d in demos)} results shown "
          f"({sum(d[4] for d in demos)} total found)")

    css = """
    body{font-family:'Segoe UI',Arial,sans-serif;background:#f4f6f8;margin:0;direction:rtl}
    header{background:#1a3a5c;color:#fff;padding:18px 28px}
    header h1{margin:0;font-size:20px} header p{margin:6px 0 0;opacity:.85;font-size:13px}
    .card{background:#fff;border:1px solid #dde3ea;border-radius:8px;margin:18px 28px;
          padding:16px 20px;box-shadow:0 1px 2px rgba(0,0,0,.05)}
    .q{font-size:21px;font-weight:700;color:#1a3a5c;letter-spacing:.5px}
    .src{color:#889;font-size:12px;margin-right:8px;font-weight:400}
    table{border-collapse:collapse;margin:10px 0;width:100%}
    th,td{border:1px solid #e3e8ee;padding:6px 10px;text-align:right;font-size:15px;vertical-align:top}
    th{background:#eef2f6;color:#1a3a5c;font-size:13px}
    .syn{color:#0b6e4f;font-weight:600} .bridge{color:#6a1fa2;font-weight:600}
    .infl{color:#345}
    .tag{font-size:11px;border-radius:3px;padding:1px 6px;margin-left:6px;font-weight:600}
    .tag.syn{background:#e2f3ec;color:#0b6e4f} .tag.bridge{background:#efe2f8;color:#6a1fa2}
    .tag.infl{background:#e8edf3;color:#345} .tag.lit{background:#f0f0f0;color:#777}
    .res{margin:8px 0;padding:8px 12px;background:#f8fafc;border-right:3px solid #1a3a5c;
         border-radius:4px;font-size:15px;line-height:1.8}
    .ref{color:#1a3a5c;font-weight:600;font-size:13px;display:block;margin-bottom:2px}
    mark{background:#ffe9a8;padding:0 2px;border-radius:2px}
    mark.sub{background:#c9ecdb}
    .count{color:#667;font-size:12px;margin:4px 0}
    details{margin-top:8px} summary{color:#999;font-size:12px;cursor:pointer}
    .relbox{color:#888;font-size:14px;padding:6px 10px;background:#fafafa;border-radius:4px}
    footer{color:#889;font-size:12px;padding:10px 28px 26px;direction:ltr}
    .note{margin:18px 28px;padding:10px 16px;background:#fff8e6;border:1px solid #eadfb8;
          border-radius:6px;font-size:13px;color:#5a4a1a;line-height:1.6}
    """
    parts = [f"<!doctype html><html lang='he'><head><meta charset='utf-8'>"
             f"<title>Search Expansion Demo v3</title><style>{css}</style></head><body>"
             f"<header><h1>Search expansion — conjunctive, precise channels</h1>"
             f"<p>a result must match EVERY query word; yellow = literal match, "
             f"green = matched through an expansion (tag shows the channel)</p></header>",
             "<div class='note'>Each card: the content words a user would type (taken from "
             "a real verse), the expansion table per word, then verses that match the WHOLE "
             "query but where at least one word matched only through an expansion — these "
             "are exactly the results literal search misses. Fewest substitutions shown "
             "first.</div>"]

    for phrase, ref, info, results, total in demos:
        parts.append("<div class='card'>")
        parts.append(f"<div class='q'>{html.escape(phrase)}"
                     f"<span class='src'>(words taken from {html.escape(ref or '')})</span></div>")
        parts.append("<table><tr><th>query word</th><th>lexeme</th>"
                     "<th>inflections</th><th>dictionary synonyms</th><th>bridge</th></tr>")
        for t, base, syns, m in info:
            infl = " ".join(sorted((f for f, (tier, ch) in m.items() if ch == "infl"),
                                   key=lambda f: -freq.get(f, 0))[:8]) or "&mdash;"
            sy = " ".join(sorted(syns)[:6]) or "&mdash;"
            br = " ".join(sorted(f for f, (tier, ch) in m.items()
                                 if ch == "bridge")[:4]) or "&mdash;"
            parts.append(f"<tr><td>{html.escape(t)}</td><td>{html.escape(base)}</td>"
                         f"<td class='infl'>{infl}</td><td class='syn'>{sy}</td>"
                         f"<td class='bridge'>{br}</td></tr>")
        parts.append("</table>")
        parts.append(f"<div class='count'>{total} additional verses found beyond "
                     f"literal search; showing {len(results)}</div>")
        if not results:
            parts.append("<div class='none' style='padding:6px 0'>no additional "
                         "results pass the guards for this query &mdash; shown "
                         "honestly rather than hidden</div>")
        for nsub, r2, c2, per_word in results:
            text = html.escape(html.unescape(clean(c2)))
            tags = []
            for t, f, ch in per_word:
                cls = "sub" if ch != "lit" else ""
                text = re.sub(
                    f"(?<![\u05D0-\u05EA])({re.escape(f)})(?![\u05D0-\u05EA])",
                    rf"<mark class='{cls}'>\1</mark>", text, count=1)
                if ch != "lit":
                    tags.append(f"<span class='tag {ch}'>{ch}: {html.escape(t)} "
                                f"&rarr; {html.escape(f)}</span>")
            parts.append(f"<div class='res'><span class='ref'>{html.escape(r2 or '')} "
                         f"{''.join(tags)}</span>{text}</div>")
        rel = []
        for t, base, _, _ in info:
            if base in ld.word_id:
                row = [w for w, _ in ld.neighbors(base, 6)]
                if row:
                    rel.append(f"<b>{html.escape(t)}</b>: " +
                               " ".join(html.escape(w) for w in row))
        if rel:
            parts.append("<details><summary>experimental: distributional rows "
                         "(logDice) — evaluation only, never used for results"
                         "</summary><div class='relbox'>" + "<br>".join(rel) +
                         "</div></details>")
        parts.append("</div>")

    parts.append(f"<footer>Generated {datetime.now():%Y-%m-%d %H:%M} · "
                 f"build_search_demo3.py · conjunctive match, channels: inflection / "
                 f"dictionary synonym / Targum bridge</footer></body></html>")

    with open(OUT, "w", encoding="utf-8") as f:
        f.write("".join(parts))
    print(f"wrote {OUT}")
    return demos


if __name__ == "__main__":
    main()
