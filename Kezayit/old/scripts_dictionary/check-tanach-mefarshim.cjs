const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true }
)

// What categories are under תנ"ך (cat 1)?
const tanachCats = db.prepare(`
  SELECT id, parentId, title, level FROM category 
  WHERE id = 1 OR parentId = 1 OR parentId IN (SELECT id FROM category WHERE parentId = 1)
  ORDER BY level, id
`).all()
console.log('תנ"ך category tree:')
tanachCats.forEach(c => console.log(`  ${'  '.repeat(c.level)}[${c.id}] L${c.level} "${c.title}"`))

// How many books total under תנ"ך?
const tanachBooks = db.prepare(`
  SELECT COUNT(*) as cnt FROM book WHERE categoryId IN (
    SELECT id FROM category WHERE id = 1 OR parentId = 1 
    OR parentId IN (SELECT id FROM category WHERE parentId = 1)
    OR parentId IN (SELECT id FROM category WHERE parentId IN (SELECT id FROM category WHERE parentId = 1))
  )
`).get()
console.log(`\nTotal books under תנ"ך: ${tanachBooks.cnt}`)

// Sample books — what kinds are there?
const sampleBooks = db.prepare(`
  SELECT b.id, b.title, b.totalLines, c.title as catTitle
  FROM book b JOIN category c ON c.id = b.categoryId
  WHERE b.categoryId IN (
    SELECT id FROM category WHERE id = 1 OR parentId = 1 
    OR parentId IN (SELECT id FROM category WHERE parentId = 1)
    OR parentId IN (SELECT id FROM category WHERE parentId IN (SELECT id FROM category WHERE parentId = 1))
  )
  ORDER BY b.totalLines DESC LIMIT 30
`).all()
console.log('\nLargest books under תנ"ך:')
sampleBooks.forEach(b => console.log(`  [${b.id}] "${b.title}" (${b.totalLines} lines) cat=${b.catTitle}`))

// What does a typical מפרש line look like? Sample from a few commentaries
// Rashi on Torah (find it)
const rashi = db.prepare(`SELECT b.id, b.title FROM book b WHERE b.title LIKE '%רש"י%' AND b.title LIKE '%בראשית%' LIMIT 3`).all()
console.log('\nRashi books:', rashi)

if (rashi.length > 0) {
  const rashiLines = db.prepare(`SELECT lineIndex, content FROM line WHERE bookId = ? ORDER BY lineIndex LIMIT 10`).all(rashi[0].id)
  console.log('\nRashi sample lines:')
  rashiLines.forEach(l => console.log(`  [${l.lineIndex}] ${l.content.substring(0, 200)}`))
}

// What's the typical format? Look for "פי'" or "פירוש" patterns
const piLines = db.prepare(`
  SELECT l.content, b.title FROM line l JOIN book b ON b.id = l.bookId
  WHERE l.content LIKE '%<b>%' AND l.content LIKE '%פי%'
  AND b.categoryId IN (
    SELECT id FROM category WHERE parentId = 1 
    OR parentId IN (SELECT id FROM category WHERE parentId = 1)
  )
  LIMIT 5
`).all()
console.log('\nSample lines with bold + פי:')
piLines.forEach(l => console.log(`  [${l.title}] ${l.content.substring(0, 200)}`))

db.close()
