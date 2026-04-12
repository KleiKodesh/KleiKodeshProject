const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Get all top-level categories to understand the structure
const topCats = db.prepare('SELECT id, parentId, title, level FROM category ORDER BY level, id').all()
console.log('ALL CATEGORIES (first 80):')
topCats.slice(0, 80).forEach(c => console.log(`  [${c.id}] L${c.level} parent=${c.parentId} "${c.title}"`))

// Search for מילון in book titles
const dictBooks = db.prepare(
  "SELECT b.id, b.categoryId, b.title, b.totalLines FROM book b WHERE b.title LIKE '%מילון%' ORDER BY b.title"
).all()
console.log('\nBOOKS WITH מילון IN TITLE:', JSON.stringify(dictBooks, null, 2))

// Search for ערוך in book titles (ערוך השלם, ערוך לנר, etc.)
const aruchBooks = db.prepare(
  "SELECT b.id, b.categoryId, b.title, b.totalLines FROM book b WHERE b.title LIKE '%ערוך%' ORDER BY b.title"
).all()
console.log('\nBOOKS WITH ערוך IN TITLE:', JSON.stringify(aruchBooks, null, 2))

// Search for אוצר in book titles
const otzarBooks = db.prepare(
  "SELECT b.id, b.categoryId, b.title, b.totalLines FROM book b WHERE b.title LIKE '%אוצר%' ORDER BY b.title"
).all()
console.log('\nBOOKS WITH אוצר IN TITLE:', JSON.stringify(otzarBooks, null, 2))

// Search for all topics
const allTopics = db.prepare('SELECT id, name FROM topic ORDER BY name').all()
console.log('\nALL TOPICS:', JSON.stringify(allTopics, null, 2))

db.close()
