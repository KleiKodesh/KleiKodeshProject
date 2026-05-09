"""
import_radak_definitions.py
────────────────────────────
Extracts clean short definitions from ספר השרשים לרד"ק and inserts
them into KitveiHakodesh_dictionary.db as a new source.

Only entries where the Radak explicitly writes ענינו/פירושו/ענין/פירוש
followed by a short clean noun phrase are included. Verse fragments,
grammatical notes, and mid-sentence explanations are rejected.

Idempotent — safe to re-run.

Usage:
    python Misc/scripts/dictionary/import_radak_definitions.py
"""
import sqlite3, sys, io, re, unicodedata
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

SEFORIM_DB  = r'C:\Users\Admin\AppData\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db'
DICT_DB     = "vue-frontend/public/dictionary/KitveiHakodesh_dictionary.db"
BOOK_ID     = 6105
SOURCE_NAME = 'ספר השרשים לרד"ק'

TAG_RE = re.compile(r'<[^>]+>')
def plain(s): return TAG_RE.sub('', s).strip()
def strip_nikud(s):
    if not s: return s
    return ''.join(c for c in unicodedata.normalize('NFD', s)
                   if unicodedata.category(c) != 'Mn').strip()

# ── Load entries ──────────────────────────────────────────────────────────────
print("Loading entries...")
seforim = sqlite3.connect(SEFORIM_DB)
seforim.execute('PRAGMA query_only = ON')
all_lines = seforim.execute(
    "SELECT content FROM line WHERE bookId=? ORDER BY lineIndex", (BOOK_ID,)
).fetchall()
seforim.close()

SKIP = {'הקדמה לספר המכלול', 'הקדמה לספר השרשים',
        'בספר בראשית', 'בספר דניאל', 'בספר עזרא', 'בספר ירמיהו'}

entries = []
current_shoresh = None
current_lines   = []
for (content,) in all_lines:
    if not content: continue
    if '<h2>' in content:
        if current_shoresh: entries.append((current_shoresh, current_lines[:]))
        current_shoresh = None; current_lines = []
    elif '<h3>' in content:
        if current_shoresh: entries.append((current_shoresh, current_lines[:]))
        current_shoresh = plain(content); current_lines = []
    elif current_shoresh is not None:
        current_lines.append(content)
if current_shoresh:
    entries.append((current_shoresh, current_lines[:]))

real_entries = [(sh, lines) for sh, lines in entries if sh not in SKIP]
print(f"  {len(real_entries)} entries")

# ── Rejection patterns ────────────────────────────────────────────────────────

DEF_KW_RE = re.compile(
    r'(?:ענינו|פירושו|ענין|פירוש)\s+([^,;.()\n]{3,60})'
)

BAD_DEF_RE = re.compile(
    r'ידוע$|ידוע והוא|גם כן ידוע|ענינם ידוע|ענינו ידוע|ענינה ידוע|'
    r'^בעודו|^לפי ש|^כי ה|^שפירושו|^ופירוש|^וכן|^כמו ש|'
    r'^אשר|^שהוא|^שהם|^שהיה|^ואמר|^ואמרו|^רצונו לומר|'
    r'^כתב רבי|^פירש רבי|^אמר רבי|^ואדוני|^ורבי|^אמר ה|^אמר הנביא|'
    r'^הוא קמוץ|^הוא בשש|^הוא בחמש|^הוא בארבע|^הוא בשלש|'
    r'^הוא אחד בסמוך|^הוא משקל|^הוא מוכרת|^הוא מן ה|'
    r'בלשון הקדש כן לועזין|כאשר פירשתיו|במקומו|'
    r'^תצוו|^תצוה|^יצוה|^ויצו|'
    r'^וד|^וה|^וכ|^ול|^ומ|^ונ|^וע|^ופ|^וצ|^וק|^ור|^וש|^ות|'
    r'^הפסוק|^הם דברי|^כמו התינוק|^שקודם|^שלחתי|^ויחפאו|'
    r'^הראשון$|^אחד$|^השני$|^הראשון כי|^אחד הם|^אחד הוא|'
    r'^לפיכך|^שהיו ה|^שהיו המ|^שהיו ב|^שהיו מ|^שהיו ז|'
    r'^האחר מורה|^חפשי מ|^שלא חשך|^לפי מקומו|'
    r'^הזה בדברי|^הזה ידוע|^ידוע בדברי|^ידוע כ|'
    r'בדברי רבותינו|בלשון רבותינו|שאמרו רבותינו|כמו שאמרו|'
    r'^אחד וכל|^כולם וכל|'
    r'^כלומר|^שהוא מ|^שהוא ה|^שהוא כ|^בפני עצמו|^תחלת הפסוק|'
    r'^הזה לפי שהוא|^שהם נ|^שהם פ|^שהם ר|'
    r'^ימעטו|^יריח|^לשבר כ|^מדברי|^פירשו|^חושב מ|'
    r'^שפת ימים|^גרגרי|^אבני כריתה|^מצחת|^שתי מלות|'
    r'^כלומר|^שהוא מ|^שהוא ה|^שהוא כ|^בפני עצמו|^תחלת הפסוק|'
    r'^הזה לפי שהוא|^שהם נ|^שהם פ|^שהם ר|'
    r'^ימעטו|^יריח|^לשבר כ|^מדברי|^פירשו|^חושב מ|'
    r'^ידוע בדברי|^ידוע כ|^ידוע ה|'
    r'^טיפים הידוע|^הנחש האסור|^השיר שהוא|^ואם לא|'
    r'^קבלה שהוא|^רושם שהוא|^תל שהוא|^מרה גסה|'
    r'^כאילו אמר|^כמו ענין שהוא'
)

VERSE_EXPLAIN_RE = re.compile(
    r'^לא |^כי |^אם |^כאשר |^אחרי |^עד |^כלומר |^שהוא |^שהם |'
    r'^שהיה |^שאתה |^שיהיה |^שיהיו |^שנאמר |^שאמר |^אחר$|^אחר '
)

VERSE_FRAG_RE = re.compile(
    r'י"י|'
    r'\bאני\b|\bאתה\b|\bאנחנו\b|\bאתם\b|\bאתן\b|'
    r'עמך|עמי|עמו|עמה|עמנו|עמכם|'
    r'לך|לי|לנו|לכם|'
    r'בך|בי|בנו|בכם'
)

NIKUD_NOTE_RE = re.compile(
    r'בשש נקודות|בחמש נקודות|בארבע נקודות|בשלש נקודות|בשני נקודות'
)

def is_bad(candidate):
    if not candidate or len(candidate) < 2: return True
    if len(candidate) > 45: return True
    if any(unicodedata.category(c) == 'Mn'
           for c in unicodedata.normalize('NFD', candidate)):
        return True
    if BAD_DEF_RE.search(candidate): return True
    if NIKUD_NOTE_RE.search(candidate): return True
    return False

def clean_candidate(candidate):
    """Strip noise suffixes from a candidate definition."""
    # Strip trailing "וידוע הוא/הם/היא/כX"
    candidate = re.sub(r'\s+וידוע\s+\S+$', '', candidate).strip()
    candidate = re.sub(r'\s+ידוע\s+(?:הוא|הם|היא)$', '', candidate).strip()
    # "X ידוע כענין Y" → keep Y
    m = re.match(r'^.+?\s+ידוע\s+כענין\s+(.+)$', candidate)
    if m:
        candidate = m.group(1).strip()
    # "X ידוע שהוא Y" → keep Y
    m = re.match(r'^.+?\s+ידוע\s+שהוא\s+(.+)$', candidate)
    if m:
        candidate = m.group(1).strip()
    # "X יש שהוא בענין Y ויש בענין Z" → "Y וZ"
    m = re.match(r'^.+?\s+יש\s+שהוא\s+בענין\s+(\S+)\s+ויש\s+בענין\s+(\S+)$', candidate)
    if m:
        candidate = m.group(1) + ' ו' + m.group(2)
    # Strip trailing loanword notes "X בלע"ז Y" → keep X
    candidate = re.sub(r'\s+בלע"ז.*$', '', candidate).strip()
    candidate = re.sub(r'\s+\[בלע"ז.*$', '', candidate).strip()
    # Strip trailing rabbinic references
    candidate = re.sub(r'\s+(?:שנשתמשו|שאמרו|כמו שאמרו)\s+.*$', '', candidate).strip()
    return candidate.rstrip('.,;').strip()

def extract_definition(plain_text):
    for m in DEF_KW_RE.finditer(plain_text):
        raw = m.group(1).strip().rstrip('.,;')
        raw = re.split(r'[,;]', raw)[0].strip()
        candidate = clean_candidate(raw)
        if not candidate or len(candidate) < 2: continue
        if is_bad(candidate): continue
        if VERSE_EXPLAIN_RE.match(candidate): continue
        if VERSE_FRAG_RE.search(candidate): continue
        return candidate
    return None

# ── Build rows ────────────────────────────────────────────────────────────────
rows = []
no_definition = 0

for shoresh, lines in real_entries:
    plain_shoresh = strip_nikud(shoresh).strip()
    if not plain_shoresh: continue
    full = plain(' '.join(lines))
    defn = extract_definition(full)
    if defn:
        rows.append((plain_shoresh, defn))
    else:
        no_definition += 1

print(f"  Entries with clean definition: {len(rows)}")
print(f"  Entries without definition:    {no_definition}")

# ── Insert into dict DB ───────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
db.execute("PRAGMA journal_mode=WAL")

src = db.execute("SELECT id FROM source_kind WHERE name=?", (SOURCE_NAME,)).fetchone()
if src:
    source_id = src[0]
else:
    db.execute("INSERT INTO source_kind (name) VALUES (?)", (SOURCE_NAME,))
    source_id = db.execute("SELECT id FROM source_kind WHERE name=?", (SOURCE_NAME,)).fetchone()[0]

db.execute("DELETE FROM sense WHERE source_id=?", (source_id,))
db.commit()

word_cache = {hw: wid for wid, hw in db.execute("SELECT id, headword FROM word")}
inserted = 0

for plain_shoresh, defn in rows:
    if plain_shoresh not in word_cache:
        db.execute("INSERT INTO word (headword) VALUES (?)", (plain_shoresh,))
        wid = db.execute("SELECT id FROM word WHERE headword=?", (plain_shoresh,)).fetchone()[0]
        word_cache[plain_shoresh] = wid
    else:
        wid = word_cache[plain_shoresh]
    db.execute(
        "INSERT INTO sense (word_id, text, source_id) VALUES (?, ?, ?)",
        (wid, defn, source_id)
    )
    inserted += 1

db.commit()
db.execute("VACUUM")
db.close()

# ── Verify ────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
total = db.execute("SELECT COUNT(*) FROM sense WHERE source_id=?", (source_id,)).fetchone()[0]
print(f"\nInserted: {total} definitions")

print("\nSample:")
for r in db.execute("""
    SELECT w.headword, s.text FROM sense s
    JOIN word w ON w.id=s.word_id
    WHERE s.source_id=? ORDER BY w.headword LIMIT 20
""", (source_id,)):
    print(f"  [{r[0]}] {r[1]}")

db.close()
print("\nDone.")
