const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// How many total lines are in the DB?
const totalLines = db.prepare('SELECT COUNT(*) as cnt FROM line').get()
console.log('Total lines:', totalLines.cnt)

// How many books?
const totalBooks = db.prepare('SELECT COUNT(*) as cnt FROM book').get()
console.log('Total books:', totalBooks.cnt)

// Sample lines from a few different book types to understand word density
// Tanach line
const tanachLine = db.prepare(`
  SELECT l.content, b.title FROM line l JOIN book b ON b.id = l.bookId
  WHERE b.categoryId = 1 LIMIT 3
`).all()
console.log('\nSample Tanach lines:')
tanachLine.forEach(l => console.log(`  [${l.title}] ${l.content.substring(0, 120)}`))

// Talmud line
const talmudLine = db.prepare(`
  SELECT l.content, b.title FROM line l JOIN book b ON b.id = l.bookId
  WHERE b.categoryId = 12 LIMIT 3
`).all()
console.log('\nSample Talmud lines:')
talmudLine.forEach(l => console.log(`  [${l.title}] ${l.content.substring(0, 120)}`))

// Estimate words per line across a sample
const sampleLines = db.prepare('SELECT content FROM line ORDER BY RANDOM() LIMIT 1000').all()
let totalWords = 0
for (const l of sampleLines) {
  const text = l.content.replace(/<[^>]+>/g, ' ').trim()
  const words = text.split(/\s+/).filter(w => w.length > 0)
  totalWords += words.length
}
const avgWordsPerLine = totalWords / sampleLines.length
console.log(`\nAvg words per line (sample of 1000): ${avgWordsPerLine.toFixed(1)}`)
console.log(`Estimated total words in DB: ${Math.round(totalLines.cnt * avgWordsPerLine).toLocaleString()}`)

// What does a typical word look like after stripping HTML and nikud?
const sampleContent = db.prepare('SELECT content FROM line WHERE bookId = 1 LIMIT 5').all()
console.log('\nSample raw words from Tanach:')
sampleContent.forEach(l => {
  const text = l.content.replace(/<[^>]+>/g, ' ').trim()
  const words = text.split(/\s+/).filter(w => w.length > 1).slice(0, 8)
  console.log(' ', words.join(' | '))
})

// Check if there's nikud (vowel points) in the text
const nikudCheck = db.prepare(`
  SELECT content FROM line WHERE bookId = 1 AND lineIndex = 0
`).get()
console.log('\nFirst Tanach line (raw):', nikudCheck?.content?.substring(0, 200))

// Check a few more book categories to understand content variety
const cats = db.prepare(`
  SELECT c.id, c.title, COUNT(l.id) as lineCount
  FROM category c
  JOIN book b ON b.categoryId = c.id
  JOIN line l ON l.bookId = b.id
  WHERE c.level = 0
  GROUP BY c.id
  ORDER BY lineCount DESC
`).all()
console.log('\nLines per top-level category:')
cats.forEach(c => console.log(`  [${c.id}] ${c.title}: ${c.lineCount.toLocaleString()} lines`))

// What does stripping HTML + nikud look like for concordance?
// Hebrew unicode ranges: \u05D0-\u05EA (letters), \u05B0-\u05C7 (nikud)
function stripToConsonants(text) {
  return text
    .replace(/<[^>]+>/g, ' ')           // strip HTML
    .replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '') // strip nikud/cantillation
    .replace(/[^\u05D0-\u05EA\s"']/g, ' ') // keep only Hebrew letters + quotes
    .replace(/\s+/g, ' ')
    .trim()
}

const tanachSample = db.prepare('SELECT content FROM line WHERE bookId = 1 LIMIT 3').all()
console.log('\nStripped consonants from Tanach:')
tanachSample.forEach(l => console.log(' ', stripToConsonants(l.content).substring(0, 100)))

// Estimate concordance table size
// If avg 8 words per line and 1.5M lines = ~12M rows
// Each row: word(20 bytes) + bookId(4) + lineId(4) + lineIndex(4) = ~32 bytes
// 12M * 32 = ~384MB — too big?
// But with deduplication (word + lineId), and only Hebrew words...
console.log('\n--- CONCORDANCE SIZE ESTIMATE ---')
console.log(`Lines: ${totalLines.cnt.toLocaleString()}`)
console.log(`Avg words/line: ${avgWordsPerLine.toFixed(1)}`)
const estRows = Math.round(totalLines.cnt * avgWordsPerLine * 0.6) // ~60% are Hebrew words
console.log(`Estimated concordance rows (Hebrew only): ${estRows.toLocaleString()}`)
console.log(`Estimated size @ 40 bytes/row: ${Math.round(estRows * 40 / 1024 / 1024)} MB`)

db.close()
