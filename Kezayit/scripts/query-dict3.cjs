const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Get all books tagged with topic מילונים (id=45)
const dictBooks = db.prepare(`
  SELECT b.id, b.title, b.totalLines, b.categoryId, b.heShortDesc,
         c.title AS categoryTitle
  FROM book b
  JOIN book_topic bt ON bt.bookId = b.id
  JOIN category c ON c.id = b.categoryId
  WHERE bt.topicId = 45
  ORDER BY b.title
`).all()
console.log('DICT BOOKS (מילונים topic):', JSON.stringify(dictBooks, null, 2))
console.log('\nTotal:', dictBooks.length)

// Also check category 79 which had ספר הערוך and אוצר לעזי רשי
const cat79 = db.prepare(`
  SELECT b.id, b.title, b.totalLines, b.categoryId, c.title AS catTitle
  FROM book b JOIN category c ON c.id = b.categoryId
  WHERE b.categoryId = 79
  ORDER BY b.orderIndex
`).all()
console.log('\nCATEGORY 79 BOOKS:', JSON.stringify(cat79, null, 2))

// Get category 79 info
const cat79info = db.prepare('SELECT id, parentId, title, level FROM category WHERE id = 79').get()
console.log('\nCATEGORY 79 INFO:', JSON.stringify(cat79info))

// Get parent of 79
if (cat79info) {
  const parent = db.prepare('SELECT id, parentId, title, level FROM category WHERE id = ?').get(cat79info.parentId)
  console.log('PARENT:', JSON.stringify(parent))
}

// Get first few lines of ספר הערוך (id=473) to understand the format
const lines = db.prepare('SELECT lineIndex, content FROM line WHERE bookId = 473 ORDER BY lineIndex LIMIT 20').all()
console.log('\nSPECIMEN LINES FROM ספר הערוך:', JSON.stringify(lines, null, 2))

// Get TOC entries for ספר הערוך
const toc = db.prepare(`
  SELECT te.id, te.parentId, te.level, te.lineId, tt.text, l.lineIndex
  FROM tocEntry te
  JOIN tocText tt ON tt.id = te.textId
  LEFT JOIN line l ON l.id = te.lineId
  WHERE te.bookId = 473
  ORDER BY te.id
  LIMIT 30
`).all()
console.log('\nTOC FOR ספר הערוך (first 30):', JSON.stringify(toc, null, 2))

db.close()
