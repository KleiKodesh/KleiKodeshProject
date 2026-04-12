/**
 * Extracts headwords from the 4 dictionary books and writes them into
 * a new `dict_entry` table in the DB.
 *
 * Books:
 *   473 - ספר הערוך        : <b><big>WORD</big></b> lines
 *   471 - הפלאה שבערכין    : <b>WORD</b> lines (not starting with <h)
 *   6105 - ספר השרשים לרדק : <h3>ROOT</h3> lines (skip intro headings)
 *   472 - אוצר לעזי רשי   : numbered lines, first <b>WORD</b> is headword
 */

const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
)

// ── Create table ──────────────────────────────────────────────────────────────
db.exec(`
  DROP TABLE IF EXISTS dict_entry;
  CREATE TABLE dict_entry (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    bookId      INTEGER NOT NULL,
    lineId      INTEGER NOT NULL,
    lineIndex   INTEGER NOT NULL,
    headword    TEXT    NOT NULL,
    FOREIGN KEY (bookId)  REFERENCES book(id),
    FOREIGN KEY (lineId)  REFERENCES line(id)
  );
  CREATE INDEX idx_dict_entry_headword ON dict_entry(headword);
  CREATE INDEX idx_dict_entry_bookId   ON dict_entry(bookId);
`)

function stripHtml(s) {
  return s.replace(/<[^>]+>/g, '').trim()
}

function extractHeadword(content, bookId) {
  if (bookId === 6105) {
    const m = content.match(/<h3>([^<]+)<\/h3>/)
    return m ? m[1].trim() : null
  }
  // For 473: <b><big>WORD</big></b>
  if (bookId === 473) {
    const big = content.match(/<big>([^<]+)<\/big>/)
    if (big) return big[1].trim()
  }
  // For 471 and 472: first <b>WORD</b>
  const bold = content.match(/<b>([^<]+)<\/b>/)
  return bold ? bold[1].trim() : null
}

const insert = db.prepare(`
  INSERT INTO dict_entry (bookId, lineId, lineIndex, headword)
  VALUES (?, ?, ?, ?)
`)

const books = [
  {
    id: 473,
    filter: `content LIKE '%<big>%' AND content NOT LIKE '<h%'`,
  },
  {
    id: 471,
    filter: `content LIKE '%<b>%' AND content NOT LIKE '<h%'`,
  },
  {
    id: 6105,
    filter: `content LIKE '<h3>%' AND content NOT LIKE '<h3>הקדמה%'`,
  },
  {
    id: 472,
    filter: `lineIndex >= 4`,
  },
]

let total = 0
const insertMany = db.transaction((rows) => {
  for (const r of rows) insert.run(r.bookId, r.lineId, r.lineIndex, r.headword)
})

for (const book of books) {
  const rows = db.prepare(`
    SELECT id, lineIndex, content FROM line
    WHERE bookId = ? AND ${book.filter}
    ORDER BY lineIndex
  `).all(book.id)

  const entries = []
  for (const row of rows) {
    const headword = extractHeadword(row.content, book.id)
    if (!headword || headword.length === 0) continue
    // Skip headwords that are just punctuation or numbers
    if (!/[\u05D0-\u05EA]/.test(headword)) continue
    entries.push({ bookId: book.id, lineId: row.id, lineIndex: row.lineIndex, headword })
  }

  insertMany(entries)
  console.log(`Book ${book.id}: inserted ${entries.length} entries`)
  total += entries.length
}

console.log(`\nTotal entries: ${total}`)

// Verify
const sample = db.prepare(`
  SELECT de.headword, de.bookId, de.lineIndex, b.title
  FROM dict_entry de JOIN book b ON b.id = de.bookId
  ORDER BY RANDOM() LIMIT 20
`).all()
console.log('\nRandom sample:')
sample.forEach(r => console.log(`  [${r.title}] "${r.headword}" line=${r.lineIndex}`))

// Count by book
const counts = db.prepare(`
  SELECT b.title, COUNT(*) as cnt
  FROM dict_entry de JOIN book b ON b.id = de.bookId
  GROUP BY de.bookId
`).all()
console.log('\nCounts by book:')
counts.forEach(r => console.log(`  ${r.title}: ${r.cnt}`))

db.close()
console.log('\nDone.')
