const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Get all books under cat 75 (מילונים וספרי יעץ) and its children
const books75 = db.prepare(`
  SELECT b.id, b.title, b.totalLines, b.categoryId, c.title AS catTitle, c.parentId AS catParentId
  FROM book b
  JOIN category c ON c.id = b.categoryId
  WHERE b.categoryId IN (
    SELECT id FROM category WHERE id = 75 OR parentId = 75
  )
  ORDER BY c.id, b.orderIndex
`).all()
console.log('ALL BOOKS UNDER CAT 75 (מילונים וספרי יעץ):')
books75.forEach(b => console.log(`  [${b.id}] cat=${b.categoryId}(${b.catTitle}) "${b.title}" (${b.totalLines} lines)`))

// Get all books under cat 1220 (ספרות עזר) and its children
const books1220 = db.prepare(`
  SELECT b.id, b.title, b.totalLines, b.categoryId, c.title AS catTitle
  FROM book b
  JOIN category c ON c.id = b.categoryId
  WHERE b.categoryId IN (
    SELECT id FROM category WHERE id = 1220 OR parentId = 1220
  )
  ORDER BY c.id, b.orderIndex
`).all()
console.log('\nALL BOOKS UNDER CAT 1220 (ספרות עזר):')
books1220.forEach(b => console.log(`  [${b.id}] cat=${b.categoryId}(${b.catTitle}) "${b.title}" (${b.totalLines} lines)`))

// Are there any other top-level categories that might be reference/dictionary?
const topCats = db.prepare('SELECT id, title, level FROM category WHERE level = 0 ORDER BY id').all()
console.log('\nALL TOP-LEVEL CATEGORIES:')
topCats.forEach(c => console.log(`  [${c.id}] "${c.title}"`))

db.close()
