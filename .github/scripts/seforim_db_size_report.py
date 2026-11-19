"""Per-table / per-index byte report for a seforim.db using dbstat. Read-only."""
import sqlite3, sys, json
path, label, tag = sys.argv[1], sys.argv[2], sys.argv[3]
c = sqlite3.connect(f"file:{path}?mode=ro&immutable=1", uri=True)
page = c.execute("pragma page_size").fetchone()[0]
pages = c.execute("pragma page_count").fetchone()[0]
total = page * pages
kinds = {n: (t, tb) for n, t, tb in c.execute("select name,type,tbl_name from sqlite_master")}
rows = c.execute("select name, sum(pgsize), sum(unused), count(*) from dbstat group by name").fetchall()
print(f"== {label} {tag}: page_size={page} pages={pages} total={total/1e9:.3f} GB")
out = []
per_table = {}
for n, b, unused, pg in rows:
    t, tb = kinds.get(n, ("autoindex/other", n))
    if n.startswith("sqlite_autoindex_"):
        t = "autoindex"
        # name is sqlite_autoindex_<table>_<n>
        tb = n[len("sqlite_autoindex_"):].rsplit("_", 1)[0]
    out.append(dict(name=n, kind=t, table=tb, bytes=b, unused=unused, pages=pg))
    d = per_table.setdefault(tb, dict(table=0, index=0))
    d["table" if t == "table" else "index"] += b
out.sort(key=lambda r: -r["bytes"])
print("\n-- top 40 objects by bytes")
print(f"{'MB':>9} {'%':>5} {'unused%':>7} kind      name (table)")
for r in out[:40]:
    print(f"{r['bytes']/1e6:9.1f} {100*r['bytes']/total:5.1f} {100*r['unused']/max(r['bytes'],1):7.1f} {r['kind']:9} {r['name']} ({r['table']})")
print("\n-- per table: data MB / index MB")
for tb, d in sorted(per_table.items(), key=lambda kv: -(kv[1]['table'] + kv[1]['index']))[:30]:
    print(f"{tb:28} data {d['table']/1e6:9.1f}  index {d['index']/1e6:9.1f}")
print("\n-- all indexes (MB)")
for r in out:
    if r["kind"] in ("index", "autoindex"):
        print(f"{r['bytes']/1e6:9.1f}  {r['name']} ({r['table']})")
print("\n-- row counts (exact) of main tables")
counts = {}
for t in ("book","line","line_content","link","link_range","link_anchor","link_coverage","link_suppressed_side","tocEntry","tocText","line_toc","line_ref","line_note","version_line","line_alt_toc","alt_toc_entry"):
    try:
        counts[t] = c.execute(f"select count(*) from {t}").fetchone()[0]
        print(f"{t:22} {counts[t]:>12,}")
    except sqlite3.Error:
        pass
for q, name in (("select avg(length(content)) from (select content from line where rowid%97=0)", "line.content avg chars (sample 1/97)"),
                ("select avg(length(content)) from (select content from line_content where rowid%97=0)", "line_content avg bytes (sample 1/97)"),
                ("select avg(length(heRef)) from (select heRef from line where rowid%97=0)", "line.heRef avg chars (sample 1/97)")):
    try:
        print(f"{name}: {c.execute(q).fetchone()[0]}")
    except sqlite3.Error:
        pass
json.dump(dict(label=label, tag=tag, page_size=page, total=total, objects=out, counts=counts), open(f"{label}-size.json", "w"))
