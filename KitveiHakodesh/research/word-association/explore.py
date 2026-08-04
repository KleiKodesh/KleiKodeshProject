"""Scratch: what link kinds exist, and how many survive into the Tanach vocab?"""
import sqlite3, sys, io, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.path.insert(0, str(__import__("pathlib").Path(__file__).parent))

DB = os.path.expandvars(
    r"%LOCALAPPDATA%\KleiKodesh\KitveiHakodesh\dictionary\KitveiHakodesh_dictionary.db")
c = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)

print("=== link kinds ===")
for r in c.execute("""
    select k.id, k.name, k.explanation, count(l.word_id)
    from link_kind k left join link l on l.kind_id = k.id
    group by k.id order by count(l.word_id) desc"""):
    print(f"  {r[0]}  {r[1]:<16s} n={r[3]:>7,d}   {(r[2] or '')[:60]}")

print("\n=== source kinds ===")
for r in c.execute("select id, name from source_kind order by id"):
    print(" ", r)

print("\n=== sample links per kind ===")
for kid, kname in c.execute("select id, name from link_kind"):
    rows = c.execute("""
        select w.headword, t.headword from link l
        join word w on w.id=l.word_id join word t on t.id=l.target_id
        where l.kind_id=? limit 6""", (kid,)).fetchall()
    print(f"\n  {kname}:")
    for a, b in rows:
        print(f"     {a}  ->  {b}")

# Overlap with the Tanach index vocabulary
from query import AssocIndex
ix = AssocIndex()
V = set(ix.words)
FINALS = str.maketrans("ךםןףץ", "כמנפצ")
print("\n\n=== how many links land inside the Tanach vocabulary? ===")
for kid, kname in c.execute("select id, name from link_kind"):
    pairs = c.execute("""
        select w.headword, t.headword from link l
        join word w on w.id=l.word_id join word t on t.id=l.target_id
        where l.kind_id=?""", (kid,)).fetchall()
    usable = [(a, b) for a, b in pairs
              if a.translate(FINALS) in V and b.translate(FINALS) in V
              and a.translate(FINALS) != b.translate(FINALS)]
    print(f"  {kname:<16s} {len(pairs):>7,d} total -> {len(usable):>5,d} usable")
ix.close()
