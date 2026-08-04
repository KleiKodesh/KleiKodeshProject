"""Audit page for expansion-seforim.db -> expansion-audit.html (local Hebrew).

Three sections for the reader agent to judge:
  A. FOLD DIVERGENCES — surfaces where the Tanach table and the full-library
     table chose different lexemes. This is precisely where the "prefer a
     Tanach-attested base" register guard weakened; the reader judges which
     side is right.
  B. BIBLICAL sample — mid-frequency content words from Tanach verses with
     their full-library expansion rows.
  C. RABBINIC sample — same, sampled from non-Tanach lines.

Stdout: ASCII counts only.
"""
import html
import random
import sqlite3
import sys
from datetime import datetime

sys.path.insert(0, "tools")
sys.path.insert(0, ".")
# the expansion pipeline now lives with the artifact it builds
sys.path.insert(0, r"../../CSharpBackend/FtsLib-Csharp/SearchExpansion/build")
import masked

masked.install()

from expansion import tokens, SEFORIM  # noqa: E402

OUT = "expansion-audit.html"


def load(path):
    con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    fold = dict(con.execute("select surface, lemma from fold"))
    return con, fold


def exp_rows(con, lemma, per=(8, 6, 4)):
    rows = list(con.execute(
        "select form, channel from exp where lemma=? order by rank", (lemma,)))
    caps = dict(zip(("infl", "syn", "bridge"), per))
    out = {"infl": [], "syn": [], "bridge": []}
    for f, ch in rows:
        if len(out[ch]) < caps[ch]:
            out[ch].append(f)
    return out


def main():
    tc, tfold = load("expansion-tanach.db")
    sc, sfold = load("expansion-seforim.db")

    diverge = [(s, tl, sfold[s]) for s, tl in tfold.items()
               if s in sfold and sfold[s] != tl]
    print(f"tanach surfaces: {len(tfold):,}; fold divergences vs seforim table: "
          f"{len(diverge):,} ({len(diverge)/len(tfold)*100:.1f}%)")

    random.seed(42)
    div_sample = random.sample(diverge, min(25, len(diverge)))

    con = sqlite3.connect(f"file:{SEFORIM}?mode=ro", uri=True)

    def sample_words(where, n):
        freq = {}
        rows = con.execute(f"select content from line {where} "
                           f"order by random() limit 30000")
        for (c,) in rows:
            for t in tokens(c):
                freq[t] = freq.get(t, 0) + 1
        cands = [t for t, f in freq.items()
                 if 5 <= f and len(t) >= 3 and sfold.get(t, t) in has_exp]
        random.shuffle(cands)
        return cands[:n]

    has_exp = {r[0] for r in sc.execute("select distinct lemma from exp")}
    bib = sample_words("where bookId between 1 and 39", 12)
    rab = sample_words("where bookId > 39", 12)
    print(f"sampled {len(bib)} biblical, {len(rab)} rabbinic words")

    css = """
    body{font-family:'Segoe UI',Arial,sans-serif;background:#f4f6f8;margin:0;direction:rtl}
    header{background:#1a3a5c;color:#fff;padding:18px 28px}
    header h1{margin:0;font-size:20px} header p{margin:6px 0 0;opacity:.85;font-size:13px}
    h2{color:#1a3a5c;margin:22px 28px 6px;font-size:17px}
    table{border-collapse:collapse;margin:10px 28px;width:calc(100% - 56px);background:#fff}
    th,td{border:1px solid #e3e8ee;padding:6px 10px;text-align:right;font-size:15px;vertical-align:top}
    th{background:#eef2f6;color:#1a3a5c;font-size:13px}
    .lem{font-weight:600;color:#1a3a5c}
    .syn{color:#0b6e4f} .bridge{color:#6a1fa2} .infl{color:#345}
    footer{color:#889;font-size:12px;padding:14px 28px;direction:ltr}
    """
    parts = [f"<!doctype html><html lang='he'><head><meta charset='utf-8'>"
             f"<title>Expansion Table Audit</title><style>{css}</style></head><body>"
             f"<header><h1>expansion-seforim.db — register audit</h1>"
             f"<p>A: fold divergences vs the Tanach table &middot; B: biblical sample "
             f"&middot; C: rabbinic sample</p></header>"]

    parts.append(f"<h2>A. Fold divergences ({len(diverge):,} total, 25 sampled)</h2>")
    parts.append("<table><tr><th>#</th><th>surface</th><th>Tanach-table lexeme</th>"
                 "<th>seforim-table lexeme</th><th>seforim expansions (sample)</th></tr>")
    for i, (s, tl, sl) in enumerate(div_sample, 1):
        e = exp_rows(sc, sl, (4, 3, 2))
        cell = " ".join(f"<span class='{ch}'>{html.escape(f)}</span>"
                        for ch in ("infl", "syn", "bridge") for f in e[ch])
        parts.append(f"<tr><td>{i}</td><td>{html.escape(s)}</td>"
                     f"<td class='lem'>{html.escape(tl)}</td>"
                     f"<td class='lem'>{html.escape(sl)}</td><td>{cell}</td></tr>")
    parts.append("</table>")

    for title, words in (("B. Biblical sample", bib), ("C. Rabbinic sample", rab)):
        parts.append(f"<h2>{title}</h2>")
        parts.append("<table><tr><th>#</th><th>word</th><th>lexeme</th>"
                     "<th>inflections</th><th>synonyms</th><th>bridge</th></tr>")
        for i, w in enumerate(words, 1):
            lm = sfold.get(w, w)
            e = exp_rows(sc, lm)
            parts.append(
                f"<tr><td>{i}</td><td>{html.escape(w)}</td>"
                f"<td class='lem'>{html.escape(lm)}</td>"
                f"<td class='infl'>{' '.join(html.escape(x) for x in e['infl']) or '&mdash;'}</td>"
                f"<td class='syn'>{' '.join(html.escape(x) for x in e['syn']) or '&mdash;'}</td>"
                f"<td class='bridge'>{' '.join(html.escape(x) for x in e['bridge']) or '&mdash;'}</td></tr>")
        parts.append("</table>")

    parts.append(f"<footer>Generated {datetime.now():%Y-%m-%d %H:%M} · "
                 f"build_expansion_audit.py</footer></body></html>")
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("".join(parts))
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
