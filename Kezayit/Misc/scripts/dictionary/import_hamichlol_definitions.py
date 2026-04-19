"""
import_hamichlol_definitions.py
────────────────────────────────
Queries the המכלול API for every word in the dictionary and imports
the first sentence of the first paragraph as a definition.

Uses the MediaWiki TextExtracts API:
  https://www.hamichlol.org.il/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&exsentences=1

Processes words in batches of 50 (API limit).
Skips words that already have a המכלול sense row.
Idempotent — safe to re-run.

Usage:
    python Misc/scripts/dictionary/import_hamichlol_definitions.py
"""
import sqlite3, sys, io, json, time, re, unicodedata
import urllib.request, urllib.parse
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

DICT_DB     = "vue-frontend/public/dictionary/kezayit_dictionary.db"
API_URL     = "https://www.hamichlol.org.il/w/api.php"
SOURCE_NAME = 'המכלול'
BATCH_SIZE  = 50
DELAY       = 0.5  # seconds between batches — be polite

def strip_nikud(s):
    if not s: return s
    return ''.join(c for c in unicodedata.normalize('NFD', s)
                   if unicodedata.category(c) != 'Mn').strip()

def clean_extract(text):
    """Extract first clean sentence from API response."""
    if not text: return None
    text = text.strip()
    # Remove parenthetical transliterations at start: "שָׁלוֹם היא" → keep
    # Split on first period
    sentences = re.split(r'(?<=[.!?])\s', text)
    first = sentences[0].strip() if sentences else text.strip()
    # Remove trailing period if we'll add it back
    first = first.rstrip('.')
    if len(first) < 5: return None
    # Skip disambiguation pages
    if 'האם התכוונתם' in first: return None
    if 'דף פירושונים' in first: return None
    # Truncate at 200 chars
    if len(first) > 200:
        first = first[:200]
    return first + '.'

def fetch_batch(titles):
    """Fetch extracts for up to 50 titles at once."""
    params = urllib.parse.urlencode({
        'action': 'query',
        'prop': 'extracts',
        'exintro': '1',
        'explaintext': '1',
        'exsentences': '1',
        'titles': '|'.join(titles),
        'format': 'json',
        'redirects': '1',
    })
    url = f"{API_URL}?{params}"
    req = urllib.request.Request(url, headers={'User-Agent': 'KezayitDict/1.0'})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        print(f"  API error: {e}", flush=True)
        return {}

    results = {}
    pages = data.get('query', {}).get('pages', {})
    # Handle redirects
    redirects = {r['from']: r['to'] for r in data.get('query', {}).get('redirects', [])}

    for page_id, page in pages.items():
        if page_id == '-1': continue
        title = page.get('title', '')
        extract = clean_extract(page.get('extract', ''))
        if extract:
            results[title] = extract
            # Also map the original title if it was redirected
            for orig, dest in redirects.items():
                if dest == title:
                    results[orig] = extract

    return results

# ── Setup ─────────────────────────────────────────────────────────────────────
db = sqlite3.connect(DICT_DB)
db.execute("PRAGMA journal_mode=WAL")

# Get or create source
src = db.execute("SELECT id FROM source_kind WHERE name=?", (SOURCE_NAME,)).fetchone()
if src:
    source_id = src[0]
else:
    db.execute("INSERT INTO source_kind (name) VALUES (?)", (SOURCE_NAME,))
    source_id = db.execute("SELECT id FROM source_kind WHERE name=?", (SOURCE_NAME,)).fetchone()[0]

print(f"Source id: {source_id}")

# Get all words that don't already have a המכלול sense
existing_words = set(
    r[0] for r in db.execute(
        "SELECT DISTINCT w.headword FROM sense s JOIN word w ON w.id=s.word_id WHERE s.source_id=?",
        (source_id,)
    )
)
print(f"Already have המכלול senses: {len(existing_words):,}")

all_words = [r[0] for r in db.execute("SELECT headword FROM word ORDER BY headword")]
words_to_fetch = [w for w in all_words if w not in existing_words]
print(f"Words to query: {len(words_to_fetch):,}")

# ── Fetch in batches ──────────────────────────────────────────────────────────
word_id_map = {hw: wid for wid, hw in db.execute("SELECT id, headword FROM word")}

inserted = 0
not_found = 0
batch_count = 0

for i in range(0, len(words_to_fetch), BATCH_SIZE):
    batch = words_to_fetch[i:i + BATCH_SIZE]
    batch_count += 1

    results = fetch_batch(batch)

    for word in batch:
        extract = results.get(word)
        if not extract:
            not_found += 1
            continue

        wid = word_id_map.get(word)
        if not wid:
            continue

        db.execute(
            "INSERT INTO sense (word_id, text, source_id) VALUES (?, ?, ?)",
            (wid, extract, source_id)
        )
        inserted += 1

    db.commit()

    if batch_count % 10 == 0:
        pct = min(i / len(words_to_fetch) * 100, 100)
        print(f"  {i:,}/{len(words_to_fetch):,} ({pct:.0f}%)  inserted={inserted:,}  not_found={not_found:,}",
              flush=True)

    time.sleep(DELAY)

db.execute("VACUUM")
db.close()

# ── Report ────────────────────────────────────────────────────────────────────
print(f"\n=== DONE ===")
print(f"  Inserted: {inserted:,}")
print(f"  Not found: {not_found:,}")

db = sqlite3.connect(DICT_DB)
total = db.execute("SELECT COUNT(*) FROM sense WHERE source_id=?", (source_id,)).fetchone()[0]
print(f"  Total המכלול senses: {total:,}")

print("\nSample:")
for r in db.execute("""
    SELECT w.headword, s.text FROM sense s
    JOIN word w ON w.id=s.word_id
    WHERE s.source_id=? ORDER BY w.headword LIMIT 15
""", (source_id,)):
    print(f"  [{r[0]}] {r[1][:80]}")

db.close()
