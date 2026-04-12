const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Get category 1225 and its parent
const cat1225 = db.prepare('SELECT id, parentId, title, level FROM category WHERE id = 1225').get()
console.log('CAT 1225:', JSON.stringify(cat1225))
if (cat1225) {
  const parent = db.prepare('SELECT id, parentId, title, level FROM category WHERE id = ?').get(cat1225.parentId)
  console.log('PARENT:', JSON.stringify(parent))
}

// Get all books in category 1225
const books1225 = db.prepare('SELECT id, title, totalLines FROM book WHERE categoryId = 1225 ORDER BY orderIndex').all()
console.log('BOOKS IN 1225:', JSON.stringify(books1225, null, 2))

// Get all books in category 75 (parent of 79)
const cat75books = db.prepare(`
  SELECT b.id, b.title, b.totalLines, b.categoryId, c.title AS catTitle
  FROM book b JOIN category c ON c.id = b.categoryId
  WHERE b.categoryId IN (
    SELECT id FROM category WHERE parentId = 75 OR id = 75
  )
  ORDER BY b.categoryId, b.orderIndex
`).all()
console.log('\nALL BOOKS UNDER מילונים וספרי יעץ (cat 75):', JSON.stringify(cat75books, null, 2))

// Get all children of cat 75
const children75 = db.prepare('SELECT id, title, level FROM category WHERE parentId = 75').all()
console.log('\nCHILDREN OF CAT 75:', JSON.stringify(children75, null, 2))

// Get all children of cat 1225 parent
const parent1225 = db.prepare('SELECT id, parentId, title, level FROM category WHERE id = ?').get(cat1225?.parentId)
if (parent1225) {
  const siblings = db.prepare('SELECT id, title, level FROM category WHERE parentId = ?').all(parent1225.id)
  console.log('\nSIBLINGS OF 1225 (children of', parent1225.title, '):', JSON.stringify(siblings, null, 2))
}

// Sample lines from ספר השרשים לרדק (id=6105)
const lines = db.prepare('SELECT lineIndex, content FROM line WHERE bookId = 6105 ORDER BY lineIndex LIMIT 15').all()
console.log('\nSAMPLE LINES FROM ספר השרשים לרדק:', JSON.stringify(lines, null, 2))

db.close()
