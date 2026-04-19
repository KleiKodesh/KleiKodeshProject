"""
add_aramaic_hamichlol.py
────────────────────────
Scans lines from known Aramaic source categories, tokenizes the text,
and adds Hamichlol definitions for any new tokens not already in the dictionary.

Sources scanned (base books only where applicable, all books for Targum):
  - תלמוד בבלי    (catId=13,  isBaseBook=1)
  - תלמוד ירושלמי (catId=20,  isBaseBook=1)
  - תרגום יונתן   (catId=1086, all books)
  - תרגום אונקלוס (catId=1092, all books)
  - תרגום ירושלמי (catId=1094, all books)

Usage:
    python Misc/scripts/dictionary/add_aramaic_hamichlol.py
"""

import sqlite3
import unicodedata
import re
import time
import sys

DICT_DB    = "vue-frontend/public/dictionary/kezayit_dictionary.db"
HAMICHLOL  = "Misc/hamichlol-dictionary/hamichlol_disambig.db"
SEFORIM_DB = r"C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db"
CHUNK_SIZE = 5_000

# Definitions containing these strings are modern culture/geography noise — skip them
NOISE_MARKERS = [
    "סרט", "שחקן", "זמר", "להקה", "אלבום", "קומיקס", "טלוויזיה",
    "כדורגל", "ספורט", "פוליטיקאי", "עיר ב", "מחוז ב", "מדינת",
    "רכב", "חברה ישראלית", "תוכנה", "אפליקציה", "דמות בדיונית",
    "כפר ב", "ישוב ב", "יישוב ב", "עיירה ב", "נהר ב", "הר ב", "אי ב",
    "יסוד כימי", "מותג", "רובוט", "עיתון", "ירחון", "שבועון",
    "מסך מחשב", "תוצר של פעולת מחשב", "המידע המוזן במחשב",
]

def is_noise(defn):
    return any(p in defn for p in NOISE_MARKERS)

# (category_id, label, base_books_only)
SOURCES = [
    (13,   'תלמוד בבלי',    True),
    (20,   'תלמוד ירושלמי', True),
    (1086, 'תרגום יונתן',   False),
    (1092, 'תרגום אונקלוס', False),
    (1094, 'תרגום ירושלמי', False),
]

TAG_RE   = re.compile(r'<[^>]+>')
TOKEN_RE = re.compile(r'[\u05D0-\u05EA\u05F3\u05F4]+(?:["\'״׳][\u05D0-\u05EA\u05F3\u05F4]+)*')

def strip_nikud(s):
    if not s: return s
    return ''.join(
        c for c in unicodedata.normalize('NFD', s)
        if unicodedata.category(c) != 'Mn'
    ).strip()

def tokenize(html):
    return TOKEN_RE.findall(TAG_RE.sub(' ', html))

# ── Load Hamichlol ────────────────────────────────────────────────────────────
print("Loading Hamichlol...")
hm = sqlite3.connect(HAMICHLOL)
hamichlol = {}
for hw, defn in hm.execute("SELECT headword, definition FROM entry").fetchall():
    plain = strip_nikud(hw)
    if plain:
        hamichlol.setdefault(plain, []).append(defn)
hm.close()
print(f"  {len(hamichlol):,} unique Hamichlol headwords")

# ── Get or create המכלול source ───────────────────────────────────────────────
dict_db = sqlite3.connect(DICT_DB)
dict_db.execute("PRAGMA journal_mode=WAL")
src = dict_db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()
if src:
    source_id = src[0]
else:
    dict_db.execute("INSERT INTO source (name) VALUES ('המכלול')")
    dict_db.commit()
    source_id = dict_db.execute("SELECT id FROM source WHERE name = 'המכלול'").fetchone()[0]

# Remove existing המכלול rows so we rebuild cleanly from all sources
dict_db.execute("DELETE FROM entry WHERE source_id = ?", (source_id,))
dict_db.commit()
print(f"Cleared existing המכלול rows. source_id={source_id}")

# Load all existing headwords from other sources
existing = set(
    r[0] for r in dict_db.execute(
        "SELECT DISTINCT headword FROM entry WHERE source_id != ?", (source_id,)
    ).fetchall()
)
print(f"Existing dictionary headwords: {len(existing):,}")

# ── Scan each source ──────────────────────────────────────────────────────────
seforim = sqlite3.connect(SEFORIM_DB)
seforim.execute("PRAGMA query_only = ON")
seforim.execute("PRAGMA cache_size = -64000")

seen = set()       # tokens seen across all sources
total_new = 0

for (cat_id, label, base_only) in SOURCES:
    base_filter = "AND b.isBaseBook = 1" if base_only else ""
    total_lines = seforim.execute(f"""
        SELECT COUNT(*) FROM line l
        JOIN book b ON b.id = l.bookId
        JOIN category_closure cc ON cc.descendantId = b.categoryId
        WHERE cc.ancestorId = ? {base_filter}
    """, (cat_id,)).fetchone()[0]

    print(f"\n{label}: {total_lines:,} lines")
    new_rows = []
    offset = 0
    t0 = time.time()

    while True:
        rows = seforim.execute(f"""
            SELECT l.content FROM line l
            JOIN book b ON b.id = l.bookId
            JOIN category_closure cc ON cc.descendantId = b.categoryId
            WHERE cc.ancestorId = ? {base_filter}
            LIMIT ? OFFSET ?
        """, (cat_id, CHUNK_SIZE, offset)).fetchall()

        if not rows: break

        for (content,) in rows:
            if not content: continue
            for token in tokenize(content):
                plain = strip_nikud(token)
                if not plain or plain in seen or plain in existing:
                    continue
                seen.add(plain)
                defs = hamichlol.get(plain)
                if defs:
                    clean = [d for d in defs if not is_noise(d)]
                    if clean:
                        for defn in clean:
                            new_rows.append((plain, None, source_id, defn))
                        existing.add(plain)

        offset += CHUNK_SIZE
        elapsed = time.time() - t0
        pct = min(offset / total_lines * 100, 100)
        print(f"  {offset:,}/{total_lines:,} ({pct:.0f}%)  "
              f"new_rows={len(new_rows):,}  elapsed={elapsed:.0f}s", end='\r')
        sys.stdout.flush()

    print()

    if new_rows:
        dict_db.executemany(
            "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
            new_rows
        )
        dict_db.commit()
        total_new += len(new_rows)
        print(f"  Inserted {len(new_rows):,} new rows from {label}")

seforim.close()

# Also re-add the original valid המכלול rows (existing dict headwords)
print("\nRe-adding Hamichlol definitions for existing dictionary headwords...")
orig_headwords = set(
    r[0] for r in dict_db.execute(
        "SELECT DISTINCT headword FROM entry WHERE source_id != ?", (source_id,)
    ).fetchall()
)
hm = sqlite3.connect(HAMICHLOL)
all_hm = hm.execute("SELECT headword, definition FROM entry").fetchall()
hm.close()

orig_rows = []
added_orig = set()
for hw, defn in all_hm:
    plain = strip_nikud(hw)
    if plain and plain in orig_headwords and plain not in added_orig and plain not in seen:
        orig_rows.append((plain, None, source_id, defn))
        added_orig.add(plain)

dict_db.executemany(
    "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
    orig_rows
)
dict_db.commit()
total_new += len(orig_rows)
print(f"  Inserted {len(orig_rows):,} rows for existing headwords")

# Add all Hamichlol entries with explicit Talmudic/halachic labels
print("\nAdding labeled Hamichlol entries (הלכה, אמורא, תנא, etc.)...")
TALMUDIC_LABELS = [
    'הלכה', 'תלמוד', 'מצווה', 'חז"ל', 'אמורא', 'תנא',
    'כהונה', 'מקרא', 'יהדות', 'קבלה', 'מדרש', 'משנה',
    'בית המקדש', 'מקדש', 'נביא', 'תנ"ך',
    'אישיות מהתנ"ך', 'עיר מקראית', 'אתר מקראי',
    'יישוב מקראי', 'יישוב תלמודי', 'אבן חן מקראית',
    'עוף מקראי', 'בגד כהונה', 'כהונה גדולה',
    'מתנות כהונה', 'פירוש למשנה',
    # Additional labels
    'עבודה זרה', 'איסור ריבית', 'בין אדם לחברו',
    'חומרא וקולא', 'צדיק', 'ברכת המזון',
    'יום הכיפורים', 'בית שני', 'ניקוד',
    'צמח מקראי', 'ארץ המוזכרת במקרא', 'עיר פיניקית',
    'מגילת אסתר', 'זמן המשיח', 'כישוף', 'מעשה עבירה',
    'אמורא בדור', 'סורא', 'פומבדיתא',
    'בהלכה', 'בלימוד הגמרא',
]
import re as _re
def _is_safe_def(d):
    import re as _re2
    _SEDER_RE = _re2.compile(r'(מסדר|בסדר) (קדשים|טהרות|נשים|נזיקין|מועד|זרעים)')
    if d.startswith('מסכת'): return True
    if d.startswith('בהלכה'): return True
    if d.startswith('מצווה'): return True
    if d.startswith('דין '): return True
    if d.startswith('איסור') and 'נשק' not in d and 'מכירת' not in d: return True
    if d.startswith('תנא '): return True
    if d.startswith('אמורא '): return True
    if d.startswith('נביא ') and 'אסלאם' not in d and 'מוסלמ' not in d: return True
    if d.startswith('כהן גדול'): return True
    if d.startswith('הראשון לציון'): return True
    if d.startswith('מדרש '): return True
    if d.startswith('ספר הלכה'): return True
    if d.startswith('ספר קבלה'): return True
    if d.startswith('אחד מ') and any(p in d for p in ['מלאכות', 'מצוות', 'אבות', 'מידות']): return True
    if d.startswith('אחת מ') and any(p in d for p in ['מלאכות', 'מצוות', 'מידות']): return True
    if 'דאורייתא' in d: return True
    if 'דרבנן' in d: return True
    if 'ל"ט מלאכות' in d: return True
    if 'ארבעה אבות' in d: return True
    if 'סנהדרין' in d: return True
    if 'תנא בדור' in d: return True
    if 'אמורא בדור' in d: return True
    if _SEDER_RE.search(d): return True
    if 'מצוות לא תעשה' in d: return True
    if 'הלכות שבת' in d: return True
    if 'הלכות כשרות' in d: return True
    if 'מצוות התורה' in d: return True
    if 'תורה שבעל פה' in d: return True
    if 'מנביאי' in d and 'אסלאם' not in d and 'מוסלמ' not in d: return True
    if d.startswith('ראש ישיבה'): return True
    if d.startswith('גאון ') and 'ישיבת' in d: return True
    if d.startswith('פוסק '): return True
    if d.startswith('מקובל '): return True
    if d.startswith('אדמו"ר'): return True
    if d.startswith("אדמו''ר"): return True
    if d.startswith('רב ופוסק'): return True
    if d.startswith('חסיד '): return True
    if 'פוסק הלכה' in d: return True
    if 'שאלות ותשובות' in d and 'תוכנה' not in d and 'מחשב' not in d: return True
    if 'הלכות עירובין' in d: return True
    if 'מצוות עשה' in d: return True
    if 'מתרי"ג' in d: return True
    # Rabbinical figures and books
    _TB = ['הלכה','תלמוד','שו"ת','קבלה','מדרש','פוסק','ראשון','אחרון','משנה','תורה']
    _TC = ['הלכה','תלמוד','ראשון','אחרון','ספרד','אשכנז','תימן','מרוקו','פוסק','גאון']
    if d.startswith('דיין '): return True
    if d.startswith('רב ו') and any(p in d for p in ['הלכה','תלמוד','פוסק','ישיבה','קבלה','מדרש','שו"ת']): return True
    if d.startswith('מחבר ') and any(p in d for p in _TB): return True
    if d.startswith('חכם ') and any(p in d for p in _TC): return True
    if d.startswith('ספר ') and any(p in d for p in ['תלמוד','הלכה','קבלה','מדרש','פירוש','חז"ל','ראשונים','שו"ת']): return True
    if d.startswith('פירוש ') and any(p in d for p in ['תלמוד','משנה','תורה','מקרא','רמב"ם','רש"י']): return True
    if 'על התלמוד' in d: return True
    if 'על המשנה' in d: return True
    if 'על התורה' in d and any(p in d for p in ['פירוש','ספר','חיבר','כתב','מחבר']): return True
    if 'מחבר ספר' in d and any(p in d for p in _TB): return True
    if 'מגדולי' in d and any(p in d for p in ['תנאים','אמוראים','ראשונים','אחרונים','פוסקים','רבני','חכמי']): return True
    if 'בעלי התוספות' in d: return True
    if 'תלמידו של רש"י' in d or 'תלמיד רש"י' in d: return True
    # Rabbinical titles and institutions
    _YSH = ['וולוז"ין',"וולוז'ין",'ישיבת מיר','סלובודקה','פוניבז','ישיבת חברון','הר עציון']
    if d.startswith('רבה של') or d.startswith('רבה ה'): return True
    if d.startswith('אב"ד') or d.startswith('אב בית דין'): return True
    if 'חכם באשי' in d: return True
    if d.startswith('נשיא ') and any(p in d for p in ['ישיבה','סנהדרין','יהודים','קהילה']): return True
    if 'מנהיג' in d and 'יהודי' in d and 'קהילה' in d: return True
    if d.startswith('ספר מוסר'): return True
    if d.startswith('ספר דרוש') or d.startswith('ספר דרשות'): return True
    if d.startswith('ספר') and 'חסידות' in d: return True
    if any(y in d for y in _YSH): return True
    if 'חתן פרס ישראל' in d and any(p in d for p in ['הלכה','תלמוד','תורה','רב','ישיבה','תורנית']): return True
    if 'גירוש ספרד' in d: return True
    if 'בית יוסף' in d and 'ספר' in d: return True
    if 'משנה תורה' in d and any(p in d for p in ['ספר','פירוש','חיבר','כתב']): return True
    if 'שולחן ערוך' in d and any(p in d for p in ['ספר','פירוש','חיבר','כתב','מחבר']): return True
    if 'ספר החינוך' in d: return True
    if 'ספר הזוהר' in d: return True
    if any(p in d for p in ['מחכמי ספרד','מחכמי אשכנז','מחכמי תימן','מחכמי מרוקו']): return True
    if 'רבי עקיבא' in d and any(p in d for p in ['תנא','תלמיד','דור']): return True
    if 'רבי יוחנן' in d and 'אמורא' in d: return True
    # Historical periods and classic sefarim
    _PER = ['תקופת הראשונים','תקופת האמוראים','תקופת התנאים','תקופת הגאונים',
            'תקופת האחרונים','תקופת הזוגות','בתקופת הראשונים','בתקופת האמוראים',
            'בתקופת התנאים','בתקופת הגאונים']
    _SFR = ['ספר הטור','ארבעה טורים','ספר הרמב"ם','ספרו של הרמב"ם',
            'ספר הרמב"ן','ספר הרשב"א','ספר הרא"ש','ספר הרי"ף',
            'ספר הרמ"א','ספר הגר"א','ספר הש"ך','ספר הט"ז','ספר הב"ח']
    if any(p in d for p in _PER): return True
    if any(p in d for p in _SFR): return True
    if any(p in d for p in ['מגדולי הראשונים','מגדולי האחרונים','מגדולי הפוסקים',
                             'מגדולי רבני','מגדולי חכמי']): return True
    if 'ספר תשובות' in d: return True
    if 'ספר חידושים' in d and any(p in d for p in ['תלמוד','הלכה','שו"ת']): return True
    if 'ספר פסקים' in d: return True
    if 'ספר תורני' in d: return True
    if 'ספר חסידי' in d: return True
    if 'ספר קבלי' in d: return True
    if 'ספר מוסרי' in d: return True
    return False
hm = sqlite3.connect(HAMICHLOL)
existing_pairs = set(
    (r[0], r[1]) for r in dict_db.execute(
        "SELECT headword, definition FROM entry WHERE source_id = ?", (source_id,)
    ).fetchall()
)
labeled_rows = []
for hw, defn in hm.execute("SELECT headword, definition FROM entry").fetchall():
    m = _re.search(r'\(([^)]+)\)', hw)
    if m and any(l in m.group(1) for l in TALMUDIC_LABELS):
        plain = strip_nikud(_re.split(r'\s*\(', hw)[0].strip())
        if plain and (plain, defn) not in existing_pairs:
            labeled_rows.append((plain, None, source_id, defn))
            existing_pairs.add((plain, defn))
    # Also add entries with 100% safe definition patterns
    elif _is_safe_def(defn):
        plain = strip_nikud(_re.split(r'\s*\(', hw)[0].strip())
        if plain and (plain, defn) not in existing_pairs:
            labeled_rows.append((plain, None, source_id, defn))
            existing_pairs.add((plain, defn))
hm.close()
dict_db.executemany(
    "INSERT INTO entry (headword, nikud, source_id, definition) VALUES (?, ?, ?, ?)",
    labeled_rows
)
dict_db.commit()
total_new += len(labeled_rows)
print(f"  Inserted {len(labeled_rows):,} labeled entries")

total_hm = dict_db.execute(
    "SELECT COUNT(*) FROM entry WHERE source_id = ?", (source_id,)
).fetchone()[0]
dict_db.close()

print(f"\nTotal new rows inserted: {total_new:,}")
print(f"Total המכלול rows: {total_hm:,}")
