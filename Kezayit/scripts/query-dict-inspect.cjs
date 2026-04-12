const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

const books = [
  { id: 473, title: 'ספר הערוך' },
  { id: 472, title: 'אוצר לעזי רשי' },
  { id: 471, title: 'הפלאה שבערכין' },
  { id: 460, title: 'ספר הבחור (דקדוק)' },
  { id: 461, title: 'שפת יתר' },
  { id: 462, title: 'מחברת מנחם' },
  { id: 463, title: 'דבש לפי' },
  { id: 464, title: 'סדר הדורות' },
  { id: 465, title: 'מדבר קדמות' },
  { id: 466, title: 'עין זוכר' },
  { id: 6104, title: 'זה הכלל' },
  { id: 6103, title: 'ספר הבחור (ספרות עזר)' },
  { id: 6105, title: 'ספר השרשים לרדק' },
]

for (const book of books) {
  console.log(`\n${'='.repeat(60)}`)
  console.log(`BOOK [${book.id}]: ${book.title}`)
  console.log('='.repeat(60))

  // First 12 lines
  const lines = db.prepare(
    'SELECT lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex LIMIT 12'
  ).all(book.id)
  lines.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 200)}`))

  // TOC structure (first 15 entries)
  const toc = db.prepare(`
    SELECT te.id, te.parentId, te.level, tt.text, l.lineIndex
    FROM tocEntry te
    JOIN tocText tt ON tt.id = te.textId
    LEFT JOIN line l ON l.id = te.lineId
    WHERE te.bookId = ?
    ORDER BY te.id
    LIMIT 15
  `).all(book.id)
  console.log('  TOC:')
  toc.forEach(t => console.log(`    L${t.level} [${t.id}] parent=${t.parentId} line=${t.lineIndex} "${t.text}"`))

  // A few lines from the middle to see entry format
  const mid = Math.floor(lines.length / 2)
  const midLines = db.prepare(
    'SELECT lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex LIMIT 5 OFFSET ?'
  ).all(book.id, Math.floor(book.id === 464 ? 5000 : (book.id === 473 ? 500 : 200)))
  console.log('  MID SAMPLE:')
  midLines.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 250)}`))
}

db.close()
