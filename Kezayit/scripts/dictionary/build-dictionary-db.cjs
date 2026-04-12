/**
 * build-dictionary-db.cjs
 *
 * Builds public/dictionary.db from two sources:
 *   1. FinalDictionary.txt  — Aramaic words + ראשי תיבות from ToratEmet
 *   2. Main seforim DB      — 4 dictionary books (ספר הערוך, הפלאה שבערכין,
 *                             ספר השרשים לרד"ק, אוצר לעזי רש"י)
 *
 * Usage:
 *   node scripts/dictionary/build-dictionary-db.cjs
 *
 * Prerequisites:
 *   npm install better-sqlite3 iconv-lite   (already in devDependencies)
 *
 * Paths (edit if your environment differs):
 *   DICT_TXT  — FinalDictionary.txt from ToratEmet install
 *   MAIN_DB   — seforim.db (the main Zayit/Otzaria database)
 *   OUT_DB    — output path (public/dictionary.db, served by Vite + shipped with app)
 *
 * The script writes to a .tmp file first, then atomically replaces OUT_DB,
 * so it is safe to run while the dev server is stopped.
 */

const Database = require('better-sqlite3')
const fs       = require('fs')
const path     = require('path')
const iconv    = require('iconv-lite')

// ── Config ────────────────────────────────────────────────────────────────────

// Prefer the pre-exported UTF-8 file in this folder (no iconv needed, no dependency
// on the ToratEmet installation). Falls back to the original Win-1255 file if present.
const LOCAL_TXT  = path.resolve(__dirname, 'torat-emet-dictionary.txt')
const ORIGIN_TXT = 'C:\\Users\\Admin\\Documents\\ToratEmetInstall\\Dictionaries\\FinalDictionary.txt'
const DICT_TXT   = fs.existsSync(LOCAL_TXT) ? LOCAL_TXT : ORIGIN_TXT

const MAIN_DB  = 'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db'
const OUT_DB   = path.resolve(__dirname, '../../public/dictionary.db')
const TMP_DB   = OUT_DB + '.tmp'

// ── Schema ────────────────────────────────────────────────────────────────────
//
// entry table columns:
//   headword   — consonants only (nikud stripped), used for indexed search
//   nikud      — vocalized form if available (shown in UI)
//   definition — full definition string; may contain *** separators and {nikud} markers
//   type       — 'aramaic' | 'abbrev' | 'book'
//   source     — 0-3 = ToratEmet txt sources; 10-13 = main DB book sources (see below)
//   bookId     — main DB book id (only for source >= 10)
//   lineIndex  — line position in main DB (only for source >= 10)
//
// source values:
//   0  = dictionary1.txt  — basic Aramaic, no nikud
//   1  = dictionary2.txt  — Aramaic with nikud + some ראשי תיבות
//   2  = dictionary3.txt  — single etymology entry
//   3  = dictionary4.txt  — Targum-style Aramaic, full nikud on every entry
//   10 = ספר הערוך        (bookId 473)
//   11 = הפלאה שבערכין    (bookId 471)
//   12 = ספר השרשים לרד"ק (bookId 6105)
//   13 = אוצר לעזי רש"י   (bookId 472)

// ── Helpers ───────────────────────────────────────────────────────────────────

/** True if the headword is an abbreviation (contains " ״ ׳ ') */
function isAbbrev(headword) {
  return /["״׳']/.test(headword)
}

/** Strip nikud/cantillation from a Hebrew string, leaving consonants only */
function stripNikud(s) {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4]/g, '').trim()
}

/** Strip HTML tags and collapse whitespace */
function stripHtml(s) {
  return s.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim()
}

/** Extract the headword from a dictionary book line based on its format */
function extractBookHeadword(content, bookId) {
  if (bookId === 6105) {
    // ספר השרשים: <h3>ROOT</h3>
    const m = content.match(/<h3>([^<]+)<\/h3>/)
    return m ? m[1].trim() : null
  }
  if (bookId === 473) {
    // ספר הערוך: <b><big>WORD</big></b>
    const big = content.match(/<big>([^<]+)<\/big>/)
    if (big) return big[1].trim()
  }
  // הפלאה שבערכין (471) and אוצר לעזי רש"י (472): first <b>WORD</b>
  const bold = content.match(/<b>([^<]+)<\/b>/)
  return bold ? bold[1].trim() : null
}

// ── Create DB ─────────────────────────────────────────────────────────────────

if (fs.existsSync(TMP_DB)) fs.unlinkSync(TMP_DB)
const dict = new Database(TMP_DB)

dict.exec(`
  PRAGMA journal_mode = OFF;
  PRAGMA synchronous  = OFF;

  CREATE TABLE entry (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    headword   TEXT    NOT NULL,
    nikud      TEXT,
    definition TEXT    NOT NULL,
    type       TEXT    NOT NULL DEFAULT 'aramaic',
    source     INTEGER NOT NULL,
    bookId     INTEGER,
    lineIndex  INTEGER
  );

  CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT
  );

  INSERT INTO meta VALUES ('txt_indexed', '0');
  INSERT INTO meta VALUES ('db_indexed',  '0');
  INSERT INTO meta VALUES ('version',     '1');
`)

const ins = dict.prepare(`
  INSERT INTO entry (headword, nikud, definition, type, source, bookId, lineIndex)
  VALUES (?, ?, ?, ?, ?, ?, ?)
`)

// ── 1. Parse FinalDictionary.txt ─────────────────────────────────────────────
//
// Format: "[source] headword={nikud} definition *** {nikud2} alt_def"
//   source prefix 0-3 identifies which original file the entry came from
//   {nikud} at the start of the definition is the vocalized form of the headword
//   *** separates multiple forms or meanings of the same word
//   (=...) in the definition expands an abbreviation

const rawBytes = fs.readFileSync(DICT_TXT)
const text = DICT_TXT === LOCAL_TXT
  ? rawBytes.toString('utf8')                  // already UTF-8
  : iconv.decode(rawBytes, 'win1255')          // original ToratEmet encoding
console.log(`Reading from: ${path.basename(DICT_TXT)}`)

let txtCount = 0
const insertTxt = dict.transaction(() => {
  // Collect all entries, deduplicating by headword.
  // When the same headword appears in multiple sources, keep the richest:
  // source 3 (full nikud Targum) > source 1 (nikud + detail) > source 0 (basic)
  const SOURCE_PRIORITY = { 3: 3, 1: 2, 2: 1, 0: 0 }
  const seen = new Map() // headword → {nikud, definition, type, source, priority}

  for (const raw of text.split('\n')) {
    const line = raw.trim()
    if (!line || line.startsWith('//') || !line.includes('=')) continue

    const prefixMatch = line.match(/^([0-3]) (.+)$/)
    const source = prefixMatch ? parseInt(prefixMatch[1]) : 0
    const rest   = prefixMatch ? prefixMatch[2] : line

    const eqIdx = rest.indexOf('=')
    if (eqIdx < 0) continue

    const rawWord = rest.slice(0, eqIdx).trim()
    const rawDef  = rest.slice(eqIdx + 1).trim()
    if (!rawWord || !rawDef) continue

    let nikud = null
    let definition = rawDef
    const nikudMatch = rawDef.match(/^\{([^}]+)\}\s*(.*)$/)
    if (nikudMatch) {
      nikud = nikudMatch[1]
      definition = nikudMatch[2].trim()
    }

    const headword = stripNikud(rawWord)
    if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue

    const displayNikud = nikud ?? (rawWord !== headword ? rawWord : null)
    const type = isAbbrev(headword) ? 'abbrev' : 'aramaic'
    const priority = SOURCE_PRIORITY[source] ?? 0

    const existing = seen.get(headword)
    if (!existing || priority > existing.priority) {
      seen.set(headword, { nikud: displayNikud, definition, type, source, priority })
    }
  }

  for (const [headword, e] of seen) {
    ins.run(headword, e.nikud, e.definition, e.type, e.source, null, null)
    txtCount++
  }
})
insertTxt()
console.log(`[1/2] FinalDictionary.txt: ${txtCount} entries`)

// ── 2. Extract from main DB dictionary books ──────────────────────────────────

const main = new Database(MAIN_DB, { readonly: true })

// ── Static books ──────────────────────────────────────────────────────────────
const BOOKS = [
  { id: 473,  source: 10, filter: `content LIKE '%<big>%' AND content NOT LIKE '<h%'` },
  { id: 471,  source: 11, filter: `content LIKE '%<b>%'   AND content NOT LIKE '<h%'` },
  { id: 6105, source: 12, filter: `content LIKE '<h3>%'   AND content NOT LIKE '<h3>הקדמה%'` },
  { id: 472,  source: 13, filter: `lineIndex >= 4` },
  // מחברת מנחם — root concordance; index ALL bold words per line
  { id: 462,  source: 14, filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex >= 10`, multiWord: true },
  // Chida encyclopedias
  { id: 463,  source: 15, filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 10` },
  { id: 465,  source: 16, filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 10` },
  { id: 466,  source: 17, filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 14` },
]

// ── Tanach commentary books (dynamic) ─────────────────────────────────────────
// Source 20: מצודת ציון — dedicated Biblical word lexicon
// Source 21: רלב"ג ביאור המלות — word explanations on Torah
// Source 22: רש"י on Tanach (not Talmud)
const mtzBookIds    = main.prepare(`SELECT id FROM book WHERE title LIKE '%מצודת ציון%'`).all().map(r => r.id)
const malbimBiurIds = main.prepare(`SELECT id FROM book WHERE title LIKE '%מלבי%' AND title LIKE '%באור המילות%'`).all().map(r => r.id)

console.log(`  מצודת ציון: ${mtzBookIds.length} books`)
console.log(`  מלבים באור המילות: ${malbimBiurIds.length} books`)

const TANACH_BOOKS = [
  ...mtzBookIds.map(id => ({ id, source: 20 })),
  ...malbimBiurIds.map(id => ({ id, source: 23 })),
]
const TANACH_FILTER = `content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 1`

// ── Headword extraction for Tanach commentaries ───────────────────────────────
// Format: <b>WORD.</b> explanation — strip trailing punctuation
function extractTanachHeadword(content) {
  const m = content.match(/<b>([^<]+)<\/b>/)
  if (!m) return null
  return m[1].replace(/[.,;:״׳"'\s]+$/, '').trim()
}

let dbCount = 0

const insertDb = dict.transaction((rows) => {
  for (const r of rows)
    ins.run(r.headword, null, r.definition, 'book', r.source, r.bookId, r.lineIndex)
})

// Process static books
for (const book of BOOKS) {
  const rows = main.prepare(`
    SELECT lineIndex, content FROM line
    WHERE bookId = ? AND ${book.filter}
    ORDER BY lineIndex
  `).all(book.id)

  const entries = []
  for (const row of rows) {
    if (book.multiWord) {
      const bolds = [...row.content.matchAll(/<b>([^<]+)<\/b>/g)].map(m => m[1].trim())
      for (const headword of bolds) {
        if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue
        if (headword.length > 20) continue
        const definition = stripHtml(row.content).slice(0, 500)
        entries.push({ headword, definition, source: book.source, bookId: book.id, lineIndex: row.lineIndex })
      }
    } else {
      const headword = extractBookHeadword(row.content, book.id)
      if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue
      const definition = stripHtml(row.content).slice(0, 500)
      entries.push({ headword, definition, source: book.source, bookId: book.id, lineIndex: row.lineIndex })
    }
  }

  insertDb(entries)
  dbCount += entries.length
  console.log(`       book ${book.id} (${book.multiWord ? 'multi-word' : 'single'}): ${entries.length} entries`)
}

// Process Tanach commentary books
let tanachCount = 0
for (const book of TANACH_BOOKS) {
  const rows = main.prepare(`
    SELECT lineIndex, content FROM line
    WHERE bookId = ? AND ${TANACH_FILTER}
    ORDER BY lineIndex
  `).all(book.id)

  const entries = []
  for (const row of rows) {
    const headword = extractTanachHeadword(row.content)
    if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue
    if (headword.length > 30) continue
    const definition = stripHtml(row.content).slice(0, 500)
    entries.push({ headword, definition, source: book.source, bookId: book.id, lineIndex: row.lineIndex })
  }

  insertDb(entries)
  dbCount += entries.length
  tanachCount += entries.length
}
console.log(`       Tanach commentaries: ${tanachCount} entries across ${TANACH_BOOKS.length} books`)

main.close()
console.log(`[2/2] Main DB books: ${dbCount} entries`)

// ── 3. Indexes + finalize ─────────────────────────────────────────────────────

dict.exec(`
  CREATE INDEX idx_entry_headword ON entry(headword);
  CREATE INDEX idx_entry_type     ON entry(type);
  CREATE INDEX idx_entry_source   ON entry(source);
  UPDATE meta SET value = '1' WHERE key = 'txt_indexed';
  UPDATE meta SET value = '1' WHERE key = 'db_indexed';
  PRAGMA optimize;
`)
dict.close()

// Atomic replace (stop dev server first if it's running)
if (fs.existsSync(OUT_DB)) fs.unlinkSync(OUT_DB)
fs.renameSync(TMP_DB, OUT_DB)

// ── Stats ─────────────────────────────────────────────────────────────────────

const stat = fs.statSync(OUT_DB)
const check = new Database(OUT_DB, { readonly: true })
const byType   = check.prepare(`SELECT type, COUNT(*) as cnt FROM entry GROUP BY type ORDER BY cnt DESC`).all()
const bySource = check.prepare(`SELECT source, COUNT(*) as cnt FROM entry GROUP BY source ORDER BY source`).all()
check.close()

console.log(`\ndictionary.db: ${(stat.size / 1024 / 1024).toFixed(2)} MB  |  total: ${txtCount + dbCount} entries`)
console.log('By type:  ', Object.fromEntries(byType.map(r => [r.type, r.cnt])))
console.log('By source:', Object.fromEntries(bySource.map(r => [r.source, r.cnt])))
