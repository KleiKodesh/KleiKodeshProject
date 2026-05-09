"""
import_radak_roots.py
─────────────────────
Extracts the root table from ספר השרשים לרד"ק.
Adds a `root_form` table to KitveiHakodesh_dictionary.db with all 2,048 roots
the Radak documents. Useful for validating whether a word's root is
a real biblical Hebrew root.

Schema added:
    root_form(id INTEGER PK, root TEXT UNIQUE NOT NULL)
    index_root_form_root ON root_form(root)

Usage:
    python Misc/scripts/dictionary/import_radak_roots.py
"""
import sqlite3, sys, io, re, unicodedata
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

SEFORIM_DB = r'C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db'
DICT_DB    = "vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db"
BOOK_ID    = 6105

TAG_RE = re.compile(r'<[^>]+>')
def plain(s): return TAG_RE.sub('', s).strip()
def strip_nikud(s):
    if not s: return s
    return ''.join(c for c in unicodedata.normalize('NFD', s)
                   if unicodedata.category(c) != 'Mn').strip()

# ── Load h3 headings from ספר השרשים ─────────────────────────────────────────
print("Loading seforim lines...")
seforim = sqlite3.connect(SEFORIM_DB)
seforim.execute('PRAGMA query_only = ON')
all_lines = seforim.execute(
    "SELECT content FROM line WHERE bookId=? ORDER BY lineIndex", (BOOK_ID,)
).fetchall()
seforim.close()

SKIP = {'הקדמה לספר המכלול', 'הקדמה לספר השרשים',
        'בספר בראשית', 'בספר דניאל', 'בספר עזרא', 'בספר ירמיהו'}

roots = []
for (content,) in all_lines:
    if not content: continue
    if '<h3>' in content or '<h3 ' in content:
        root = strip_nikud(plain(content)).strip()
        if root and root not in SKIP:
            roots.append(root)

print(f"  {len(roots)} roots extracted")

# ── Write to dict DB ──────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
db.execute("PRAGMA journal_mode=WAL")

db.execute("DROP TABLE IF EXISTS root_form")
db.execute("""
    CREATE TABLE root_form (
        id   INTEGER PRIMARY KEY AUTOINCREMENT,
        root TEXT    NOT NULL UNIQUE
    )
""")
db.execute("CREATE INDEX index_root_form_root ON root_form(root)")

db.executemany("INSERT OR IGNORE INTO root_form (root) VALUES (?)", [(r,) for r in roots])
db.commit()
db.execute("VACUUM")
db.close()

# ── Verify ────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
count = db.execute("SELECT COUNT(*) FROM root_form").fetchone()[0]
print(f"  {count} roots in root_form table")

print("\nSample roots:")
for (r,) in db.execute("SELECT root FROM root_form ORDER BY root LIMIT 20"):
    print(f"  {r}")

print("\nSpot-check known roots:")
for r in ['אדם', 'ידע', 'שמר', 'כתב', 'ברך', 'נשא', 'עלה', 'ראה', 'הלך', 'בוא']:
    exists = db.execute("SELECT 1 FROM root_form WHERE root=?", (r,)).fetchone()
    print(f"  {r}: {'✓' if exists else '✗'}")

db.close()
print("\nDone.")
