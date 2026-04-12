const Database = require('better-sqlite3')
const db = new Database(
  'C:\\Users\\Admin\\AppData\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db',
  { readonly: true },
)

// Find dictionary-related categories
const cats = db
  .prepare(
    "SELECT id, parentId, title, level FROM category WHERE title LIKE '%מילון%' OR title LIKE '%ערוך%' OR title LIKE '%לשון%'",
  )
  .all()
console.log('DICT CATEGORIES:', JSON.stringify(cats, null, 2))

// Find books in those categories
if (cats.length > 0) {
  const ids = cats.map((c) => c.id).join(',')
  const books = db
    .prepare(
      `SELECT b.id, b.categoryId, b.title, b.totalLines FROM book b WHERE b.categoryId IN (${ids}) ORDER BY b.orderIndex LIMIT 50`,
    )
    .all()
  console.log('\nBOOKS IN DICT CATS:', JSON.stringify(books, null, 2))
}

// Also search books by title
const booksByTitle = db
  .prepare(
    "SELECT b.id, b.categoryId, b.title, b.totalLines FROM book b WHERE b.title LIKE '%מילון%' OR b.title LIKE '%ערוך%' OR b.title LIKE '%ג%' LIMIT 30",
  )
  .all()
console.log('\nBOOKS BY TITLE (sample):', JSON.stringify(booksByTitle.slice(0, 10), null, 2))

// Check topics
const topics = db.prepare("SELECT id, name FROM topic WHERE name LIKE '%מילון%' OR name LIKE '%לשון%'").all()
console.log('\nTOPICS:', JSON.stringify(topics, null, 2))

// Check book_topic for those topic ids
if (topics.length > 0) {
  const tids = topics.map((t) => t.id).join(',')
  const booksWithTopic = db
    .prepare(
      `SELECT b.id, b.title, b.totalLines, b.categoryId FROM book b JOIN book_topic bt ON bt.bookId = b.id WHERE bt.topicId IN (${tids}) ORDER BY b.title LIMIT 50`,
    )
    .all()
  console.log('\nBOOKS WITH DICT TOPIC:', JSON.stringify(booksWithTopic, null, 2))
}

db.close()
