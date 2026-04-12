/**
 * Builds dictionary.db from:
 * 1. FinalDictionary.txt (Aramaic/Hebrew word forms)
 * 2. The 4 dictionary books in the main DB (ספר הערוך etc.)
 */
const Database = require('better-sqlite3')
const fs = require('fs')
const path = require('path')

const DICT_TXT = 'C:\\Users\\Admin\\Documents\\ToratEmetInstall\\Dictionaries\\FinalDictionary.txt'
const MAIN_DB  = 'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db'
const OUT_DB   = path.join(__dirname, '..', 'public', 'dictionary.db')

// ── Create output DB ──────────────────────────────────────────────────────────
if (fs.existsSync(OUT_DB)) fs.unlinkSync(OUT_DB)
const dict = new Database(OUT_DB)

dict.exec(`
  PRAGMA journal_mode = OFF;
  PRAGMA synchronous  = OFF;

  CREATE TABLE entry (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    headword   TEXT NOT NULL,          -- consonants only, for search
    nikud      TEXT,                   -- vocalized form if available
    definition TEXT NOT NULL,
    source     INTEGER NOT NULL,       -- 0=txt-basic 1=txt-nikud 3=txt-full 10=ערוך 11=הפלאה 12=שרשים 13=לעזי
    bookId     INTEGER,                -- only for source>=10 (links back to main DB)
    lineIndex  INTEGER                 -- only for source>=10
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
  INSERT INTO entry (headword, nikud, definition, source, bookId, lineIndex)
  VALUES (?, ?, ?, ?, ?, ?)
`)

// ── 1. Parse FinalDictionary.txt ─────────────────────────────────────────────
const enc = require('iconv-lite')
const rawBytes = fs.readFileSync(DICT_TXT)
const text = enc.decode(rawBytes, 'win1255')

let txtCount = 0
const insertTxt = dict.transaction(() => {
  for (const raw of text.split('\n')) {
    const line = raw.trim()
    if (!line || line.startsWith('//') || !line.includes('=')) continue

    // Parse prefix: "3 word=def" or "0 word=def"
    const prefixMatch = line.match(/^([0-3]) (.+)$/)
    const source = prefixMatch ? parseInt(prefixMatch[1]) : 0
    const rest   = prefixMatch ? prefixMatch[2] : line

    const eqIdx = rest.indexOf('=')
    if (eqIdx < 0) continue

    const rawWord = rest.slice(0, eqIdx).trim()
    const rawDef  = rest.slice(eqIdx + 1).trim()
    if (!rawWord || !rawDef) continue

    // Extract nikud from {vocalized} if present at start of definition
    let nikud = null
    let definition = rawDef

    const nikudMatch = rawDef.match(/^\{([^}]+)\}\s*(.*)$/)
    if (nikudMatch) {
      nikud = nikudMatch[1]
      definition = nikudMatch[2].trim()
    }

    // headword = strip nikud from rawWord (keep consonants + spaces)
    const headword = rawWord.replace(/[\u05B0-\u05C7\u05F0-\u05F4]/g, '').trim()
    if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue

    ins.run(headword, nikud ?? rawWord, definition, source, null, null)
    txtCount++
  }
})
insertTxt()
console.log(`Inserted ${txtCount} entries from FinalDictionary.txt`)

// ── 2. Extract from main DB dictionary books ──────────────────────────────────
const main = new Database(MAIN_DB, { readonly: true })

const BOOKS = [
  { id: 473, source: 10, filter: `content LIKE '%<big>%' AND content NOT LIKE '<h%'` },
  { id: 471, source: 11, filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%'` },
  { id: 6105, source: 12, filter: `content LIKE '<h3>%' AND content NOT LIKE '<h3>הקדמה%'` },
  { id: 472, source: 13, filter: `lineIndex >= 4` },
]

function extractHeadword(content, bookId) {
  if (bookId === 6105) {
    const m = content.match(/<h3>([^<]+)<\/h3>/)
    return m ? m[1].trim() : null
  }
  if (bookId === 473) {
    const big = content.match(/<big>([^<]+)<\/big>/)
    if (big) return big[1].trim()
  }
  const bold = content.match(/<b>([^<]+)<\/b>/)
  return bold ? bold[1].trim() : null
}

function stripHtml(s) {
  return s.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim()
}

let dbCount = 0
const insertDb = dict.transaction((rows) => {
  for (const r of rows) ins.run(r.headword, null, r.definition, r.source, r.bookId, r.lineIndex)
})

for (const book of BOOKS) {
  const rows = main.prepare(`
    SELECT id, lineIndex, content FROM line
    WHERE bookId = ? AND ${book.filter}
    ORDER BY lineIndex
  `).all(book.id)

  const entries = []
  for (const row of rows) {
    const headword = extractHeadword(row.content, book.id)
    if (!headword || !/[\u05D0-\u05EA]/.test(headword)) continue
    const definition = stripHtml(row.content).slice(0, 500)
    entries.push({ headword, definition, source: book.source, bookId: book.id, lineIndex: row.lineIndex })
  }
  insertDb(entries)
  dbCount += entries.length
  console.log(`  Book ${book.id}: ${entries.length} entries`)
}
console.log(`Inserted ${dbCount} entries from main DB books`)

main.close()

// ── 3. Create indexes and finalize ────────────────────────────────────────────
dict.exec(`
  CREATE INDEX idx_entry_headword ON entry(headword);
  CREATE INDEX idx_entry_source   ON entry(source);
  UPDATE meta SET value = '1' WHERE key = 'txt_indexed';
  UPDATE meta SET value = '1' WHERE key = 'db_indexed';
  PRAGMA optimize;
`)

dict.close()

// ── Report size ───────────────────────────────────────────────────────────────
const stat = fs.statSync(OUT_DB)
const kb = Math.round(stat.size / 1024)
const mb = (stat.size / 1024 / 1024).toFixed(2)
console.log(`\ndictionary.db: ${kb} KB (${mb} MB)`)
console.log(`Total entries: ${txtCount + dbCount}`)
