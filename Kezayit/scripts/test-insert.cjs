'use strict'
const Database = require('better-sqlite3')
const db = new Database('./public/wikidictionary.db')
db.pragma('journal_mode = WAL')

// Check source
db.prepare("INSERT OR IGNORE INTO source (label) VALUES ('ויקימילון')").run()
const sourceId = db.prepare("SELECT id FROM source WHERE label = 'ויקימילון'").get()?.id
console.log('sourceId:', sourceId)

// Try inserting a sense
try {
  const r = db.prepare(`INSERT OR IGNORE INTO sense (headword, nikud, pos, binyan, shoresh, ktiv_male, etymology, period_tag, source_id, sense_order) VALUES (?,?,?,?,?,?,?,?,?,?)`)
    .run('שלום', 'שָׁלוֹם', 'שם עצם', null, 'ש-ל-מ', null, null, null, sourceId, 0)
  console.log('insert result:', r.changes, 'lastInsertRowid:', r.lastInsertRowid)
} catch(e) {
  console.error('INSERT ERROR:', e.message)
}

console.log('senses after:', db.prepare('SELECT COUNT(*) as c FROM sense').get().c)
db.close()
