const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Understand entry structure for each book so we know how to fetch "one entry"

// ספר הערוך: each <big> line is an entry. How many lines does a typical entry span?
// Let's look at consecutive <big> lines to see the gap
const aruchEntries = db.prepare(`
  SELECT lineIndex FROM line WHERE bookId = 473 AND content LIKE '%<big>%'
  ORDER BY lineIndex LIMIT 30
`).all()
console.log('ספר הערוך entry line gaps:')
for (let i = 1; i < aruchEntries.length; i++) {
  console.log(`  entry at ${aruchEntries[i-1].lineIndex} → next at ${aruchEntries[i].lineIndex} (gap=${aruchEntries[i].lineIndex - aruchEntries[i-1].lineIndex})`)
}

// So an entry = lines from this <big> line up to (but not including) the next <big> line
// Let's fetch a full entry (lines 8 to 9 exclusive = just line 8)
const entry1 = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 473 AND lineIndex >= 8 AND lineIndex < 10
  ORDER BY lineIndex
`).all()
console.log('\nספר הערוך entry "אא" (lines 8-9):')
entry1.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 200)}`))

// ספר השרשים: each <h3> is a root, followed by content lines until next <h3>
const radakEntries = db.prepare(`
  SELECT lineIndex FROM line WHERE bookId = 6105 AND content LIKE '<h3>%'
  ORDER BY lineIndex LIMIT 10
`).all()
console.log('\nספר השרשים entry line gaps:')
for (let i = 1; i < radakEntries.length; i++) {
  console.log(`  entry at ${radakEntries[i-1].lineIndex} → next at ${radakEntries[i].lineIndex} (gap=${radakEntries[i].lineIndex - radakEntries[i-1].lineIndex})`)
}

// Fetch a full root entry
const radakEntry = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 6105 AND lineIndex >= 26 AND lineIndex < 28
  ORDER BY lineIndex
`).all()
console.log('\nספר השרשים entry "אבב":')
radakEntry.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 300)}`))

// אוצר לעזי רשי: each numbered entry is one line
const rashiEntry = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 472 AND lineIndex >= 4 AND lineIndex <= 8
  ORDER BY lineIndex
`).all()
console.log('\nאוצר לעזי רשי entries 4-8:')
rashiEntry.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 300)}`))

// How many total word entries in each book?
const aruchCount = db.prepare(`SELECT COUNT(*) as cnt FROM line WHERE bookId = 473 AND content LIKE '%<big>%'`).get()
const radakCount = db.prepare(`SELECT COUNT(*) as cnt FROM line WHERE bookId = 6105 AND content LIKE '<h3>%' AND content NOT LIKE '<h3>הקדמה%'`).get()
// אוצר לעזי רשי: numbered entries (lines that start with a number)
const rashiCount = db.prepare(`SELECT COUNT(*) as cnt FROM line WHERE bookId = 472 AND lineIndex >= 4`).get()
// הפלאה שבערכין: same format as ערוך
const haflaaCount = db.prepare(`SELECT COUNT(*) as cnt FROM line WHERE bookId = 471 AND content LIKE '%<b>%' AND content NOT LIKE '<h%'`).get()

console.log('\nEntry counts:')
console.log('  ספר הערוך:', aruchCount.cnt)
console.log('  ספר השרשים:', radakCount.cnt)
console.log('  אוצר לעזי רשי (approx):', rashiCount.cnt)
console.log('  הפלאה שבערכין (approx):', haflaaCount.cnt)

// For the search: we need to extract the headword from each entry
// ספר הערוך: extract from <big>WORD</big>
// ספר השרשים: extract from <h3>ROOT</h3>
// אוצר לעזי רשי: extract <b>WORD</b> from the entry line (first bold = the Talmudic word)

// Sample אוצר לעזי רשי word extraction
const rashiSample = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 472 AND lineIndex BETWEEN 4 AND 20
  ORDER BY lineIndex
`).all()
console.log('\nאוצר לעזי רשי word extraction test:')
rashiSample.forEach(l => {
  const match = l.content.match(/<b>([^<]+)<\/b>/)
  if (match) console.log(`  line=${l.lineIndex} word="${match[1]}" | ${l.content.substring(0, 100)}`)
})

// הפלאה שבערכין: what does an entry look like?
const haflaaEntries = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 471 AND lineIndex BETWEEN 11 AND 30
  ORDER BY lineIndex
`).all()
console.log('\nהפלאה שבערכין entries 11-30:')
haflaaEntries.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 200)}`))

db.close()
