const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// The 74 words/line is suspicious — let's look at actual line lengths
const lineLengths = db.prepare(`
  SELECT length(content) as len FROM line ORDER BY RANDOM() LIMIT 2000
`).all()
const avgLen = lineLengths.reduce((s, l) => s + l.len, 0) / lineLengths.length
const maxLen = Math.max(...lineLengths.map(l => l.len))
const minLen = Math.min(...lineLengths.map(l => l.len))
const under100 = lineLengths.filter(l => l.len < 100).length
const under300 = lineLengths.filter(l => l.len < 300).length
console.log(`Avg line length: ${avgLen.toFixed(0)} chars`)
console.log(`Min: ${minLen}, Max: ${maxLen}`)
console.log(`Under 100 chars: ${under100}/2000 (${(under100/20).toFixed(0)}%)`)
console.log(`Under 300 chars: ${under300}/2000 (${(under300/20).toFixed(0)}%)`)

// Look at actual short lines (likely Tanach/Mishna style)
const shortLines = db.prepare(`
  SELECT l.content, b.title, b.categoryId
  FROM line l JOIN book b ON b.id = l.bookId
  WHERE length(l.content) BETWEEN 20 AND 80
  AND l.content NOT LIKE '<%'
  ORDER BY RANDOM() LIMIT 10
`).all()
console.log('\nSample short lines (20-80 chars, no HTML):')
shortLines.forEach(l => console.log(`  [${l.title}] "${l.content}"`))

// Look at very long lines (Talmud/commentary)
const longLines = db.prepare(`
  SELECT length(content) as len, bookId FROM line
  WHERE length(content) > 2000
  LIMIT 5
`).all()
console.log('\nVery long lines (>2000 chars):', longLines.length > 0 ? longLines : 'none in sample')

// What does a Tanach line actually look like (book 1 = בראשית)
const genesisLines = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 1 ORDER BY lineIndex LIMIT 10
`).all()
console.log('\nGenesis lines:')
genesisLines.forEach(l => console.log(`  [${l.lineIndex}] len=${l.content.length} "${l.content.substring(0, 100)}"`)  )

// What does a Talmud line look like (book 112 = מגילה)
const talmudLines = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 112 ORDER BY lineIndex LIMIT 5
`).all()
console.log('\nTalmud (מגילה) lines:')
talmudLines.forEach(l => console.log(`  [${l.lineIndex}] len=${l.content.length} "${l.content.substring(0, 150)}"`)  )

// Key question: are Tanach lines one-verse-per-line?
// Count lines in בראשית vs expected ~1534 verses
const genesisCount = db.prepare('SELECT COUNT(*) as cnt FROM line WHERE bookId = 1').get()
console.log(`\nGenesis line count: ${genesisCount.cnt} (expected ~1534 verses + headers)`)

// What about Mishna? (book 42 = משנה ברכות)
const mishnaLines = db.prepare(`
  SELECT lineIndex, content FROM line WHERE bookId = 42 ORDER BY lineIndex LIMIT 5
`).all()
console.log('\nMishna (ברכות) lines:')
mishnaLines.forEach(l => console.log(`  [${l.lineIndex}] len=${l.content.length} "${l.content.substring(0, 150)}"`)  )

// The real question: how many lines are SHORT (verse/mishna style) vs LONG (commentary)?
const lengthDist = db.prepare(`
  SELECT
    SUM(CASE WHEN length(content) < 200 THEN 1 ELSE 0 END) as short,
    SUM(CASE WHEN length(content) BETWEEN 200 AND 1000 THEN 1 ELSE 0 END) as medium,
    SUM(CASE WHEN length(content) > 1000 THEN 1 ELSE 0 END) as long,
    COUNT(*) as total
  FROM line
`).get()
console.log('\nLine length distribution (ALL lines):')
console.log(`  Short (<200 chars): ${lengthDist.short.toLocaleString()} (${(lengthDist.short/lengthDist.total*100).toFixed(1)}%)`)
console.log(`  Medium (200-1000): ${lengthDist.medium.toLocaleString()} (${(lengthDist.medium/lengthDist.total*100).toFixed(1)}%)`)
console.log(`  Long (>1000): ${lengthDist.long.toLocaleString()} (${(lengthDist.long/lengthDist.total*100).toFixed(1)}%)`)

// Better word count estimate using actual short lines
const shortSample = db.prepare(`
  SELECT content FROM line WHERE length(content) < 200 ORDER BY RANDOM() LIMIT 500
`).all()
let shortWords = 0
for (const l of shortSample) {
  const text = l.content.replace(/<[^>]+>/g, ' ').replace(/[^\u05D0-\u05EA\s]/g, ' ').trim()
  shortWords += text.split(/\s+/).filter(w => w.length >= 2).length
}
console.log(`\nAvg Hebrew words in short lines: ${(shortWords/shortSample.length).toFixed(1)}`)

db.close()
