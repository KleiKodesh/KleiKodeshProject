const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// For each dictionary book, understand the TOC depth and entry structure
// Focus on the ones that are actual word-lookup dictionaries

// ספר הערוך (473) - full TOC count and sample entries
const aruchTocCount = db.prepare('SELECT COUNT(*) as cnt FROM tocEntry WHERE bookId = 473').get()
console.log('ספר הערוך TOC entries:', aruchTocCount.cnt)

// Level 1 = letter sections, level 2 = individual word entries?
const aruchLevels = db.prepare(`
  SELECT level, COUNT(*) as cnt FROM tocEntry WHERE bookId = 473 GROUP BY level ORDER BY level
`).all()
console.log('ספר הערוך TOC levels:', JSON.stringify(aruchLevels))

// Sample level 2 entries (individual words)
const aruchL2 = db.prepare(`
  SELECT te.id, te.level, tt.text, l.lineIndex
  FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
  LEFT JOIN line l ON l.id = te.lineId
  WHERE te.bookId = 473 AND te.level = 1
  ORDER BY te.id LIMIT 30
`).all()
console.log('ספר הערוך L1 entries (first 30):', aruchL2.map(e => `"${e.text}" line=${e.lineIndex}`).join(', '))

// ספר השרשים לרדק (6105) - TOC structure
const radakTocCount = db.prepare('SELECT COUNT(*) as cnt FROM tocEntry WHERE bookId = 6105').get()
console.log('\nספר השרשים TOC entries:', radakTocCount.cnt)
const radakLevels = db.prepare(`
  SELECT level, COUNT(*) as cnt FROM tocEntry WHERE bookId = 6105 GROUP BY level ORDER BY level
`).all()
console.log('ספר השרשים TOC levels:', JSON.stringify(radakLevels))

// Sample level 3 entries (individual roots)
const radakL3 = db.prepare(`
  SELECT te.id, te.level, tt.text, l.lineIndex
  FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
  LEFT JOIN line l ON l.id = te.lineId
  WHERE te.bookId = 6105 AND te.level = 2
  ORDER BY te.id LIMIT 30
`).all()
console.log('ספר השרשים L2 entries (first 30):', radakL3.map(e => `"${e.text}"`).join(', '))

// אוצר לעזי רשי (472) - TOC structure
const rashiTocCount = db.prepare('SELECT COUNT(*) as cnt FROM tocEntry WHERE bookId = 472').get()
console.log('\nאוצר לעזי רשי TOC entries:', rashiTocCount.cnt)
const rashiLevels = db.prepare(`
  SELECT level, COUNT(*) as cnt FROM tocEntry WHERE bookId = 472 GROUP BY level ORDER BY level
`).all()
console.log('אוצר לעזי רשי TOC levels:', JSON.stringify(rashiLevels))

// Sample entries
const rashiL1 = db.prepare(`
  SELECT te.id, te.level, tt.text, l.lineIndex
  FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
  LEFT JOIN line l ON l.id = te.lineId
  WHERE te.bookId = 472 AND te.level = 1
  ORDER BY te.id LIMIT 20
`).all()
console.log('אוצר לעזי רשי L1 entries:', rashiL1.map(e => `"${e.text}" line=${e.lineIndex}`).join(', '))

// מחברת מנחם (462) - TOC structure
const menachemLevels = db.prepare(`
  SELECT level, COUNT(*) as cnt FROM tocEntry WHERE bookId = 462 GROUP BY level ORDER BY level
`).all()
console.log('\nמחברת מנחם TOC levels:', JSON.stringify(menachemLevels))

// Now look at actual line content for ספר הערוך to understand word entry format
// Each bold+big tag is a word entry - let's count them
const aruchWordLines = db.prepare(`
  SELECT COUNT(*) as cnt FROM line WHERE bookId = 473 AND content LIKE '%<big>%'
`).get()
console.log('\nספר הערוך lines with <big> (word entries):', aruchWordLines.cnt)

// Sample word entries from ספר הערוך - just the headword
const aruchWords = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 473 AND content LIKE '%<big>%'
  ORDER BY lineIndex LIMIT 20
`).all()
console.log('Sample ספר הערוך word entries:')
aruchWords.forEach(l => {
  // Extract the word from <big>WORD</big>
  const match = l.content.match(/<big>([^<]+)<\/big>/)
  if (match) console.log(`  line=${l.lineIndex} word="${match[1]}"`)
})

// For ספר השרשים - each h3 is a root entry
const radakRootLines = db.prepare(`
  SELECT COUNT(*) as cnt FROM line WHERE bookId = 6105 AND content LIKE '<h3>%'
`).get()
console.log('\nספר השרשים lines with <h3> (root entries):', radakRootLines.cnt)

const radakRoots = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 6105 AND content LIKE '<h3>%'
  ORDER BY lineIndex LIMIT 20
`).all()
console.log('Sample ספר השרשים root entries:')
radakRoots.forEach(l => {
  const match = l.content.match(/<h3>([^<]+)<\/h3>/)
  if (match) console.log(`  line=${l.lineIndex} root="${match[1]}"`)
})

// For אוצר לעזי רשי - what does a word entry look like?
const rashiSample = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 472
  ORDER BY lineIndex LIMIT 30
`).all()
console.log('\nאוצר לעזי רשי first 30 lines:')
rashiSample.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 150)}`))

db.close()
