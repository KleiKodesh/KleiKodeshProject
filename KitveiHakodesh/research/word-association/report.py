"""
HTML report for the word-association index.

Styling deliberately mirrors FtsLib's report generator
(`CSharpBackend/FtsLib-Csharp/FtsLibTest/Shared/HtmlReport.cs`) so the two look
like they came from the same project: same palette (#1a3a5c headers, #f4f6f9
page), same card/table/meta/alert structure, same "Generated ..." footer.

Usage:
    python report.py                                  # report on index/
    python report.py --index index-full --open
    python report.py --index index-full --compare index index-bavli-base
"""

from __future__ import annotations

import argparse
import datetime as _dt
import html
import json
import os
import statistics
import subprocess
import sys
import time
from pathlib import Path

from assoc_db import open_index
from evaluate import RELATIONS, evaluate, load_gold, random_baseline

# Terms used for the qualitative panels. Chosen as mid-frequency content words,
# which FINDINGS.md §12 identifies as this method's actual operating range.
SHOWCASE = ["מזבח", "זהב", "כספ", "מלכ", "כהנ", "שבת", "מלחמה", "לחמ",
            "ברכה", "אור", "צדיק", "חכמה"]

# Pairs the index should rank close if the contrast finding (§10) holds.
CONTRAST_PAIRS = [("אור", "חשכ"), ("בקר", "ערב"), ("רשע", "צדיק"),
                  ("ברכה", "קללה"), ("קרוב", "רחוק"), ("מלא", "ריק"),
                  ("חי", "מת"), ("אמת", "שקר")]

EXPAND_QUERIES = ["מזבח קרבנ", "מלכ מלחמה", "לחמ יינ", "חכמה מוסר"]


def esc(s) -> str:
    return html.escape(str(s), quote=True)


class Report:
    """Accumulates HTML fragments. Mirrors HtmlReport.cs's builder API."""

    def __init__(self, title: str):
        self.title = title
        self.parts: list[str] = []

    def banner(self, text: str) -> None:
        self.parts.append(f"<div class='banner'>{esc(text)}</div>")

    def meta(self, label: str, value) -> None:
        self.parts.append(
            f"<div class='meta'><span class='meta-label'>{esc(label)}</span>"
            f"<span class='meta-value'>{esc(value)}</span></div>")

    def section(self, heading: str) -> None:
        self.parts.append(f"<h2>{esc(heading)}</h2>")

    def note(self, text: str, error: bool = False) -> None:
        cls = "alert alert-error" if error else "alert alert-info"
        self.parts.append(f"<div class='{cls}'>{text}</div>")

    def raw(self, h: str) -> None:
        self.parts.append(h)

    def table(self, headers, rows, cell_class=None, rtl_cols=()) -> None:
        out = ["<div class='table-wrap'><table><thead><tr>"]
        out += [f"<th>{esc(h)}</th>" for h in headers]
        out.append("</tr></thead><tbody>")
        for r, row in enumerate(rows):
            out.append("<tr>")
            for c, cell in enumerate(row):
                cls = cell_class(r, c) if cell_class else None
                if c in rtl_cols:
                    cls = (cls + " heb") if cls else "heb"
                attr = f" class='{cls}'" if cls else ""
                out.append(f"<td{attr}>{esc(cell)}</td>")
            out.append("</tr>")
        out.append("</tbody></table></div>")
        self.parts.append("".join(out))

    def bars(self, title: str, items: list[tuple[str, float]], scale: float) -> None:
        """A word + weight list rendered as horizontal bars."""
        rows = [f"<div class='card'><div class='card-head heb'>{esc(title)}</div>"]
        if not items:
            rows.append("<div class='card-body empty'>not in vocabulary</div>")
        else:
            rows.append("<div class='card-body'>")
            for w, s in items:
                pct = max(2.0, min(100.0, s / scale * 100.0))
                rows.append(
                    f"<div class='bar-row'>"
                    f"<span class='bar-label heb'>{esc(w)}</span>"
                    f"<span class='bar-track'><span class='bar-fill' "
                    f"style='width:{pct:.1f}%'></span></span>"
                    f"<span class='bar-val'>{s:.2f}</span></div>")
            rows.append("</div>")
        rows.append("</div>")
        self.parts.append("".join(rows))

    def grid_open(self) -> None:
        self.parts.append("<div class='grid'>")

    def grid_close(self) -> None:
        self.parts.append("</div>")

    def render(self) -> str:
        stamp = _dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        return _PAGE.format(title=esc(self.title), body="\n".join(self.parts),
                            stamp=stamp)


_PAGE = """<!DOCTYPE html>
<html lang='he'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>{title}</title>
<style>
  *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    font-family: 'Segoe UI', Arial, sans-serif; font-size: 14px;
    background: #f4f6f9; color: #1a1a2e; padding: 24px; direction: ltr;
  }}
  h1 {{ font-size: 1.6rem; margin-bottom: 6px; color: #0d1b2a; }}
  h2 {{
    font-size: 1rem; margin: 22px 0 8px; padding: 5px 12px;
    background: #1a3a5c; color: #fff; border-radius: 4px;
  }}
  h3 {{ font-size: .95rem; margin: 16px 0 6px; color: #1a3a5c; }}
  .report-block {{
    background: #fff; border: 1px solid #dde3ec; border-radius: 8px;
    padding: 20px 24px; margin-bottom: 28px; box-shadow: 0 2px 8px rgba(0,0,0,.07);
  }}
  .banner {{
    background: #0d1b2a; color: #e0e7ff; font-size: 1.2rem; font-weight: 700;
    padding: 12px 18px; border-radius: 5px; margin-bottom: 14px; letter-spacing: .4px;
  }}
  .meta {{ display: flex; gap: 10px; padding: 3px 0; font-size: 13px; color: #444; }}
  .meta-label {{ font-weight: 600; min-width: 200px; color: #1a3a5c; }}
  .meta-value {{ color: #222; }}
  .table-wrap {{ overflow-x: auto; margin: 10px 0 16px; }}
  table {{
    border-collapse: collapse; width: 100%; background: #fff;
    border-radius: 5px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,.07);
  }}
  th {{
    background: #1a3a5c; color: #fff; padding: 7px 11px; text-align: left;
    font-size: 12px; text-transform: uppercase; letter-spacing: .3px;
    white-space: nowrap;
  }}
  td {{ padding: 6px 11px; border-bottom: 1px solid #eef0f4; font-size: 13px; }}
  tr:last-child td {{ border-bottom: none; }}
  tr:nth-child(even) td {{ background: #f8f9fc; }}
  .heb {{ direction: rtl; text-align: right; font-size: 15px; }}
  .ok     {{ color: #1a7a3c; font-weight: 600; }}
  .bogus  {{ color: #c0392b; font-weight: 700; }}
  .warn   {{ color: #b8860b; font-weight: 600; }}
  .empty  {{ color: #888; }}
  .num    {{ font-variant-numeric: tabular-nums; }}
  .best   {{ background: #e8f8ee !important; font-weight: 700; }}
  .alert {{ padding: 9px 14px; border-radius: 4px; margin: 8px 0; font-size: 13px; }}
  .alert-info  {{ background: #e8f4fd; border-left: 4px solid #2980b9; color: #1a4a6e; }}
  .alert-error {{ background: #fdecea; border-left: 4px solid #c0392b; color: #7b1a1a; }}
  .grid {{
    display: grid; gap: 12px; margin: 10px 0 16px;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  }}
  .card {{
    background: #fafbfd; border: 1px solid #dde3ec; border-radius: 5px; overflow: hidden;
  }}
  .card-head {{
    padding: 7px 12px; background: #eef2f8; border-bottom: 1px solid #dde3ec;
    font-weight: 700; color: #0d1b2a; font-size: 15px;
  }}
  .card-body {{ padding: 8px 12px; }}
  .bar-row {{ display: flex; align-items: center; gap: 8px; padding: 2px 0; }}
  .bar-label {{ min-width: 84px; font-size: 14px; }}
  .bar-track {{ flex: 1; height: 9px; background: #e6ebf3; border-radius: 5px; overflow: hidden; }}
  .bar-fill {{ display: block; height: 100%; background: linear-gradient(90deg,#1a3a5c,#4a7ba8); }}
  .bar-val {{ font-size: 11px; color: #666; min-width: 34px; text-align: right;
              font-variant-numeric: tabular-nums; }}
  .page-title {{
    font-size: 1.7rem; font-weight: 800; color: #0d1b2a; margin-bottom: 20px;
    padding-bottom: 10px; border-bottom: 3px solid #0d1b2a;
  }}
  .generated {{ margin-top: 28px; font-size: 11px; color: #aaa; }}
  code {{ background: #eef2f8; padding: 1px 5px; border-radius: 3px; font-size: 12px; }}
</style>
</head>
<body>
<p class='page-title'>{title}</p>
<div class='report-block'>
{body}
</div>
<p class='generated'>Generated {stamp}</p>
</body>
</html>"""


# ---------------------------------------------------------------------------


def build(index_dir: Path, compare: list[Path], k: int, limit: int,
          best: bool = False) -> Report:
    ix = open_index(index_dir)
    if best:
        # The measured-best composition (FINDINGS.md §15): overlap similarity
        # (2.2x cosine) + lexicon display filter (junk-free result lists).
        from lexicon import LexiconView
        from similarity import SimilarityView
        ix = LexiconView(SimilarityView(ix, "overlap", top_n=100), "filter")
    m = ix.meta
    corpus = m.get("corpus_desc") or m.get("corpus") or "unknown"
    rep = Report(f"Word-Association Index — {corpus}")

    rep.banner(f"{corpus}   ·   "
               f"{m['vocab_size']:,} words   ·   {m['edge_count']:,} associations")

    # ── Build parameters ────────────────────────────────────────────
    rep.section("Corpus and build")
    rep.meta("Corpus", f"{corpus}"
             + (" [base books only]" if m.get("base_only") else ""))
    rep.meta("Books", f"{m.get('books', 'n/a'):,}"
             if isinstance(m.get("books"), int) else m.get("books", "n/a"))
    rep.meta("Text units (lines)", f"{m['verses']:,}")
    rep.meta("Tokens", f"{m['tokens']:,}")
    rep.meta("Tokens per unit", f"{m['tokens'] / max(1, m['verses']):.1f}")
    rep.meta("Vocabulary (freq >= %s)" % m.get("min_count"), f"{m['vocab_size']:,}")
    rep.meta("Associations kept", f"{m['edge_count']:,}")
    rep.meta("Window", m.get("window"))
    rep.meta("Length norm (BM25 b)", m.get("length_norm_b"))
    rep.meta("Prefix folding", f"{m.get('strip_prefixes')} "
             f"(min stem freq {m.get('min_stem_freq')}, "
             f"{m.get('folded_forms', 0):,} forms folded)")
    rep.meta("Builder", m.get("builder", "build_index.py"))
    rep.meta("Build time", f"{m.get('build_seconds')} s")
    idx_bytes = (m.get("offsets_bytes", 0) + m.get("edges_bytes", 0))
    rep.meta("Index size (mapped)", f"{idx_bytes / 1e6:.2f} MB")

    # ── Lookup cost ─────────────────────────────────────────────────
    rep.section("Lookup cost — the core claim")
    import random
    rng = random.Random(7)
    sample = [ix.words[rng.randrange(len(ix.words))] for _ in range(20000)]
    t = time.perf_counter()
    for w in sample:
        ix.neighbors(w, 20)
    per_nb = (time.perf_counter() - t) / len(sample) * 1e6
    t = time.perf_counter()
    for w in sample:
        ix._slice(ix.word_id[w])
    per_off = (time.perf_counter() - t) / len(sample) * 1e6
    rep.table(["operation", "per call", "note"],
              [["offsets[i] lookup", f"{per_off:.2f} µs", "two array reads"],
               ["neighbors(top-20)", f"{per_nb:.2f} µs",
                "includes per-edge struct.unpack in Python"]])
    rep.note("Measured in <b>Python</b>. A native reader over an mmap'd span would be "
             "a fraction of this. The point is unchanged: association lookup is not "
             "where query latency goes — <b>expansion breadth</b> is, because each "
             "expanded term costs a posting-list read in the FTS index.")

    # ── Index shape ─────────────────────────────────────────────────
    rep.section("Index shape")
    deg = [ix._slice(i)[1] - ix._slice(i)[0] for i in range(len(ix.words))]
    deg_s = sorted(deg)
    cnt = ix.counts
    rep.table(
        ["metric", "value"],
        [["words with >=1 association", f"{sum(1 for d in deg if d):,}"],
         ["associations per word — median", f"{statistics.median(deg_s):.0f}"],
         ["associations per word — mean", f"{statistics.mean(deg_s):.1f}"],
         ["associations per word — p90", f"{deg_s[int(.9 * len(deg_s))]:,}"],
         ["associations per word — max", f"{max(deg_s):,}"],
         ["vocabulary with freq < 5", f"{sum(1 for c in cnt if c < 5) / len(cnt) * 100:.0f}%"],
         ["vocabulary with freq >= 100", f"{sum(1 for c in cnt if c >= 100) / len(cnt) * 100:.1f}%"]])

    # ── Scored quality ──────────────────────────────────────────────
    rep.section("Measured quality vs an independent gold set")
    rep.note("Gold pairs come from the project dictionary's <code>link</code> table — "
             "hand-built from lexicographic sources, so it knows nothing about "
             "co-occurrence and a good score cannot be an artifact of the method. "
             "<code>כתיב</code> (spelling) and <code>נגזרת</code> (derivation) links are "
             "excluded on purpose.")
    vocab = set(ix.words)
    counts = dict(zip(ix.words, ix.counts))
    qrows = []
    for rel in ("synonym", "antonym"):
        gold = load_gold(rel, vocab, 10, counts)
        if len(gold) < 20:
            continue
        base = random_baseline(ix, gold, k)
        for mode in ("assoc", "similar"):
            r = evaluate(ix, gold, k, mode, limit)
            ratio = (r[f"P@{k}"] / base[f"P@{k}"]) if base[f"P@{k}"] > 0 else float("inf")
            qrows.append([rel, mode, f"{len(gold):,}", f"{r['evaluated']:,}",
                          f"{r[f'P@{k}']:.4f}", f"{r['MRR']:.4f}",
                          f"{r[f'recall@{k}']:.4f}",
                          "n/a" if ratio == float("inf") else f"{ratio:.0f}x"])
        qrows.append([rel, "random baseline", f"{len(gold):,}", f"{len(gold):,}",
                      f"{base[f'P@{k}']:.4f}", f"{base['MRR']:.4f}",
                      f"{base[f'recall@{k}']:.4f}", "1x"])
    rep.table(["relation", "mode", "gold words", "evaluated",
               f"P@{k}", "MRR", f"recall@{k}", "vs chance"], qrows,
              cell_class=lambda r, c: "num" if c >= 2 else None)

    # ── Contrast check ──────────────────────────────────────────────
    rep.section("Contrast pairs — does the index rank opposites close?")
    rep.note("The distributional hypothesis predicts that <b>opposites share "
             "contexts</b> (both sides of a contrast occur in the same frames), so a "
             "distributional index should rank them near each other even though they "
             "mean opposite things. This is the strongest measured signal in this "
             "index — see FINDINGS.md §10.")
    crows = []
    for a, b in CONTRAST_PAIRS:
        if a not in ix.word_id or b not in ix.word_id:
            crows.append([a, b, "—", "—", "not in vocabulary"])
            continue
        nb = [w for w, _ in ix.neighbors(a, 50)]
        sm = [w for w, _ in ix.similar(a, 50)]
        rn = str(nb.index(b) + 1) if b in nb else ">50"
        rs = str(sm.index(b) + 1) if b in sm else ">50"
        hit = "found" if (b in nb or b in sm) else "missed"
        crows.append([a, b, rn, rs, hit])
    rep.table(["term", "expected opposite", "rank (assoc)", "rank (similar)", ""],
              crows,
              cell_class=lambda r, c: ("ok" if crows[r][4] == "found" else "bogus")
              if c == 4 else None,
              rtl_cols=(0, 1))

    # ── Qualitative panels ─────────────────────────────────────────
    rep.section("First-order associations — words the corpus places nearby")
    rep.grid_open()
    for w in SHOWCASE:
        res = ix.neighbors(w, 8)
        top = max((s for _, s in res), default=1.0)
        freq = counts.get(w)
        label = f"{w}" + (f"   ({freq:,}x)" if freq else "")
        rep.bars(label, res, top or 1.0)
    rep.grid_close()

    rep.section("Second-order similarity — words used in similar contexts")
    rep.note("Cosine over the sparse association profiles. No embeddings and no "
             "training: two words score high when their <i>profiles</i> overlap.")
    rep.grid_open()
    for w in SHOWCASE[:8]:
        res = ix.similar(w, 8)
        top = max((s for _, s in res), default=1.0)
        rep.bars(w, res, top or 1.0)
    rep.grid_close()

    # ── Expansion ───────────────────────────────────────────────────
    rep.section("Query expansion")
    for q in EXPAND_QUERIES:
        terms, exp = ix.expand(q, per_term=5, mode="similar")
        rows = [[w, f"{s:.2f}", tag] for w, s, tag in exp[:10]]
        rep.raw(f"<h3 class='heb'>{esc(q)}</h3>")
        if rows:
            rep.table(["expanded term", "score", "source"], rows,
                      cell_class=lambda r, c: "ok" if (c == 2 and "agreed" in rows[r][2]) else None,
                      rtl_cols=(0,))
        else:
            rep.note("No expansion — none of the terms are in the vocabulary.", True)

    # ── Cross-index comparison ─────────────────────────────────────
    if compare:
        rep.section("Comparison across corpora")
        rows = []
        for p in [index_dir] + compare:
            try:
                o = open_index(p)
            except Exception:
                continue
            om = o.meta
            ov, oc = set(o.words), dict(zip(o.words, o.counts))
            g = load_gold("synonym", ov, 10, oc)
            r = evaluate(o, g, k, "assoc", limit) if len(g) >= 20 else None
            b = random_baseline(o, g, k) if len(g) >= 20 else None
            ratio = (r[f"P@{k}"] / b[f"P@{k}"]) if (r and b and b[f"P@{k}"] > 0) else None
            rows.append([
                om.get("corpus", p.name), p.name,
                f"{om['tokens']:,}", f"{om['vocab_size']:,}",
                f"{om['tokens'] / max(1, om['verses']):.1f}",
                str(om.get("window")),
                f"{r[f'P@{k}']:.4f}" if r else "—",
                f"{r['MRR']:.4f}" if r else "—",
                f"{ratio:.0f}x" if ratio else "—"])
            o.close()
        rep.table(["corpus", "index dir", "tokens", "vocab", "tok/unit",
                   "window", f"P@{k}", "MRR", "vs chance"], rows,
                  cell_class=lambda r, c: "num" if c >= 2 else None)
        rep.note("Read this table with care. The gold set is a <b>Hebrew</b> lexicon, "
                 "so corpora in Mishnaic Hebrew or Talmudic Aramaic are scored against "
                 "targets partly outside their own register. The numbers show that "
                 "pooling more text does not improve Biblical-Hebrew association "
                 "quality; they do <b>not</b> show that those corpora are worse on "
                 "their own terms.")

    ix.close()
    return rep


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--index", default="index")
    ap.add_argument("--compare", nargs="*", default=[])
    ap.add_argument("-k", type=int, default=20)
    ap.add_argument("--limit", type=int, default=300,
                    help="cap evaluated gold words (similar() is the slow path)")
    ap.add_argument("--out", default=None)
    ap.add_argument("--open", action="store_true", help="open in the default browser")
    ap.add_argument("--best", action="store_true",
                    help="apply the measured-best stack: overlap similarity + "
                         "lexicon filter (FINDINGS.md §15)")
    a = ap.parse_args()

    base = Path(__file__).parent
    idx = base / a.index
    out = Path(a.out) if a.out else base / f"report-{idx.name}.html"

    rep = build(idx, [base / c for c in a.compare], a.k, a.limit, a.best)
    out.write_text(rep.render(), encoding="utf-8")
    print(f"Report -> {out}")
    if a.open:
        os.startfile(str(out))  # noqa: S606 - Windows-only, intentional


if __name__ == "__main__":
    main()
